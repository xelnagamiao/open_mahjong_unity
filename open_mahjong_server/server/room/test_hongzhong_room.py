"""MIL Hongzhong room entry, strict settings, route and persistence contracts."""
import asyncio
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .hongzhong_room import HongzhongRoomValidator, create_hongzhong_room, handle_create_hongzhong_room
from ..game_calculation.hongzhong.scoring import CONFIG, SUB_RULE
from ..response import Response, Show_result_info

BASE = dict(room_name="红中", game_round=1, round_timer=20, step_timer=5)
SWITCHES = ("tips", "count_tips", "pointer_tips", "tourist_limit", "allow_spectator")
FIXED_FALSE = ("use_flowers", "claim_protection", "open_cuohe", "tactical_call", "tian_di_ren_he")


def manager():
    connection = SimpleNamespace(user_id=101, username="红中测试", current_room_id=None)
    result = SimpleNamespace(
        game_server=SimpleNamespace(players={"conn": connection}, db_manager=SimpleNamespace(get_user_settings=Mock(return_value={"username": "红中测试"}))),
        rooms={}, room_passwords={}, _reject_room_entry_conflicts=Mock(return_value=None),
        _normalize_event_id=lambda value: value, _validate_event_for_room=Mock(return_value=None),
        _generate_room_id=lambda: "hz-unit-room", _apply_event_fields=lambda room, event: room.update(event_id=event, room_type="events" if event else "custom"),
        _broadcast_room_info=AsyncMock(),
    )
    result.game_server.room_manager = result
    return result


def create(m, **config):
    return asyncio.run(create_hongzhong_room(m, "conn", **{"room_name": " 红中 ", **config}))


def test_fixed_profile_defaults_are_independent():
    first = HongzhongRoomValidator(**BASE)
    assert first.sub_rule == SUB_RULE and first.detailed_config == CONFIG
    assert all(getattr(first, flag) is False for flag in FIXED_FALSE)
    assert first.hepai_limit == 0 and type(first.hepai_limit) is int
    first.detailed_config["fan_cap"] = 100
    assert HongzhongRoomValidator(**BASE).detailed_config == CONFIG
    explicit = HongzhongRoomValidator(**BASE, **{key: False for key in FIXED_FALSE}, hepai_limit=0)
    assert explicit.hepai_limit == 0 and type(explicit.hepai_limit) is int


@pytest.mark.parametrize("config", [
    {"room_name": " "}, {"room_name": "长" * 129}, {"sub_rule": "hongque/standard"},
    {"game_round": 0}, {"game_round": 5}, {"game_round": True}, {"game_round": "1"},
    {"round_timer": -1}, {"round_timer": 1001}, {"round_timer": 0.5},
    {"step_timer": -1}, {"step_timer": 101}, {"step_timer": "5"},
    {"random_seed": "bad"}, {"detailed_config": []}, {"detailed_config": {"unknown": True}},
    *[{"detailed_config": {key: value}} for key, value in [("fan_cap", 5), ("fan_cap", True), ("bird_count", 4), ("self_draw_only", False), ("joker_tile", 47), ("rule_version", "future")]],
    *[{key: True} for key in FIXED_FALSE],
    *[{key: "false"} for key in (*FIXED_FALSE, *SWITCHES)],
    {"hepai_limit": 1}, {"hepai_limit": "0"}, {"hepai_limit": False},
])
def test_reject_invalid_or_foreign_settings(config):
    with pytest.raises(ValueError):
        HongzhongRoomValidator(**{**BASE, **config})


