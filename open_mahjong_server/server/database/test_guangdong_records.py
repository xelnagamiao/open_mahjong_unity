"""Database and wire-format contracts; no shared database or service is opened."""

import asyncio
from copy import deepcopy
from datetime import datetime
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .db_manager import DatabaseManager
from .taiwan.store_taiwan import store_taiwan_game_record
from .player_recent_records import get_player_recent_records, update_player_recent_records
from .scene_stats import record_game_metrics
from ..game_calculation.guangdong.config import DEFAULT_CONFIG, SUB_RULE
from ..game_calculation.tuidao.rules import SUB_RULE as TUIDAO_SUB_RULE
from ..room.tuidao_room import CONFIG as TUIDAO_CONFIG


UIDS = (10000301, 10000302, 10000303, 10000304)


def players():
    return [SimpleNamespace(
        user_id=uid, username=f"测试{index}", score=(3 if index == 0 else -1) * 4,
        original_player_index=index, record_counter=SimpleNamespace(rank_result=index + 1),
    ) for index, uid in enumerate(UIDS)]


def record(sub_rule=SUB_RULE, *, require_minimum=True, room_type="custom"):
    config = {**DEFAULT_CONFIG, "require_minimum_score": require_minimum} if sub_rule == SUB_RULE else dict(TUIDAO_CONFIG)
    return {
        "game_title": {
            "rule": "guangdong", "sub_rule": sub_rule, "room_type": room_type,
            "detailed_config": config, "end_time": "2026-10-02T12:30:00",
            "event_id": "gd-unit-event" if room_type == "events" else None,
            **{f"p{i}_uid": uid for i, uid in enumerate(UIDS)},
        },
        "game_round": {"round_index_1": {
            "round_index": 1, "p0_tiles": [11, 12, 55],
            "action_ticks": [["d", 56], ["c", 55, "F"], ["end"]],
            "guangdong_result": {"joker_assignments": {"55": 13}, "horses": [11, 56, 43, 45]},
        }},
    }


def database():
    cursor = Mock()
    connection = Mock()
    connection.cursor.return_value = cursor
    # get_player_recent_records uses a context-managed cursor; other adapters close it directly.
    cursor.__enter__ = Mock(return_value=cursor)
    cursor.__exit__ = Mock(return_value=False)
    db = SimpleNamespace(_get_connection=Mock(return_value=connection), _put_connection=Mock())
    return db, connection, cursor


def player_rows(sub_rule=SUB_RULE):
    return [{
        "game_id": "gd-unit-record", "user_id": p.user_id, "username": p.username,
        "score": p.score, "rank": p.record_counter.rank_result, "original_player_index": p.original_player_index,
        "rule": "guangdong", "sub_rule": sub_rule, "room_type": "custom", "match_type": "1/4",
        "created_at": datetime(2026, 10, 2, 12, 30),
    } for p in players()]


def test_new_and_legacy_record_stores_use_the_generic_unfiltered_adapter():
    assert DatabaseManager.store_guangdong_game_record is store_taiwan_game_record
    assert DatabaseManager.store_tuidao_game_record is store_taiwan_game_record


@pytest.mark.parametrize("sub_rule", [SUB_RULE, TUIDAO_SUB_RULE])
@pytest.mark.parametrize("require_minimum", [False, True])
@pytest.mark.parametrize("room_type", ["custom", "events"])
def test_store_keeps_subrule_exact_config_joker_data_and_event_metrics(sub_rule, require_minimum, room_type):
    db, conn, cursor = database()
    source = record(sub_rule, require_minimum=require_minimum, room_type=room_type)
    original = deepcopy(source)
    with patch("server.database.player_recent_records.update_player_recent_records") as recent, \
         patch("server.database.scene_stats.record_game_metrics") as metrics:
        game_id = DatabaseManager.store_guangdong_game_record(db, source, players(), room_type, "1/4")
    assert game_id and source == original
    assert len(cursor.execute.call_args_list) == 5
    stored = json.loads(cursor.execute.call_args_list[0].args[1][1])
    assert stored == source
    for call in cursor.execute.call_args_list[1:]:
        values = call.args[1]
        assert values[6:10] == ("guangdong", sub_rule, "1/4", room_type)
        assert values[10:12] == (("gd-unit-event", "gd-unit-event") if room_type == "events" else (None, None))
    recent.assert_called_once_with(cursor, game_id, source)
    scene = metrics.call_args.args[4]
    assert scene["rule"] == "guangdong" and scene["sub_rule"] == sub_rule
    conn.commit.assert_called_once()
    cursor.close.assert_called_once()
    db._put_connection.assert_called_once_with(conn)


def test_generic_bot_record_exclusion_still_applies_to_new_rule():
    db, _, _ = database()
    people = players()
    people[0].user_id = 1
    assert DatabaseManager.store_guangdong_game_record(db, record(), people, "custom", "1/4") is None
    db._get_connection.assert_not_called()


