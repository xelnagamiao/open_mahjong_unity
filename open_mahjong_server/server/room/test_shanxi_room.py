import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock
import pytest
from .shanxi_room import ShanxiRoomValidator, create_shanxi_room, handle_create_shanxi_room
from ..game_calculation.shanxi.rules import SUB_RULE, VERSION
from ..response import Response

BASE = dict(room_name="山西测试",game_round=1,round_timer=20,step_timer=5)

@pytest.mark.parametrize("key,value", [
    ("sub_rule","shanxi/wildcard"),("detailed_config",[]),("detailed_config",{"flowers":True}),
    ("detailed_config",{"rule_version":"future"}),("game_round",0),("game_round",5),
    ("round_timer",-1),("step_timer",101),("room_name"," "),
    *[(key,True) for key in ("use_flowers","open_cuohe","tactical_call","claim_protection","tian_di_ren_he")],
    *[(key,"false") for key in ("tips","allow_spectator","tourist_limit","count_tips","pointer_tips")],
])
def test_validator_rejects_unsupported_or_malformed_configuration(key,value):
    with pytest.raises(ValueError):
        ShanxiRoomValidator(**{**BASE,key:value})

def test_validator_fixed_rules_and_independent_default():
    a,b=ShanxiRoomValidator(**BASE),ShanxiRoomValidator(**BASE,detailed_config=None)
    assert a.sub_rule==SUB_RULE and a.detailed_config=={"rule_version":VERSION}
    assert not a.use_flowers and not a.open_cuohe and not a.claim_protection
    a.detailed_config["extra"]="x"
    assert b.detailed_config=={"rule_version":VERSION}

def manager():
    connection=SimpleNamespace(user_id=101,username="玩家",current_room_id=None)
    db=SimpleNamespace(get_user_settings=Mock(return_value={"username":"玩家"}))
    server=SimpleNamespace(players={"conn":connection},db_manager=db)
    return SimpleNamespace(game_server=server,rooms={},room_passwords={},
        _reject_room_entry_conflicts=Mock(return_value=None),_normalize_event_id=lambda v:v,
        _validate_event_for_room=Mock(return_value=None),_generate_room_id=lambda:"sx123",
        _apply_event_fields=Mock(),_broadcast_room_info=AsyncMock())

def test_room_roundtrip_bools_password_profile_and_version():
    m=manager()
    response=asyncio.run(create_shanxi_room(m,"conn",room_name="山西",password="1234",tips=False,
        allow_spectator=False,count_tips=True,pointer_tips=False,gameround=4,roundTimerValue=0,stepTimerValue=0))
    assert response.success
    room=m.rooms["sx123"]
    assert room["room_rule"]=="shanxi" and room["sub_rule"]==SUB_RULE
    assert room["detailed_config"]=={"rule_version":VERSION}
    assert not room["allow_spectator"] and not room["tips"] and not room["pointer_tips"]
    assert room["count_tips"] and not room["use_flowers"] and room["hepai_limit"]==0
    assert room["game_round"]==4 and room["round_timer"]==room["step_timer"]==0
    assert m.room_passwords=={"sx123":"1234"}
    assert m.game_server.players["conn"].current_room_id=="sx123"
    m._broadcast_room_info.assert_awaited_once_with("sx123")


@pytest.mark.parametrize("bits",range(32))
def test_all_five_switch_combinations_with_rounds_and_timer_boundaries(bits):
    keys=("tips","tourist_limit","allow_spectator","count_tips","pointer_tips")
    options={key:bool(bits&(1<<i)) for i,key in enumerate(keys)}
    for rounds in range(1,5):
        for round_time,step_time in ((0,0),(20,5),(1000,100)):
            m=manager()
            response=asyncio.run(create_shanxi_room(m,"conn",room_name="配置矩阵",gameround=rounds,
                roundTimerValue=round_time,stepTimerValue=step_time,password="1234" if bits&1 else "",**options))
            assert response.success
            room=m.rooms["sx123"]
            assert all(room[key] is value for key,value in options.items())
            assert (room["game_round"],room["round_timer"],room["step_timer"])==(rounds,round_time,step_time)
            assert room["detailed_config"]=={"rule_version":VERSION}

