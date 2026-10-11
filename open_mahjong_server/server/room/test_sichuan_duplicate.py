import asyncio
from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

import pytest

from .room_manager import RoomManager
from .room_seats import get_seats, move_duplicate_seat
from .room_teams import assign_team
from .sichuan_duplicate import create_pair, delete_pair, restart_pair, restore_pairs, request_start
from ..response import Response
from ..gamestate.game_sichuan.test_duplicate import run_async
from ..gamestate.gamestate_manager import GameStateManager
from ..gamestate.verifier.host import build_db_manager
from ..game_calculation.game_calculation_service import GameCalculationService
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE


@pytest.fixture
def rooms(monkeypatch):
    db = build_db_manager()
    db.get_event.return_value = dict(event_id='evt_pair', kind='event', status='active')
    server = NS(db_manager=db, players={}, user_id_to_connection={}, calculation_service=GameCalculationService())
    manager = RoomManager(server)
    server.room_manager = manager
    server.gamestate_manager = GameStateManager(server)
    manager._validate_event_for_room = lambda *_: None
    manager._broadcast_room_info = AsyncMock()
    manager.room_list_update = AsyncMock()
    saved = []
    def create(_store, snapshot, **_):
        snapshot.update(key='dup_'+'a'*32,wall_id=1,event_id='evt_pair')
        saved.append(deepcopy(snapshot))
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.create',create)
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.save',lambda _s,d,r: saved.append(deepcopy(d)))
    manager.saved = saved
    return manager


def seated_room():
    return dict(sub_rule=SBR_DUPLICATE, duplicate_table='open', player_list=[101,102,103], seat_list=[101,102,103,-1],
                host_user_id=101, roster_revision=7, ready_list=[102,103], duplicate_status='ready')


@pytest.mark.parametrize('actor,source,target',[(101,0,2),(101,1,3),(102,1,3),(101,0,0)])
def test_fixed_seat_changes_preserve_teams_and_host(actor,source,target):
    room=seated_room(); before=deepcopy(room)
    move_duplicate_seat(room,actor,source,target,7)
    expected=list(before['seat_list']); expected[source],expected[target]=expected[target],expected[source]
    assert room['seat_list']==expected and room['player_list']==before['player_list']
    assert room['team_by_seat']==[0,1,0,1] and room['host_user_id']==101
    assert room['roster_revision']==(7 if source==target else 8)
    assert room['ready_list']==(before['ready_list'] if source==target else [])


@pytest.mark.parametrize('patch,actor,source,target,revision',[
    ({'duplicate_table':None},101,0,1,7), ({'is_game_running':True},101,0,1,7),
    ({'event_seating_pending':True},101,0,1,7), ({},101,0,1,6),
    ({},101,True,1,7), ({},101,0,4,7), ({},101,3,1,7),
    ({},102,0,3,7), ({},102,1,0,7),
])
def test_invalid_seat_moves_are_atomic(patch,actor,source,target,revision):
    room=seated_room();room.update(patch);before=deepcopy(room)
    with pytest.raises(ValueError): move_duplicate_seat(room,actor,source,target,revision)
    assert room['seat_list']==before['seat_list'] and room['ready_list']==before['ready_list']
    assert room['roster_revision']==before['roster_revision']


@run_async
async def test_seat_move_api_membership_and_broadcast(rooms):
    room=seated_room();rooms.rooms['1']=room
    server=rooms.game_server;server.players['host']=NS(user_id=101)
    assert not (await rooms.move_duplicate_seat('stranger','1',revision=7,seat_index=0,target_seat=1)).success
    assert not (await rooms.move_duplicate_seat('host','1',revision=6,seat_index=0,target_seat=1)).success
    assert await rooms.move_duplicate_seat('host','1',revision=7,seat_index=0,target_seat=1) is None
    assert room['seat_list']==[102,101,103,-1]
    rooms._broadcast_room_info.assert_awaited_once_with('1')


@pytest.mark.parametrize('sub',[SBR_DUPLICATE,XUELIU_DUPLICATE])
@run_async
async def test_four_player_seating_cannot_create_half_a_pair(rooms,sub):
    result=await rooms._seat_event_table_locked(101,'evt_pair',[101,102,103,104],'sichuan',{'sub_rule':sub},automatic=False)
    assert not result.success and '成对创建' in result.message and not rooms.rooms


