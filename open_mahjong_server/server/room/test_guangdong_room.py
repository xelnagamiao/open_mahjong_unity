"""Offline contracts for Guangdong sub-rule selection and room configuration."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .guangdong_room import (
    FIXED_OPTIONS, GuangdongMilRoomValidator, GuangdongRoomValidator,
    create_guangdong_room, handle_create_guangdong_room,
)
from .tuidao_room import CONFIG as TUIDAO_CONFIG, TuidaoRoomValidator
from ..game_calculation.guangdong.config import SUB_RULE, DEFAULT_CONFIG
from ..game_calculation.tuidao.rules import SUB_RULE as TUIDAO_SUB_RULE
from ..response import Response


BASE = dict(room_name="广东花鬼", game_round=1, round_timer=20, step_timer=5)


def manager():
    connection = SimpleNamespace(user_id=101, username="测试玩家", current_room_id=None)
    db = SimpleNamespace(get_user_settings=Mock(return_value={"username": "测试玩家"}))
    server = SimpleNamespace(players={"conn": connection}, db_manager=db)
    result = SimpleNamespace(
        game_server=server, rooms={}, room_passwords={},
        room_validators={"guangdong": GuangdongRoomValidator},
        _reject_room_entry_conflicts=Mock(return_value=None),
        _normalize_event_id=lambda value: value,
        _validate_event_for_room=Mock(return_value=None),
        _generate_room_id=lambda: "gd-unit-room",
        _apply_event_fields=lambda room, event: room.update(event_id=event),
        _broadcast_room_info=AsyncMock(),
    )
    server.room_manager = result
    return result


def create(room_manager, **overrides):
    return asyncio.run(create_guangdong_room(room_manager, "conn", **{
        "room_name": " 广东花鬼 ", "sub_rule": SUB_RULE, **overrides,
    }))


def test_selector_preserves_legacy_default_and_selects_new_profile_explicitly():
    old = GuangdongRoomValidator(**BASE)
    new = GuangdongRoomValidator(**BASE, sub_rule=SUB_RULE)
    assert isinstance(old, TuidaoRoomValidator)
    assert old.sub_rule == TUIDAO_SUB_RULE and old.detailed_config == TUIDAO_CONFIG
    assert isinstance(new, GuangdongMilRoomValidator)
    assert new.sub_rule == SUB_RULE and new.detailed_config == DEFAULT_CONFIG
    assert not new.use_flowers and not new.claim_protection and new.hepai_limit == 2
    new.detailed_config["require_minimum_score"] = False
    assert GuangdongRoomValidator(**BASE, sub_rule=SUB_RULE).detailed_config == DEFAULT_CONFIG


def test_new_validator_cannot_be_used_to_coerce_a_different_profile():
    with pytest.raises(ValueError):
        GuangdongMilRoomValidator(**BASE, sub_rule=TUIDAO_SUB_RULE)


@pytest.mark.parametrize("overrides", [
    {"sub_rule": "guangdong/local"}, {"sub_rule": None},
    {"game_round": 0}, {"game_round": 5}, {"game_round": True}, {"game_round": "1"},
    {"round_timer": -1}, {"round_timer": 1001}, {"round_timer": "20"},
    {"step_timer": -1}, {"step_timer": 101}, {"step_timer": True}, {"room_name": " "},
    {"random_seed": "123456789"},
    {"hepai_limit": 0}, {"hepai_limit": 4}, {"hepai_limit": "2"},
    {"detailed_config": []}, {"detailed_config": {"edition": "unknown"}},
    {"detailed_config": {"wildcards": False}}, {"detailed_config": {"horse_count": 8}},
    {"detailed_config": {"minimum_fan": 1}}, {"detailed_config": {"minimum_score": 2}},
    {"detailed_config": {"require_minimum_score": "false"}},
    {"detailed_config": {"unknown": True}},
    *[{key: True} for key in FIXED_OPTIONS],
    *[{key: "false"} for key in (*FIXED_OPTIONS, "tactical_call", "tips", "tourist_limit", "allow_spectator", "count_tips", "pointer_tips")],
])
def test_new_profile_rejects_wrong_types_unknown_settings_and_fixed_rule_changes(overrides):
    with pytest.raises(ValueError):
        GuangdongRoomValidator(**{**BASE, "sub_rule": SUB_RULE, **overrides})


@pytest.mark.parametrize("bits", range(64))
def test_all_switch_combinations_survive_room_creation_and_defaults(bits):
    keys = ("tips", "tourist_limit", "allow_spectator", "count_tips", "pointer_tips")
    options = {key: bool(bits & (1 << index)) for index, key in enumerate(keys)}
    require_minimum = bool(bits & 32)
    for rounds, round_timer, step_timer in ((1, 0, 0), (2, 20, 5), (3, 1000, 100), (4, 20, 5)):
        m = manager()
        response = create(m, gameround=rounds, roundTimerValue=round_timer,
                          stepTimerValue=step_timer, password="unit-pass" if bits & 1 else "",
                          random_seed="0123456789abcdef" * 4, detailed_config={"require_minimum_score": require_minimum},
                          event_id="unit-event", **options)
        assert response.success
        room = m.rooms["gd-unit-room"]
        assert room["room_rule"] == "guangdong" and room["sub_rule"] == SUB_RULE
        assert room["room_name"] == "广东花鬼" and room["hepai_limit"] == 2
        assert (room["game_round"], room["round_timer"], room["step_timer"]) == (rounds, round_timer, step_timer)
        assert all(room[key] is value for key, value in options.items())
        assert room["detailed_config"] == {**DEFAULT_CONFIG, "require_minimum_score": require_minimum}
        assert not room["use_flowers"] and not room["claim_protection"]
        assert room["event_id"] == "unit-event" and room["is_player_set_random_seed"]
        assert room["has_password"] is bool(bits & 1) and "password" not in room
        assert m.room_passwords == ({"gd-unit-room": "unit-pass"} if bits & 1 else {})
        assert m.game_server.players["conn"].current_room_id == "gd-unit-room"
        assert room["player_settings"][101]["avatar_frame_id"] == 0
        m._broadcast_room_info.assert_awaited_once_with("gd-unit-room")


@pytest.mark.parametrize("failure", [
    "missing_connection", "not_logged_in", "already_in_room", "entry_conflict", "event", "settings", "config",
])
def test_creation_rejections_leave_room_password_and_membership_untouched(failure):
    m = manager()
    options = {}
    if failure == "missing_connection":
        m.game_server.players = {}
    elif failure == "not_logged_in":
        m.game_server.players["conn"].user_id = None
    elif failure == "already_in_room":
        m.game_server.players["conn"].current_room_id = "existing"
    elif failure == "entry_conflict":
        m._reject_room_entry_conflicts.return_value = Response(type="tips", success=False, message="活动对局")
    elif failure == "event":
        m._validate_event_for_room.return_value = Response(type="tips", success=False, message="活动不可用")
    elif failure == "settings":
        m.game_server.db_manager.get_user_settings.return_value = None
    elif failure == "config":
        options["detailed_config"] = {"wildcards": False}
    result = create(m, password="unit-pass", **options)
    assert not result.success and not m.rooms and not m.room_passwords
    m._broadcast_room_info.assert_not_awaited()
    if "conn" in m.game_server.players:
        assert m.game_server.players["conn"].current_room_id == ("existing" if failure == "already_in_room" else None)


def test_create_without_sub_rule_still_uses_tuidao():
    m = manager()
    result = asyncio.run(create_guangdong_room(m, "conn", room_name="推倒和"))
    assert result.success
    assert result.room_info["sub_rule"] == TUIDAO_SUB_RULE
    assert result.room_info["detailed_config"] == TUIDAO_CONFIG
    assert result.room_info["hepai_limit"] == 0


@pytest.mark.parametrize("require_minimum", [False, True])
def test_websocket_handler_preserves_required_rule_and_optional_minimum(require_minimum):
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_guangdong_room(m.game_server, "conn", {
        "roomname": "花鬼", "sub_rule": SUB_RULE, "detailed_config": {"require_minimum_score": require_minimum},
        "use_flowers": False, "hepai_limit": 2, "count_tips": True,
    }, socket))
    result = socket.send_json.await_args.args[0]
    assert result["success"] and result["room_info"]["detailed_config"]["require_minimum_score"] is require_minimum
    assert not result["room_info"]["use_flowers"] and result["room_info"]["count_tips"]


@pytest.mark.parametrize("flag", FIXED_OPTIONS)
def test_websocket_handler_does_not_silently_ignore_unsupported_fixed_options(flag):
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_guangdong_room(m.game_server, "conn", {
        "roomname": "花鬼", "sub_rule": SUB_RULE, flag: True,
    }, socket))
    assert not socket.send_json.await_args.args[0]["success"] and not m.rooms


@pytest.mark.parametrize("sub_rule", [None, TUIDAO_SUB_RULE, SUB_RULE])
def test_public_room_router_dispatches_guangdong_family(sub_rule):
    from .room_router import handle_room_message
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    message = {"type": "room/create_Guangdong_room", "roomname": "广东"}
    if sub_rule is not None:
        message["sub_rule"] = sub_rule
    asyncio.run(handle_room_message(m.game_server, "conn", message, socket))
    room = socket.send_json.await_args.args[0]["room_info"]
    assert room["sub_rule"] == (sub_rule or TUIDAO_SUB_RULE)


def test_old_tuidao_route_remains_the_old_profile():
    from .room_router import handle_room_message
    m = manager()
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_room_message(m.game_server, "conn", {
        "type": "room/create_Tuidao_room", "roomname": "推倒和",
    }, socket))
    assert socket.send_json.await_args.args[0]["room_info"]["detailed_config"] == TUIDAO_CONFIG


@pytest.mark.parametrize("sub_rule", [None, TUIDAO_SUB_RULE, SUB_RULE])
@pytest.mark.parametrize("require_minimum", [False, True])
def test_event_room_uses_same_subrule_validator_and_configuration(sub_rule, require_minimum):
    from .room_manager import RoomManager
    m = manager()
    config = {"game_round": 2, "use_flowers": False, "allow_spectator": False,
              "count_tips": True, "pointer_tips": False}
    if sub_rule is not None:
        config["sub_rule"] = sub_rule
    if sub_rule == SUB_RULE:
        config["detailed_config"] = {"require_minimum_score": require_minimum}
    result = asyncio.run(RoomManager.create_empty_event_room(m, "unit-event", "guangdong", config, broadcast=False))
    assert result.success
    room = result.room_info
    assert room["sub_rule"] == (sub_rule or TUIDAO_SUB_RULE)
    assert not room["use_flowers"] and not room["allow_spectator"]
    if sub_rule == SUB_RULE:
        assert room["hepai_limit"] == 2 and room["count_tips"] and not room["pointer_tips"]
        assert room["detailed_config"] == {**DEFAULT_CONFIG, "require_minimum_score": require_minimum}
    else:
        assert room["detailed_config"] == TUIDAO_CONFIG and room["hepai_limit"] == 0


@pytest.mark.parametrize("override", [
    {"use_flowers": True}, {"tips": "false"}, {"allow_spectator": "false"},
    {"detailed_config": {"wildcards": False}}, {"sub_rule": "guangdong/unknown"},
    {"hepai_limit": 0}, {"game_round": True}, {"game_round": "2"},
    {"round_timer": True}, {"round_timer": "20"}, {"step_timer": "5"},
    {"random_seed": "123456789"},
])
def test_event_room_rejects_invalid_new_profile_without_creating_room(override):
    from .room_manager import RoomManager
    m = manager()
    result = asyncio.run(RoomManager.create_empty_event_room(m, "unit-event", "guangdong", {
        "sub_rule": SUB_RULE, **override,
    }, broadcast=False))
    assert not result.success and not m.rooms


def test_event_room_preserves_valid_hex_master_seed_and_fixed_defaults():
    from .room_manager import RoomManager
    m = manager()
    seed = "fedcba9876543210" * 4
    result = asyncio.run(RoomManager.create_empty_event_room(m, "unit-event", "guangdong", {
        "sub_rule": SUB_RULE, "random_seed": seed,
    }, broadcast=False))
    assert result.success and result.room_info["random_seed"] == int(seed, 16)
    assert result.room_info["is_player_set_random_seed"]
    assert result.room_info["detailed_config"] == DEFAULT_CONFIG


def test_real_room_manager_registers_the_family_validator():
    from .room_manager import RoomManager
    m = RoomManager(SimpleNamespace())
    assert m.room_validators["guangdong"] is GuangdongRoomValidator
    assert m.room_validators["guangdong"](**BASE, sub_rule=SUB_RULE).detailed_config == DEFAULT_CONFIG
