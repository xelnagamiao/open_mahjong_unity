"""九江固定规则、房间入口与失败回滚。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .jiujiang_room import CONFIG, SUB_RULE, JiujiangRoomValidator, create_jiujiang_room, handle_create_jiujiang_room
from ..response import Response

BASE = dict(room_name="九江", game_round=1, round_timer=20, step_timer=5)


def manager():
    connection = SimpleNamespace(user_id=101, username="九江测试", current_room_id=None)
    result = SimpleNamespace(
        game_server=SimpleNamespace(players={"conn":connection}, db_manager=SimpleNamespace(get_user_settings=Mock(return_value={"username":"九江测试"}))),
        rooms={}, room_passwords={}, _reject_room_entry_conflicts=Mock(return_value=None),
        _normalize_event_id=lambda value:value, _validate_event_for_room=Mock(return_value=None),
        _generate_room_id=lambda:"jj-unit-room", _apply_event_fields=lambda room,event:room.update(event_id=event,room_type="events" if event else "custom"),
        _broadcast_room_info=AsyncMock(),
    )
    result.game_server.room_manager = result
    return result


def test_fixed_profile():
    config = JiujiangRoomValidator(**BASE)
    assert config.sub_rule == SUB_RULE and config.hepai_limit == 1 and config.use_flowers
    assert not config.open_cuohe and not config.tactical_call and not config.claim_protection
    config.detailed_config["rule_version"] = "modified"
    assert JiujiangRoomValidator(**BASE).detailed_config == CONFIG
    assert JiujiangRoomValidator(**BASE,detailed_config=None).detailed_config == CONFIG


@pytest.mark.parametrize("override", [
    {"room_name":" "}, {"room_name":"长"*129}, {"sub_rule":"shanghai/lajihe"},
    {"game_round":0}, {"game_round":5}, {"game_round":True}, {"game_round":"1"},
    {"round_timer":-1}, {"round_timer":1001}, {"step_timer":-1}, {"step_timer":101},
    {"random_seed":"invalid"}, {"use_flowers":False}, {"hepai_limit":0}, {"hepai_limit":True},
    {"detailed_config":[]}, {"detailed_config":{"unknown":1}},
    *[{key:True} for key in ("open_cuohe","tactical_call","claim_protection","tian_di_ren_he")],
    *[{key:"false"} for key in ("tips","count_tips","pointer_tips","tourist_limit","allow_spectator")],
])
def test_invalid_settings(override):
    with pytest.raises(ValueError):
        JiujiangRoomValidator(**{**BASE,**override})


@pytest.mark.parametrize("bits", range(32))
def test_creation_preserves_hint_and_room_switches(bits):
    m = manager()
    flags = {key:bool(bits & (1<<i)) for i,key in enumerate(("tips","count_tips","pointer_tips","tourist_limit","allow_spectator"))}
    result = asyncio.run(create_jiujiang_room(m,"conn",room_name=" 九江 ",gameround=bits%4+1,
        password="private",random_seed="0123456789abcdef"*4,event_id="jj-event",**flags))
    assert result.success, result.message
    room = result.room_info
    assert room["room_rule"] == "jiujiang" and room["sub_rule"] == SUB_RULE
    assert room["room_name"] == "九江" and room["hepai_limit"] == 1 and room["detailed_config"] == CONFIG
    assert room["game_round"] == bits%4+1 and room["room_type"] == "events"
    assert all(room[key] is value for key,value in flags.items())
    assert room["has_password"] and "password" not in room and room["is_player_set_random_seed"]
    assert m.room_passwords == {"jj-unit-room":"private"}
    assert m.game_server.players["conn"].current_room_id == "jj-unit-room"
    m._broadcast_room_info.assert_awaited_once_with("jj-unit-room")


@pytest.mark.parametrize("failure", ["connection","login","membership","conflict","event","settings","config"])
def test_creation_failure_has_no_partial_room(failure):
    m, options = manager(), {}
    if failure == "connection": m.game_server.players = {}
    elif failure == "login": m.game_server.players["conn"].user_id = 0
    elif failure == "membership": m.game_server.players["conn"].current_room_id = "old"
    elif failure == "conflict": m._reject_room_entry_conflicts.return_value = Response(type="tips",success=False,message="active")
    elif failure == "event": m._validate_event_for_room.return_value = Response(type="tips",success=False,message="event")
    elif failure == "settings": m.game_server.db_manager.get_user_settings.return_value = None
    else: options["hepai_limit"] = 8
    result = asyncio.run(create_jiujiang_room(m,"conn",room_name="九江",**options))
    assert not result.success and not m.rooms and not m.room_passwords


def test_wire_handler_and_plain_room():
    m, websocket = manager(), AsyncMock()
    asyncio.run(handle_create_jiujiang_room(m.game_server,"conn",{"roomname":"九江"},websocket))
    payload = websocket.send_json.await_args.args[0]
    assert payload["success"] and payload["type"] == "room/create_room_done"
    assert payload["room_info"]["room_rule"] == "jiujiang" and payload["room_info"]["room_type"] == "custom"
    assert not m.room_passwords
@pytest.mark.parametrize('fields',[{'use_flowers':True},{'hepai_limit':1},{'open_cuohe':False},{'tactical_call':False},{'claim_protection':False},{'tian_di_ren_he':False}])
def test_explicit_fixed_values_are_valid(fields):
    JiujiangRoomValidator(room_name='九江',round_timer=20,step_timer=5,**fields)
