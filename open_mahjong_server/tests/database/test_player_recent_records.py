"""纯牌谱测试 + 可选独立 PostgreSQL 集成测试（OM_RECENT_TEST_DSN）。"""
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
from copy import deepcopy
from datetime import datetime, timedelta
import importlib
import os
from types import SimpleNamespace
from uuid import uuid4

import psycopg2
from psycopg2.extras import Json
from psycopg2.pool import ThreadedConnectionPool
import pytest

from server.database import player_recent_records as recent
from server.gamestate.public.guobiao_win_snapshot import (
    capture_guobiao_win, restore_guobiao_best_wins, SNAPSHOT_VERSION,
)


HAND = [11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 21, 21, 22]
UIDS = [10000001, 10000002, 10000003, 10000004]


def replay(rule="guobiao", category="custom", fan=24, day=0, ticks=None, hands=None, seats=None):
    return {
        "game_title": {
            "rule": rule, "sub_rule": rule + "/standard", "room_type": category,
            "end_time": (datetime(2026, 1, 1) + timedelta(days=day)).isoformat(),
            **{f"p{i}_uid": uid for i, uid in enumerate(UIDS)},
        },
        "game_round": {"round_index_1": {
            "round_index": 1, "seats": seats or [0, 1, 2, 3], "start_player_index": 0,
            **{f"p{i}_tiles": list((hands or {}).get(i, HAND)) for i in range(4)},
            "action_ticks": ticks if ticks is not None else [
                ["d", 22], ["hu_self", 0, fan, ["清一色", "自摸"], [0, 0, 0, 0], 22], ["end"],
            ],
        }},
    }


def extract(record, uid=UIDS[0]):
    best, errors = restore_guobiao_best_wins(record)
    assert errors == []
    return best[str(uid)]


def test_restore_self_draw_seat_rotation_and_actual_total():
    win = extract(replay(fan=29, seats=[2, 0, 3, 1]), UIDS[1])
    assert win["total_fan"] == 29
    assert win["fan_name"] == "清一色"
    assert win["concealed_tiles"] == sorted(HAND + [22])


@pytest.mark.parametrize("explicit", [True, False])
def test_restore_ron_tile_once(explicit):
    tick = ["hu_first", 1, 12, ["平和"], [0] * 4] + ([22] if explicit else [])
    win = extract(replay(ticks=[["d", 22], ["c", 22, "T"], tick]), UIDS[1])
    assert win["concealed_tiles"].count(22) == 2
    assert len(win["concealed_tiles"]) == 14


@pytest.mark.parametrize("action,tile,removed", [("cl", 13, [11, 12]), ("cm", 12, [11, 13]), ("cr", 11, [12, 13])])
@pytest.mark.parametrize("explicit", [True, False])
def test_restore_legacy_and_current_chi(action, tile, removed, explicit):
    claim = [action, tile, 1] + (removed if explicit else [])
    win = extract(replay(ticks=[
        ["d", tile], ["c", tile, "T"], claim, ["c", 22, "F"], ["reset", 1],
        ["d", 11 if tile == 11 else removed[0]],
        ["hu_self", 1, 8, ["平和"], [0] * 4],
    ]), UIDS[1])
    assert win["melds"] == [[11, 12, 13]]
    assert len(win["concealed_tiles"]) == 11


def test_restore_flowers_ankan_and_supplement_draw():
    hand = [11] * 4 + [12, 13, 14, 15, 16, 17, 21, 21, 51]
    win = extract(replay(hands={0: hand}, ticks=[
        ["bh", 51, 0], ["bd", 22, 0], ["d", 23], ["ag", 11, "F"], ["gd", 22],
        ["hu_self", 0, 18, ["花牌*1", "暗杠"], [0] * 4, 22],
    ]))
    assert win["flower_tiles"] == [51]
    assert win["melds"] == [[11] * 4]
    assert len(win["concealed_tiles"]) == 11


def test_restore_open_kan_and_added_kan():
    hand = [11, 11, 11, 12, 13, 14, 15, 16, 17, 21, 21, 22, 22]
    win = extract(replay(hands={1: hand}, ticks=[
        ["d", 11], ["c", 11, "T"], ["g", 11, 1], ["gd", 23],
        ["hu_self", 1, 8, ["明杠"], [0] * 4, 23],
    ]), UIDS[1])
    assert win["melds"] == [[11] * 4]
    hand = [11, 11, 12, 13, 14, 15, 16, 17, 21, 21, 22, 22, 23]
    win = extract(replay(hands={1: hand}, ticks=[
        ["d", 11], ["c", 11, "T"], ["p", 11, 1], ["c", 23, "F"],
        ["reset", 1], ["d", 11], ["jg", 11, "T"], ["gd", 23],
        ["hu_self", 1, 8, ["明杠"], [0] * 4, 23],
    ]), UIDS[1])
    assert win["melds"] == [[11] * 4]


