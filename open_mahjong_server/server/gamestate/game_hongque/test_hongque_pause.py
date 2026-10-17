"""Shared pause/resume must not consume Hongque action clocks or skip a round."""
import asyncio
from unittest.mock import patch, AsyncMock, Mock
import pytest

from .test_hongque_reconnect import stage_fixture
from ..public.vote_manager import VoteManager
from ..public import vote_manager


@pytest.mark.parametrize("stage", ["turn", "claim", "tactical", "onlycut"])
def test_resume_restarts_usable_action_window_without_spending_paused_time(stage):
    async def run():
        game, server = await stage_fixture(stage)
        game._start_round = AsyncMock()
        vote = game.vote_manager = VoteManager(game)
        vote.phase = "pause_pending"
        with patch.object(vote_manager, "RESUME_COUNTDOWN", 0):
            game.game_task = asyncio.create_task(game.run_game_loop())
            try:
                async with asyncio.timeout(2):
                    while vote.phase != "paused":
                        await asyncio.sleep(0.001)
                tick = game.action_tick
                deadline = (game.claim_window.deadline if stage == "tactical" else
                            game.claim_deadlines[1] if stage == "claim" else game.turn_deadline)
                await asyncio.sleep(0.05)
                vote._pause_event.set()
                async with asyncio.timeout(2):
                    while not game._in_wait_action:
                        await asyncio.sleep(0.001)
                restored_deadline = (game.claim_window.deadline if stage == "tactical" else
                                     game.claim_deadlines[1] if stage == "claim" else game.turn_deadline)
                assert restored_deadline >= deadline + 0.045
                assert game.action_tick > tick
                seat = (min(game.claim_window.pending) if game.phase == "claim"
                        else game.current_player_index)
                packets = server.user_id_to_connection[game.players[seat].user_id].websocket.packets
                state = next(p["hongque_state"] for p in reversed(packets) if "hongque_state" in p)
                expected = "claim" if stage in {"claim", "tactical"} else "discard"
                assert expected in state["legal_actions"] and state["action_tick"] == game.action_tick
            finally:
                vote._cancel_all_timers()
                await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("bot_id", [2, 3])
def test_resume_reschedules_bot_claim_for_the_new_action_tick(bot_id):
    async def run():
        game, _ = await stage_fixture("claim")
        game.players[1].user_id = bot_id
        game._start_round = AsyncMock()
        game._schedule_bot_claim = Mock()
        vote = game.vote_manager = VoteManager(game)
        vote.phase = "pause_pending"
        with patch.object(vote_manager, "RESUME_COUNTDOWN", 0):
            game.game_task = asyncio.create_task(game.run_game_loop())
            try:
                async with asyncio.timeout(2):
                    while vote.phase != "paused":
                        await asyncio.sleep(0.001)
                vote._pause_event.set()
                async with asyncio.timeout(2):
                    while not game._in_wait_action:
                        await asyncio.sleep(0.001)
                game._schedule_bot_claim.assert_called_once_with(1, game.action_tick)
            finally:
                vote._cancel_all_timers()
                await game.cleanup_game_state()
    asyncio.run(run())


def test_next_round_waits_until_resume_broadcast_and_clock_restore_finish():
    async def run():
        game, _ = await stage_fixture("self_draw")
        game._start_round = AsyncMock()
        game._wait_for_hu_ready = AsyncMock()
        vote = game.vote_manager = VoteManager(game)
        vote.phase = "pause_pending"
        idle_sent = asyncio.Event()
        finish_broadcast = asyncio.Event()
        original = vote._broadcast

        async def slow_broadcast(*args, **kwargs):
            if vote.phase == "idle":
                idle_sent.set()
                await finish_broadcast.wait()
            await original(*args, **kwargs)

        vote._broadcast = slow_broadcast
        with patch.object(vote_manager, "RESUME_COUNTDOWN", 0):
            game.game_task = asyncio.create_task(game.run_game_loop())
            try:
                async with asyncio.timeout(2):
                    while vote.phase != "paused":
                        await asyncio.sleep(0.001)
                game._round_task = asyncio.create_task(game._complete_round_after_result(True, 0))
                vote._pause_event.set()
                await asyncio.wait_for(idle_sent.wait(), 1)
                await asyncio.sleep(0.08)
                assert game.current_round == 1
                finish_broadcast.set()
                await asyncio.wait_for(game._round_task, 1)
                assert game.current_round == 2
            finally:
                finish_broadcast.set()
                vote._cancel_all_timers()
                await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("phase", ["paused", "resume_voting", "resume_countdown"])
def test_ready_confirmation_cannot_start_next_round_during_pause(phase):
    async def run():
        game, _ = await stage_fixture("self_draw")
        vote = game.vote_manager = VoteManager(game)
        vote.phase = phase
        game._wait_for_hu_ready = AsyncMock()
        task = asyncio.create_task(game._complete_round_after_result(True, 0))
        try:
            await asyncio.sleep(0.03)
            assert game.current_round == 1 and game.phase == "round_end"
            vote.phase = "idle"
            await asyncio.wait_for(task, 1)
            assert game.current_round == 2 and game.phase == "turn"
        finally:
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await game.cleanup_game_state()
    asyncio.run(run())
