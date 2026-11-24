"""PostgreSQL transaction tests; every case owns an isolated temporary schema."""
from concurrent.futures import ThreadPoolExecutor
from copy import deepcopy
import os
from types import SimpleNamespace
from uuid import uuid4

import psycopg2
from psycopg2 import sql
from psycopg2.extras import Json
from psycopg2.pool import ThreadedConnectionPool
import pytest

from .format import build_match, digest, restore_source
from .store import ensure_tables, paired_game_id, publish_table, read_record, list_replays
from .test_format import records
from .wall_alignment import RecordConflict


@pytest.fixture
def db():
    dsn = os.getenv("OM_RECENT_TEST_DSN")
    if not dsn:
        pytest.skip("OM_RECENT_TEST_DSN is required; tests only write to their own schema")
    schema = "match_v2_test_" + uuid4().hex
    admin = psycopg2.connect(dsn)
    admin.autocommit = True
    with admin.cursor() as cur:
        cur.execute(sql.SQL("CREATE SCHEMA {}").format(sql.Identifier(schema)))
    pool = ThreadedConnectionPool(1, 8, dsn=dsn, options=f"-c search_path={schema}")
    database = SimpleNamespace(_get_connection=pool.getconn, _put_connection=pool.putconn)
    conn = pool.getconn()
    with conn, conn.cursor() as cur:
        ensure_tables(cur)
        cur.execute("""CREATE TABLE game_player_records(game_id text REFERENCES game_record_tables(game_id) ON DELETE CASCADE,
                    user_id bigint, username text, score int DEFAULT 0, rank int DEFAULT 1, original_player_index int DEFAULT 0,
                    title_used int,character_used int,profile_used int,voice_used int,avatar_frame_used int,match_type text,
                    room_type text,rule text,sub_rule text,is_favorite boolean DEFAULT FALSE,note text,event_id text)""")
    pool.putconn(conn)
    try:
        yield database
    finally:
        pool.closeall()
        with admin.cursor() as cur:
            cur.execute(sql.SQL("DROP SCHEMA {} CASCADE").format(sql.Identifier(schema)))
        admin.close()


def query(db, statement, args=(), one=False):
    conn = db._get_connection()
    try:
        with conn, conn.cursor() as cur:
            cur.execute(statement, args)
            if cur.description:
                return cur.fetchone() if one else cur.fetchall()
    finally:
        db._put_connection(conn)


def publish(db, table, entry):
    conn = db._get_connection()
    try:
        with conn, conn.cursor() as cur:
            return publish_table(cur, entry["game_id"], entry["record"], entry.get("players", []))
    finally:
        db._put_connection(conn)


def test_single_stored_compact_reads_as_public_v2_with_original_cursor_map(db, monkeypatch):
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda *args: True)
    payload = {"game_title": {"rule": "guobiao"}, "game_round": {"round_index_1": {
        "action_ticks": [["d", 11], ["c", 11, "T"]], "action_times_ms": [0, 90]}}}
    entry = {"game_id": "solo", "record": payload}
    expected = build_match({"main": entry})
    assert publish(db, "main", entry) and not publish(db, "main", entry)
    stored = query(db, "SELECT record FROM game_replays WHERE game_id='solo'", one=True)[0]
    physical = stored["tables"]["main"]["round_meta"]["round_index_1"]
    assert "legacy_node_map" not in physical and "action_meta" not in physical
    result = read_record(db, "solo")
    assert result["record"] == expected
    assert result["legacy_cursor_map"]["node_map"] == {"round_index_1": [0, 1, 2]}
    query(db, "UPDATE game_replays SET record=%s WHERE game_id='solo'", (Json(expected),))
    assert read_record(db, "solo") == result


def test_concurrent_tables_and_retries_publish_one_body(db, monkeypatch):
    from ..database.duplicate_walls import DuplicateRecordLockedError
    source = records()
    for side in source:
        source[side]["players"] = [{"user_id": i + (100 if side == "open" else 200), "username": str(i)} for i in range(4)]
    original = source["open"]["record"]["game_round"]["round_index_1"]
    original["action_ticks"].insert(0, ["ask_hand", 1, "c"])
    original["action_times_ms"] = [None, 10]
    with ThreadPoolExecutor(2) as executor:
        assert all(executor.map(lambda pair: publish(db, *pair), source.items()))
    for side, entry in source.items():
        assert not publish(db, side, entry)
        assert restore_source(query(db, "SELECT r.record->'tables'->t.table_id FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id WHERE t.game_id=%s", (entry["game_id"],), True)[0]) == entry["record"]
    assert query(db, "SELECT count(*) FROM game_replays", one=True) == (1,)
    assert query(db, "SELECT count(*) FROM game_record_tables", one=True) == (2,)
    public_id = paired_game_id("test-match")
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda *a: True)
    alias = read_record(db, "idclosed")
    direct = read_record(db, public_id)
    assert alias["game_id"] == direct["game_id"] == public_id
    assert alias["selected_table_id"] == "closed" and direct["selected_table_id"] == "open"
    assert direct["record"]["completeness"] == "complete" and len(alias["record"]["tables"]["closed"]["players"]) == 4
    assert read_record(db, "idopen")["legacy_cursor_map"]["node_map"]["round_index_1"] == [0, 0, 1, 2]
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda db, data, gid: gid == "idclosed")
    with pytest.raises(DuplicateRecordLockedError):
        read_record(db, public_id)  # The visible side also embeds the shared original wall.
    with pytest.raises(DuplicateRecordLockedError):
        read_record(db, "idopen")
    query(db, "DELETE FROM game_replays WHERE game_id=%s", (public_id,))
    assert all(read_record(db, gid) is None for gid in (public_id, "idopen", "idclosed"))