@pytest.mark.parametrize("sub_rule", [SUB_RULE, TUIDAO_SUB_RULE])
def test_database_record_list_and_detail_preserve_family_identity(sub_rule):
    db, _, cursor = database()
    rows = player_rows(sub_rule)
    source = record(sub_rule)
    cursor.fetchall.side_effect = [
        [{"game_id": "gd-unit-record", "is_favorite": True, "note": "花鬼验证"}], rows,
    ]
    listing = DatabaseManager.get_record_list(db, UIDS[0])
    assert listing[0]["rule"] == "guangdong" and listing[0]["sub_rule"] == sub_rule
    assert listing[0]["is_favorite"] and len(listing[0]["players"]) == 4
    cursor.fetchall.side_effect = None
    cursor.fetchall.return_value = rows
    cursor.fetchone.side_effect = [
        {"game_id": "gd-unit-record", "record": json.dumps(source), "created_at": rows[0]["created_at"]},
        (True,),  # Existing duplicate-record visibility guard performs its own SQL lookup.
    ]
    detail = DatabaseManager.get_record_by_id(db, " gd-unit-record ")
    assert detail["rule"] == "guangdong" and detail["sub_rule"] == sub_rule
    assert detail["record"] == source
    assert len(detail["players"]) == 4


@pytest.mark.parametrize("sub_rule", [SUB_RULE, TUIDAO_SUB_RULE])
def test_websocket_list_and_detail_roundtrip_keep_raw_joker_events_and_config(sub_rule):
    from .data_router import handle_data_message
    source = record(sub_rule, require_minimum=False)
    rows = player_rows(sub_rule)
    response_record = {
        "game_id": "gd-unit-record", "rule": "guangdong", "sub_rule": sub_rule,
        "created_at": "2026-10-02T12:30:00", "players": rows, "record": source,
    }
    db = SimpleNamespace(get_record_list=Mock(return_value=[response_record]), get_record_by_id=Mock(return_value=response_record))
    server = SimpleNamespace(players={"conn": SimpleNamespace(user_id=UIDS[0])}, db_manager=db)
    socket = SimpleNamespace(send_json=AsyncMock())

    async def run():
        await handle_data_message(server, "conn", {"type": "data/get_record_list"}, socket)
        await handle_data_message(server, "conn", {"type": "data/get_record_by_id", "game_id": "gd-unit-record"}, socket)

    asyncio.run(run())
    listing, detail = [call.args[0] for call in socket.send_json.await_args_list]
    assert listing["record_list"][0]["sub_rule"] == sub_rule
    assert detail["record_detail"]["sub_rule"] == sub_rule
    assert detail["record_detail"]["record"] == source and detail["record_detail"]["cloud_saved"]


@pytest.mark.parametrize("sub_rule", [SUB_RULE, TUIDAO_SUB_RULE])
def test_guangdong_custom_records_update_and_read_recent_placements(sub_rule):
    db, conn, cursor = database()
    source = record(sub_rule)
    cursor.fetchone.side_effect = [(source, datetime(2026, 10, 2, 12, 30))] + [([], None)] * 4
    cursor.fetchall.return_value = [(p.user_id, "guangdong", "custom", p.record_counter.rank_result, "1/4") for p in players()]
    update_player_recent_records(cursor, "gd-unit-record", source)
    updates = [call for call in cursor.execute.call_args_list if "UPDATE player_recent_records SET" in call.args[0]]
    assert len(updates) == 4
    placements = updates[0].args[1][0].adapted
    assert placements[0]["game_id"] == "gd-unit-record" and placements[0]["rank"] == 1
    cursor.fetchall.return_value = [("guangdong", "custom", placements, None)]
    recent = get_player_recent_records(db, UIDS[0])
    assert recent["guangdong"]["custom"] == {"placements": placements, "big_win": None}
    assert recent["guangdong"]["match"] == {"placements": [], "big_win": None}
    conn.commit.assert_called_once()


@pytest.mark.parametrize("sub_rule", [SUB_RULE, TUIDAO_SUB_RULE])
def test_event_scene_metrics_keep_rule_and_subrule_per_player(sub_rule):
    db, conn, cursor = database()
    scene = {"rule": "guangdong", "sub_rule": sub_rule, "room_type": "events", "event_id": "gd-unit-event", "match_type": "1/4"}
    record_game_metrics(db, "gd-unit-record", record(sub_rule, room_type="events"), players(), scene)
    assert cursor.execute.call_count == 4
    for call in cursor.execute.call_args_list:
        assert call.args[1][3:8] == ("guangdong", sub_rule, "events", "gd-unit-event", "gd-unit-event")
        assert call.args[1][12] == 1
    conn.commit.assert_called_once()
