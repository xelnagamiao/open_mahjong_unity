"""掉线托管首个出牌询问保护测试。"""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from .ai.auto_cut_ai import auto_cut_action
from .offline.offline_auto_action import (
    _OFFLINE_FIRST_CUT_READY,
    _OFFLINE_FIRST_CUT_TICK,
    offline_auto_action,
    schedule_offline_auto_on_disconnect,
)


def _make_player(user_id=11, username="u"):
    return SimpleNamespace(
        user_id=user_id,
        username=username,
        player_index=0,
        hand_tiles=[1, 2, 3],
        tag_list=[],
        dingque_suit=0,
    )


def _make_state(player, tick=1):
    return SimpleNamespace(
        player_list=[player],
        server_action_tick=tick,
        room_rule="taiwan",
    )


def _set_protection_pending(player):
    setattr(player, _OFFLINE_FIRST_CUT_READY, True)
    setattr(player, _OFFLINE_FIRST_CUT_TICK, None)


def test_disconnect_marks_first_cut_protection():
    player = _make_player()
    state = SimpleNamespace(player_list=[player])
    schedule_offline_auto_on_disconnect(state, 11)
    assert getattr(player, _OFFLINE_FIRST_CUT_READY) is True
    assert getattr(player, _OFFLINE_FIRST_CUT_TICK) is None


def test_first_ask_after_disconnect_skips_cut_then_next_ask_cuts():
    async def run():
        player = _make_player()
        _set_protection_pending(player)
        state = _make_state(player, tick=10)
        with patch(
            "server.gamestate.public.offline.offline_auto_action.get_ai_action",
            new_callable=AsyncMock,
        ) as act:
            await offline_auto_action(state, 0, ["cut"], "waiting_hand_action")
            act.assert_not_awaited()
            assert getattr(player, _OFFLINE_FIRST_CUT_READY) is False
            assert getattr(player, _OFFLINE_FIRST_CUT_TICK) == 10

            # 同一次询问的重复派发同样跳过，避免与断开瞬间的任务竞态
            await offline_auto_action(state, 0, ["cut"], "waiting_hand_action")
            act.assert_not_awaited()

            # 下一次询问恢复自动切牌
            state.server_action_tick = 11
            await offline_auto_action(state, 0, ["cut"], "waiting_hand_action")
            act.assert_awaited_once()

    asyncio.run(run())


def test_onlycut_after_action_first_ask_is_protected():
    async def run():
        player = _make_player()
        _set_protection_pending(player)
        state = _make_state(player, tick=3)
        state.claim_protection = False
        with patch(
            "server.gamestate.public.offline.offline_auto_action.get_ai_action",
            new_callable=AsyncMock,
        ) as act:
            await offline_auto_action(state, 0, ["cut"], "onlycut_after_action")
            act.assert_not_awaited()
            state.server_action_tick = 4
            await offline_auto_action(state, 0, ["cut"], "onlycut_after_action")
            act.assert_awaited_once()

    asyncio.run(run())


def test_auto_cut_action_first_ask_protected_for_offline_player():
    async def run():
        player = _make_player()
        player.tag_list = ["offline"]
        _set_protection_pending(player)
        state = _make_state(player, tick=5)
        state.claim_protection = False
        state.waiting_players_list = [0]
        state._waiting_action_tick = 5
        with patch(
            "server.gamestate.public.ai.auto_cut_ai.get_ai_action",
            new_callable=AsyncMock,
        ) as act:
            await auto_cut_action(state, 0, ["cut"], "waiting_hand_action")
            act.assert_not_awaited()
            state.server_action_tick = 6
            state._waiting_action_tick = 6
            await auto_cut_action(state, 0, ["cut"], "waiting_hand_action")
            act.assert_awaited_once()

    asyncio.run(run())


def test_auto_cut_action_bot_ignores_first_ask_protection():
    async def run():
        player = _make_player(user_id=0, username="bot")
        _set_protection_pending(player)
        state = _make_state(player, tick=5)
        state.claim_protection = False
        state.waiting_players_list = [0]
        state._waiting_action_tick = 5
        with patch(
            "server.gamestate.public.ai.auto_cut_ai.get_ai_action",
            new_callable=AsyncMock,
        ) as act:
            await auto_cut_action(state, 0, ["cut"], "waiting_hand_action")
            act.assert_awaited_once()

    asyncio.run(run())
