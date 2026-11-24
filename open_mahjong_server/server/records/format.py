"""V2 match container. Legacy action payloads stay lossless; metadata is verified."""
from copy import deepcopy
import hashlib
import json
import uuid
from decimal import Decimal

from .wall_alignment import RecordConflict, round_origins, validate_ref, ref_key

FORMAT = "open_mahjong.match_record"
VERSION = 2
NAMESPACE = uuid.UUID("c2c84d54-322a-4fb4-9ad0-54aee59b1ce3")
PAIRED_RULES = {"sichuan/doubles_duplicate", "sichuan/xueliu_doubles_duplicate"}

def canonical(value):
    # A shared decimal spelling avoids 1.0/1 and 1e-07/1e-7 discrepancies
    # between Python, Newtonsoft and browser JSON implementations.
    value = json.loads(json.dumps(value, ensure_ascii=False, allow_nan=False, default=str))
    def encode(item):
        if isinstance(item, str):
            return json.encoder.encode_basestring(item)
        if item is None:
            return "null"
        if isinstance(item, bool):
            return "true" if item else "false"
        if isinstance(item, (int, float)):
            if isinstance(item, int) or item.is_integer():
                return str(int(item))
            text = format(Decimal(format(item, ".17g")), "f")
            return (text.rstrip("0").rstrip(".") if "." in text else text) if item else "0"
        if isinstance(item, list):
            return "[" + ",".join(encode(v) for v in item) + "]"
        return "{" + ",".join(encode(k) + ":" + encode(item[k]) for k in sorted(item, key=lambda k: k.encode("utf-16-be"))) + "}"
    return encode(value)


def digest(value):
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest()


def identity(kind, *parts):
    return str(uuid.uuid5(NAMESPACE, canonical([kind, *parts])))


def match_identity(title, source_id):
    value = title.get("duplicate_match_id")
    if value:
        return str(value)
    return identity("single", source_id)


def ensure_board_instances(data):
    """Upgrade durable paired state, without changing existing board identities."""
    instances = data.setdefault("board_instances", [])
    for number in range(len(instances) + 1, len(data.get("boards", [])) + 1):
        instances.append({"board_instance_id": identity("board", data["id"], number, "initial"),
                          "board_no": number, "attempt": 1})
    data.setdefault("state_schema_version", 1)
    return instances


def board_from_record(title, rd, number):
    """Final archives contain original walls; a live header may contain only remainders."""
    from ..game_calculation.sichuan.duplicate_rules import board_from_tiles
    tiles = title.get("duplicate_round_tiles")
    flat = tiles[number - 1] if isinstance(tiles, list) and 0 < number <= len(tiles) else None
    if flat is None and number == 1:
        flat = title.get("duplicate_tiles")
    if flat is None:
        return None
    layout = rd.get("duplicate_wall_layout") or (title.get("duplicate_profile") or {}).get("wall_layout", "standard")
    return board_from_tiles(flat, number, layout=layout)


def clean_round(rd):
    """Only transport-only asks are removed. Persist a prefix map for old cursors."""
    result = deepcopy(rd)
    ticks, mapping, kept_times = [], [0], []
    times = rd.get("action_times_ms", [])
    for i, tick in enumerate(rd.get("action_ticks", [])):
        if not isinstance(tick, list) or not tick:
            raise RecordConflict("Invalid action tick")
        if tick[0] not in ("ask_hand", "ask_other"):
            ticks.append(deepcopy(tick))
            kept_times.append(times[i] if i < len(times) else None)
        mapping.append(len(ticks))
    result["action_ticks"] = ticks
    if "action_times_ms" in result:
        result["action_times_ms"] = kept_times
    return result, mapping


