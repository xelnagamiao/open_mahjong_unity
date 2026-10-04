import pytest
from server.gamestate.game_guizhou.clock import ActionClock
from server.gamestate.game_guizhou import clock as module
from server.gamestate.game_guizhou.test_flow import configured_turn, act
from server.gamestate.game_guizhou.actions import claim_actions
from server.gamestate.game_guizhou.state_machine import Phase as P


def test_clock_preserves_fractional_budget_across_selection_and_resume(monkeypatch):
    now=[100.0]
    monkeypatch.setattr(module.time,'monotonic',lambda:now[0])
    c=ActionClock(5,10)
    assert c.display()==(10,5) and c.remaining()==15
    c.start(5,10);now[0]+=3.2;c.stop()
    assert c.display()==(10,2) and c.remaining()==pytest.approx(11.8)
    now[0]+=100
    assert c.remaining()==pytest.approx(11.8)  # Waiting for a new ask is paused.
    c.start(999,999);now[0]+=2.9
    assert c.display()==(9,0) and c.remaining()==pytest.approx(8.9)
    c.start(999,999)  # Reentry cannot reset an already-running clock.
    assert c.remaining()==pytest.approx(8.9)
    now[0]+=20;c.stop()
    assert c.display()==(0,0) and c.remaining()==0


def test_clock_clamps_disabled_budgets_and_does_not_mutate_on_display():
    c=ActionClock(-1,-3)
    before=vars(c).copy()
    assert c.remaining()==0 and c.display()==(0,0)
    assert vars(c)==before


def test_ready_selection_carries_clock_but_real_discard_creates_new_clocks():
    s=configured_turn();s.player_list[0].discard_count=0
    s.open_action_window(s.begin_turn(0))
    c=s.live_pending_window['_clocks'][0]
    act(s,0,'guizhou_ready')
    assert s.live_pending_window['_clocks'][0] is c
    act(s,0,'guizhou_ready_cancel')
    assert s.live_pending_window['_clocks'][0] is c
    act(s,0,'cut',TileId=28,cutClass=True,cutIndex=13)
    assert all(clock is not c for clock in s.live_pending_window['_clocks'].values())


def test_claim_reconnect_reports_clock_without_changing_authoritative_bank(monkeypatch):
    s=configured_turn();s.step_time=5
    p=s.player_list[1];p.hand_tiles=[31,31,11,12,14,16,18,21,24,27,33,35,39];p.remaining_time=10
    s.open_action_window(s._window(P.RESPONSE,0,31,claim_actions(s,0,31)))
    now=[100.0];monkeypatch.setattr(module.time,'monotonic',lambda:now[0])
    c=s.live_pending_window['_clocks'][1];c.start(5,10);now[0]+=7.2
    payload=s.build_pending_action_payload(1)['ask_other_action_info']
    assert payload['remaining_time']==8 and payload['step_remaining']==0
    assert p.remaining_time==10 and c.remaining()==pytest.approx(7.8)


"""Regression probes against the active or staged Guizhou implementation."""
import asyncio
from server.gamestate.game_guizhou.test_flow import configured_turn

def test_ready_selection_cannot_refresh_the_step_budget_indefinitely():
    async def run():
        s=configured_turn();s.step_time=1
        p=s.player_list[0];p.remaining_time=0;p.discard_count=0
        s.open_action_window(s.begin_turn(0))
        timed_out=False
        for n in range(5):
            task=asyncio.create_task(s.wait_action())
            await asyncio.sleep(.35)
            if task.done():
                responses=await task
                assert responses[0]['action_type']=='cut'
                timed_out=True;break
            action='guizhou_ready' if n%2==0 else 'guizhou_ready_cancel'
            await s.submit_action(0,action)
            s.apply_action_results(s.live_pending_window,await task)
        assert timed_out,'Every ready/cancel click incorrectly grants another full step budget'
    asyncio.run(run())

def test_reconnect_ask_reports_the_remaining_step_not_a_fresh_step():
    async def run():
        s=configured_turn();s.step_time=1;s.player_list[0].remaining_time=2
        s.open_action_window(s.begin_turn(0))
        task=asyncio.create_task(s.wait_action())
        try:
            await asyncio.sleep(1.1)
            ask=s.build_pending_action_payload(0)['ask_hand_action_info']
            assert ask.get('step_remaining')==0,'Reconnect would restart the client step display'
            assert 0<ask['remaining_time']<=2
        finally:
            task.cancel();await asyncio.gather(task,return_exceptions=True)
    asyncio.run(run())
