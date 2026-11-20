import asyncio
import os
from contextlib import closing
from functools import partial
from types import SimpleNamespace
from unittest.mock import AsyncMock

import psycopg2
from psycopg2.extras import Json
import pytest

from server.database.db_manager import DatabaseManager
from server.database.data_router import handle_get_record_list


@pytest.fixture
def records_db():
    dsn = os.getenv("RECORD_METADATA_TEST_DATABASE_URL")
    if not dsn:
        pytest.skip("Requires a PostgreSQL test connection")
    with closing(psycopg2.connect(dsn)) as conn:
        with conn.cursor() as cursor:
            cursor.execute("""
                CREATE TEMP TABLE game_replays(game_id text PRIMARY KEY, record jsonb, summary jsonb, created_at timestamp DEFAULT now());
                CREATE TEMP TABLE game_record_tables(game_id text PRIMARY KEY,replay_id text,table_id text);
                CREATE TEMP TABLE game_player_records(game_id text, user_id int, username text, score int, rank int,
                    original_player_index int, rule text, sub_rule text, match_type text, room_type text, title_used int,
                    character_used int, profile_used int, avatar_frame_used int, voice_used int, event_id text,
                    is_favorite boolean, note text);
            """)
            cases = [
                ("indexed", "riichi", "riichi/sanma", {"rule": "old", "sub_rule": "old/old"}, False),
                ("legacy", "", None, {"rule": "sichuan", "sub_rule": "sichuan/xuezhan_exchange"}, True),
                ("partial", "guangdong", "  ", {"sub_rule": "guangdong/mil2023-doubles"}, True),
                ("unknown", "future", "future/variant", {}, False),
                ("absent", "", None, {}, False),
            ]
            for game_id, rule, sub_rule, title, favorite in cases:
                cursor.execute("INSERT INTO game_replays(game_id, record) VALUES (%s, %s)",
                               (game_id, Json(title)))
                cursor.execute("INSERT INTO game_record_tables VALUES(%s,%s,'main')",(game_id,game_id))
                cursor.execute("""
                    INSERT INTO game_player_records(game_id, user_id, username, score, rank, original_player_index,
                        rule, sub_rule, match_type, room_type, event_id, is_favorite)
                    SELECT %s, seat, 'player' || seat, 0, seat, seat - 1, %s, %s, '1/4', 'events', 'test', %s
                    FROM generate_series(1, 4) seat
                """, (game_id, rule, sub_rule, favorite))
        conn.commit()
        yield SimpleNamespace(_get_connection=lambda: conn, _put_connection=lambda _: None)


@pytest.mark.parametrize("view", ["overview", "favorites", "event"])
def test_record_card_rule_fields(records_db, view):
    if view == "event":
        records = DatabaseManager.list_event_records(records_db, "test")
    else:
        records = DatabaseManager.get_record_list(records_db, 1, favorites_only=view == "favorites")
    expected = {
        "legacy": ("sichuan", "sichuan/xuezhan_exchange"),
        "partial": ("guangdong", "guangdong/mil2023-doubles"),
    }
    if view != "favorites":
        expected.update(indexed=("riichi", "riichi/sanma"), unknown=("future", "future/variant"), absent=("", None))
    assert {row["game_id"]: (row["rule"], row["sub_rule"]) for row in records} == expected
    assert all(len(row["players"]) == 4 for row in records)
    with records_db._get_connection().cursor() as cursor:
        cursor.execute("SELECT rule, sub_rule FROM game_player_records WHERE game_id='legacy' AND user_id=1")
        assert cursor.fetchone() == ("", None)


def test_missing_rule_does_not_drop_other_rows_from_wire_response(records_db):
    records_db.get_record_list = partial(DatabaseManager.get_record_list, records_db)
    server = SimpleNamespace(players={"conn": SimpleNamespace(user_id=1)}, db_manager=records_db)
    websocket = AsyncMock()
    asyncio.run(handle_get_record_list(server, "conn", {}, websocket))
    reply = websocket.send_json.await_args.args[0]
    assert reply["success"]
    records = {row["game_id"]: row for row in reply["record_list"]}
    assert len(records) == 5
    assert records["absent"]["rule"] == "" and "sub_rule" not in records["absent"]
    assert records["legacy"]["sub_rule"] == "sichuan/xuezhan_exchange"
