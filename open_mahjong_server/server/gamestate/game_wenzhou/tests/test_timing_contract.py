"""Room-configured clocks through real Wenzhou actions; no wall-clock waits."""
import asyncio
import copy
from types import SimpleNamespace

import pytest

from .. import session
from ..WenzhouGameState import WenzhouGameState
from ..actions import empty_actions
from ..state_machine import Phase as P
from .helpers import STANDARD, SocketRecorder, fixture_state, make_state
from .test_flow import JUNK, open_claim, open_turn


class ControlledClock:
    def __init__(self):
        self.now = 100.0

    def __call__(self):
        return self.now

    def advance(self, seconds):
        self.now += seconds


@pytest.fixture
def clock(monkeypatch):
    value = ControlledClock()
    # Replace this module's clock, not asyncio's process-wide monotonic source.
    monkeypatch.setattr(session, "time", SimpleNamespace(monotonic=value))
    return value


async def collect(state, clock, monkeypatch, callbacks, *, timeout=None):
    script = iter(callbacks)
    budgets = []

    async def wait(tasks, *, timeout, return_when):
        budgets.append(timeout)
        callback = next(script)
        await callback(timeout)
        await asyncio.sleep(0)
        return {t for t in tasks if t.done()}, {t for t in tasks if not t.done()}

    shim = SimpleNamespace(create_task=asyncio.create_task, wait=wait, gather=asyncio.gather,
                           FIRST_COMPLETED=asyncio.FIRST_COMPLETED)
    with monkeypatch.context() as scoped:
        scoped.setattr(session, "asyncio", shim)
        result = await state.wait_action(timeout)
    return result, budgets


def timed_state(bank=20, step=5, phase=P.TURN):
    if phase == P.DISCARD_ONLY:
        state = fixture_state({0:JUNK},phase=P.RESPONSE,drawn=True,
            melds={0:[("k11",[11,11,11])]})
        state.player_list[0].has_draw_slot=False
    elif phase in (P.RESPONSE,P.KONG):
        state = fixture_state({0:STANDARD[:-1]},actor=1,phase=phase,drawn=phase == P.KONG)
    else:
        state = fixture_state({0:STANDARD})
    state.round_time, state.step_time = bank, step
    for player in state.player_list:
        player.remaining_time = bank
    if phase in (P.TURN, P.DISCARD_ONLY):
        window = state.open_action_window(state.begin_turn(0,discard_only=phase == P.DISCARD_ONLY))
    else:
        window = state.open_action_window(dict(status=phase.value, player=1, tile=43,
            actions={**empty_actions(),0:["pass"]}))
    return state, window


@pytest.mark.parametrize("bank,step", [(20,5),(11,8),(0,10),(20,0),(0,0)])
def test_real_room_configuration_reaches_round_reset_and_first_query(bank,step):
    state = make_state(round_timer=bank,step_timer=step)
    state.initialize_round()
    window = open_turn(state)
    ask = state.build_pending_action_payload(0)
    assert state.round_time == bank and state.step_time == step
    assert [p.remaining_time for p in state.player_list] == [bank]*4
    assert ask["game_info"]["round_time"] == bank and ask["game_info"]["step_time"] == step
    assert ask["ask_hand_action_info"]["remaining_time"] == bank
    assert ask["ask_hand_action_info"]["step_remaining"] == step
    assert ask["game_info"]["wenzhou_info"]["clock_remaining_ms"] == (bank+step)*1000
    assert window["_clock_started"] is None


def test_missing_room_uses_the_same_20_plus_5_defaults():
    state = WenzhouGameState(calculation_service=object())
    assert state.round_time == 20 and state.step_time == 5


@pytest.mark.parametrize("phase", [P.TURN,P.DISCARD_ONLY,P.RESPONSE,P.KONG])
@pytest.mark.parametrize("bank,step,elapsed", [(20,5,4.9),(20,5,8),(11,8,9.25),(0,10,9.9),(20,0,0.6)])
def test_all_player_queries_use_step_then_exact_bank_without_a_fixed_cap(clock,monkeypatch,phase,bank,step,elapsed):
    async def run():
        state,window = timed_state(bank,step,phase)
        action = "hu_self" if phase == P.TURN else "cut" if phase == P.DISCARD_ONLY else "pass"
        async def reply(_):
            clock.advance(elapsed)
            kwargs = dict(TileId=state.player_list[0].hand_tiles[-1]) if action == "cut" else {}
            await state.submit_action(0,action,**kwargs)
        responses,budgets = await collect(state,clock,monkeypatch,[reply],timeout=3)
        assert budgets == [bank+step]  # A legacy MIL / caller timeout cannot cap a human action.
        assert responses[0]["action_type"] == action and not responses[0].get("is_timeout_action")
        assert state.player_list[0].remaining_time == pytest.approx(bank-max(0,elapsed-step))
        assert window["_clock_deadlines"][0] == 100+bank+step
        assert [p.remaining_time for p in state.player_list[1:]] == [bank]*3
    asyncio.run(run())