def preserve_source(table, source):
    """Keep only information omitted by the playback projection, including transport asks."""
    transport, fields = {}, {}
    for key, rd in source["game_round"].items():
        original = rd.get("action_ticks", [])
        times = rd.get("action_times_ms", [])
        asks = []
        for i, tick in enumerate(original):
            if tick[0] in ("ask_hand", "ask_other"):
                item = {"node": i + 1, "tick": deepcopy(tick)}
                if i < len(times):
                    item["time_ms"] = times[i]
                asks.append(item)
        if asks:
            transport[key] = asks
        extra = {}
        if "action_ticks" not in rd:
            extra["no_action_ticks"] = True
        if "action_times_ms" in rd and len(times) != len(original):
            extra["time_count"] = len(times)
            if len(times) > len(original):
                extra["time_tail"] = times[len(original):]
        if extra:
            fields[key] = extra
    if transport:
        table["transport_actions"] = transport
    if fields:
        table["source_round_fields"] = fields
    return table


def restore_source(table):
    """Reproduce the original per-table source without storing another JSON body."""
    source = deepcopy(table["payload"])
    for key in table.get("source_omitted_title_fields", []):
        source["game_title"].pop(key)
    for key, rd in source["game_round"].items():
        extra = table.get("source_round_fields", {}).get(key, {})
        for item in table.get("transport_actions", {}).get(key, []):
            i = item["node"] - 1
            if not 0 <= i <= len(rd["action_ticks"]):
                raise RecordConflict("Invalid transport action position")
            rd["action_ticks"].insert(i, deepcopy(item["tick"]))
            if "action_times_ms" in rd:
                rd["action_times_ms"].insert(i, item.get("time_ms"))
        if "time_count" in extra:
            rd["action_times_ms"] = (rd["action_times_ms"] + extra.get("time_tail", []))[:extra["time_count"]]
        if extra.get("no_action_ticks"):
            del rd["action_ticks"]
    return source


def public_players(players):
    """Favorites and notes belong to each player's library, never to a shared replay."""
    return [{k: v for k, v in player.items() if k not in ("note", "is_favorite")} for player in (players or [])]


def single_action_metadata(rd):
    metadata = [{"actor_original_seat": None, "occurred_at_ms": None,
                 "wall_ref": None, "wall_ref_basis": "not_applicable"} for _ in rd["action_ticks"]]
    for action, timestamp in zip(metadata, rd.get("action_times_ms", [])):
        action["occurred_at_ms"] = timestamp if type(timestamp) is int and timestamp >= 0 else None
    return metadata


