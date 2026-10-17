"""Online claims use the room clock, including rob-kong and reconnect."""
import asyncio

import pytest

from .state_machine import Phase as P
from .test_state import PLAIN, act, turn


def claim_window(action, bank=20, step=5):
    state = turn(PLAIN[:-1] + [22])
    state.step_time = step
    for player in state.player_list:
        player.remaining_time = bank
    if action == "rob_kong":
        player = state.player_list[0]
        player.hand_tiles = [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 22]
        player.combination_tiles = ["k22"]
        player.combination_mask = [[1, 22, 0, 22, 0, 22]]
        player.meld_sources = [2]
        player.pung_melds = {0}
        state.player_list[1].hand_tiles = PLAIN[:-1]
    else:
        state.player_list[1].hand_tiles = (
            [22, 22, 22] + PLAIN[:10] if action == "gang" else PLAIN[:-1]
        )
        if action == "peng":
            state.player_list[1].hand_tiles = [22, 22] + PLAIN[:11]
    state.open_action_window(state.begin_turn(0))
    act(state, **(dict(action_type="jiagang", target_tile=22) if action == "rob_kong"
                 else dict(action_type="cut", TileId=22, cutClass=True)))
    assert state.machine.phase == (P.KONG if action == "rob_kong" else P.RESPONSE)
    assert ("hu" if action == "rob_kong" else action) in state.action_dict[1]
    return state


@pytest.mark.parametrize("action", ["peng", "gang", "hu", "rob_kong"])
@pytest.mark.parametrize("bank,step", [(20, 5), (8, 2), (0, 5)])
def test_claim_deadline_and_wire_follow_room_clock(action, bank, step):
    state = claim_window(action, bank, step)
    state._timing_now = lambda: 100
    clock = state.action_clocks[1]
    clock.start(100)
    info = state.build_pending_action_payload(1)["ask_other_action_info"]
    assert clock.deadline == 100 + bank + step
    assert (info["remaining_time"], info["step_remaining"]) == (bank, step)


@pytest.mark.parametrize("action", ["peng", "gang", "hu", "rob_kong"])
@pytest.mark.parametrize("elapsed,remaining", [(4, 20), (7, 18)])
def test_claim_after_three_seconds_uses_arrival_time_and_resolves(action, elapsed, remaining):
    async def run():
        state = claim_window(action)
        for clock in state.action_clocks.values():
            clock.start(100)
        state._timing_now = lambda: 100 + elapsed
        for index in list(state.waiting_players_list):
            await state.submit_action(index, ("hu" if action == "rob_kong" else action) if index == 1 else "pass")
        state._timing_now = lambda: 200  # Collector/network work is not thinking time.
        responses = await state.wait_action()
        assert state.player_list[1].remaining_time == remaining
        assert state.player_list[0].remaining_time == 20
        act(state, responses)
        assert state.machine.phase in (P.END, P.DISCARD_ONLY, P.TURN, P.REPLACEMENT)
    asyncio.run(run())


@pytest.mark.parametrize("action", ["peng", "rob_kong"])
def test_claim_reconnect_does_not_refill_time_and_expiry_passes(action):
    async def run():
        state = claim_window(action)
        for clock in state.action_clocks.values():
            clock.start(100)
        state._timing_now = lambda: 108
        sent = []
        async def send(index, payload):
            sent.append(payload)
        state.send_payload_to_player = send
        await state.player_reconnect(state.player_list[1].user_id)
        info = sent[-1]["ask_other_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (17, 0)
        assert state.action_clocks[1].deadline == 125
        state._timing_now = lambda: 125
        with pytest.raises(ValueError, match="耗尽"):
            await state.submit_action(1, "pass")
        responses = await state.wait_action()
        assert all(data["action_type"] == "pass" for data in responses.values())
        assert all(state.player_list[i].remaining_time == 0 for i in responses)
        assert state.player_list[0].remaining_time == 20
    asyncio.run(run())