@pytest.mark.parametrize("bits", range(32))
def test_all_switch_and_round_choices_survive_room_creation(bits):
    flags = {key: bool(bits & (1 << i)) for i, key in enumerate(SWITCHES)}
    for rounds, timer, step in ((1, 0, 0), (2, 20, 5), (3, 1000, 100), (4, 20, 5)):
        m = manager()
        result = create(m, gameround=rounds, roundTimerValue=timer, stepTimerValue=step,
                        password="private" if bits & 1 else "", random_seed="0123456789abcdef" * 4,
                        event_id="hz-unit-event", **flags)
        assert result.success
        room = result.room_info
        assert room["room_rule"] == "hongzhong" and room["sub_rule"] == SUB_RULE
        assert room["detailed_config"] == CONFIG and room["room_name"] == "红中"
        assert all(room[key] is value for key, value in flags.items())
        assert (room["game_round"], room["round_timer"], room["step_timer"]) == (rounds, timer, step)
        assert room["event_id"] == "hz-unit-event" and room["is_player_set_random_seed"]
        assert room["has_password"] is bool(bits & 1) and "password" not in room
        assert m.room_passwords == ({"hz-unit-room": "private"} if bits & 1 else {})
        assert m.game_server.players["conn"].current_room_id == "hz-unit-room"
        m._broadcast_room_info.assert_awaited_once_with("hz-unit-room")


@pytest.mark.parametrize("failure", ["connection", "login", "membership", "conflict", "event", "settings", "config"])
def test_failed_creation_does_not_mutate_room(failure):
    m = manager()
    opts = {}
    if failure == "connection": m.game_server.players = {}
    elif failure == "login": m.game_server.players["conn"].user_id = 0
    elif failure == "membership": m.game_server.players["conn"].current_room_id = "old"
    elif failure == "conflict": m._reject_room_entry_conflicts.return_value = Response(type="tips", success=False, message="active")
    elif failure == "event": m._validate_event_for_room.return_value = Response(type="tips", success=False, message="event")
    elif failure == "settings": m.game_server.db_manager.get_user_settings.return_value = None
    else: opts["detailed_config"] = {"fan_cap": 1}
    assert not create(m, **opts).success
    assert not m.rooms and not m.room_passwords
    m._broadcast_room_info.assert_not_awaited()


@pytest.mark.parametrize("flag", (*FIXED_FALSE, "hepai_limit"))
def test_handler_forwards_fixed_options_to_validator(flag):
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_hongzhong_room(m.game_server, "conn", {"roomname": "红中", flag: True}, socket))
    assert not socket.send_json.await_args.args[0]["success"]


def test_handler_and_result_preserve_version_and_scoring_evidence():
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_hongzhong_room(m.game_server, "conn", {"roomname": "红中", "count_tips": True}, socket))
    payload = socket.send_json.await_args.args[0]
    assert payload["room_info"]["count_tips"] and payload["room_info"]["detailed_config"] == CONFIG
    info = {"bird_tiles": [45, 15], "bird_hits": 2, "detail": {"joker_substitutions": [[45, 11]]}, "kong_ledger": []}
    assert Show_result_info(hu_class="hu_self", action_tick=7, hongzhong_info=info).model_dump()["hongzhong_info"] == info
    assert Show_result_info(hu_class="liuju", action_tick=7).hongzhong_info is None


def test_database_adapter_preserves_family_profile_actions_and_metrics():
    from ..database.taiwan.store_taiwan import store_taiwan_game_record
    from ..database.player_recent_records import RULES
    assert "hongzhong" in RULES
    cursor = Mock()
    connection = Mock(cursor=Mock(return_value=cursor))
    db = SimpleNamespace(_get_connection=lambda: connection, _put_connection=Mock())
    players = [SimpleNamespace(user_id=i+100, username=str(i), score=0, original_player_index=i,
                record_counter=SimpleNamespace(rank_result=i+1)) for i in range(4)]
    record = {"game_title": {"rule": "hongzhong", "sub_rule": SUB_RULE, "detailed_config": CONFIG},
              "game_round": {"round_index_1": {"action_ticks": [["hongzhong", "birds", {"tiles": [45], "hits": 1, "detail": {"joker_substitutions": [[45, 11]]}}]]}}}
    with patch('server.database.player_recent_records.update_player_recent_records') as recent, patch('server.database.scene_stats.record_game_metrics') as metrics:
        game_id = store_taiwan_game_record(db, record, players, "custom", "1/4")
        assert len(game_id) == 10
        inserted_record = cursor.execute.call_args_list[0].args[1][1]
        assert json.loads(inserted_record) == record
        for call in cursor.execute.call_args_list[1:]:
            assert call.args[1][6:8] == ("hongzhong", SUB_RULE)
        recent.assert_called_once_with(cursor, game_id, record)
        assert metrics.call_args.args[-1]["rule"] == "hongzhong"
        connection.commit.assert_called_once()
        db._put_connection.assert_called_once_with(connection)


