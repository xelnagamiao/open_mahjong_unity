import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock,MagicMock,patch
import pytest
from .test_flow import state,arrange,apply
from .MalaysiaGameState import MalaysiaGameState
from .phases import P,Machine
from .get_action import handle_action
from ...game_calculation.malaysia import rules as R
from ...room.malaysia_room import MalaysiaRoomValidator,create_malaysia_room
from ...database.shared_record_metrics import analyze_shared_record_for_player
from ...database.subrule_fans import collect_subrule_fans
from ...match.test_lifecycle import server_fixture

@pytest.mark.parametrize('fields',[{'game_round':0},{'game_round':5},{'sub_rule':'malaysia/other'},{'use_flowers':False},{'claim_protection':True},{'tips':1},{'detailed_config':{'multiple_max':1}}])
def test_room_validation(fields):
    with pytest.raises(ValueError):MalaysiaRoomValidator(**{'room_name':'QA','game_round':1,'round_timer':20,'step_timer':5,**fields})

def test_room_creation_join_three_and_route_registry():
    async def run():
        server=server_fixture();server.match_manager.committed_users.clear();server.match_manager.winning_queues.clear()
        users=list(server.user_id_to_connection)
        server.db_manager.get_user_settings.return_value={'username':'QA'}
        r=await create_malaysia_room(server.room_manager,str(users[0]),room_name='MY',gameround=1)
        assert r.success and r.room_info['max_player']==3
        room=r.room_info['room_id']
        for uid in users[1:3]:assert (await server.room_manager.join_room(str(uid),room,'')).success
        assert not (await server.room_manager.join_room(str(users[3]),room,'')).success
        with patch.object(MalaysiaGameState,'run_game_loop',new=AsyncMock()):
            for uid in users[1:3]:await server.room_manager.set_player_ready(str(uid),room,True)
            response=await server.gamestate_manager.start_game(str(users[0]),room)
            assert response is None,getattr(response,'message','')
            game=server.gamestate_manager.get_game_state_by_room_id(room)
            assert isinstance(game,MalaysiaGameState)
            await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
        assert room not in server.gamestate_manager.room_id_to_MalaysiaGameState
    asyncio.run(run())

def test_authenticated_seat_stale_tick_duplicate_and_outsider():
    async def run():
        s=state();s.open_action_window(s.opening_window())
        seat=next(i for i,a in s.action_dict.items() if a);p=s.player_list[seat]
        ws=SimpleNamespace(send_json=AsyncMock())
        server=SimpleNamespace(players={'me':SimpleNamespace(user_id=p.user_id)},gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:s))
        action=s.automatic_action(seat)
        message=dict(type='gamestate/malaysia/send_action',gamestate_id=s.gamestate_id,action_tick=s.server_action_tick,action=action['action_type'],player_index=2)
        await handle_action(server,'unknown',message,ws);assert s.action_queues[seat].empty()
        await handle_action(server,'me',{**message,'action_tick':-1},ws);assert s.action_queues[seat].empty()
        await handle_action(server,'me',message,ws);assert s.action_queues[seat].qsize()==1
        await handle_action(server,'me',message,ws);assert s.action_queues[seat].qsize()==1
        assert ws.send_json.call_args.args[0]['success'] is False
    asyncio.run(run())

def test_hints_private_and_concealed_kong_projection(monkeypatch):
    s=state();arrange(s,[[21,22,23,24,25,26,27,28,29,29],[22],[23]])
    s.tips=True;p=s.player_list[0]
    p.melds=[R.Meld('kong',(45,)*4,(45,)*4,concealed=True)];p.combination_tiles=['G45'];p.combination_mask=[[2,45]*4]
    monkeypatch.setattr(R, 'waits', lambda *a, **k: pytest.fail('private hints must not search on the server'))
    for viewer in range(3):
        payload=s.build_game_start_payload(viewer)['game_info']
        assert payload['self_hand_tiles']==s.player_list[viewer].hand_tiles
        assert payload['malaysia_hints']['source_hand_tiles']==s.player_list[viewer].hand_tiles
        assert list(payload['malaysia_info']['melds'][0][0]['physical'])==([45]*4 if viewer==0 else [0]*4)
    first=s.client_context(0)
    assert first == s.client_context(0)
    assert not {'waiting_tiles', 'waiting_by_discard'} & first.keys()
    assert list(first['local_request']['context']['meld_records'][0]['physical']) == [45]*4
    p.hand_tiles.append(29)
    assert len(first['source_hand_tiles']) == 10  # Saved inputs cannot change later.

def test_actual_loop_three_rounds_and_exact_once_persistence():
    async def run():
        data=MalaysiaGameState._default_room_data();data['player_list']=[1,2,3]
        s=MalaysiaGameState(room_data=data,calculation_service=object())
        with patch('server.gamestate.game_malaysia.runtime.wait_bot_delay',new=AsyncMock()),patch('server.gamestate.game_malaysia.runtime.bot_delay',return_value=0):
            await asyncio.wait_for(s.run_game_loop(),15)
        assert s.machine.phase==P.FINISHED and s.lifecycle_completed
        assert len(s.game_record['game_round'])==3
        before=len(s.outbound_payloads);await s.complete_game_lifecycle();assert len(s.outbound_payloads)==before
        db=MagicMock();db.store_jiandan_game_record.return_value='saved';s.db_manager=db
        assert s.persist_game_record()=='saved' and s.persist_game_record()=='saved'
        db.store_jiandan_game_record.assert_called_once()
        assert db.store_jiandan_game_record.call_args.args[-1]=='1/3'
    asyncio.run(run())

def test_metrics_include_instant_payments_and_correct_discarder():
    s=state();arrange(s,[[29,21],[24],[22,23,24,25,26,27,28,28,28,29,29,29,21]])
    s.pay_instant(1,[0,2],50,'花套')
    s.player_list[0].hand_tiles.remove(21);s.player_list[0].discard_tiles=[21]
    s.machine.phase=P.RESPONSE;s.settle_win(2,'discard',21,payer=0);s.finalize_round_recording()
    results=[analyze_shared_record_for_player(s.game_record,i) for i in range(3)]
    assert [x['deal_in_count'] for x in results]==[1,0,0]
    assert [x['win_count'] for x in results]==[0,0,1]
    assert [x['total_round_score'] for x in results]==[p.score for p in s.player_list]
    assert all(x['total_fan_score']==x['total_fangchong_score']==0 for x in results)
    assert all(collect_subrule_fans(s.game_record,i,R.SUB_RULE) is None for i in range(3))

def test_initial_four_fly_and_timeout_draw():
    s=state();arrange(s,[[81]*4+[21,22,24,25,27,28,41,42,43,44],[22],[23]])
    s.opening=True;s.opening_interrupted=False;s.machine.phase=P.START
    s.open_action_window(s.opening_window())
    assert s.machine.phase==P.END
    assert s.build_final_settlement_payload(0)['show_result_info']['hu_class']=='initial_hu'
    assert sum(p.score for p in s.player_list)==0

def test_liability_and_illegal_transition():
    s=state();p=s.player_list[1]
    p.melds=[R.Meld('triplet',(t,)*3,(t,)*3,0) for t in (45,46)]
    s.remember_liability(1,2,47);assert p.liability==2
    p.liability=None;p.melds=[R.Meld('triplet',(t,)*3,(t,)*3,0) for t in (41,42,43)]
    s.remember_liability(1,2,44);assert p.liability==2
    with pytest.raises(ValueError):Machine().transition(P.READY)
