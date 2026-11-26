"""One bounded match subscription, two independent released replay streams."""
import asyncio
from collections import deque
from copy import copy, deepcopy
from dataclasses import dataclass, field
import hashlib
import logging
import time
import uuid

from .format import board_from_record, build_match, identity
from .wall_alignment import RecordConflict, RoundOriginTracker
from ..gamestate.public.connection_delivery import send_bounded
from ..gamestate.public.spectator_rules import duplicate_spectator_blocked, too_many_ai_for_spectator

logger = logging.getLogger(__name__)
MAX_RETAINED_EVENTS = 512


class PublishedStream:
    def __init__(self, stream_id, body):
        self.stream_id = stream_id
        self.epoch = str(uuid.uuid4())
        self.seq = 0
        self.body = deepcopy(body)
        self.events = deque(maxlen=MAX_RETAINED_EVENTS)

    def reset(self, body):
        self.epoch = str(uuid.uuid4())
        self.seq = 0
        self.events.clear()
        self.body = deepcopy(body)

    def publish(self, event, body, *, owned_body=False):
        self.seq += 1
        self.events.append((self.seq, deepcopy(event)))
        self.body = body if owned_body else deepcopy(body)

    def fork(self):
        # Bodies and retained events are replaced or appended, never edited in place.
        stream = copy(self)
        stream.events = self.events.copy()
        return stream

    def packet(self, cursor):
        after = cursor.get("applied_seq") if isinstance(cursor, dict) else None
        earliest = self.events[0][0] - 1 if self.events else self.seq
        resumable = (isinstance(cursor, dict) and cursor.get("stream_epoch") == self.epoch
                     and type(after) is int and earliest <= after <= self.seq)
        base = {"stream_id": self.stream_id, "stream_epoch": self.epoch}
        if not resumable:
            return {**base, "type": "spectator/stream_snapshot", "at_seq": self.seq, "body": deepcopy(self.body)}
        pending = [(seq, event) for seq, event in self.events if seq > after]
        if not pending:
            return None
        return {**base, "type": "spectator/stream_delta", "from_seq": pending[0][0],
                "to_seq": pending[-1][0], "events": [deepcopy(event) for _, event in pending]}


def snapshot_fields(fields, previous):
    # Most fields (including the dealt wall) stay fixed for the whole round.
    return {key: previous[key] if key in previous and previous[key] == value else deepcopy(value)
            for key, value in fields.items()}


def visible_record(state, now, previous, *, cursors=None):
    source = state.spectator_manager
    cutoff = now - source.spectator_delay
    if not source.game_title:
        return None
    rounds = {}
    for index, header in sorted(source.round_headers.items()):
        if header["timestamp"] > cutoff:
            break
        # Recorded ticks are append-only. Freeze the visible range before yielding
        # to another coroutine, including when only the delay cutoff has advanced.
        raw_ticks = source.round_ticks.get(index, ())
        raw_cursor = 0
        entries = []
        for i, e in enumerate(raw_ticks):
            if e["timestamp"] <= cutoff:
                raw_cursor = i + 1
                if e["tick"][0] not in ("ask_hand", "ask_other"):
                    entries.append(e)
        if cursors is not None:
            cursors[index] = raw_cursor
        key = f"round_index_{index}"
        rd = snapshot_fields(header["data"], previous.get("game_round", {}).get(key, {}))
        rd["action_ticks"] = [e["tick"] for e in entries]
        rd["action_times_ms"] = [int(e["timestamp"] * 1000) for e in entries]
        rounds[key] = rd
    if rounds:
        record = snapshot_fields({"game_title": source.game_title, "players_settings": source.players_settings}, previous)
        record["game_round"] = rounds
        return record


def released_all(state, now):
    source = state.spectator_manager
    cutoff = now - source.spectator_delay
    times = [h["timestamp"] for h in source.round_headers.values()]
    times.extend(e["timestamp"] for entries in source.round_ticks.values() for e in entries)
    return bool(times) and max(times) <= cutoff


def table_body(table):
    rounds = table.get("round_meta", {})
    key = max(rounds, key=lambda k: rounds[k]["round_index"], default=None)
    return {"table_id": table["table_id"], "availability": "ready" if key else "waiting",
            "table_record": table,
            "released_head": {"table_round_id": rounds[key]["table_round_id"],
                              "node": len(rounds[key]["action_meta"])} if key else None,
            "table_phase": "ended" if table["record_state"] == "complete" else "playing"}