def test_fractional_overtime_accumulates_across_windows(clock,monkeypatch):
    async def run():
        state,_ = timed_state()
        for elapsed,expected in [(5.6,19.4),(5.6,18.8),(6.2,17.6)]:
            window = open_turn(state)
            async def reply(_):
                clock.advance(elapsed)
                await state.submit_action(0,"hu_self")
            await collect(state,clock,monkeypatch,[reply])
            assert state.player_list[0].remaining_time == pytest.approx(expected)
            assert window["_clock_finished_exact"][0][0] == pytest.approx(expected)
    asyncio.run(run())


def test_charge_at_receipt_even_if_collector_and_other_sends_are_late(clock,monkeypatch):
    async def run():
        state,window = timed_state()
        async def reply(_):
            clock.advance(4)
            await state.submit_action(0,"hu_self")
            assert state.player_list[0].remaining_time == 20
            clock.advance(40)
        responses,_ = await collect(state,clock,monkeypatch,[reply])
        assert responses[0]["action_type"] == "hu_self"
        assert state.player_list[0].remaining_time == 20
        assert state.remaining_clock(0,window) == (20,1)
        restored = state.build_pending_action_payload(0)
        assert restored["ask_hand_action_info"]["action_list"] == []
        assert not restored["game_info"]["wenzhou_info"]["clock_active"]
    asyncio.run(run())


@pytest.mark.parametrize("phase,default", [(P.TURN,"cut"),(P.DISCARD_ONLY,"cut"),(P.RESPONSE,"pass"),(P.KONG,"pass")])
@pytest.mark.parametrize("bank,step", [(20,5),(3,8),(0,10),(0,0)])
def test_expiry_uses_correct_default_and_exhausts_only_that_player(clock,monkeypatch,phase,default,bank,step):
    async def run():
        state,window = timed_state(bank,step,phase)
        async def expire(wait_for):
            assert wait_for == bank+step
            clock.advance(wait_for)
            with pytest.raises(ValueError,match="时间已耗尽"):
                action = "cut" if default == "cut" else "pass"
                await state.submit_action(0,action,TileId=state.player_list[0].hand_tiles[-1])
        callbacks = [expire] if bank+step else []
        response,_ = await collect(state,clock,monkeypatch,callbacks,timeout=1)
        assert response[0]["action_type"] == default
        assert state.player_list[0].remaining_time == 0
        assert state.remaining_clock(0,window) == (0,0)
        assert [p.remaining_time for p in state.player_list[1:]] == [bank]*3
        if default == "cut":
            assert response[0]["TileId"] == state.player_list[0].hand_tiles[-1]
            assert response[0]["cutClass"] == (phase != P.DISCARD_ONLY)
            assert response[0]["is_timeout_action"]
    asyncio.run(run())


def test_reconnect_duplicate_snapshot_and_spectator_preserve_one_deadline_and_one_debit(clock):
    async def run():
        state,window = timed_state()
        sockets = {101:SimpleNamespace(websocket=SocketRecorder()),777:SimpleNamespace(websocket=SocketRecorder())}
        state.game_server = SimpleNamespace(user_id_to_connection=sockets)
        await state._deliver_claim_payload(0,state.build_pending_action_payload(0))
        deadline = copy.deepcopy(window["_clock_deadlines"])
        tick = state.server_action_tick
        clock.advance(7.25)
        for _ in range(3):
            await state.player_reconnect(101)
            await state.send_realtime_spectator_snapshot(777,0)
            for uid in (101,777):
                ask = sockets[uid].websocket.messages[-1]
                assert ask["ask_hand_action_info"]["remaining_time"] == 18
                assert ask["ask_hand_action_info"]["step_remaining"] == 0
                assert ask["game_info"]["wenzhou_info"]["clock_remaining_ms"] == 17750
            assert window["_clock_deadlines"] == deadline
            assert state.player_list[0].remaining_time == 20
        await state.submit_action(0,"hu_self")
        assert state.player_list[0].remaining_time == 17.75
        with pytest.raises(ValueError):
            await state.submit_action(0,"hu_self")
        clock.advance(100)
        await state.player_reconnect(101)
        assert sockets[101].websocket.messages[-1]["ask_hand_action_info"]["action_list"] == []
        responses = await state.wait_action()
        assert responses[0]["action_type"] == "hu_self"
        assert state.player_list[0].remaining_time == 17.75
        assert state.server_action_tick == tick and window["_clock_deadlines"] == deadline
    asyncio.run(run())