@pytest.mark.parametrize("failure",["unauthenticated","already_in_room","active_game","event","settings","config"])
def test_creation_failure_does_not_mutate_room_or_player(failure):
    m=manager()
    kw={}
    if failure=="unauthenticated": m.game_server.players={}
    if failure=="already_in_room": m.game_server.players["conn"].current_room_id="other"
    if failure=="active_game": m._reject_room_entry_conflicts.return_value=Response(type="tips",success=False,message="测试拒绝")
    if failure=="event": m._validate_event_for_room.return_value=Response(type="tips",success=False,message="测试拒绝")
    if failure=="settings": m.game_server.db_manager.get_user_settings.return_value=None
    if failure=="config": kw["detailed_config"]={"joker":True}
    result=asyncio.run(create_shanxi_room(m,"conn",room_name="山西",**kw))
    assert not result.success and not m.rooms and not m.room_passwords
    m._broadcast_room_info.assert_not_awaited()

def test_websocket_adapter_and_event_room_use_same_fixed_profile():
    from .room_manager import RoomManager
    m=manager()
    m.game_server.room_manager=m
    socket=SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_shanxi_room(m.game_server,"conn",{"roomname":"山西"},socket))
    assert socket.send_json.await_args.args[0]["room_info"]["sub_rule"]==SUB_RULE
    real=RoomManager(m.game_server)
    real._normalize_event_id=lambda v:v
    real._validate_event_for_room=lambda *a:None
    real._apply_event_fields=lambda *a:None
    real._generate_room_id=lambda:"event-sx"
    real._broadcast_room_info=AsyncMock()
    result=asyncio.run(real.create_empty_event_room("event-id","shanxi",{"allow_spectator":False},broadcast=False))
    assert result.success and result.room_info["detailed_config"]=={"rule_version":VERSION}
    assert result.room_info["use_flowers"] is False and result.room_info["hepai_limit"]==0
    assert result.room_info["allow_spectator"] is False


@pytest.mark.parametrize("timers", [None,(20,5),(11,9),(0,9),(11,0),(0,0)])
def test_request_room_and_state_keep_the_same_timing_contract(timers):
    from ..gamestate.game_shanxi.ShanxiGameState import ShanxiGameState
    m=manager()
    m.game_server.room_manager=m
    m.game_server.gamestate_manager=SimpleNamespace()
    m.game_server.user_id_to_connection={}
    socket=SimpleNamespace(send_json=AsyncMock())
    message={"roomname":"时间合同","allow_spectator":False}
    if timers is not None:
        message.update(roundTimerValue=timers[0],stepTimerValue=timers[1])
    asyncio.run(handle_create_shanxi_room(m.game_server,"conn",message,socket))
    wire=socket.send_json.await_args.args[0]
    assert wire["success"]
    expected=timers or (20,5)
    room=m.rooms["sx123"]
    assert (room["round_timer"],room["step_timer"])==expected
    assert (wire["room_info"]["round_timer"],wire["room_info"]["step_timer"])==expected
    state=ShanxiGameState(m.game_server,{**room,"player_list":[101,102,103,104]},None,None,"sx-timing")
    assert (state.round_time,state.step_time)==expected
    assert all(p.remaining_time==expected[0] for p in state.player_list)


@pytest.mark.parametrize("timers", [(20,5),(11,9),(0,9),(11,0),(0,0)])
def test_empty_event_room_does_not_replace_configured_timers(timers):
    from .room_manager import RoomManager
    m=manager()
    real=RoomManager(m.game_server)
    real._normalize_event_id=lambda v:v
    real._validate_event_for_room=lambda *a:None
    real._apply_event_fields=lambda *a:None
    real._generate_room_id=lambda:"event-time"
    real._broadcast_room_info=AsyncMock()
    result=asyncio.run(real.create_empty_event_room("event-id","shanxi",
        {"round_timer":timers[0],"step_timer":timers[1]},broadcast=False))
    assert result.success
    assert (result.room_info["round_timer"],result.room_info["step_timer"])==timers
