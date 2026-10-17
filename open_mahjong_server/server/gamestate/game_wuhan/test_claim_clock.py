"""Room-clock regression cases for ordinary claims and robbing added kongs."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_flow import PURE, hand, indicator, make_state
from ..game_taiwan.boardcast import reconnected_send_pending_ask


async def claim_window(action, bank=20, step=5):
    state = make_state(round_timer=bank, step_timer=step)
    indicator(state)
    tile = 15 if action in ("hu", "rob_kong") else 23
    if action in ("hu", "rob_kong"):
        hand(state, 1, PURE, ["s12"])
        for player in state.player_list:
            player.huapai_list = [45]
    else:
        owned = {"chi_left": [21, 22], "chi_mid": [22, 24], "chi_right": [24, 25],
                 "peng": [23, 23], "gang": [23, 23, 23]}[action]
        rest = [11, 14, 17, 28, 29, 31, 32, 33, 34, 35, 36, 41, 42]
        hand(state, 1, owned + rest[:13-len(owned)])
    if action == "rob_kong":
        hand(state, 0, [15], ["k15"])
        await state.execute_jiagang(0, 15)
        assert state._pending_jiagang
        state.action_dict = state.check_added_kong_actions(tile)
    else:
        state.player_list[0].discard_tiles = [tile]
        state.game_status = "waiting_action_after_cut"
        state.action_dict = state.check_discard_actions(tile)
    choice = next(a for a in state.action_dict[1] if a.startswith("hu")) if action in ("hu", "rob_kong") else action
    assert choice in state.action_dict[1]
    state.action_clock.now = lambda: 100
    state.prepare_action_window()
    state.on_action_window_broadcast()
    for i, choices in state.action_dict.items():
        if choices:
            state.on_action_window_delivered(i)
    return state, choice


ACTIONS = ["chi_left", "chi_mid", "chi_right", "peng", "gang", "hu", "rob_kong"]


@pytest.mark.parametrize("action", ACTIONS)
@pytest.mark.parametrize("bank,step", [(20, 5), (8, 2), (0, 5)])
def test_claim_deadline_and_wire_follow_room_clock(action, bank, step):
    async def run():
        state, _ = await claim_window(action, bank, step)
        assert state.action_clock.deadline(1) == 100 + bank + step
        assert state.claim_clock(state.player_list[1]) == (bank, step)
        ws = SimpleNamespace(send_json=AsyncMock())
        state.game_server.user_id_to_connection[102] = SimpleNamespace(websocket=ws)
        state.send_to_realtime_spectators = AsyncMock()
        await reconnected_send_pending_ask(state, 102)
        info = ws.send_json.call_args.args[0]["ask_other_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (bank, step)
    asyncio.run(run())


@pytest.mark.parametrize("action", ACTIONS)
@pytest.mark.parametrize("elapsed,remaining", [(4, 20), (7, 18)])
def test_claim_after_three_seconds_uses_arrival_time_and_resolves(action, elapsed, remaining):
    async def run():
        state, choice = await claim_window(action)
        state.action_clock.now = lambda: 100 + elapsed
        for index, choices in state.action_dict.items():
            if choices:
                state.action_queues[index].put_nowait({"action_type": choice if index == 1 else "pass"})
        state.action_clock.now = lambda: 200
        responses, allowed = await state.collect_action_responses()
        assert responses[1]["action_type"] == choice
        assert state.player_list[1].remaining_time == remaining
        assert state.player_list[0].remaining_time == 20
        if action == "rob_kong":
            await state.resolve_rob_kong_responses(responses, allowed)
        else:
            await state.resolve_discard_responses(responses, allowed)
        if action in ("hu", "rob_kong"):
            assert state.pending_winners[0]["index"] == 1
        else:
            assert state.player_list[1].combination_tiles
    asyncio.run(run())


@pytest.mark.parametrize("action", ["peng", "rob_kong"])
def test_reconnect_and_timeout_keep_the_original_deadline(action):
    async def run():
        state, choice = await claim_window(action)
        state.action_clock.now = lambda: 108
        assert state.claim_clock(state.player_list[1], True) == (17, 0)
        state.on_action_window_broadcast()
        state.on_action_window_delivered(1)
        assert state.action_clock.deadline(1) == 125
        state.action_clock.now = lambda: 125
        state.action_queues[1].put_nowait({"action_type": choice})
        responses, allowed = await state.collect_action_responses()
        assert responses == {}
        assert all(state.player_list[i].remaining_time == 0 for i in allowed)
        assert state.player_list[0].remaining_time == 20
        assert not state.waiting_players_list
    asyncio.run(run())


def test_all_replies_close_the_window_without_waiting_out_the_budget():
    async def run():
        state, choice = await claim_window("peng")
        state.action_clock.now = lambda: 104
        state.action_queues[1].put_nowait({"action_type": choice})
        responses, _ = await asyncio.wait_for(state.collect_action_responses(), .2)
        assert responses[1]["action_type"] == choice
        assert state.player_list[1].remaining_time == 20
    asyncio.run(run())


def test_waiting_for_another_seat_does_not_spend_the_early_responder_bank():
    async def run():
        state, choice = await claim_window("peng")
        state.action_dict[2] = ["pass"]
        state.on_action_window_delivered(2)
        state.action_clock.now = lambda: 104
        state.action_queues[1].put_nowait({"action_type": choice})
        state.action_clock.now = lambda: 125
        state.action_queues[2].put_nowait({"action_type": "pass"})
        responses, _ = await state.collect_action_responses()
        assert responses == {1: {"action_type": choice}}
        assert state.player_list[1].remaining_time == 20
        assert state.player_list[2].remaining_time == 0
        assert not state.is_action_pending(1) and not state.is_action_pending(2)
    asyncio.run(run())