def test_restore_robbing_kan_uses_added_tile_not_last_discard():
    hand = [11, 11, 12, 13, 14, 15, 16, 17, 21, 21, 22, 22, 23]
    win = extract(replay(hands={1: hand}, ticks=[
        ["d", 11], ["c", 11, "T"], ["p", 11, 1], ["c", 23, "F"],
        ["reset", 1], ["d", 11], ["jg", 11, "T"],
        ["hu_first", 2, 8, ["抢杠和"], [0] * 4],
    ]), UIDS[2])
    assert win["winning_tile"] == 11


def test_cuohe_does_not_mutate_hand_or_count_as_win():
    win = extract(replay(ticks=[
        ["d", 22], ["hu_self", 0, 999, ["错和"], [0] * 4],
        ["c", 22, "T"], ["reset", 0], ["d", 22],
        ["hu_self", 0, 8, ["平和"], [0] * 4, 22],
    ]))
    assert win["total_fan"] == 8
    assert len(win["concealed_tiles"]) == 14


def test_all_rounds_and_later_equal_fan_win():
    record = replay(fan=88)
    second = deepcopy(record["game_round"]["round_index_1"])
    second["round_index"] = 2
    record["game_round"]["round_index_2"] = second
    assert extract(record)["round_index"] == 2
    second["action_ticks"][1][2] = 8
    assert extract(record)["round_index"] == 1


def test_live_capture_is_deep_copy_and_matches_restore():
    record = replay()
    expected = extract(record)
    record.update(player_best_wins_version=SNAPSHOT_VERSION, player_best_wins={})
    player = SimpleNamespace(user_id=UIDS[0], hand_tiles=HAND + [22], combination_mask=[], huapai_list=[])
    state = SimpleNamespace(game_record=record, player_list=[player], round_index=1, player_action_tick=2)
    capture_guobiao_win(state, "hu_self", 24, ["清一色", "自摸"], 0, 22)
    player.hand_tiles.clear()
    assert extract(record) == expected


def test_broken_round_reported_and_other_rounds_still_restored():
    record = replay()
    broken = deepcopy(record["game_round"]["round_index_1"])
    broken["action_ticks"].insert(0, ["c", 47, "F"])
    record["game_round"]["round_index_2"] = broken
    wins, errors = restore_guobiao_best_wins(record)
    assert wins[str(UIDS[0])]["total_fan"] == 24
    assert len(errors) == 1 and "round_index_2" in errors[0]


@pytest.fixture
def database():
    dsn = os.environ.get("OM_RECENT_TEST_DSN")
    if not dsn:
        pytest.skip("设置 OM_RECENT_TEST_DSN 才运行独立 PostgreSQL 集成测试")
    schema = "recent_test_" + uuid4().hex
    admin = psycopg2.connect(dsn)
    admin.autocommit = True
    with admin.cursor() as cursor:
        cursor.execute(f'CREATE SCHEMA "{schema}"')
    pool = ThreadedConnectionPool(1, 8, dsn=dsn, options=f"-c search_path={schema}")
    manager = SimpleNamespace(_get_connection=pool.getconn, _put_connection=pool.putconn)
    with connection(manager) as conn:
        with conn.cursor() as cursor:
            cursor.execute("""
                CREATE TABLE game_records (game_id VARCHAR(16) PRIMARY KEY, record JSONB NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP);
                CREATE TABLE game_player_records (
                    game_id VARCHAR(16) REFERENCES game_records(game_id), user_id BIGINT,
                    username TEXT, score INT, rank INT CHECK (rank BETWEEN 1 AND 4),
                    original_player_index INT, rule VARCHAR(10), sub_rule TEXT, match_type TEXT,
                    room_type TEXT, match_tier TEXT, event_id TEXT, title_used INT,
                    character_used INT, profile_used INT, voice_used INT,
                    PRIMARY KEY (game_id, user_id));
            """)
    yield manager
    pool.closeall()
    with admin.cursor() as cursor:
        cursor.execute(f'DROP SCHEMA "{schema}" CASCADE')
    admin.close()