def test_first_delivery_is_per_seat_and_spectator_delay_does_not_renew_it(clock):
    async def run():
        state = fixture_state({0:STANDARD[:-1]},actor=3,phase=P.RESPONSE,drawn=False)
        state.step_time=8
        for player in state.player_list:
            player.remaining_time=11
        window=state.open_action_window(dict(status=P.RESPONSE.value,player=3,tile=43,
            actions={**empty_actions(),0:["pass"],1:["pass"]}))
        state.game_server=SimpleNamespace(user_id_to_connection={p.user_id:SimpleNamespace(websocket=SocketRecorder()) for p in state.player_list})
        async def delayed_spectator(index,_):
            if index == 0:
                clock.advance(6)
        state.send_to_realtime_spectators=delayed_spectator
        await state._deliver_claim_payload(0,state.build_pending_action_payload(0))
        assert window["_clock_deadlines"][0] == 119
        await state._deliver_claim_payload(1,state.build_pending_action_payload(1))
        assert window["_clock_deadlines"][1] == 125
        await state.submit_action(0,"pass")
        assert state.player_list[0].remaining_time == 11
        clock.advance(10)
        await state.submit_action(1,"pass")
        assert state.player_list[1].remaining_time == 9
        clock.advance(100)
        response=await state.wait_action()
        assert set(response) == {0,1}
        assert [p.remaining_time for p in state.player_list] == [11,9,11,11]
    asyncio.run(run())


def test_collector_retry_does_not_restart_or_lose_a_previous_response(clock,monkeypatch):
    async def run():
        state = fixture_state({0:STANDARD[:-1]},actor=3,phase=P.RESPONSE,drawn=False)
        state.step_time=5
        for player in state.player_list:
            player.remaining_time=20
        window=state.open_action_window(dict(status=P.RESPONSE.value,player=3,tile=43,
            actions={**empty_actions(),0:["pass"],1:["pass"]}))
        async def answer_one(_):
            clock.advance(7)
            await state.submit_action(0,"pass")
        async def cancel(_):
            raise asyncio.CancelledError
        with pytest.raises(asyncio.CancelledError):
            await collect(state,clock,monkeypatch,[answer_one,cancel])
        original=copy.deepcopy(window["_clock_deadlines"])
        clock.advance(2)
        await state.submit_action(1,"pass")
        response=await state.wait_action()
        assert set(response) == {0,1}
        assert state.player_list[0].remaining_time == 18
        assert state.player_list[1].remaining_time == 16
        assert window["_clock_deadlines"] == original
    asyncio.run(run())


@pytest.mark.parametrize("action", ["chi_mid","peng"])
def test_real_call_opens_a_new_discard_window_with_fresh_step_only(clock,action):
    async def run():
        held = [11,13] if action == "chi_mid" else [46,46]
        state=fixture_state({1:held+JUNK},caishen=12,actor=0,phase=P.RESPONSE,drawn=False,river={0:[46]})
        state.step_time=5
        for player in state.player_list:
            player.remaining_time=20
        first=open_claim(state,0,46)
        for index in state.waiting_players_list:
            state.start_action_clock(index,first)
        clock.advance(7)
        for index in list(state.waiting_players_list):
            await state.submit_action(index,action if index == 1 else "pass")
        responses=await state.wait_action()
        second=state.apply_action_results(first,responses)
        assert second["status"] == P.DISCARD_ONLY.value and second["player"] == 1
        assert state.remaining_clock(1,second) == (18,5)
        assert state.player_list[1].remaining_time == 18
        state.start_action_clock(1,second)
        clock.advance(4)
        await state.submit_action(1,"cut",TileId=state.player_list[1].hand_tiles[-1])
        await state.wait_action()
        assert state.player_list[1].remaining_time == 18
    asyncio.run(run())


def test_no_response_concealed_kong_does_not_consume_a_human_window(clock):
    async def run():
        hand=[11]*4+[21,22,23,24,25,26,31,32,33,41,41,41,43]
        state=fixture_state({0:hand})
        state.step_time=5
        for player in state.player_list:
            player.remaining_time=20
        first=open_turn(state)
        state.start_action_clock(0,first)
        clock.advance(2)
        await state.submit_action(0,"angang",target_tile=11)
        response=await state.wait_action()
        automatic=state.apply_action_results(first,response)
        assert automatic["status"] == P.KONG.value and not any(automatic["actions"].values())
        clock.advance(80)
        assert await state.wait_action() == {}
        next_window=state.apply_action_results(automatic,{})
        assert next_window["status"] == P.TURN.value
        assert state.remaining_clock(0,next_window) == (20,5)
        assert state.player_list[0].remaining_time == 20
    asyncio.run(run())


def test_result_confirmation_preserves_bank_and_next_hand_restores_configured_bank(clock):
    async def run():
        state=make_state(round_timer=20,step_timer=5)
        state.initialize_round()
        open_turn(state)
        for index,player in enumerate(state.player_list):
            player.remaining_time=index+0.25
        state.end_draw()
        before=[p.remaining_time for p in state.player_list]
        await state.run_round_ready_phase(timeout=0)
        assert [p.remaining_time for p in state.player_list] == before
        state.advance_round_after_ready()
        state.initialize_round()
        second=open_turn(state)
        assert [p.remaining_time for p in state.player_list] == [20]*4
        assert state.remaining_clock(0,second) == (20,5)
    asyncio.run(run())
