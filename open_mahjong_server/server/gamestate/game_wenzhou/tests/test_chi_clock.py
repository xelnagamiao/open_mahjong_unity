"""Physical chow previews and reconnect clocks use the authoritative window."""

import asyncio
import copy
import time
from types import SimpleNamespace

import pytest

from ..actions import CHOWS, empty_actions
from ..state_machine import Phase as P
from .helpers import STANDARD, SocketRecorder, assert_conserved, fixture_state, make_state
from .test_flow import JUNK, open_claim, open_turn


@pytest.mark.parametrize("action,called,held,expected", [
    ("chi_left",13,[11,46],[11,46]),
    ("chi_mid",46,[11,13],[11,13]),
    ("chi_right",11,[46,13],[46,13]),
])
def test_chow_preview_is_actual_held_pair_when_white_is_held_or_claimed(action,called,held,expected):
    state=fixture_state({1:held+JUNK},caishen=12,actor=0,phase=P.RESPONSE,
                        drawn=False,river={0:[called]})
    window=open_claim(state,0,called)
    ask=state.build_pending_action_payload(1)["ask_other_action_info"]
    assert ask["cut_tile"] == called
    assert ask["chi_candidates"][action] == [expected]
    assert set(ask["chi_candidates"]) == set(window["actions"][1]).intersection(CHOWS)
    assert all(tile in state.player_list[1].hand_tiles for tile in expected)
    assert 12 not in expected  # physical caishen never replaces a held tile
    assert_conserved(state)


@pytest.mark.parametrize("caishen,called,held,expected", [
    (12,13,[11,46,14,15],{"chi_left":[[11,46]],"chi_mid":[[46,14]],"chi_right":[[14,15]]}),
    (15,46,[13,14,16,17],{"chi_left":[[13,14]],"chi_mid":[[14,16]],"chi_right":[[16,17]]}),
    (15,14,[12,13,46,16],{"chi_left":[[12,13]],"chi_mid":[[13,46]],"chi_right":[[46,16]]}),
])
def test_all_three_chow_directions_match_offers_and_keep_physical_white(caishen,called,held,expected):
    state=fixture_state({1:held+JUNK[:12]},caishen=caishen,actor=0,phase=P.RESPONSE,
                        drawn=False,river={0:[called]})
    window=open_claim(state,0,called)
    payload=state.build_pending_action_payload(1)
    assert set(window["actions"][1]).intersection(CHOWS) == set(CHOWS)
    assert payload["ask_other_action_info"]["chi_candidates"] == expected
    # Other views cannot use the next player's candidates or derive a chow
    # from the physical white's numeric ID 46.
    for viewer in (0,2,3):
        other=state.build_pending_action_payload(viewer)["ask_other_action_info"]
        assert other["chi_candidates"] == {}
    assert_conserved(state)


def test_actual_white_caishen_and_rob_kong_windows_offer_no_chow_preview():
    state=fixture_state({1:[11,13]+JUNK},caishen=46,actor=0,phase=P.RESPONSE,
                        drawn=False,river={0:[46]})
    open_claim(state,0,46)
    assert state.build_pending_action_payload(1)["ask_other_action_info"]["chi_candidates"] == {}
    state=fixture_state({0:JUNK[:13]+[46],1:[11,13]+JUNK},caishen=12,
                        melds={0:[("k12",[46]*3)]})
    window=open_turn(state)
    state.apply_action_results(window,{0:{"action_type":"jiagang","target_tile":46}})
    assert state.machine.phase == P.KONG
    for viewer in range(4):
        assert state.build_pending_action_payload(viewer)["ask_other_action_info"]["chi_candidates"] == {}


def test_clock_projection_before_start_and_inactive_view_does_not_mutate_bank():
    state=fixture_state({0:STANDARD})
    state.step_time=5
    for player in state.player_list:
        player.remaining_time=10
    window=open_turn(state)
    before=copy.deepcopy(window)
    assert state.remaining_clock(0,window) == (10,5)
    assert state.remaining_clock(1,window) == (10,5)
    assert window == before
    # Only the acting seat has consumed 7.2 seconds. A nonacting viewer has no
    # ticking clock even though it sees the same hand-action broadcast.
    window["_clock_started"]=time.monotonic()-7.2
    assert state.remaining_clock(0,window) == (8,0)
    assert state.remaining_clock(1,window) == (10,5)
    assert [p.remaining_time for p in state.player_list] == [10]*4
    ask=state.build_pending_action_payload(0)["ask_hand_action_info"]
    assert ask["remaining_time"] == 8 and ask["step_remaining"] == 0