@pytest.mark.parametrize("message_type", ["room/create_Hongzhong_room", "room/create_hongzhong_room"])
def test_room_router_dispatches_both_client_spellings(message_type):
    from .room_router import handle_room_message
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_room_message(m.game_server, "conn", {"type": message_type, "roomname": "红中"}, socket))
    assert socket.send_json.await_args.args[0]["room_info"]["room_rule"] == "hongzhong"


@pytest.mark.parametrize("family", ["hongzhong", "hongque", None])
@pytest.mark.parametrize("action", ["cut_tile", "send_action"])
def test_action_router_never_forwards_red_center_messages_to_rainbow(family, action):
    from ..gamestate import gamestate_router
    state = SimpleNamespace(room_rule=family)
    server = SimpleNamespace(players={}, gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=Mock(return_value=state)))
    socket = SimpleNamespace(send_json=AsyncMock())
    with patch.object(gamestate_router, "handle_cut_tile", new_callable=AsyncMock) as cut, patch.object(gamestate_router, "handle_send_action", new_callable=AsyncMock) as send:
        message = {"type": f"gamestate/hongzhong/{action}", "gamestate_id": "unit"}
        asyncio.run(gamestate_router.handle_gamestate_message(server, "conn", message, socket))
        expected = cut if action == "cut_tile" else send
        assert expected.await_count == (1 if family == "hongzhong" else 0)
        assert (send if action == "cut_tile" else cut).await_count == 0


def test_manager_room_index_and_database_alias_use_red_center_family():
    from ..gamestate.gamestate_manager import GameStateManager
    from ..database.db_manager import DatabaseManager
    from ..database.taiwan.store_taiwan import store_taiwan_game_record
    room = {"instance_id": "hz-instance"}
    server = SimpleNamespace(room_manager=SimpleNamespace(rooms={"HZ": room}, active_game_room_ids={}))
    manager = GameStateManager(server)
    state = SimpleNamespace(room_rule="hongzhong", room_id="HZ", gamestate_id="hz-test", player_list=[])
    manager.register_game(state, room)
    assert manager.room_id_to_HongzhongGameState["HZ"] is state
    assert manager.get_game_state_by_room_id("HZ") is state
    assert manager.get_game_state_by_gamestate_id("hz-test") is state
    assert "HZ" not in manager.room_id_to_HongqueGameState
    assert DatabaseManager.store_hongzhong_game_record is store_taiwan_game_record


def test_event_empty_room_defaults_and_switches_use_strict_red_center_validator():
    from .room_manager import RoomManager
    for bits in range(32):
        m = manager()
        m.room_validators = {"hongzhong": HongzhongRoomValidator}
        options = {key: bool(bits & (1 << i)) for i, key in enumerate(SWITCHES)}
        options.update(room_name="赛事红中", sub_rule=SUB_RULE, game_round=4, round_timer=0, step_timer=0, detailed_config=CONFIG)
        result = asyncio.run(RoomManager.create_empty_event_room(m, "hz-event", "hongzhong", options, password="unit", broadcast=False))
        assert result.success
        assert result.room_info["room_rule"] == "hongzhong" and result.room_info["room_type"] == "events"
        assert all(result.room_info[key] is options[key] for key in SWITCHES)
        assert result.room_info["hepai_limit"] == 0 and result.room_info["detailed_config"] == CONFIG
    for opts in [{"tips": "false"}, {"game_round": True}, {"use_flowers": True}, {"hepai_limit": 1}, {"detailed_config": {"self_draw_only": False}}]:
        m = manager(); m.room_validators = {"hongzhong": HongzhongRoomValidator}
        result = asyncio.run(RoomManager.create_empty_event_room(m, "hz-event", "hongzhong", opts, broadcast=False))
        assert not result.success and not m.rooms