def test_safe_partial_read_and_inconsistent_historical_favorites(db, monkeypatch):
    from ..database.db_manager import DatabaseManager
    from ..database.duplicate_walls import DuplicateRecordLockedError
    source = records()
    uid = 101
    for side, entry in source.items():
        del entry["record"]["game_title"]["duplicate_round_tiles"]
        entry["players"] = [{"user_id":uid,"username":"player","rank":1,"score":0}]
        assert publish(db,side,entry)
        query(db,"INSERT INTO game_player_records(game_id,user_id,username,is_favorite) VALUES(%s,%s,%s,%s)",
              (entry["game_id"],uid,side + "-name",side == "open"))
    public_id = paired_game_id("test-match")
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda *args: True)
    for response in (read_record(db,public_id),list_replays(db,user_id=uid)[0]):
        assert {p["table_id"]:p["username"] for p in response["players"]} == {"open":"open-name","closed":"closed-name"}
    db.MAX_RECORD_FAVORITES = 1
    assert DatabaseManager.set_record_favorite(db, uid, public_id, True)["success"]
    assert query(db,"SELECT bool_and(is_favorite) FROM game_player_records",one=True) == (True,)
    assert DatabaseManager.set_record_favorite(db, uid, "idclosed", False)["success"]
    assert query(db,"SELECT bool_or(is_favorite) FROM game_player_records",one=True) == (False,)
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda db, data, gid: gid=="idclosed")
    redacted = read_record(db,public_id)
    assert redacted["selected_table_id"]=="closed" and redacted["record"]["tables"]["open"]["payload"] is None
    assert not redacted["record"]["provenance"] and all("wall_definition" not in b for b in redacted["record"]["board_catalog"])
    with pytest.raises(DuplicateRecordLockedError):
        read_record(db,"idopen")


def test_rename_between_settlements_and_canonical_list(db, monkeypatch):
    source = records()
    for side, uid in (("closed", 201), ("open", 101)):
        entry = source[side]
        entry["players"] = [{"user_id": uid, "username": "old", "rank": 1, "score": 0}]
        entry["record"]["game_title"].update(p0_uid=uid, p0_name="old")
        assert publish(db, side, entry)
        query(db, "INSERT INTO game_player_records(game_id,user_id,username,event_id,is_favorite,note) VALUES(%s,%s,%s,'evt',true,'kept')",
              (entry["game_id"],uid,"new"))
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda *a: True)
    canonical_id = paired_game_id("test-match")
    from ..database.db_manager import DatabaseManager
    db.MAX_RECORD_NOTE_LENGTH = DatabaseManager.MAX_RECORD_NOTE_LENGTH
    assert DatabaseManager.set_record_note(db,201,canonical_id,"new-note")["success"]
    detail = read_record(db, canonical_id)
    closed = detail["record"]["tables"]["closed"]
    assert closed["players"][0]["username"] == "new"
    assert closed["payload"]["game_title"]["p0_name"] == "old"
    assert query(db,"SELECT record #>> '{tables,closed,payload,game_title,p0_name}' FROM game_replays",one=True) == ("old",)
    rows = list_replays(db,event_id="evt")
    assert len(rows) == 1 and rows[0]["game_id"] == canonical_id
    assert rows[0]["match_summary"]["tables"]["closed"]["players"][0]["username"] == "new"
    assert {(p["table_id"],p["user_id"]) for p in rows[0]["players"]} == {("open",101),("closed",201)}
    result = list_replays(db,user_id=201,favorites_only=True)
    assert len(result) == 1 and result[0]["is_favorite"] and result[0]["note"] == "new-note"