def test_clock_projection_clamps_expired_budget_and_uses_frozen_reply_value():
    state=fixture_state({0:STANDARD})
    state.step_time=1
    state.player_list[0].remaining_time=2
    window=open_turn(state)
    window["_clock_started"]=time.monotonic()-20
    assert state.remaining_clock(0,window) == (0,0)
    window["_clock_finished"][0]=(2,1)
    assert state.remaining_clock(0,window) == (2,1)
    window["_clock_started"]=time.monotonic()-100
    assert state.remaining_clock(0,window) == (2,1)


def test_reconnect_reads_running_clock_without_reset_and_reply_freezes_it():
    async def run():
        state=fixture_state({0:STANDARD})
        state.step_time=1
        state.player_list[0].remaining_time=4
        socket=SocketRecorder()
        state.game_server=SimpleNamespace(user_id_to_connection={101:SimpleNamespace(websocket=socket)})
        window=open_turn(state)
        tick=state.server_action_tick
        waiter=asyncio.create_task(state.wait_action())
        try:
            await asyncio.sleep(1.1)
            started=window["_clock_started"]
            await state.player_reconnect(101)
            first=socket.messages[-1]["ask_hand_action_info"]
            assert first["step_remaining"] == 0
            assert 0 < first["remaining_time"] <= 4
            assert state.player_list[0].remaining_time == 4
            await asyncio.sleep(1.1)
            await state.player_reconnect(101)
            second=socket.messages[-1]["ask_hand_action_info"]
            assert second["step_remaining"] == 0
            assert 0 < second["remaining_time"] < first["remaining_time"]
            assert window["_clock_started"] == started
            assert state.server_action_tick == tick
            await state.submit_action(0,"hu_self")
            response=await asyncio.wait_for(waiter,1)
            assert response[0]["action_type"] == "hu_self"
            frozen=state.remaining_clock(0,window)
            await asyncio.sleep(0.08)
            assert state.remaining_clock(0,window) == frozen
            assert state.player_list[0].remaining_time == frozen[0]
        finally:
            waiter.cancel()
            await asyncio.gather(waiter,return_exceptions=True)
    asyncio.run(run())


def test_answered_claimant_clock_freezes_while_unanswered_claimant_continues():
    async def run():
        state=fixture_state({1:[11,13]+JUNK,2:[46,46]+JUNK},caishen=12,
                            actor=0,phase=P.RESPONSE,drawn=False,river={0:[46]})
        state.step_time=1
        for player in state.player_list:
            player.remaining_time=4
        window=open_claim(state,0,46)
        assert "chi_mid" in state.action_dict[1] and "peng" in state.action_dict[2]
        waiting=list(state.waiting_players_list)
        waiter=asyncio.create_task(state.wait_action())
        try:
            await asyncio.sleep(0)
            await state.submit_action(1,"pass")
            for _ in range(100):
                if 1 not in state.waiting_players_list:
                    break
                await asyncio.sleep(0.001)
            assert 1 not in state.waiting_players_list and 2 in state.waiting_players_list
            frozen=state.remaining_clock(1,window)
            assert frozen == (4,1)
            await asyncio.sleep(1.1)
            one=state.build_pending_action_payload(1)["ask_other_action_info"]
            two=state.build_pending_action_payload(2)["ask_other_action_info"]
            assert (one["remaining_time"],one["step_remaining"]) == frozen
            assert one["action_list"] == [] and one["chi_candidates"] == {}
            assert two["step_remaining"] == 0
            assert (two["remaining_time"],two["step_remaining"]) != frozen
            for index in waiting:
                if index in state.waiting_players_list:
                    await state.submit_action(index,"pass")
            responses=await asyncio.wait_for(waiter,1)
            assert set(responses) == set(waiting)
            assert state.player_list[1].remaining_time == 4
        finally:
            waiter.cancel()
            await asyncio.gather(waiter,return_exceptions=True)
    asyncio.run(run())


def test_timeout_clock_freezes_at_zero_and_next_window_gets_a_fresh_step():
    async def run():
        state=fixture_state({0:STANDARD})
        state.step_time=0
        state.player_list[0].remaining_time=0
        first=open_turn(state)
        response=await state.wait_action(timeout=0)
        assert response[0]["is_timeout_action"]
        assert state.remaining_clock(0,first) == (0,0)
        state.step_time=3
        second=state.open_action_window(dict(status=P.TURN.value,player=0,actions={**empty_actions(),0:["cut"]}))
        assert second["_clock_started"] is None
        assert second["_clock_finished"] == {}
        assert state.remaining_clock(0,second) == (0,3)
    asyncio.run(run())
