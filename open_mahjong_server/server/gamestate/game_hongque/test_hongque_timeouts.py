"""Disconnect/timeout boundaries must advance or retain a usable action window."""
import asyncio
import importlib
import time
from unittest.mock import AsyncMock, patch

import pytest

from .test_hongque_reconnect import stage_fixture
from .state_machine import HongqueStatus
from . import wait_action as waits
from . import get_action as bots

module = importlib.import_module("server.gamestate.game_hongque.HongqueGameState")


@pytest.mark.parametrize("stage", ["turn", "onlycut", "supplement", "kong"])
@pytest.mark.parametrize("clock_missing", [False, True])
def test_disconnected_turn_timeout_discards_once_and_remains_reconnectable(stage, clock_missing):
    async def run():
        game, server = await stage_fixture(stage)
        player = game.players[game.current_player_index]
        expected = player.drawn_tile or player.hand[-1]
        before = len(player.hand)
        await game.player_disconnect(player.user_id)
        player.remaining_time = 0
        game.step_time = 0
        game.turn_started_at = None if clock_missing else time.monotonic()-1
        game.turn_deadline = None if clock_missing else time.monotonic()-1
        try:
            await asyncio.wait_for(game.wait_action(), 2)
            assert player.discards[-1] == expected and len(player.hand) == before-1
            assert game.state_machine.status == HongqueStatus.RESOLVING_DISCARD
            await game._drive_local()
            await game.player_reconnect(player.user_id)
            packet = next(p for p in reversed(server.user_id_to_connection[player.user_id].websocket.packets)
                          if p["type"] == "gamestate/hongque/reconnect")
            assert packet["hongque_state"]["hand"] == player.hand
            assert not game._in_wait_action
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_initial_claim_timeouts_pass_every_player_and_continue_next_draw():
    async def run():
        game, _ = await stage_fixture("claim")
        try:
            for seat in game.claim_window.pending:
                game.claim_deadlines[seat] = time.monotonic()-1
            await asyncio.wait_for(game.wait_action(), 2)
            await game._drive_local()
            assert game.phase == "turn" and game.current_player_index == 1
            assert game.claim_window is None
            assert all(p.remaining_time == 0 for p in game.players[1:])
            assert all(not p.melds for p in game.players)
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("all_ready", [False, True])
def test_ready_timeout_and_duplicate_ready_advance_exactly_one_round(all_ready):
    async def run():
        game, _ = await stage_fixture("self_draw")
        try:
            if all_ready:
                for player in game.players:
                    await game.submit_action(player.user_id, "ready")
                    await game.submit_action(player.user_id, "ready")
            with patch.object(module, "hu_result_ready_wait_seconds", return_value=0), \
                 patch.object(module, "hu_result_ready_pre_panel_seconds", return_value=0):
                await game._complete_round_after_result(True, 0)
            assert game.current_round == 2 and game.phase == "turn"
            assert all(len(p.score_history) == 1 for p in game.players)
            assert not game._ready_phase_active
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_ready_arriving_during_event_clear_is_not_lost():
    async def run():
        game, _ = await stage_fixture("self_draw")
        try:
            original_clear = game._ready_event.clear
            def racing_clear():
                original_clear()
                game._ready_players.update(range(4))
            game._ready_event.clear = racing_clear
            await asyncio.wait_for(game._wait_for_hu_ready(0, pre_panel_delay_sec=0), 1)
            assert game._ready_players == set(range(4))
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("plan,can_supplement,expected", [
    ({"action": "win"}, True, "supplement"),
    ({"action": "win"}, False, "discard"),
    ({"action": "supplement"}, True, "supplement"),
    ({"action": "kong", "candidate_id": "k"}, True, "kong"),
    ({"action": "discard", "tile": "stale-tile"}, True, "discard"),
])
def test_bot_onlycut_decision_adapter_and_fallback_keep_correct_seat(plan, can_supplement, expected):
    async def run():
        game, _ = await stage_fixture("onlycut")
        player = game.players[game.current_player_index]
        player.user_id = 2
        if not can_supplement:
            player.supplements = 2
        game.submit_action = AsyncMock()
        try:
            with patch.object(bots, "run_room_bot_cpu", AsyncMock(return_value=plan)), \
                 patch.object(bots, "wait_bot_delay", AsyncMock()):
                await bots.bot_turn(game, game.action_tick)
            game.submit_action.assert_awaited_once()
            call = game.submit_action.call_args
            assert call.args == (2, expected)
            assert call.kwargs["player_index"] == player.index
            if expected == "discard":
                assert call.kwargs["tile"] in player.hand
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("when", ["before", "after"])
def test_delayed_bot_result_does_not_act_in_new_window(when):
    async def run():
        game, _ = await stage_fixture("turn")
        player = game.players[0]
        player.user_id = 2
        game.submit_action = AsyncMock()
        tick = game.action_tick
        if when == "before":
            game._advance_tick()
        async def advance(*args):
            game._advance_tick()
        try:
            with patch.object(bots, "run_room_bot_cpu", AsyncMock(return_value={"action": "discard", "tile": player.hand[0]})), \
                 patch.object(bots, "wait_bot_delay", advance):
                await bots.bot_turn(game, tick)
            game.submit_action.assert_not_awaited()
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


def test_claim_bot_failure_degrades_to_pass_and_does_not_leave_waiter_hanging():
    async def run():
        game, _ = await stage_fixture("claim")
        game.players[1].user_id = 2
        game.submit_action = AsyncMock()
        try:
            with patch.object(bots, "run_room_bot_cpu", AsyncMock(side_effect=RuntimeError("decision failed"))), \
                 patch.object(bots, "wait_bot_delay", AsyncMock()):
                await bots.bot_claim(game, 1, game.action_tick)
            game.submit_action.assert_awaited_once()
            assert game.submit_action.call_args.args == (2, "pass")
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())