@dataclass
class _RoundFeedState:
    round_index: int
    round_key: str
    header_data: dict
    raw_cursor: int
    table_round_id: str
    board_instance_id: object
    tracker: object


@dataclass
class _TableFeedState:
    game_title: dict
    players_settings: list
    game_id: object
    rounds: dict

    def shallow_copy(self):
        return _TableFeedState(self.game_title, self.players_settings, self.game_id, dict(self.rounds))


def _resolve_layout(title, header_data):
    layout = header_data.get("duplicate_wall_layout") or (title.get("duplicate_profile") or {}).get("wall_layout", "standard")
    if title.get("sub_rule") == "sichuan/xueliu_doubles_duplicate":
        layout = "xueliu"
    return layout


def _board_for_number(snapshot, title, header_data, number):
    if snapshot and number <= len(snapshot.get("boards", [])):
        return snapshot["boards"][number - 1]
    return board_from_record(title, header_data, number)


def _seed_table_states(sources, raw_cursors, bundle, snapshot):
    """Build incremental per-table state from a full build_match() result."""
    states = {}
    if not bundle:
        return states
    for table, entry in sources.items():
        tr = bundle["tables"].get(table)
        if not tr or tr.get("record_state") == "missing":
            continue
        raw_rec = entry["record"]
        title = deepcopy(raw_rec["game_title"])
        players_settings = deepcopy(raw_rec.get("players_settings", []))
        rounds = {}
        table_cursors = raw_cursors.get(table, {})
        for key, rd in tr["payload"]["game_round"].items():
            meta = tr["round_meta"][key]
            index = meta["round_index"]
            header_data = {k: deepcopy(v) for k, v in raw_rec["game_round"][key].items()
                           if k not in ("action_ticks", "action_times_ms")}
            instance = meta.get("board_instance_id")
            tracker = None
            if instance:
                number = header_data.get("current_round", index)
                layout = _resolve_layout(title, header_data)
                board = _board_for_number(snapshot, title, header_data, number)
                tracker = RoundOriginTracker(rd, instance, layout=layout, board=board)
                tracker.advance(rd["action_ticks"], rd.get("action_times_ms"))
            rounds[index] = _RoundFeedState(
                round_index=index,
                round_key=key,
                header_data=header_data,
                raw_cursor=table_cursors.get(index, len(rd["action_ticks"])),
                table_round_id=meta["table_round_id"],
                board_instance_id=instance,
                tracker=tracker,
            )
        states[table] = _TableFeedState(title, players_settings, entry.get("game_id"), rounds)
    return states


def _rolling_partial_sha(previous_sha, round_count, head_node, delta_count):
    token = f"{previous_sha}:{round_count}:{head_node}:{delta_count}".encode("ascii")
    return hashlib.sha256(token).hexdigest()


