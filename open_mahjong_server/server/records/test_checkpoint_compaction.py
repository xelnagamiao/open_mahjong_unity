from copy import deepcopy
from uuid import uuid4

from psycopg2.extras import Json

from ..database.sichuan_duplicate import compact_completed_replays, _load_checkpoint_rounds
from .test_format import records
from .test_store import db, publish


def test_only_identical_finished_timelines_are_replaced_and_recoverable(db):
    mid = str(uuid4())
    sources = records()
    for side, entry in sources.items():
        entry["record"]["game_title"]["duplicate_match_id"] = mid
        publish(db, side, entry)
    data = {"record_ids": {k: v["game_id"] for k, v in sources.items()},
            "final_records": {k: v["record"] for k, v in sources.items()},
            "checkpoints": {k: {"game_record": deepcopy(v["record"])} for k, v in sources.items()}}
    data["checkpoints"]["open"]["game_record"]["game_round"]["round_index_1"]["extra"] = "checkpoint-only"
    conn = db._get_connection()
    try:
        with conn, conn.cursor() as cur:
            cur.execute("CREATE TABLE sichuan_duplicate_matches(id uuid PRIMARY KEY, status text, data jsonb)")
            cur.execute("INSERT INTO sichuan_duplicate_matches VALUES (%s,'running',%s)", (mid,Json(data)))
            assert compact_completed_replays(cur) == 0
            cur.execute("UPDATE sichuan_duplicate_matches SET status='completed'")
            assert compact_completed_replays(cur) == 1
            assert compact_completed_replays(cur) == 0
            cur.execute("SELECT data FROM sichuan_duplicate_matches WHERE id=%s", (mid,))
            stored, = cur.fetchone()
            assert stored["round_sources"] == data["record_ids"]
            assert "game_round" not in stored["final_records"]["open"]
            assert "game_round" not in stored["checkpoints"]["closed"]["game_record"]
            assert stored["checkpoints"]["open"] == data["checkpoints"]["open"]
            assert _load_checkpoint_rounds(cur, stored) == data
    finally:
        db._put_connection(conn)