def table_record(payload, table_id, match_id, *, source_id=None, players=None, complete=True,
                 catalog=None, reconstruct=False):
    if not isinstance(payload, dict) or not isinstance(payload.get("game_title"), dict):
        raise RecordConflict("Missing record title")
    if table_id not in ("main", "open", "closed"):
        raise RecordConflict("Invalid table identity")
    original = deepcopy(payload)
    payload = deepcopy(payload)
    title = payload["game_title"]
    paired = title.get("sub_rule") in PAIRED_RULES
    if paired and (title.get("duplicate_match_id") not in (None, match_id) or title.get("duplicate_table") not in (None, table_id)):
        raise RecordConflict("Record belongs to a different paired match/table")
    result = {"table_id": table_id, "table_record_id": source_id, "record_state": "complete" if complete else "partial",
              "payload_codec": "om.action_ticks.v1", "payload": payload,
              "players": deepcopy(public_players(players if players is not None else payload.get("players_settings", []))),
              "round_meta": {}, "source_sha256": digest(original)}
    rounds = payload.get("game_round")
    if not isinstance(rounds, dict):
        raise RecordConflict("Missing record rounds")
    for key, raw in list(rounds.items()):
        if not key.startswith("round_index_") or not isinstance(raw, dict):
            raise RecordConflict("Invalid round key")
        index = int(key.removeprefix("round_index_"))
        if index < 1 or (raw.get("round_index") is not None and raw["round_index"] != index):
            raise RecordConflict("Round index mismatch")
        rd, node_map = clean_round(raw)
        rounds[key] = rd
        number = rd.get("current_round", index)
        board_info = (catalog or {}).get(number)
        instance = rd.get("board_instance_id") or (board_info or {}).get("board_instance_id")
        if paired and not instance:
            # A migration caller must first establish the paired-board identity.
            instance = None
        if instance and board_info and instance != board_info["board_instance_id"]:
            raise RecordConflict("Board attempt mismatch")
        round_id = rd.get("table_round_id") or identity("round", match_id, table_id, instance or key)
        meta = {"table_round_id": round_id, "board_instance_id": instance, "round_index": index,
                "legacy_node_map": node_map, "initial_anchors": [], "action_meta": [],
                "alignment_status": "unavailable" if paired else "not_applicable", "alignment_reason": None}
        if paired and instance:
            layout = rd.get("duplicate_wall_layout") or (title.get("duplicate_profile") or {}).get("wall_layout", "standard")
            if title.get("sub_rule") == "sichuan/xueliu_doubles_duplicate":
                layout = "xueliu"
            board = (board_info or {}).get("wall_definition") or board_from_record(title, rd, number)
            meta.update(round_origins(rd, instance, layout=layout, reconstruct=reconstruct, board=board))
        elif paired:
            meta["action_meta"] = [{"actor_original_seat": None, "occurred_at_ms": None, "wall_ref": None,
                                    "wall_ref_basis": "unavailable"} for _ in rd["action_ticks"]]
            meta["alignment_reason"] = "board_instance_unverified"
        else:
            meta["action_meta"] = single_action_metadata(rd)
        if paired:
            for action, timestamp in zip(meta["action_meta"], rd.get("action_times_ms", [])):
                action["occurred_at_ms"] = timestamp if type(timestamp) is int and timestamp >= 0 else None
        result["round_meta"][key] = meta
    return preserve_source(result, original)


def resolve_sync_mode(values):
    recorded = [v for v in values if v is not None]
    if any(type(v) is not bool for v in recorded) or len(set(recorded)) > 1:
        raise RecordConflict("Paired synchronization modes disagree or are invalid")
    return recorded[0] if recorded else None