class MatchFeed:
    def __init__(self, pair):
        self.pair = pair
        self.streams = {}
        self._table_states = {}
        self._last_snapshot = None
        self._refresh_lock = asyncio.Lock()
        self.last_poll = 0.0
        self.last_used = time.monotonic()
        self.complete = False

    async def refresh(self, now=None):
        async with self._refresh_lock:
            self.last_used = time.monotonic()
            if now is None and self.streams and self.last_used - self.last_poll < 0.25:
                return self.streams
            if self.complete:
                self.last_poll = self.last_used
                return self.streams
            plan = self._plan_refresh(time.time() if now is None else now)
            if plan is not None:
                streams, table_states, snapshot, complete = await asyncio.to_thread(
                    self._build_streams, plan, self.streams, self._table_states, self.complete
                )
                self.streams = streams
                self._table_states = table_states
                self._last_snapshot = snapshot
                self.complete = complete
            self.last_poll = self.last_used
            return self.streams

    def _collect_full_sources(self, now, complete):
        data = self.pair.data
        states = self.pair.states
        finals = data.get("final_records", {})
        sources = {}
        raw_cursors = {}
        for table, state in states.items():
            if (table not in ("open", "closed") or duplicate_spectator_blocked(state)
                    or too_many_ai_for_spectator(state.player_list)):
                continue
            cursors = {}
            payload = finals[table] if complete else visible_record(state, now, {}, cursors=cursors)
            if payload is not None:
                sources[table] = {"record": payload, "game_id": data.get("record_ids", {}).get(table),
                                  "players": data.get("final_players", {}).get(table) if complete else None}
                raw_cursors[table] = cursors
        return sources, raw_cursors

    def _plan_refresh(self, now):
        data = self.pair.data
        states = self.pair.states
        finals = data.get("final_records", {})
        complete = (self.pair.completed and all(t in states and t in finals and released_all(states[t], now)
                                                for t in ("open", "closed")))
        # PairedMatch replaces its persisted state on update. Capture only the
        # fields used by this view, excluding live scores and private pending actions.
        fields = ("id", "profile", "planned_boards", "board_instances", "boards", "sync_mode")
        previous_snapshot = self._last_snapshot or {}
        snapshot = snapshot_fields({k: data[k] for k in fields if k in data}, previous_snapshot)
        snapshot["phase"] = "completed" if complete else ("interrupted" if data["status"] == "interrupted" else "running")
        snapshot_changed = self._last_snapshot is None or snapshot != self._last_snapshot

        if complete or not self.streams:
            sources, raw_cursors = self._collect_full_sources(now, complete)
            return ("full", sources, raw_cursors, snapshot, complete)

        prev_inst = previous_snapshot.get("board_instances", [])
        cur_inst = snapshot.get("board_instances", [])
        prev_boards = previous_snapshot.get("boards", [])
        cur_boards = snapshot.get("boards", [])
        needs_full = (
            snapshot.get("id") != previous_snapshot.get("id")
            or snapshot.get("profile") != previous_snapshot.get("profile")
            or snapshot.get("sync_mode") != previous_snapshot.get("sync_mode")
            or len(cur_inst) < len(prev_inst)
            or cur_inst[:len(prev_inst)] != prev_inst
            or len(cur_boards) < len(prev_boards)
            or cur_boards[:len(prev_boards)] != prev_boards
        )

        eligible = {}
        for table, state in states.items():
            if (table not in ("open", "closed") or duplicate_spectator_blocked(state)
                    or too_many_ai_for_spectator(state.player_list)):
                continue
            eligible[table] = state

        table_deltas = {}
        if not needs_full:
            for table in ("open", "closed"):
                tstate = self._table_states.get(table)
                state = eligible.get(table)
                if state is None:
                    if tstate is not None:
                        needs_full = True
                        break
                    continue
                source = state.spectator_manager
                cutoff = now - source.spectator_delay
                if not source.game_title:
                    if tstate is not None:
                        needs_full = True
                        break
                    continue
                if tstate is None:
                    if any(h["timestamp"] <= cutoff for h in source.round_headers.values()):
                        needs_full = True
                        break
                    continue
                game_id = data.get("record_ids", {}).get(table)
                if (source.game_title != tstate.game_title
                        or source.players_settings != tstate.players_settings
                        or game_id != tstate.game_id):
                    needs_full = True
                    break

                released_indices = []
                for index, header in sorted(source.round_headers.items()):
                    if header["timestamp"] > cutoff:
                        break
                    released_indices.append((index, header))
                released_set = {idx for idx, _ in released_indices}
                if any(idx not in released_set for idx in tstate.rounds):
                    needs_full = True
                    break

                appends = []
                new_rounds = []
                for index, header in released_indices:
                    raw_ticks = source.round_ticks.get(index, ())
                    if index in tstate.rounds:
                        rstate = tstate.rounds[index]
                        if header["data"] != rstate.header_data or len(raw_ticks) < rstate.raw_cursor:
                            needs_full = True
                            break
                        new_cursor = rstate.raw_cursor
                        delta_ticks = []
                        delta_times_ms = []
                        for i in range(rstate.raw_cursor, len(raw_ticks)):
                            entry = raw_ticks[i]
                            if entry["timestamp"] > cutoff:
                                break
                            new_cursor = i + 1
                            tick = entry["tick"]
                            if tick[0] not in ("ask_hand", "ask_other"):
                                delta_ticks.append(deepcopy(tick))
                                delta_times_ms.append(int(entry["timestamp"] * 1000))
                        if delta_ticks:
                            appends.append((index, new_cursor, delta_ticks, delta_times_ms))
                        elif new_cursor > rstate.raw_cursor:
                            # Only private ask ticks crossed cutoff; advance cursor without rebuilding.
                            rstate.raw_cursor = new_cursor
                    else:
                        new_cursor = 0
                        ticks = []
                        times_ms = []
                        for i, entry in enumerate(raw_ticks):
                            if entry["timestamp"] > cutoff:
                                break
                            new_cursor = i + 1
                            tick = entry["tick"]
                            if tick[0] not in ("ask_hand", "ask_other"):
                                ticks.append(deepcopy(tick))
                                times_ms.append(int(entry["timestamp"] * 1000))
                        new_rounds.append((index, new_cursor, deepcopy(header["data"]), ticks, times_ms))
                if needs_full:
                    break
                if appends or new_rounds:
                    table_deltas[table] = (appends, new_rounds)

        if needs_full:
            sources, raw_cursors = self._collect_full_sources(now, False)
            return ("full", sources, raw_cursors, snapshot, False)
        if not table_deltas and not snapshot_changed:
            return None
        return ("incremental", table_deltas, snapshot, False)

    @staticmethod
    def _build_streams(plan, previous_streams, previous_table_states, was_complete):
        if plan[0] == "incremental":
            _, table_deltas, snapshot, _ = plan
            return MatchFeed._build_incremental(table_deltas, snapshot, previous_streams, previous_table_states)
        _, sources, raw_cursors, snapshot, complete = plan
        return MatchFeed._build_full(sources, raw_cursors, snapshot, complete, previous_streams, was_complete)

    @staticmethod
    def _build_full(sources, raw_cursors, snapshot, complete, previous_streams, was_complete):
        streams = {key: stream.fork() for key, stream in previous_streams.items()}
        bundle = build_match(sources, match_id=snapshot["id"], snapshot=snapshot, complete=complete) if sources else None
        for table in ("open", "closed"):
            if bundle is None or bundle["tables"][table]["record_state"] == "missing":
                body = {"table_id": table, "availability": "waiting", "table_record": None, "released_head": None,
                        "table_phase": "waiting"}
            else:
                body = table_body(bundle["tables"][table])
            MatchFeed._update_table(streams, table, body)
        # Delayed view is derived solely from released data, not live pair pace.
        public_catalog = deepcopy(bundle["board_catalog"]) if bundle else []
        if not complete:
            for board in public_catalog:
                board.pop("wall_definition", None)
        manifest = {"match_id": snapshot["id"], "match_format": "paired_tables_2v2",
                    "rule": "sichuan", "sub_rule": snapshot["profile"]["sub_rule"],
                    "sync_mode": snapshot.get("sync_mode", True), "planned_boards": snapshot["planned_boards"],
                    "phase": snapshot["phase"],
                    "board_catalog": public_catalog, "record_revision": 1,
                    "result": bundle["result"] if complete and len(sources) == 2 else None,
                    "teams": deepcopy(bundle["teams"]) if bundle else [],
                    "rule_profile": deepcopy(snapshot["profile"])}
        if complete:
            manifest["settlements"] = bundle["settlements"] if bundle and len(sources) == 2 else []
            manifest["provenance"] = bundle["provenance"] if bundle else {}
            manifest["final_streams"] = {t: {"stream_epoch": streams[t].epoch, "applied_seq": streams[t].seq}
                                         for t in ("open", "closed")}
        if "match" not in streams:
            streams["match"] = PublishedStream("match", manifest)
        elif streams["match"].body != manifest:
            streams["match"].publish({"kind": "manifest_updated", "manifest": manifest}, manifest)
        if complete and not was_complete:
            streams["match"].publish({"kind": "record_ready", "manifest": manifest}, manifest)
        table_states = {} if complete else _seed_table_states(sources, raw_cursors, bundle, snapshot)
        return streams, table_states, snapshot, complete

    @staticmethod
    def _build_incremental(table_deltas, snapshot, previous_streams, previous_table_states):
        streams = {key: stream.fork() for key, stream in previous_streams.items()}
        table_states = {t: ts.shallow_copy() for t, ts in previous_table_states.items()}
        match_id = snapshot["id"]
        old_manifest = streams["match"].body
        manifest = None
        catalog_by_no = {b["board_no"]: b for b in old_manifest.get("board_catalog", [])}

        if (old_manifest.get("phase") != snapshot["phase"]
                or old_manifest.get("planned_boards") != snapshot["planned_boards"]):
            manifest = {**old_manifest, "phase": snapshot["phase"], "planned_boards": snapshot["planned_boards"]}

        for table, (appends, new_rounds) in table_deltas.items():
            tstate = table_states[table]
            stream = streams[table]
            old_body = stream.body
            old_tr = old_body["table_record"]
            old_payload = old_tr["payload"]
            new_rounds_dict = dict(old_payload["game_round"])
            new_round_meta = dict(old_tr["round_meta"])
            new_payload = {**old_payload, "game_round": new_rounds_dict}
            new_tr = {**old_tr, "payload": new_payload, "round_meta": new_round_meta}
            events = []
            total_added = 0

            for index, new_cursor, delta_ticks, delta_times_ms in appends:
                rstate = tstate.rounds[index]
                key = rstate.round_key
                old_rd = new_rounds_dict[key]
                old_meta = new_round_meta[key]
                start = len(old_rd["action_ticks"])
                if rstate.tracker is not None:
                    tracker = rstate.tracker.fork()
                    delta_meta = tracker.advance(delta_ticks, delta_times_ms)
                    alignment_status = tracker.alignment_status
                    alignment_reason = tracker.alignment_reason
                else:
                    tracker = None
                    delta_meta = [
                        {"actor_original_seat": None,
                         "occurred_at_ms": ts if type(ts) is int and ts >= 0 else None,
                         "wall_ref": None, "wall_ref_basis": "unavailable"}
                        for ts in delta_times_ms
                    ]
                    alignment_status = old_meta["alignment_status"]
                    alignment_reason = old_meta["alignment_reason"]

                new_rounds_dict[key] = {
                    **old_rd,
                    "action_ticks": old_rd["action_ticks"] + delta_ticks,
                    "action_times_ms": old_rd.get("action_times_ms", []) + delta_times_ms,
                }
                new_round_meta[key] = {
                    **old_meta,
                    "action_meta": old_meta["action_meta"] + delta_meta,
                    "legacy_node_map": old_meta["legacy_node_map"] + list(range(start + 1, start + len(delta_ticks) + 1)),
                    "alignment_status": alignment_status,
                    "alignment_reason": alignment_reason,
                }
                tstate.rounds[index] = _RoundFeedState(
                    round_index=index,
                    round_key=key,
                    header_data=rstate.header_data,
                    raw_cursor=new_cursor,
                    table_round_id=rstate.table_round_id,
                    board_instance_id=rstate.board_instance_id,
                    tracker=tracker,
                )
                total_added += len(delta_ticks)
                events.append({
                    "kind": "actions_appended",
                    "table_round_id": rstate.table_round_id,
                    "start_node": start,
                    "ticks": delta_ticks,
                    "action_meta": delta_meta,
                })

            for index, new_cursor, header_data, ticks, times_ms in new_rounds:
                key = f"round_index_{index}"
                number = header_data.get("current_round", index)
                if type(number) is not int or number < 1:
                    raise RecordConflict("Invalid paired board number")
                descriptor = (snapshot["board_instances"][number - 1]
                              if number <= len(snapshot.get("board_instances", [])) else None)
                instance = header_data.get("board_instance_id") or (descriptor or {}).get("board_instance_id")
                if descriptor and instance and instance != descriptor["board_instance_id"]:
                    raise RecordConflict("Snapshot and live record board attempts disagree")
                existing_cat = catalog_by_no.get(number)
                if existing_cat and instance and existing_cat["board_instance_id"] != instance:
                    raise RecordConflict("Paired records refer to different board attempts")

                layout = _resolve_layout(tstate.game_title, header_data)
                board = _board_for_number(snapshot, tstate.game_title, header_data, number)
                round_id = header_data.get("table_round_id") or identity("round", match_id, table, instance or key)
                node_map = list(range(len(ticks) + 1))
                tracker = None
                if instance:
                    tracker = RoundOriginTracker(header_data, instance, layout=layout, board=board)
                    action_meta = tracker.advance(ticks, times_ms)
                    initial_anchors = tracker.initial_anchors
                    alignment_status = tracker.alignment_status
                    alignment_reason = tracker.alignment_reason
                else:
                    action_meta = [
                        {"actor_original_seat": None,
                         "occurred_at_ms": ts if type(ts) is int and ts >= 0 else None,
                         "wall_ref": None, "wall_ref_basis": "unavailable"}
                        for ts in times_ms
                    ]
                    initial_anchors = []
                    alignment_status = "unavailable"
                    alignment_reason = "board_instance_unverified"

                new_rd = {**header_data, "action_ticks": ticks, "action_times_ms": times_ms}
                new_meta = {
                    "table_round_id": round_id,
                    "board_instance_id": instance,
                    "round_index": index,
                    "legacy_node_map": node_map,
                    "initial_anchors": initial_anchors,
                    "action_meta": action_meta,
                    "alignment_status": alignment_status,
                    "alignment_reason": alignment_reason,
                }
                new_rounds_dict[key] = new_rd
                new_round_meta[key] = new_meta
                tstate.rounds[index] = _RoundFeedState(
                    round_index=index,
                    round_key=key,
                    header_data=header_data,
                    raw_cursor=new_cursor,
                    table_round_id=round_id,
                    board_instance_id=instance,
                    tracker=tracker,
                )
                if instance and number not in catalog_by_no:
                    attempt = descriptor.get("attempt") if descriptor else header_data.get("board_attempt")
                    catalog_by_no[number] = {
                        "board_instance_id": instance,
                        "board_no": number,
                        "attempt": attempt,
                        "wall_layout": layout,
                        "status": "playing",
                    }
                    base = manifest if manifest is not None else dict(old_manifest)
                    base["board_catalog"] = [catalog_by_no[k] for k in sorted(catalog_by_no)]
                    manifest = base
                total_added += max(1, len(ticks))
                events.append({
                    "kind": "round_opened",
                    "round_key": key,
                    "round_header": new_rd,
                    "round_meta": new_meta,
                })

            new_body = table_body(new_tr)
            head_node = (new_body["released_head"] or {}).get("node", 0)
            new_tr["source_sha256"] = _rolling_partial_sha(
                old_tr["source_sha256"], len(new_rounds_dict), head_node, total_added
            )
            for event in events:
                stream.publish(event, new_body, owned_body=True)

        if manifest is not None and manifest != old_manifest:
            streams["match"].publish({"kind": "manifest_updated", "manifest": manifest}, manifest, owned_body=True)

        return streams, table_states, snapshot, False

    @staticmethod
    def _update_table(streams, table, body):
        if table not in streams:
            streams[table] = PublishedStream(table, body)
            return
        stream = streams[table]
        old = stream.body.get("table_record")
        new = body.get("table_record")
        if stream.body == body:
            return
        if old is None or new is None:
            stream.publish({"kind": "table_available", "body": body}, body)
            return
        previous = old["payload"]["game_round"]
        current = new["payload"]["game_round"]
        if any(k not in current or old["round_meta"][k]["table_round_id"] != new["round_meta"][k]["table_round_id"]
               or current[k]["action_ticks"][:len(v["action_ticks"])] != v["action_ticks"] for k, v in previous.items()):
            stream.reset(body)
            return
        if new["record_state"] == "complete" and old["record_state"] != "complete":
            stream.publish({"kind": "table_finalized", "body": body}, body)
            return
        header = lambda rd: {k: v for k, v in rd.items() if k not in ("action_ticks", "action_times_ms")}
        if (old.get("players") != new.get("players")
                or {k: v for k, v in old["payload"].items() if k != "game_round"}
                != {k: v for k, v in new["payload"].items() if k != "game_round"}
                or any(header(previous[k]) != header(current[k]) for k in previous)):
            stream.publish({"kind": "table_available", "body": body}, body)
            return
        events = []
        for key, rd in current.items():
            if key not in previous:
                events.append({"kind": "round_opened", "round_key": key, "round_header": rd,
                               "round_meta": new["round_meta"][key]})
            elif len(rd["action_ticks"]) > len(previous[key]["action_ticks"]):
                start = len(previous[key]["action_ticks"])
                events.append({"kind": "actions_appended", "table_round_id": new["round_meta"][key]["table_round_id"],
                               "start_node": start, "ticks": rd["action_ticks"][start:],
                               "action_meta": new["round_meta"][key]["action_meta"][start:]})
        for event in events:
            stream.publish(event, body, owned_body=True)
        if not events and old != new:
            stream.publish({"kind": "table_available", "body": body}, body, owned_body=True)


