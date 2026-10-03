"""Exercise bot decisions through the same action queue used by live rooms."""
import asyncio

import pytest

from server.gamestate.game_hongque import get_action
from server.gamestate.game_hongque.HongqueGameState import HongqueGameState
from server.gamestate.game_hongque.action_check import check_action_after_cut
from server.gamestate.game_hongque.wait_action import ClaimWindow


async def _inline_cpu(state, func, *args, **kwargs):
    return func(*args, **kwargs)


def _repeated_bots(bot_id):
    state = HongqueGameState(None, {
        "room_id": "repeated-bots",
        "player_list": [101, bot_id, bot_id, bot_id],
        "game_round": 1,
    })
    # Match the live loop: submission only enqueues, the waiter applies it.
    state._in_wait_action = True
    return state


@pytest.mark.parametrize("bot_id", [0, 2, 3])
@pytest.mark.parametrize("seat", [1, 2, 3])
def test_repeated_bot_discard_reaches_its_own_seat(monkeypatch, bot_id, seat):
    monkeypatch.setattr(get_action, "run_room_bot_cpu", _inline_cpu)
    monkeypatch.setattr(get_action, "BOT_ACTION_DELAY", 0)

    async def exercise():
        state = _repeated_bots(bot_id)
        state.phase = "turn"
        state.current_player_index = seat
        player = state.players[seat]
        player.hand = ["AX1", "AX2", "GY9"]
        player.drawn_tile = "GY9"
        player.supplements = 2
        await state._bot_turn(state.action_tick)
        queued = state.action_queues[seat].get_nowait()
        assert queued["action_type"] == "discard"
        assert queued["tile"] in player.hand
        assert all(queue.empty() for queue in state.action_queues.values())

    asyncio.run(exercise())


def test_ordinary_bot_passes_tactical_recheck_without_waiting_for_timeout():
    async def exercise():
        state = HongqueGameState(None, {
            "room_id": "ordinary-tactical-pass",
            "player_list": [101, 0, 3, 104],
            "game_round": 1,
        })
        state._schedule_bot_if_needed = lambda: None
        state.phase = "claim"
        state.players[0].discards = ["AX3"]
        state.players[1].hand = ["AX1", "AX2", "BX9"]
        state.players[2].hand = ["AX4", "AX5", "CY9"]
        state.last_discard = {"player": 0, "tile": "AX3"}
        state.wall = ["GX9"]
        state.claim_options = check_action_after_cut(state)
        state.claim_window = ClaimWindow(state.claim_options, {2})
        state.claim_responses = {1: {"action": "pass"}}
        chosen = next(candidate for candidate in state.claim_options[2]
                      if candidate["kind"] == "sequence")
        try:
            await state._handle_claim_action(state.players[2], "claim", chosen["id"])
            assert state.phase == "turn"
            assert state.current_player_index == 2
            assert state.players[2].melds[0]["tiles"] == ["AX3", "AX4", "AX5"]
        finally:
            await state.cleanup_game_state()

    asyncio.run(exercise())


@pytest.mark.parametrize("bot_id", [2, 3])
@pytest.mark.parametrize("seat", [1, 2, 3])
def test_repeated_bot_ron_reaches_its_own_seat(monkeypatch, bot_id, seat):
    monkeypatch.setattr(get_action, "run_room_bot_cpu", _inline_cpu)
    monkeypatch.setattr(get_action, "BOT_ACTION_DELAY", 0)

    async def exercise():
        state = _repeated_bots(bot_id)
        state.phase = "claim"
        state.players[seat].hand = ["AX1", "AX2"]
        state.players[0].discards = ["AX3"]
        state.last_discard = {"player": 0, "tile": "AX3"}
        candidate = {"id": "ron", "kind": "win", "priority": 12}
        state.claim_options = {seat: [candidate]}
        state.claim_window = ClaimWindow(state.claim_options, {seat})
        await state._bot_claim(seat, state.action_tick)
        queued = state.action_queues[seat].get_nowait()
        assert queued["action_type"] == "claim"
        assert queued["candidate_id"] == "ron"
        assert all(queue.empty() for queue in state.action_queues.values())

    asyncio.run(exercise())