@contextmanager
def connection(manager):
    conn = manager._get_connection()
    try:
        with conn:
            yield conn
    finally:
        manager._put_connection(conn)


def insert_game(manager, game_id, record, rank=1, update=False):
    with connection(manager) as conn, conn.cursor() as cursor:
        cursor.execute("INSERT INTO game_records (game_id, record) VALUES (%s, %s)", (game_id, Json(record)))
        title = record["game_title"]
        for i in range(4):
            cursor.execute("""
                INSERT INTO game_player_records (game_id, user_id, rule, room_type, rank, match_type)
                VALUES (%s, %s, %s, %s, %s, '1/4')
            """, (game_id, title[f"p{i}_uid"], title["rule"], title["room_type"], rank))
        if update:
            recent.update_player_recent_records(cursor, game_id, record)


def test_backfill_all_rules_scopes_ten_games_and_lifetime_best(database):
    for r, rule in enumerate(recent.RULES):
        for c, category in enumerate(recent.CATEGORIES):
            for day in range(12):
                insert_game(database, f"{r}{c}{day:02}", replay(rule, category, day=day, fan=88 if day == 0 else 8), rank=day % 4 + 1)
    for game_id, rule, category in [("free", "free", "custom"), ("events", "guobiao", "events")]:
        insert_game(database, game_id, replay(rule, category, fan=999))
    bot_record = replay(fan=999)
    bot_record["game_title"]["p3_uid"] = 1
    insert_game(database, "robot", bot_record)
    result = recent.backfill_player_recent_records(database, batch_size=17)
    assert result["complete"]
    assert result["processed"] == 219
    for uid in UIDS:
        data = recent.get_player_recent_records(database, uid)
        assert set(data) == set(recent.RULES)
        for rule in recent.RULES:
            for category in recent.CATEGORIES:
                values = data[rule][category]
                assert [entry["rank"] for entry in values["placements"]] == [i % 4 + 1 for i in range(2, 12)]
                if rule == "guobiao" and uid == UIDS[0]:
                    assert values["big_win"]["total_fan"] == 88
                else:
                    assert values["big_win"] is None
    assert recent.backfill_player_recent_records(database)["processed"] == 0


def test_lifetime_record_survives_more_than_thirty_games_and_updates(database):
    recent.backfill_player_recent_records(database)
    for day in range(42):
        insert_game(database, f"game{day:03}", replay(day=day, fan=88 if day == 0 else 8), update=True)
    data = recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]
    assert data["big_win"]["game_id"] == "game000"
    assert len(data["placements"]) == 10
    insert_game(database, "newbest", replay(day=44, fan=99), update=True)
    with connection(database) as conn, conn.cursor() as cursor:
        recent.update_player_recent_records(cursor, "newbest")
    data = recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]
    assert data["big_win"]["total_fan"] == 99
    assert len(data["placements"]) == 10
    assert [entry["game_id"] for entry in data["placements"]].count("newbest") == 1


@pytest.mark.parametrize("rule", recent.RULES)
def test_real_rule_save_entry_updates_in_same_transaction(database, monkeypatch, rule):
    recent.backfill_player_recent_records(database)
    from server.database import scene_stats
    monkeypatch.setattr(scene_stats, "record_game_metrics", lambda *args: None)
    module = importlib.import_module(f"server.database.{rule}.store_{rule}")
    save = getattr(module, f"store_{rule}_game_record")
    players = [SimpleNamespace(user_id=uid, username=f"u{i}", score=0, original_player_index=i,
                               record_counter=SimpleNamespace(rank_result=i + 1)) for i, uid in enumerate(UIDS)]
    game_id = save(database, replay(rule), players, "custom", "1/4")
    assert game_id
    for i, uid in enumerate(UIDS):
        data = recent.get_player_recent_records(database, uid)[rule]["custom"]
        assert [(entry["game_id"], entry["rank"]) for entry in data["placements"]] == [(game_id, i + 1)]


def test_concurrent_games_do_not_lose_points_or_best_win(database):
    recent.backfill_player_recent_records(database)
    with ThreadPoolExecutor(max_workers=4) as workers:
        futures = [workers.submit(insert_game, database, f"parallel{i}", replay(day=i, fan=40 - i), i % 4 + 1, True) for i in range(4)]
        for future in futures:
            future.result(timeout=15)
    data = recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]
    assert len(data["placements"]) == 4
    assert data["big_win"]["total_fan"] == 40