def build_match(records, *, match_id=None, snapshot=None, complete=True, reconstruct=False):
    """records maps table_id to {record, game_id, players}; absent siblings remain explicit."""
    if not records:
        raise RecordConflict("No table records")
    first = records.get("open") or records.get("main") or records[sorted(records)[0]]
    title = first["record"]["game_title"]
    match_id = match_id or match_identity(title, first.get("game_id") or digest(first["record"]))
    paired = title.get("sub_rule") in PAIRED_RULES
    expected = ("open", "closed") if paired else ("main",)
    sync_mode = resolve_sync_mode([
        (snapshot or {}).get("sync_mode"),
        *[entry["record"].get("game_title", {}).get("duplicate_sync_mode") for entry in records.values()],
        *[(entry["record"].get("duplicate_match") or {}).get("sync_mode") for entry in records.values()],
    ]) if paired else None
    if any(t not in expected for t in records):
        raise RecordConflict("Table set does not match format")
    catalog = {}
    if paired:
        if snapshot is not None and snapshot.get("id") != match_id:
            raise RecordConflict("Wrong paired snapshot")
        by_number = {}
        for table, entry in records.items():
            other_title = entry["record"].get("game_title", {})
            if other_title.get("sub_rule") != title.get("sub_rule") or other_title.get("rule") != title.get("rule"):
                raise RecordConflict("Paired rules disagree")
            if other_title.get("duplicate_profile") != title.get("duplicate_profile"):
                raise RecordConflict("Frozen paired rule profiles disagree")
            if other_title.get("duplicate_match_id") != match_id or other_title.get("duplicate_table") != table:
                raise RecordConflict("Paired record identity is missing or conflicting")
            for key, rd in entry["record"].get("game_round", {}).items():
                number = rd.get("current_round", rd.get("round_index"))
                if type(number) is not int or number < 1:
                    raise RecordConflict("Invalid paired board number")
                by_number.setdefault(number, []).append((table, other_title, rd))
        for number, entries in sorted(by_number.items()):
            instances = {rd["board_instance_id"] for _, _, rd in entries if rd.get("board_instance_id")}
            if len(instances) > 1:
                raise RecordConflict("Paired records refer to different board attempts")
            definitions = [board_from_record(t, rd, number) for _, t, rd in entries]
            definitions = [b for b in definitions if b is not None]
            if len({digest(b) for b in definitions}) > 1:
                raise RecordConflict("Paired original walls disagree")
            layout = "xueliu" if title["sub_rule"] == "sichuan/xueliu_doubles_duplicate" else "standard"
            snapshot_wall = None
            if snapshot and number <= len(snapshot.get("boards", [])):
                from ..game_calculation.sichuan.duplicate_rules import validate_board
                snapshot_wall = snapshot["boards"][number - 1]
                try:
                    validate_board(snapshot_wall)
                except ValueError as exc:
                    raise RecordConflict("Invalid original wall in paired snapshot") from exc
                if snapshot_wall["number"] != number or snapshot_wall.get("wall_layout", "standard") != layout:
                    raise RecordConflict("Paired snapshot board number or layout differs")
                if definitions and digest(snapshot_wall) != digest(definitions[0]):
                    raise RecordConflict("Paired snapshot and final record original walls disagree")
            descriptor = None
            attempts = {rd["board_attempt"] for _, _, rd in entries if rd.get("board_attempt") is not None}
            if len(attempts) > 1 or any(type(a) is not int or a < 1 for a in attempts):
                raise RecordConflict("Paired board attempt numbers disagree")
            if snapshot and number <= len(snapshot.get("board_instances", [])):
                descriptor = snapshot["board_instances"][number - 1]
                if instances and descriptor["board_instance_id"] not in instances:
                    raise RecordConflict("Snapshot and final record board attempts disagree")
                if attempts and descriptor["attempt"] not in attempts:
                    raise RecordConflict("Snapshot and final record attempt numbers disagree")
            if instances:
                instance = next(iter(instances))
            elif descriptor:
                instance = descriptor["board_instance_id"]
            elif len(entries) == 2 and len(definitions) == 2:
                instance = identity("legacy-effective-board", match_id, number)
            elif snapshot_wall is not None and definitions:
                instance = identity("legacy-effective-board", match_id, number)
            else:
                continue
            catalog[number] = {"board_instance_id": instance, "board_no": number,
                               "attempt": descriptor.get("attempt") if descriptor else next(iter(attempts), None),
                               "wall_layout": layout, "status": "paired" if complete and len(entries) == 2 else "playing",
                               "wall_definition": deepcopy(definitions[0] if definitions else snapshot_wall)}
    tables = {}
    for table in expected:
        if table not in records:
            tables[table] = {"table_id": table, "table_record_id": None, "record_state": "missing",
                             "payload": None, "round_meta": {}, "players": []}
        else:
            entry = records[table]
            tables[table] = table_record(entry["record"], table, match_id, source_id=entry.get("game_id"),
                                         players=entry.get("players"), complete=complete, catalog=catalog, reconstruct=reconstruct)
    teams = {}
    for table_id in expected:
        if table_id not in records:
            continue
        entry = records[table_id]
        table_title = entry["record"]["game_title"]
        for original_seat, team_id in enumerate(table_title.get("team_by_player_index", [])):
            if type(team_id) is not int or not 0 <= original_seat < 4:
                raise RecordConflict("Invalid team roster")
            teams.setdefault(team_id, {"team_id": team_id, "members": []})["members"].append({
                "table_id": table_id, "original_player_index": original_seat,
                "user_id": table_title.get(f"p{original_seat}_uid")})
    result = {"format": FORMAT, "schema_version": VERSION, "match_id": match_id, "record_revision": 1,
              "match_format": "paired_tables_2v2" if paired else "single_table",
              "completeness": "complete" if complete and set(records) == set(expected) else "partial",
              "rule": title.get("rule"), "sub_rule": title.get("sub_rule"),
              "rule_profile": deepcopy(title.get("duplicate_profile") or {"version": title.get("rule_version")}),
              "sync_mode": sync_mode, "teams": [teams[k] for k in sorted(teams)],
              "board_catalog": list(catalog.values()), "tables": tables,
              "settlements": deepcopy((first["record"].get("duplicate_match") or {}).get("boards", [])),
              "result": deepcopy(first["record"].get("doubles_result") or first["record"].get("match_result")),
              "provenance": {"converter": "match-record-v2.1", "sources": [
                  {"table_id": t, "game_id": e.get("game_id"), "sha256": digest(e["record"])} for t, e in sorted(records.items())]}}
    validate_match(result)
    return result


