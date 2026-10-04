import asyncio
from unittest.mock import AsyncMock
from types import SimpleNamespace as NS
import pytest
from pydantic import ValidationError
from .test_bot_speed import manager
from .room_router import handle_room_message, handle_create_Riichi_room
from .room_validators import GBRoomValidator, RiichiRoomValidator, SichuanRoomValidator, ChangshaRoomValidator, JiandanRoomValidator
from ..gamestate.public.claim_protection import CLAIM_PROTECTION_RULES


@pytest.mark.parametrize('rule',sorted(CLAIM_PROTECTION_RULES))
@pytest.mark.parametrize('enabled',[False,True])
def test_host_can_toggle_and_room_broadcast_is_authoritative(rule,enabled):
    async def run():
        m=manager();m.rooms['123']['room_rule']=rule
        m.game_server.room_manager=m
        socket=NS(send_json=AsyncMock())
        await handle_room_message(m.game_server,'host',dict(type='room/set_claim_protection',room_id='123',claim_protection=enabled),socket)
        assert m.rooms['123']['claim_protection'] is enabled
        assert m.rooms['123']['ready_list']==[101]
        m._broadcast_room_info.assert_awaited_once_with('123')
        socket.send_json.assert_not_awaited()
    asyncio.run(run())


@pytest.mark.parametrize('who,room,rule,running,value',[
    ('guest','123','riichi',False,True),('outside','123','guobiao',False,False),
    ('missing','123','qingque',False,True),('host','missing','riichi',False,True),
    ('host','123','riichi',True,False),('host','123','taiwan',False,True),
    ('host','123','shanghai',False,True),('host','123','classical',False,True),
    ('host','123','hongque',False,True),('host','123','hongkong',False,True),
    ('host','123','free',False,True),('host','123','riichi',False,'false'),
    ('host','123','riichi',False,1),('host','123','riichi',False,None),
    ('host','123','riichi',False,[]),
])
def test_unauthorized_or_invalid_toggle_does_not_mutate(who,room,rule,running,value):
    m=manager();m.rooms['123'].update(room_rule=rule,is_game_running=running)
    result=asyncio.run(m.set_claim_protection(who,room,value))
    assert not result.success and 'claim_protection' not in m.rooms['123']
    m._broadcast_room_info.assert_not_awaited()


@pytest.mark.parametrize('cls',[GBRoomValidator,RiichiRoomValidator,SichuanRoomValidator,ChangshaRoomValidator,JiandanRoomValidator])
@pytest.mark.parametrize('value',[True,False,'true','false',0,1,None,[]])
def test_room_config_accepts_only_actual_switch_values(cls,value):
    data=dict(room_name='test',game_round=1,round_timer=20,step_timer=5,claim_protection=value)
    if type(value) is bool: assert cls(**data).claim_protection is value
    else:
        with pytest.raises(ValidationError):cls(**data)


@pytest.mark.parametrize('value',[None,False,True])
def test_riichi_creation_router_forwards_switch_with_legacy_default_off(value):
    async def run():
        server=NS(players={},create_Riichi_room=AsyncMock(return_value=NS(dict=lambda **kw:{})))
        socket=NS(send_json=AsyncMock())
        msg=dict(roomname='test',gameround=1,password='',roundTimerValue=20,stepTimerValue=5,tips=False)
        if value is not None:msg['claim_protection']=value
        await handle_create_Riichi_room(server,'host',msg,socket)
        assert server.create_Riichi_room.await_args.kwargs['claim_protection'] is (value is True)
    asyncio.run(run())

@pytest.mark.parametrize('rule,method,sub_rule',[
    ('guobiao','GB','guobiao/standard'),('guobiao','GB','guobiao/blood_battle'),
    ('qingque','Qingque','qingque/standard'),('changsha','Changsha','changsha/classic_double_bird'),
    ('sichuan','Sichuan','sichuan/standard'),('sichuan','Sichuan','sichuan/xueliu'),
    ('zhongyong','Jiandan','zhongyong/standard'),('zhongyong','Jiandan','zhongyong/nanque'),
    ('riichi','Riichi','riichi/standard'),('riichi','Riichi','riichi/langyong'),
])
@pytest.mark.parametrize('enabled',[False,True])
@pytest.mark.parametrize('event',[False,True])
def test_create_and_event_paths_keep_the_selected_switch(rule,method,sub_rule,enabled,event):
    from .test_guobiao_flowers import room_manager
    async def run():
        m=room_manager()
        m.game_server.db_manager.get_event_admin_role=lambda *_: "owner"
        if event and rule=='zhongyong':
            # This family uses direct event creation; the empty-admin-room API
            # does not offer it yet.
            result=await getattr(m,'create_'+method+'_room')('connection','protection',1,'',20,5,False,sub_rule=sub_rule,claim_protection=enabled,event_id='event1')
        elif event:
            result=await m.create_empty_event_room('event1',rule,dict(sub_rule=sub_rule,claim_protection=enabled),broadcast=False)
        else:
            result=await getattr(m,'create_'+method+'_room')('connection','protection',1,'',20,5,False,sub_rule=sub_rule,claim_protection=enabled)
        assert result.success,result.message
        assert result.room_info['claim_protection'] is enabled
    asyncio.run(run())

@pytest.mark.parametrize('rule,expected',[('guobiao',True),('qingque',True),('changsha',True),('sichuan',True),('zhongyong',True),('riichi',False)])
def test_legacy_room_snapshot_displays_the_same_default_as_game(rule,expected):
    from ..response import public_room_data
    room=dict(room_rule=rule)
    assert public_room_data(room)['claim_protection'] is expected
    assert 'claim_protection' not in room
    room['claim_protection']=not expected
    assert public_room_data(room)['claim_protection'] is not expected


@pytest.mark.parametrize('enabled', [False, True])
@pytest.mark.parametrize('method,uid', [
    ('add_bot_to_room', 0), ('add_smart_bot_to_room', 2), ('add_guobiao_heuristic_bot_to_room', 3),
])
def test_adding_and_removing_last_bot_updates_every_client_without_losing_preference(enabled, method, uid):
    from .test_room_seats import manager as seat_manager
    from ..response import public_room_data
    async def run():
        m = seat_manager()
        del m._broadcast_room_info
        room = m.rooms['123']
        room['claim_protection'] = enabled
        assert (await m.join_room('101', '123', '')).success
        for seat in (2, 3):
            assert (await getattr(m, method)('100', '123', seat)).success
            assert room['claim_protection'] is enabled
            for human in (100, 101):
                payload = m.game_server.user_id_to_connection[human].websocket.send_json.await_args.args[0]
                assert payload['room_info']['claim_protection'] is False
        assert (await m.kick_player_from_room('100', '123', uid, 2)).success
        assert public_room_data(room)['claim_protection'] is False
        assert (await m.kick_player_from_room('100', '123', uid, 3)).success
        for human in (100, 101):
            payload = m.game_server.user_id_to_connection[human].websocket.send_json.await_args.args[0]
            assert payload['room_info']['claim_protection'] is enabled
        assert room['claim_protection'] is enabled
    asyncio.run(run())


def test_empty_seats_and_offline_humans_do_not_disable_protection():
    from ..gamestate.public.claim_protection import claim_protection_enabled, room_claim_protection_enabled
    room = dict(room_rule='guobiao', claim_protection=True, player_list=[101], seat_list=[101, -1, -1, -1])
    assert room_claim_protection_enabled(room)
    state = NS(claim_protection=True, player_list=[NS(user_id=101, tag_list=['offline'])])
    assert claim_protection_enabled(state)