def test_archived_private_player_fields_never_leave_the_owners_list(db, monkeypatch):
    from .store import summarize_match
    source = records()
    for seat,(side,entry) in enumerate(source.items()):
        uid = 100 + seat
        entry["players"] = [{"user_id":uid, "username":side, "note":"from settlement", "is_favorite":True}]
        assert publish(db,side,entry)
        query(db,"INSERT INTO game_player_records(game_id,user_id,username,event_id,note,is_favorite) VALUES(%s,%s,%s,'competition',%s,%s)",
              (entry["game_id"],uid,side,"my library" if seat == 0 else "other library",seat == 0))
    physical, summary = query(db,"SELECT record,summary FROM game_replays",one=True)
    for side in source:
        assert physical["tables"][side]["players"] == [{"user_id":100 + int(side=="closed"), "username":side}]
        # A previously stored archive or a stale pre-fix summary may still contain both fields.
        for old in (physical,summary):
            old["tables"][side]["players"][0].update(note="old private note",is_favorite=True)
    query(db,"UPDATE game_replays SET record=%s,summary=%s",(Json(physical),Json(summary)))
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible",lambda *args:True)
    event = list_replays(db,event_id="competition")[0]
    own = list_replays(db,user_id=100)[0]
    other = list_replays(db,user_id=101)[0]
    assert (own["note"],own["is_favorite"]) == ("my library",True)
    assert (other["note"],other["is_favorite"]) == ("other library",False)
    assert "note" not in event and "is_favorite" not in event
    buffers=[summarize_match(physical), event["match_summary"], own["match_summary"]]
    rosters=[event["players"],own["players"],other["players"]]
    for key in ("idopen", "idclosed", paired_game_id("test-match")):
        detail = read_record(db,key)
        buffers.append(detail["record"])
        rosters.append(detail["players"])
    for buf in buffers:
        rosters.extend(t["players"] for t in buf["tables"].values())
    assert all("note" not in p and "is_favorite" not in p for roster in rosters for p in roster)
    assert query(db,"SELECT record,summary FROM game_replays",one=True) == (physical,summary)


def test_conflicting_retries_and_failed_transactions_do_not_leave_partial_data(db):
    entry = records()["open"]
    conn = db._get_connection()
    try:
        with pytest.raises(RuntimeError), conn, conn.cursor() as cursor:
            publish_table(cursor, entry["game_id"], entry["record"], [])
            raise RuntimeError("rolled back")
    finally:
        db._put_connection(conn)
    assert query(db,"SELECT count(*) FROM game_replays",one=True) == (0,)
    assert publish(db,"open",entry)
    changed = deepcopy(entry)
    changed["record"]["another"] = 3
    with pytest.raises(RecordConflict):
        publish(db, "open", changed)
    assert restore_source(query(db,"SELECT r.record->'tables'->t.table_id FROM game_record_tables t JOIN game_replays r ON r.game_id=t.replay_id WHERE t.game_id=%s",(entry["game_id"],),True)[0]) == entry["record"]

def test_wire_preserves_source_nulls_and_all_historical_seats_without_inventing_scores(db, monkeypatch):
    import asyncio
    from unittest.mock import AsyncMock
    from ..database.data_router import handle_get_record_by_id
    source = records()
    for side, entry in source.items():
        entry["record"]["extension_for_future_rule"] = {"missing": None, "clock": [None, 27]}
        start = 100 if side == "open" else 200
        entry["players"] = [{"user_id": start+i, "username":"name", "original_player_index":i} for i in range(4)]
        assert publish(db, side, entry)
        query(db, """INSERT INTO game_player_records(game_id,user_id,room_type,match_type)
                     VALUES(%s,%s,'events','normal')""", (entry["game_id"], start))
    monkeypatch.setattr("server.database.duplicate_walls.duplicate_record_is_visible", lambda *args: True)
    canonical_id = paired_game_id("test-match")
    server = SimpleNamespace(players={"c":SimpleNamespace(user_id=100)},
                             db_manager=SimpleNamespace(get_record_by_id=lambda gid:read_record(db,gid)))
    socket = AsyncMock()
    asyncio.run(handle_get_record_by_id(server,"c",{"game_id":canonical_id},socket))
    reply = socket.send_json.await_args.args[0]["record_detail"]
    assert reply["room_type"] == "events" and reply["match_type"] == "normal"
    assert len(reply["players"]) == 8 and all("rank" not in p and "score" not in p for p in reply["players"])
    card = list_replays(db, user_id=100)[0]
    assert len(card["players"]) == 8 and {p["table_id"] for p in card["players"]} == {"open", "closed"}
    assert reply["record"]["tables"]["open"]["payload"]["extension_for_future_rule"] == {"missing":None,"clock":[None,27]}

def test_concurrent_favorites_share_one_limit(db):
    from ..database.db_manager import DatabaseManager
    ids = ["first", "second"]
    user = 101
    db.MAX_RECORD_FAVORITES = 1
    for gid in ids:
        entry = {"game_id":gid, "record":{"game_title":{"rule":"guobiao"},"game_round":{}}}
        publish(db, "main", entry)
        query(db,"INSERT INTO game_player_records(game_id,user_id) VALUES(%s,%s)",(gid,user))
    with ThreadPoolExecutor(2) as workers:
        results = list(workers.map(lambda gid: DatabaseManager.set_record_favorite(db,user,gid,True), ids))
    assert sum(r["success"] for r in results) == 1
    assert query(db,"SELECT count(*) FROM game_player_records WHERE is_favorite",one=True) == (1,)
