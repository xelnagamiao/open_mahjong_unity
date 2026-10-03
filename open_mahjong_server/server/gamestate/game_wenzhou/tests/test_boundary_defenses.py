"""Reserve, reconnect and cancellation edges; no fabricated fifth-copy game."""

import asyncio
from types import SimpleNamespace

import pytest

from ..actions import empty_actions, turn_actions
from ..bot import choose_action, play_bot
from ..get_action import handle_action
from ..state_machine import Phase as P
from .helpers import STANDARD, WAIT, SocketRecorder, assert_conserved, fixture_state, make_state, response_map, snapshot
from .test_flow import JUNK, open_claim, open_turn
from .test_protocol_session import socket_server


def reserve_only(state, count):
    # All consumed live-wall tiles remain in a public river in this boundary
    # fixture, so supply conservation still holds.
    state.player_list[3].discard_tiles.extend(state.tiles_list[count:])
    del state.tiles_list[count:]


def test_at_last_two_stacks_win_is_legal_but_pung_chow_and_kong_are_not():
    wait_white = [11,13,21,22,23,31,32,33,17,18,19,41,41,41,43,43]
    state = fixture_state({1:[46,46]+JUNK,2:wait_white},caishen=12,actor=0,
                          phase=P.RESPONSE,drawn=False,river={0:[46]})
    reserve_only(state,4)
    window=open_claim(state,0,46)
    assert window["actions"][1] == []
    assert window["actions"][2] == ["hu","pass"]
    state.apply_action_results(window,response_map(window))
    assert state.machine.phase == P.END and state.ended_by == "wall"
    assert len(state.tiles_list) == 4
    assert_conserved(state)


def test_final_legal_draw_still_offers_self_draw_before_exhaustion():
    state=fixture_state({1:WAIT},actor=0,phase=P.RESPONSE,drawn=False,river={0:[39]})
    where=state.tiles_list.index(43)
    state.tiles_list[where],state.tiles_list[0]=state.tiles_list[0],state.tiles_list[where]
    reserve_only(state,5)
    window=open_claim(state,0,39)
    state.apply_action_results(window,response_map(window))
    assert len(state.tiles_list) == 4
    assert "hu_self" in state.action_dict[1]
    assert "angang" not in state.action_dict[1] and "jiagang" not in state.action_dict[1]
    state.apply_action_results(state.live_pending_window,{1:{"action_type":"hu_self"}})
    assert state.ended_by == "win"
    assert_conserved(state)


def test_no_live_window_or_ended_stale_packet_only_resynchronizes_when_available():
    async def run():
        state=make_state()
        state.initialize_round()
        socket=SocketRecorder()
        # Reconnect between initialization and the first ask is a legitimate
        # transport boundary: the full snapshot exists, pending ask does not.
        assert len(state.restore_payloads(0)) == 1
        await state.player_reconnect(101)
        await handle_action(socket_server(state),"connection",{},socket)
        assert not socket.messages
        state.machine.transition(P.TURN)
        state.end_draw()
        await handle_action(socket_server(state),"connection",{},socket)
        assert not socket.messages
        state.machine.transition(P.READY)
        await handle_action(socket_server(state),"connection",{},socket)
        assert socket.messages[-1]["type"].endswith("ready_status")
    asyncio.run(run())


def test_defensive_helpers_reject_unavailable_physical_claims_without_mutation():
    state=fixture_state({1:JUNK+[11,13]},caishen=12,actor=0,phase=P.RESPONSE,drawn=False,river={0:[19]})
    before=snapshot(state)
    for tile in [12,19]:
        with pytest.raises(ValueError):
            state._claim(1,"peng",0,tile)
    assert snapshot(state) == before
    window=state.open_action_window(dict(status=P.RESPONSE.value,player=0,tile=19,actions=empty_actions()))
    # A transport window in an end/ready phase is rejected even with an empty
    # reply set, instead of reaching the response domain transition.
    window["status"]=P.END.value
    with pytest.raises(ValueError,match="当前阶段"):
        state.apply_action_results(window,{})


def test_empty_or_stale_hint_paths_do_not_invent_actions():
    state=make_state(tips=True)
    assert state.authoritative_waits(0) == {}
    assert turn_actions(state,0,discard_only=True) == empty_actions()
    assert state._adapt({"success":True},0) == {"success":True,"player_index":0}
    state.player_list[0].hand_tiles=JUNK+[11,13]
    state.live_pending_window={"tile":19}
    assert choose_action(state,0,["peng","pass"]) == {"action_type":"pass"}
    assert choose_action(state,0,["pass"]) == {"action_type":"pass"}


def test_record_waits_compatibly_initializes_missing_snapshot_cache():
    state=fixture_state({0:STANDARD},tips=True)
    state.start_game_recording()
    state.start_round_recording()
    if hasattr(state,"_recorded_waits"):
        del state._recorded_waits
    state.record_waits()
    assert set(state._recorded_waits) == {0,1,2,3}


def test_interrupted_game_loop_cancels_outstanding_bot_pacing_task():
    async def run():
        state=make_state(player_list=[1,2,3,4],bot_speed="slow")
        running=asyncio.create_task(state.run_game_loop())
        for _ in range(100):
            await asyncio.sleep(0.001)
            if state.bot_tasks:
                break
        tasks=list(state.bot_tasks)
        assert tasks
        running.cancel()
        with pytest.raises(asyncio.CancelledError):
            await running
        await asyncio.sleep(0)
        assert all(task.done() for task in tasks)
    asyncio.run(run())


def test_bot_response_rejection_is_contained_at_submission_boundary():
    async def run():
        state=fixture_state({0:STANDARD})
        state.bot_speed="instant"
        open_turn(state)
        async def rejecting_transport(*args,**kwargs):
            raise ValueError("injected socket handover rejection")
        # Only the transport failure is injected. The bot and win calculation
        # run unchanged through the production scorer and chooser.
        state.submit_action=rejecting_transport
        await play_bot(state,0,state.server_action_tick)
        assert state.action_queues[0].empty()
    asyncio.run(run())