@pytest.mark.parametrize('sub',[SBR_DUPLICATE,XUELIU_DUPLICATE])
@pytest.mark.parametrize('count',[1,4,8,12,16])
@run_async
async def test_admin_pair_creation_is_atomic_fixed_seats_private(rooms,sub,count):
    response = await create_pair(rooms,'evt_pair',sub,dict(sub_rule=sub,duplicate_round_count=count),'pw',101)
    assert response.success and len(rooms.rooms)==2
    assert 'duplicate_key' not in response.model_dump()['room_info']
    open_room,closed_room = list(rooms.rooms.values())
    assert open_room['team_by_seat']==[0,1,0,1] and closed_room['team_by_seat']==[1,0,1,0]
    assert open_room['duplicate_peer_room_id']==closed_room['room_id']
    assert not open_room['allow_spectator'] and not closed_room['allow_spectator']
    assert all(r['duplicate_round_count']==count and get_seats(r)==[-1]*4 for r in rooms.rooms.values())
    with pytest.raises(ValueError,match='固定'):
        assign_team(open_room,101,0,1,0)


@pytest.mark.parametrize('bad',[0,2,3,17,True,'8',None])
@run_async
async def test_invalid_count_does_not_create_either_room(rooms,bad):
    result=await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE,duplicate_round_count=bad),'',101)
    assert not result.success and not rooms.rooms and not rooms.saved


@pytest.mark.parametrize('config',[{'random_seed':1},{'duplicate_key':'dup_x'}])
@run_async
async def test_external_walls_rejected(rooms,config):
    result=await create_pair(rooms,'evt_pair',SBR_DUPLICATE,config,'',101)
    assert not result.success and not rooms.rooms


@run_async
async def test_database_failure_rolls_back_both_rooms_before_broadcast(rooms,monkeypatch):
    def fail(*_,**__): raise OSError('database unavailable')
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.create',fail)
    with pytest.raises(OSError):
        await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    assert not rooms.rooms and not rooms.room_passwords
    rooms._broadcast_room_info.assert_not_awaited()


@pytest.mark.parametrize('sub',[SBR_DUPLICATE,XUELIU_DUPLICATE])
@run_async
async def test_regular_create_rejected_even_without_login(rooms,sub):
    result=await rooms.create_Sichuan_room('missing','x',1,'',20,5,True,sub_rule=sub)
    assert not result.success and '只能在比赛场' in result.message and not rooms.rooms


@pytest.mark.parametrize('status',['completed','cancelled'])
@run_async
async def test_finished_pair_cannot_be_reused_for_another_match(rooms,status):
    rooms.rooms['1']={'duplicate_match_id':'done','duplicate_status':status}
    result=await rooms.join_room('any','1','')
    assert not result.success and '已结束' in result.message


@pytest.mark.parametrize('busy',['is_game_running','event_seating_pending',None])
@run_async
async def test_delete_pair_checks_peer_and_cascades(rooms,busy):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    a,b=list(rooms.rooms.values())
    if busy: b[busy]=True
    result=await delete_pair(rooms,a)
    assert result.success == (busy is None)
    assert len(rooms.rooms)==(2 if busy else 0)
    if not busy: assert rooms.saved[-1]['status']=='cancelled'


@run_async
async def test_restart_preserves_completed_scores_and_regenerates_both(rooms):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    pair=next(iter(rooms.game_server.gamestate_manager.sichuan_duplicate_matches.values()))
    pair.data['status']='running'
    await asyncio.gather(pair.submit('open',1,[3,-3],[0,0],{},{}),pair.submit('closed',1,[0,0],[0,0],{},{}))
    previous=deepcopy(pair.data)
    pair.interrupt('test')
    room=next(iter(rooms.rooms.values()))
    result=await restart_pair(rooms,room['room_id'],'evt_pair')
    assert result['success'] and pair.data['status']=='waiting'
    assert pair.data['boards'][0]==previous['boards'][0] and pair.data['boards'][1]!=previous['boards'][1]
    assert pair.data['scores']==previous['scores'] and pair.data['completed_boards']==1
    assert pair.data['pending']=={} and pair.data['checkpoints']=={'open':{},'closed':{}}


@run_async
async def test_restart_rejects_wrong_event_or_live_peer(rooms):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    a,b=list(rooms.rooms.values())
    with pytest.raises(ValueError,match='不属于'):
        await restart_pair(rooms,a['room_id'],'evt_other')
    b['is_game_running']=True
    with pytest.raises(ValueError,match='清理'):
        await restart_pair(rooms,a['room_id'],'evt_pair')


