import asyncio
import time
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .bot import shanghai_smart_bot_action


def state_for_action():
    player = SimpleNamespace(hand_tiles=[11], riichi_candidate_cuts={},
                             kuikae_forbidden_tiles=set(), ready_locked=True,
                             has_draw_slot=True)
    return SimpleNamespace(player_list=[player], waiting_players_list=[0],
                           server_action_tick=1, claim_protection=False)


@pytest.mark.parametrize("status,action", [
    ("waiting_hand_action", "buhua"),
    ("waiting_hand_action", "cut"),
    ("onlycut_after_action", "cut"),
])
def test_bot_does_not_submit_flower_or_cut_in_same_frame(status, action):
    async def run():
        state = state_for_action()
        with patch('server.gamestate.game_shanghai.bot.get_ai_action', new=AsyncMock()) as send:
            started = time.monotonic()
            await shanghai_smart_bot_action(state, 0, [action], status)
            assert time.monotonic() - started >= 0.45
            send.assert_awaited_once()
            assert send.await_args.args[2] == action
    asyncio.run(run())


def test_bot_cancels_delayed_action_when_ask_window_changes():
    async def run():
        state = state_for_action()
        with patch('server.gamestate.game_shanghai.bot.get_ai_action', new=AsyncMock()) as send:
            task = asyncio.create_task(shanghai_smart_bot_action(state, 0, ["buhua"], "waiting_hand_action"))
            await asyncio.sleep(0.05)
            state.server_action_tick += 1
            await task
            send.assert_not_awaited()
    asyncio.run(run())