def test_backfill_resumes_from_committed_page(database, monkeypatch):
    for i in range(5):
        insert_game(database, f"game{i}", replay(day=i))
    original = recent.update_player_recent_records
    def interrupt(cursor, game_id, *args, **kwargs):
        if game_id == "game3":
            raise RuntimeError("simulated restart")
        original(cursor, game_id, *args, **kwargs)
    monkeypatch.setattr(recent, "update_player_recent_records", interrupt)
    with pytest.raises(RuntimeError):
        recent.backfill_player_recent_records(database, batch_size=2)
    assert len(recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]["placements"]) == 2
    monkeypatch.setattr(recent, "update_player_recent_records", original)
    assert recent.backfill_player_recent_records(database, batch_size=2)["processed"] == 3
    assert len(recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]["placements"]) == 5


def test_incomplete_replay_is_reported_and_retried_after_repair(database):
    record = replay()
    record["game_round"]["round_index_1"]["action_ticks"].insert(0, ["c", 47, "F"])
    insert_game(database, "broken", record)
    result = recent.backfill_player_recent_records(database)
    assert result == {"processed": 1, "errors": 1, "complete": False}
    data = recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]
    assert len(data["placements"]) == 1 and data["big_win"] is None
    with connection(database) as conn, conn.cursor() as cursor:
        cursor.execute("UPDATE game_records SET record = %s WHERE game_id = 'broken'", (Json(replay()),))
    assert recent.backfill_player_recent_records(database) == {"processed": 0, "errors": 0, "complete": True}
    assert recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]["big_win"]["total_fan"] == 24


def test_invalid_added_kan_is_a_recoverable_replay_error():
    record = replay(ticks=[["jg", 11, "F"], ["d", 22], ["hu_self", 0, 8, ["平和"], [0] * 4, 22]])
    best, errors = restore_guobiao_best_wins(record)
    assert best == {} and len(errors) == 1
    assert "加杠缺少对应碰牌" in errors[0]


def test_record_and_summary_rollback_together(database):
    recent.backfill_player_recent_records(database)
    with pytest.raises(RuntimeError), connection(database) as conn, conn.cursor() as cursor:
        record = replay()
        cursor.execute("INSERT INTO game_records (game_id, record) VALUES ('rollback', %s)", (Json(record),))
        cursor.execute("""
            INSERT INTO game_player_records (game_id, user_id, rule, room_type, rank, match_type)
            VALUES ('rollback', %s, 'guobiao', 'custom', 1, '1/4')
        """, (UIDS[0],))
        recent.update_player_recent_records(cursor, "rollback", record)
        raise RuntimeError("failure before commit")
    with connection(database) as conn, conn.cursor() as cursor:
        cursor.execute("SELECT COUNT(*) FROM game_records")
        assert cursor.fetchone()[0] == 0
    data = recent.get_player_recent_records(database, UIDS[0])["guobiao"]["custom"]
    assert data == {"placements": [], "big_win": None}


def test_finished_time_not_import_order_and_tied_final_ranks(database):
    insert_game(database, "a_new", replay(day=5), rank=2)
    insert_game(database, "z_old", replay(day=1), rank=2)
    recent.backfill_player_recent_records(database)
    for uid in UIDS:
        points = recent.get_player_recent_records(database, uid)["guobiao"]["custom"]["placements"]
        assert [(point["game_id"], point["rank"]) for point in points] == [("z_old", 2), ("a_new", 2)]


def test_api_all_rules_and_request_identity(database):
    import asyncio
    from server.database.data_router import handle_data_message
    recent.backfill_player_recent_records(database)
    insert_game(database, "one", replay(), update=True)
    messages = []
    async def send_json(value):
        messages.append(value)
    request = {"type": "data/get_player_recent_records", "userid": str(UIDS[0]), "request_id": "request-a"}
    asyncio.run(handle_data_message(SimpleNamespace(db_manager=database), "connection", request, SimpleNamespace(send_json=send_json)))
    response = messages[0]
    assert response["success"]
    payload = response["player_recent_records"]
    assert payload["request_id"] == "request-a" and payload["user_id"] == UIDS[0]
    assert set(payload["rules"]) == set(recent.RULES)
    assert payload["rules"]["guobiao"]["custom"]["big_win"]["total_fan"] == 24