@run_async
async def test_process_recovery_never_resumes_an_exposed_hand(rooms,monkeypatch):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'pw',101)
    snapshot=deepcopy(rooms.saved[-1]); snapshot['status']='running'
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.unfinished',lambda _: [snapshot])
    rooms.rooms.clear()
    restore_pairs(rooms.game_server)
    pair=rooms.game_server.gamestate_manager.sichuan_duplicate_matches[snapshot['id']]
    assert pair.data['status']=='interrupted'
    assert len(rooms.rooms)==2
    assert all(r['seat_list']==[-1]*4 and not r['is_game_running'] and r['duplicate_status']=='interrupted' for r in rooms.rooms.values())


async def prepare_pair(rooms,monkeypatch):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    gm=rooms.game_server.gamestate_manager
    pair=next(iter(gm.sichuan_duplicate_matches.values()))
    for t,room in enumerate(rooms.rooms.values()):
        ids=list(range(101+4*t,105+4*t))
        room.update(player_list=ids.copy(),seat_list=ids.copy(),host_user_id=ids[0],ready_list=ids[1:],roster_revision=1)
        rooms.game_server.user_id_to_connection[ids[0]]=NS(Connect_id=str(ids[0]),user_id=ids[0])
    monkeypatch.setattr('server.database.duplicate_walls.reserve_duplicate_game',lambda *_:'reserved-id')
    async def start(_,rid,**__):
        room=rooms.rooms[rid];room['is_game_running']=True
        pair.states[room['duplicate_table']]=NS(gamestate_id=rid,run_game_loop=AsyncMock())
    gm.start_game=AsyncMock(side_effect=start)
    gm.run_game=AsyncMock()
    gm.cleanup_game_state_complete=AsyncMock()
    return gm,pair,list(rooms.rooms.values())


@run_async
async def test_both_hosts_ready_gate_and_single_launch(rooms,monkeypatch):
    gm,pair,(a,b)=await prepare_pair(rooms,monkeypatch)
    result=await request_start(gm,a,'101')
    assert result.success and pair.data['status']=='waiting' and not pair.start_gate.is_set()
    gm.start_game.assert_not_awaited()
    assert await request_start(gm,b,'105') is None
    await asyncio.sleep(0)
    assert pair.data['status']=='running' and pair.start_gate.is_set() and gm.run_game.await_count==2
    assert len(pair.data['members']['open'])==len(pair.data['members']['closed'])==4
    assert not (await request_start(gm,a,'101')).success
    assert gm.start_game.await_count==2


@run_async
async def test_resume_keeps_original_eight_seats_and_reserved_record_ids(rooms,monkeypatch):
    gm,pair,(a,b)=await prepare_pair(rooms,monkeypatch)
    pair.data['completed_boards']=1
    pair.data['members']={t:[{'user_id':uid} for uid in room['seat_list']] for t,room in zip(('open','closed'),(a,b))}
    a['duplicate_game_id']='keep-open';b['duplicate_game_id']='keep-closed'
    await request_start(gm,a,'101');assert await request_start(gm,b,'105') is None
    await asyncio.sleep(0)
    assert a['duplicate_game_id']=='keep-open' and b['duplicate_game_id']=='keep-closed'


@pytest.mark.parametrize('mode',['duplicate_user','bot','offline_host','wrong_resume','prepare_error','cancelled','changed_roster'])
@run_async
async def test_pair_start_failures_never_launch_one_table(rooms,monkeypatch,mode):
    gm,pair,(a,b)=await prepare_pair(rooms,monkeypatch)
    if mode=='duplicate_user':b['seat_list'][2]=a['seat_list'][2]
    if mode=='bot':b['seat_list'][2]=2
    if mode=='offline_host':rooms.game_server.user_id_to_connection.pop(105)
    if mode=='wrong_resume':
        pair.data.update(completed_boards=1,members={'open':[{'user_id':999}]*4,'closed':[{'user_id':999}]*4})
    original=gm.start_game.side_effect
    async def start(conn,rid,**kwargs):
        if rid==b['room_id']:
            if mode=='prepare_error':return Response(type='tips',success=False,message='test preparation failed')
            if mode=='cancelled':raise asyncio.CancelledError()
        await original(conn,rid,**kwargs)
        if mode=='changed_roster':b['roster_revision']+=1
    gm.start_game.side_effect=start
    assert (await request_start(gm,a,'101')).success
    if mode=='cancelled':
        with pytest.raises(asyncio.CancelledError):await request_start(gm,b,'105')
    else:
        result=await request_start(gm,b,'105');assert not result.success
    assert pair.data['status']=='interrupted'
    gm.run_game.assert_not_awaited()
    assert gm.cleanup_game_state_complete.await_count==len(pair.states)


