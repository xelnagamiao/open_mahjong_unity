"""Competition bot addition: authorization, seats, paired launch and timers."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace as NS

import pytest
from fastapi import FastAPI

from .test_room_seats import manager
from .test_admin_rooms import request
from .test_sichuan_duplicate import rooms, prepare_pair
from .sichuan_duplicate import create_pair, request_start
from .room_seats import get_seats
from ..webapi.admin_room import register_admin_room_routes
from ..gamestate.game_sichuan.test_duplicate import run_async
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE


@pytest.mark.parametrize('sub_rule',['guobiao/standard','guobiao/lanshi','riichi/standard','riichi/sanma','sichuan/standard','sichuan/doubles','sichuan/xueliu_doubles'])
@run_async
async def test_admin_can_add_to_exact_empty_seat_without_being_a_room_player(sub_rule):
    rm=manager(); room=rm.rooms['123']
    room.update(event_id='evt', room_rule=sub_rule.split('/')[0],sub_rule=sub_rule)
    rm._validate_event_for_room=lambda *_:None
    assert (await rm.admin_add_event_bot('123','evt','test',2,2)).success
    assert room['seat_list']==[100,-1,2,-1]
    assert room['host_user_id']==100
    assert not (await rm.admin_add_event_bot('123','evt','test',0,2)).success
    assert room['player_list']==[100,2]


@pytest.mark.parametrize('mutation',[{'event_id':'other'},{'instance_id':'old'},{'is_game_running':True},{'event_seating_pending':True},{'admin_closing':True}])
@run_async
async def test_out_of_scope_stale_or_busy_room_never_changes_roster(mutation):
    rm=manager(); room=rm.rooms['123']
    room.update({'event_id':'evt',**mutation})
    rm._validate_event_for_room=lambda *_:None
    before=deepcopy(room)
    assert not (await rm.admin_add_event_bot('123','evt','test',2,2)).success
    assert room==before


@run_async
async def test_bot_endpoint_requires_signature_and_validates_types():
    rm=manager();rm.rooms['123']['event_id']='evt'
    rm._validate_event_for_room=lambda *_:None
    app=FastAPI();register_admin_room_routes(app,NS(room_manager=rm),proxy_secret='test-room-admin-secret')
    body=dict(event_id='evt',instance_id='test',seat_index=2,bot_type='smart')
    path='/admin/rooms/123/bots'
    for kwargs in [dict(signed=False),dict(origin='https://example.test'),dict(host='203.0.113.1'),dict(signed_path='/another/path')]:
        assert (await request(app,'POST',path,body,**kwargs))[0]==403
    for update in [dict(seat_index=True),dict(seat_index='2'),dict(seat_index=4),dict(bot_type='unknown')]:
        assert (await request(app,'POST',path,{**body,**update}))[0]==422
    assert rm.rooms['123']['player_list']==[100]
    assert (await request(app,'POST',path,body))[0]==200
    assert rm.rooms['123']['seat_list']==[100,-1,2,-1]


@run_async
async def test_mixed_tables_accept_repeated_bot_ids_as_separate_seats(rooms,monkeypatch):
    gm,pair,(a,b)=await prepare_pair(rooms,monkeypatch)
    for room in (a,b):
        room['seat_list'][1:]=[2,2,2]
        room['player_list']=room['seat_list'].copy()
        room['ready_list']=[]
    assert (await request_start(gm,a,'101')).success
    assert await request_start(gm,b,'105') is None
    await asyncio.sleep(0)
    assert gm.run_game.await_count==2
    assert [m['user_id'] for m in pair.data['members']['closed']]==[105,2,2,2]
    assert [m['seat'] for m in pair.data['members']['closed']]==[0,1,2,3]


@pytest.mark.parametrize('sub_rule',[SBR_DUPLICATE,XUELIU_DUPLICATE])
@run_async
async def test_one_human_can_start_real_pair_with_seven_bots(rooms,monkeypatch,sub_rule):
    from ..gamestate.test_room_lifecycle import make_server, parked_loop
    from ..gamestate.game_sichuan.DuplicateGameState import DuplicateMixin
    human=make_server([101]).players['101']
    server=rooms.game_server
    server.players['101']=human;server.user_id_to_connection[101]=human
    await create_pair(rooms,'evt_pair',sub_rule,dict(sub_rule=sub_rule,round_timer=127,step_timer=17),'',101)
    gm=server.gamestate_manager
    pair=next(iter(gm.sichuan_duplicate_matches.values()))
    a,b=list(rooms.rooms.values())
    a.update(player_list=[101],seat_list=[101,-1,-1,-1],host_user_id=101,player_settings={101:{'username':'test'}})
    human.current_room_id=a['room_id']
    for room in (a,b):
        for index,uid in enumerate(get_seats(room)):
            if uid<0:
                response=await rooms.admin_add_event_bot(room['room_id'],'evt_pair',room['instance_id'],2,index)
                assert response.success,response
    monkeypatch.setattr('server.database.duplicate_walls.reserve_duplicate_game',lambda *_:'reserved-id')
    monkeypatch.setattr(DuplicateMixin,'run_game_loop',parked_loop)
    try:
        rejected=await gm.start_game(None,b['room_id'],roster_revision=b['roster_revision'])
        assert not rejected.success
        assert await gm.start_game('101',a['room_id'],roster_revision=a['roster_revision']) is None
        await asyncio.sleep(0)
        assert pair.data['status']=='running' and pair.start_gate.is_set()
        assert len(pair.states)==2 and len(gm.gamestate_id_to_game_state)==2
        assert set(gm.user_id_to_game_state)=={101}
        for table,state in pair.states.items():
            assert len(state.player_list)==4
            assert [p.user_id for p in state.player_list]==([101,2,2,2] if table=='open' else [2,2,2,2])
            assert state.round_time==127 and state.step_time==17
    finally:
        for gid in list(gm.gamestate_id_to_game_state):
            await gm.cleanup_game_state_complete(gamestate_id=gid)
