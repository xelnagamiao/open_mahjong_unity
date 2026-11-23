"""Resolve archived checkpoint references without reviving a room or writing any data."""
from copy import deepcopy
from .format import restore_source
from .wall_alignment import RecordConflict


def source_rounds(cursor, match_id, ids):
    cursor.execute("""SELECT t.game_id,t.table_id,r.record->'tables'->t.table_id
        FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id
        WHERE t.game_id=ANY(%s) AND r.record->>'match_id'=%s""", (list(ids.values()),str(match_id)))
    return {sid: restore_source(table)["game_round"] for sid, side, table in cursor.fetchall()
            if ids.get(side) == sid}


def resolve_archive(cursor, match_id, snapshot):
    data = deepcopy(snapshot)
    refs = data.pop("round_sources", {})
    if not refs:
        return data
    rounds = source_rounds(cursor, match_id, refs)
    if set(rounds) != set(refs.values()):
        raise RecordConflict("Archived checkpoint references another match or a missing table")
    for side, sid in refs.items():
        records = [(data.get("final_records") or {}).get(side)]
        for group in ("checkpoints","table_checkpoints","pending_checkpoints"):
            records.append((((data.get(group) or {}).get(side)) or {}).get("game_record"))
        for record in records:
            if record is not None and "round_source_game_id" in record:
                if record.pop("round_source_game_id") != sid or "game_round" in record:
                    raise RecordConflict("Conflicting archived checkpoint source")
                record["game_round"] = deepcopy(rounds[sid])
    return data