def validate_match(record):
    if record.get("format") != FORMAT or record.get("schema_version") != VERSION:
        raise RecordConflict("Unsupported match record version")
    if not isinstance(record.get("match_id"), str) or not record["match_id"]:
        raise RecordConflict("Missing match identity")
    if type(record.get("record_revision")) is not int or record["record_revision"] < 1:
        raise RecordConflict("Invalid record revision")
    expected = {"open", "closed"} if record.get("match_format") == "paired_tables_2v2" else {"main"}
    if set(record.get("tables", {})) != expected:
        raise RecordConflict("Invalid match table set")
    if record.get("match_format") == "paired_tables_2v2":
        resolve_sync_mode([
            record.get("sync_mode"),
            *[(table.get("payload") or {}).get("game_title", {}).get("duplicate_sync_mode") for table in record["tables"].values()],
            *[((table.get("payload") or {}).get("duplicate_match") or {}).get("sync_mode") for table in record["tables"].values()],
        ])
    catalog = {b["board_instance_id"]: b for b in record.get("board_catalog", [])}
    if len(catalog) != len(record.get("board_catalog", [])):
        raise RecordConflict("Duplicate board identities")
    for table_id, table in record["tables"].items():
        if table["table_id"] != table_id:
            raise RecordConflict("Table identity mismatch")
        if table.get("record_state") == "missing":
            if table.get("payload") is not None:
                raise RecordConflict("Missing table carries a payload")
            continue
        if table.get("record_state") not in ("complete", "partial") or table.get("payload_codec") != "om.action_ticks.v1":
            raise RecordConflict("Invalid table record state or codec")
        rounds = table["payload"]["game_round"]
        if set(rounds) != set(table["round_meta"]):
            raise RecordConflict("Round metadata mismatch")
        instances, round_ids = set(), set()
        for key, rd in rounds.items():
            meta = table["round_meta"][key]
            if meta["table_round_id"] in round_ids:
                raise RecordConflict("Duplicate table round identity")
            round_ids.add(meta["table_round_id"])
            instance = meta.get("board_instance_id")
            if instance:
                if instance in instances:
                    raise RecordConflict("One board instance appears in multiple table rounds")
                instances.add(instance)
            if len(meta["action_meta"]) != len(rd["action_ticks"]):
                raise RecordConflict("Action metadata length mismatch")
            seen = set()
            for action in [*meta.get("initial_anchors", []), *meta["action_meta"]]:
                ref = action.get("wall_ref")
                if ref:
                    if ref["board_instance_id"] != meta.get("board_instance_id") or ref["board_instance_id"] not in catalog:
                        raise RecordConflict("Unbound wall origin")
                    validate_ref(ref, catalog[ref["board_instance_id"]]["wall_layout"])
                    k = ref_key(ref)
                    if k in seen:
                        raise RecordConflict("Duplicate physical origin")
                    seen.add(k)
    return record