@dataclass
class Subscription:
    user_id: int
    connection: object
    feed: MatchFeed
    tables: list
    id: str = field(default_factory=lambda: str(uuid.uuid4()))
    sent: dict = field(default_factory=dict)
    acknowledged: dict = field(default_factory=dict)
    task: object = None
    send_lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    delivery_lock: asyncio.Lock = field(default_factory=asyncio.Lock)


class MatchSpectatorHub:
    def __init__(self, server):
        self.server = server
        self.feeds = {}
        self.subscriptions = {}
        self.admission = asyncio.Lock()
        self.send_timeout = 5.0

    def authorize(self, user_id, match_id, requested, *, subscription=None):
        manager = self.server.gamestate_manager
        pair = manager.sichuan_duplicate_matches.get(match_id)
        if (pair is None and subscription and subscription.feed.pair.completed
                and subscription.feed.pair.data["id"] == match_id):
            # An admitted spectator may finish the delayed stream after its lobby is gone.
            pair = subscription.feed.pair
        if pair is None:
            raise PermissionError("MATCH_UNAVAILABLE")
        if pair.data.get("wall_source") != "training":
            raise PermissionError("SPECTATOR_FORBIDDEN")
        matchmaking = getattr(self.server, "match_manager", None)
        if matchmaking and matchmaking.blocks_spectator(user_id):
            raise PermissionError("IN_MATCH_QUEUE")
        if user_id in getattr(manager, "user_id_to_game_state", {}):
            raise PermissionError("PARTICIPANT_FORBIDDEN")
        members = [p.user_id for s in pair.states.values() for p in s.player_list]
        members.extend(m.get("user_id") for ms in pair.data.get("members", {}).values() for m in ms)
        if user_id in members:
            raise PermissionError("PARTICIPANT_FORBIDDEN")
        if not isinstance(requested, list) or not requested or any(t not in ("open", "closed") for t in requested):
            raise ValueError("INVALID_TABLES")
        allowed = [t for t in ("open", "closed") if t in requested and t in pair.states
                   and not duplicate_spectator_blocked(pair.states[t])
                   and not too_many_ai_for_spectator(pair.states[t].player_list)]
        if not allowed:
            raise PermissionError("SPECTATOR_FORBIDDEN")
        return pair, allowed

    async def remove(self, user_id, subscription_id=None):
        sub = self.subscriptions.get(user_id)
        if sub is None or (subscription_id is not None and sub.id != subscription_id):
            return
        self.subscriptions.pop(user_id, None)
        if sub.task and sub.task is not asyncio.current_task():
            sub.task.cancel()
            await asyncio.gather(sub.task, return_exceptions=True)

    async def send(self, sub, packet):
        if packet.get("stream_id") == "match":
            packet = deepcopy(packet)
            manifests = [packet["body"]] if "body" in packet else [e["manifest"] for e in packet.get("events", [])]
            for manifest in manifests:
                if "final_streams" in manifest:
                    manifest["final_streams"] = {k: v for k, v in manifest["final_streams"].items() if k in sub.tables}
                if len(sub.tables) < 2:
                    manifest["result"] = None
                    manifest["settlements"] = []
                    manifest["provenance"] = {}
                    for team in manifest.get("teams", []):
                        team["members"] = [m for m in team.get("members", []) if m["table_id"] in sub.tables]
                    for board in manifest.get("board_catalog", []):
                        board.pop("wall_definition", None)
        async with sub.send_lock:
            await send_bounded(sub.connection.websocket, {"protocol_version": 2, "success": True,
                "match_id": sub.feed.pair.data["id"], "subscription_id": sub.id, **packet}, timeout=self.send_timeout)

    async def subscribe(self, connection, message):
        async with self.admission:
            user_id = connection.user_id
            pair, allowed = self.authorize(user_id, message.get("match_id"), message.get("requested_tables", ["open", "closed"]))
            await self.server.gamestate_manager.remove_spectator_from_all_games(user_id)
            await self.remove(user_id)
            active = {s.feed.pair.data["id"] for s in self.subscriptions.values()}
            for key, feed in list(self.feeds.items()):
                if key not in active and time.monotonic() - feed.last_used > 120:
                    self.feeds.pop(key)
            feed = self.feeds.get(pair.data["id"])
            if feed is None or feed.pair is not pair:
                feed = self.feeds[pair.data["id"]] = MatchFeed(pair)
            await feed.refresh()
            current_pair, allowed = self.authorize(user_id, pair.data["id"], allowed)
            if current_pair is not pair:
                raise PermissionError("MATCH_UNAVAILABLE")
            sub = Subscription(user_id, connection, feed, allowed)
            resume = message.get("resume")
            if isinstance(resume, dict):
                sub.sent = {k: deepcopy(v) for k, v in resume.items() if k in ["match", *allowed] and isinstance(v, dict)}
            self.subscriptions[user_id] = sub
            try:
                await self.send(sub, {"type": "spectator/match_accepted", "request_id": message.get("request_id"),
                                     "allowed_tables": allowed, "policy_version": 1})
                await self.deliver(sub)
            except BaseException:
                await self.remove(user_id, sub.id)
                raise
            sub.task = asyncio.create_task(self.run(sub))
            return sub

    async def deliver(self, sub):
        async with sub.delivery_lock:
            return await self._deliver_locked(sub)

    async def _deliver_locked(self, sub):
        streams = sub.feed.streams
        for stream_id in ["match", *sub.tables]:
            stream = streams[stream_id]
            packet = stream.packet(sub.sent.get(stream_id))
            if packet:
                await self.send(sub, packet)
                sub.sent[stream_id] = {"stream_epoch": stream.epoch, "applied_seq": packet.get("at_seq", packet.get("to_seq"))}
        return "final_streams" in streams["match"].body

    async def run(self, sub):
        try:
            while self.subscriptions.get(sub.user_id) is sub:
                await asyncio.sleep(1)
                await sub.feed.refresh()
                pair, allowed = self.authorize(sub.user_id, sub.feed.pair.data["id"], sub.tables, subscription=sub)
                if pair is not sub.feed.pair or allowed != sub.tables:
                    raise PermissionError("SPECTATOR_FORBIDDEN")
                if await self.deliver(sub):
                    return
        except asyncio.CancelledError:
            pass
        except Exception as exc:
            logger.warning("Match spectator closed user=%s reason=%s", sub.user_id, type(exc).__name__)
            try:
                await self.send(sub, {"type": "spectator/error", "success": False, "code": "SUBSCRIPTION_CLOSED", "retryable": True})
            except Exception:
                pass
            await self.remove(sub.user_id, sub.id)