@run_async
async def test_stale_peer_readiness_requires_new_host_start(rooms,monkeypatch):
    gm,pair,(a,b)=await prepare_pair(rooms,monkeypatch)
    await request_start(gm,a,'101')
    a['roster_revision']+=1
    assert (await request_start(gm,b,'105')).success
    assert set(pair.ready)=={'closed'} and a['duplicate_status']=='waiting'
    gm.start_game.assert_not_awaited()
    assert await request_start(gm,a,'101') is None
    await asyncio.sleep(0)
    assert pair.data['status']=='running'


@run_async
async def test_missing_pair_or_peer_fails_without_mutation(rooms,monkeypatch):
    gm,pair,(a,b)=await prepare_pair(rooms,monkeypatch)
    rooms.rooms.pop(b['room_id'])
    assert not (await request_start(gm,a,'101')).success
    gm.sichuan_duplicate_matches.clear()
    assert not (await request_start(gm,a,'101')).success
    assert not (await delete_pair(rooms,a)).success
    gm.start_game.assert_not_awaited()


@run_async
async def test_non_event_pair_and_internal_second_creation_failure(rooms):
    rooms.game_server.db_manager.get_event.return_value={'kind':'base'}
    assert not (await create_pair(rooms,'evt_pair',SBR_DUPLICATE,{},'',101)).success
    rooms.game_server.db_manager.get_event.return_value={'kind':'event'}
    original=rooms.create_empty_event_room
    async def create(*args,**kwargs):
        if rooms.rooms:return Response(type='tips',success=False,message='no capacity')
        return await original(*args,**kwargs)
    rooms.create_empty_event_room=create
    with pytest.raises(ValueError,match='no capacity'):await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    assert not rooms.rooms


@run_async
async def test_completed_restore_repairs_missing_record_and_delete_preserves_result(rooms,monkeypatch):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    snapshot=deepcopy(rooms.saved[-1]);snapshot.update(status='completed',record_ids={'open':'saved-open'},
        final_records={'open':{},'closed':{'game_title':{}}},final_players={'closed':[{'user_id':105,'username':'p','score':0,'original_player_index':0,'rank':1}]})
    deleted=deepcopy(snapshot);deleted['rooms_deleted']=True
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.unfinished',lambda _:[deleted,snapshot])
    rooms.game_server.db_manager.store_sichuan_game_record.return_value='saved-closed'
    rooms.rooms.clear();restore_pairs(rooms.game_server)
    pair=rooms.game_server.gamestate_manager.sichuan_duplicate_matches[snapshot['id']]
    assert pair.data['record_ids']=={'open':'saved-open','closed':'saved-closed'}
    assert (await delete_pair(rooms,next(iter(rooms.rooms.values())))).success
    assert pair.data['status']=='completed' and pair.data['rooms_deleted']


@run_async
async def test_completed_barrier_restores_both_records_before_save_hooks(rooms,monkeypatch):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    snapshot=deepcopy(rooms.saved[-1]);snapshot.update(status='completed',completed_boards=1,scores=[1,0],checkpoints={})
    for table in ('open','closed'):
        snapshot['checkpoints'][table]={'game_record':{'game_title':{'end_time':'saved-time'},'game_round':{}},
            'players':[dict(user_id=101+i,username='test',score=0,team_id=i%2,original_player_index=i) for i in range(4)]}
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.unfinished',lambda _:[snapshot])
    rooms.game_server.db_manager.store_sichuan_game_record.side_effect=['record-open','record-closed']
    restore_pairs(rooms.game_server)
    saved=rooms.saved[-1]
    assert saved['record_ids']=={'open':'record-open','closed':'record-closed'}
    assert set(saved['final_records'])=={'open','closed'}
    assert all(record['game_title']['end_time']=='saved-time' for record in saved['final_records'].values())


@run_async
async def test_incomplete_legacy_completed_snapshot_does_not_fabricate_records(rooms,monkeypatch):
    await create_pair(rooms,'evt_pair',SBR_DUPLICATE,dict(sub_rule=SBR_DUPLICATE),'',101)
    snapshot=deepcopy(rooms.saved[-1]);snapshot['status']='completed'
    monkeypatch.setattr('server.room.sichuan_duplicate.PairedMatchStore.unfinished',lambda _:[snapshot])
    restore_pairs(rooms.game_server)
    rooms.game_server.db_manager.store_sichuan_game_record.assert_not_called()
