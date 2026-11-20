"""One bounded match subscription, two independent released replay streams."""
import asyncio
from collections import deque
from copy import copy, deepcopy
from dataclasses import dataclass, field
import logging
import time
import uuid

from .format import build_match
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

    def publish(self, event, body):
        self.seq += 1
        self.events.append((self.seq, deepcopy(event)))
        self.body = deepcopy(body)

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


def visible_record(state, now, previous):
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
        entries = [e for e in source.round_ticks.get(index, ())
                   if e["timestamp"] <= cutoff and e["tick"][0] not in ("ask_hand", "ask_other")]
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


class MatchFeed:
    def __init__(self, pair):
        self.pair = pair
        self.streams = {}
        self._last_inputs = None
        self._refresh_lock = asyncio.Lock()
        self.last_poll = 0.0
        self.last_used = time.monotonic()
        self.complete = False

    async def refresh(self, now=None):
        async with self._refresh_lock:
            self.last_used = time.monotonic()
            if now is None and self.streams and self.last_used - self.last_poll < 0.25:
                return self.streams
            inputs = self._released_inputs(time.time() if now is None else now)
            if inputs != self._last_inputs:
                sources, snapshot, complete = inputs
                streams = await asyncio.to_thread(self._build_streams, sources, snapshot, complete,
                                                  self.streams, self.complete)
                self.streams, self.complete = streams, complete
                self._last_inputs = inputs
            self.last_poll = self.last_used
            return self.streams

    def _released_inputs(self, now):
        data = self.pair.data
        states = self.pair.states
        finals = data.get("final_records", {})
        previous_sources, previous_snapshot, _ = self._last_inputs or ({}, {}, False)
        complete = (self.pair.completed and all(t in states and t in finals and released_all(states[t], now)
                                              for t in ("open", "closed")))
        sources = {}
        for table, state in states.items():
            if (table not in ("open", "closed") or duplicate_spectator_blocked(state)
                    or too_many_ai_for_spectator(state.player_list)):
                continue
            previous = previous_sources.get(table, {}).get("record", {})
            payload = finals[table] if complete else visible_record(state, now, previous)
            if payload is not None:
                sources[table] = {"record": payload, "game_id": data.get("record_ids", {}).get(table),
                                  "players": data.get("final_players", {}).get(table) if complete else None}
        # PairedMatch replaces its persisted state on update. Capture only the
        # fields used by this view, excluding live scores and private pending actions.
        fields = ("id", "profile", "planned_boards", "board_instances", "boards", "sync_mode")
        snapshot = snapshot_fields({k: data[k] for k in fields if k in data}, previous_snapshot)
        snapshot["phase"] = "completed" if complete else ("interrupted" if data["status"] == "interrupted" else "running")
        return sources, snapshot, complete

    @staticmethod
    def _build_streams(sources, snapshot, complete, previous_streams, was_complete):
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
        return streams

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
            stream.publish(event, body)
        if not events and old != new:
            stream.publish({"kind": "table_available", "body": body}, body)


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

    def authorize(self, user_id, match_id, requested):
        manager = self.server.gamestate_manager
        pair = manager.sichuan_duplicate_matches.get(match_id)
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
                pair, allowed = self.authorize(sub.user_id, sub.feed.pair.data["id"], sub.tables)
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
