import asyncio

from types import SimpleNamespace

import pytest

from server.gamestate.game_duocai.DuocaiGameState import DuocaiGameState
from server.gamestate.game_duocai.state_machine import Phase as P

@pytest.fixture(params=['guobiao','riichi'])
def active(request):
    room=DuocaiGameState._default_room_data()
    room.update(sub_rule='duocai/'+request.param,game_round=4 if request.param=='guobiao' else 2)
    state=DuocaiGameState(room_data=room)
    assert all(not p.is_bot for p in state.player_list)
    state.initialize_round()
    now=[100.0]
    state._timing_now=lambda:now[0]
    window=state.open_action_window(state.opening_window())
    return state,window,now

def cut(state):
    hand=state.player_list[0].hand_tiles
    return dict(action_type='cut',TileId=hand[-1],cutIndex=len(hand)-1,cutClass=True)

def test_clock_starts_once_for_current_sent_ask_and_reconnect_preserves_deadline(active):
    state,window,now=active
    clock=state._action_clocks[0]
    assert clock.started_at is None
    state._start_action_clock(0,{'action_tick':state.server_action_tick-1})
    assert clock.started_at is None
    packet=state._ask_payload(0,window)
    state._start_action_clock(0,packet)
    assert clock.started_at==100 and clock.deadline==125
    now[0]=109.25
    restored=state.build_pending_action_payload(0)
    state._refresh_clock_payload(0,restored)
    state._start_action_clock(0,restored)
    assert clock.deadline==125
    info=restored['game_info']['duocai_info']
    assert info['clock_remaining_ms']==15750 and info['clock_step_remaining_ms']==0

def test_on_time_receipt_survives_late_scheduler_without_extra_bank_charge(active):
    state,window,now=active
    state._start_action_clock(0)
    now[0]=107.25
    async def run():
        await state.submit_action(0,**cut(state))
        with pytest.raises(ValueError):await state.submit_action(0,**cut(state))
        now[0]=150
        responses=await state.wait_action()
        assert responses[0]['_receipt_time']==107.25
        assert state.player_list[0].remaining_time==pytest.approx(17.75)
        state.apply_action_results(window,responses)
    asyncio.run(run())

def test_exact_deadline_rejects_user_and_timeout_uses_draw_identity(active):
    state,window,now=active
    state._start_action_clock(0)
    now[0]=125
    expected=cut(state)
    async def run():
        with pytest.raises(ValueError):await state.submit_action(0,**expected)
        responses=await state.wait_action()
        response=responses[0]
        assert response['action_type']=='cut' and response['TileId']==expected['TileId']
        assert response['cutClass'] is True and response['is_timeout_action'] is True
        assert state.player_list[0].remaining_time==0
        state.apply_action_results(window,responses)
    asyncio.run(run())

def test_claim_timeout_passes_without_winning_or_calling(active):
    state,_,now=active
    window=state._window(P.RESPONSE,0,11,{0:[],1:['hu','pass'],2:['peng','pass'],3:[]})
    state.open_action_window(window)
    for i in (1,2):state._start_action_clock(i)
    now[0]=125
    responses=asyncio.run(state.wait_action())
    assert {i:r['action_type'] for i,r in responses.items()}=={1:'pass',2:'pass'}
    assert not state.waiting_players_list

def test_ready_timeout_has_no_play_action_or_time_bank_penalty(active):
    state,_,_=active
    state.machine.transition(P.END)
    state.machine.transition(P.READY)
    state.action_dict={0:[],1:['ready'],2:[],3:[]}
    state.waiting_players_list=[1]
    before=state.player_list[1].remaining_time
    responses=asyncio.run(state.wait_action(timeout=0))
    assert responses[1]['action_type']=='ready'
    assert state.player_list[1].remaining_time==before

def test_paused_or_unoffered_action_does_not_enter_queue(active):
    state,_,_=active
    async def run():
        with pytest.raises(ValueError):await state.submit_action(2,**cut(state))
        state.vote_manager=SimpleNamespace(phase='paused')
        with pytest.raises(ValueError):await state.submit_action(0,**cut(state))
        assert all(q.empty() for q in state.action_queues.values())
        assert state._action_clocks[0].received_at is None
    asyncio.run(run())