def get_hub(server):
    manager = server.gamestate_manager
    hub = getattr(manager, "match_spectator_hub", None)
    if hub is None:
        hub = manager.match_spectator_hub = MatchSpectatorHub(server)
    return hub


async def handle_message(server, connect_id, message, websocket):
    connection = server.players.get(connect_id)
    request_id = message.get("request_id")
    try:
        if connection is None or not connection.user_id or connection.websocket is not websocket:
            raise PermissionError("NOT_AUTHENTICATED")
        if message.get("protocol_version") != 2:
            raise ValueError("UNSUPPORTED_PROTOCOL")
        hub = get_hub(server)
        kind = message.get("type")
        if kind == "spectator/match_subscribe":
            lock = getattr(getattr(server, "room_manager", None), "event_seating_lock", None)
            if lock:
                async with lock:
                    await hub.subscribe(connection, message)
            else:
                await hub.subscribe(connection, message)
            return
        sub = hub.subscriptions.get(connection.user_id)
        if sub is None or sub.id != message.get("subscription_id") or sub.connection is not connection:
            raise ValueError("STALE_SUBSCRIPTION")
        if kind == "spectator/match_unsubscribe":
            await hub.remove(connection.user_id, sub.id)
            await websocket.send_json({"type": "spectator/match_unsubscribed", "protocol_version": 2,
                                       "success": True, "subscription_id": sub.id, "request_id": request_id})
        elif kind == "spectator/stream_resync":
            stream_id = message.get("stream_id")
            if stream_id not in ["match", *sub.tables]:
                raise PermissionError("TABLE_UNAVAILABLE")
            async with sub.delivery_lock:
                sub.sent.pop(stream_id, None)
                await hub._deliver_locked(sub)
        elif kind == "spectator/stream_ack":
            cursors = message.get("cursors", {})
            if not isinstance(cursors, dict) or len(cursors) > 3:
                raise ValueError("INVALID_CURSOR")
            for key, cursor in cursors.items():
                sent = sub.sent.get(key, {})
                if (not isinstance(cursor, dict) or cursor.get("stream_epoch") != sent.get("stream_epoch")
                        or type(cursor.get("applied_seq")) is not int or not 0 <= cursor["applied_seq"] <= sent.get("applied_seq", -1)):
                    raise ValueError("INVALID_CURSOR")
                sub.acknowledged[key] = deepcopy(cursor)
        else:
            raise ValueError("UNKNOWN_MESSAGE")
    except (PermissionError, ValueError) as exc:
        await send_bounded(websocket, {"type": "spectator/error", "protocol_version": 2, "success": False,
                                      "request_id": request_id, "subscription_id": message.get("subscription_id"),
                                      "code": str(exc), "retryable": False})
