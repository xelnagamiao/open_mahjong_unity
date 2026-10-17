"""Main-loop matches, authenticated action routing and lifecycle failure branches."""
import asyncio
import importlib
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, patch

import pytest

from .test_hongque_reconnect import stage_fixture
from .state_machine import HongqueStatus, HongqueStateMachine, ALLOWED_TRANSITIONS
from ..gamestate_router import handle_gamestate_message

module = importlib.import_module("server.gamestate.game_hongque.HongqueGameState")


def route_server(game, server):
    server.players = {str(uid): conn for uid, conn in server.user_id_to_connection.items()}
    server.gamestate_manager.get_game_state_by_gamestate_id = lambda gid: game if gid == game.gamestate_id else None


async def route(game, server, seat, **action):
    uid = game.players[seat].user_id
    await handle_gamestate_message(server, str(uid), dict(
        type="gamestate/hongque/action", gamestate_id=game.gamestate_id,
        action_tick=game.action_tick, **action), server.user_id_to_connection[uid].websocket)


@pytest.mark.parametrize("rounds", [8, 16])
def test_full_main_loop_match_all_draws_reconnect_and_final_record(rounds):
    async def run():
        game, server = await stage_fixture("turn", game_round=rounds)
        route_server(game, server)
        rounds_seen, reconnects, actions = set(), set(), 0
        with patch.object(module, "liuju_ready_wait_seconds", return_value=0):
            game.game_task = asyncio.create_task(game.run_game_loop())
            try:
                deadline = asyncio.get_running_loop().time() + 50
                last_submitted = None
                while game.phase != "game_end":
                    assert asyncio.get_running_loop().time() < deadline, (game.phase, game.current_round)
                    assert not game.game_task.done(), game.game_task.exception()
                    rounds_seen.add(game.current_round)
                    if game._in_wait_action and game.action_tick != last_submitted:
                        if game.phase == "turn":
                            seat = game.current_player_index
                            if game.current_round not in reconnects:
                                await game.player_disconnect(game.players[seat].user_id)
                                await game.player_reconnect(game.players[seat].user_id)
                                reconnects.add(game.current_round)
                            last_submitted = game.action_tick
                            await route(game, server, seat, action="discard", tile=game.players[seat].drawn_tile)
                            actions += 1
                        elif game.phase == "claim":
                            last_submitted = game.action_tick
                            for seat in list(game.claim_window.pending):
                                await route(game, server, seat, action="force_pass")
                    await asyncio.sleep(0.001)
                await asyncio.wait_for(game.game_task, 5)
                if game._round_task:
                    await asyncio.wait_for(game._round_task, 8)
                assert rounds_seen == set(range(1, rounds+1))
                assert reconnects == rounds_seen
                assert actions == rounds * 82  # 126 tiles minus the other 44 starting tiles.
                assert len(game.game_record["game_round"]) == rounds
                assert [len(p.score_history) for p in game.players] == [rounds]*4
                for connection in server.user_id_to_connection.values():
                    endings = [p for p in connection.websocket.packets if p["type"] == "gamestate/hongque/game_end"]
                    assert len(endings) == 1
                    assert endings[0]["game_end_info"]["record_detail"]
                server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(gamestate_id=game.gamestate_id)
            finally:
                await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("stage,seat,action", [
    ("turn", 1, dict(action="discard", tile="AX1")),
    ("turn", 0, dict(action="discard", tile="invalid")),
    ("turn", 0, dict(action="discard", tile="AX1")),
    ("turn", 0, dict(action="win")),
    ("turn", 0, dict(action="kong", candidate_id="invalid")),
    ("turn", 0, dict(action="ready")),
    ("turn", 0, dict(action="unknown")),
    ("onlycut", 1, dict(action="win")),
    ("claim", 1, dict(action="claim", candidate_id="invalid")),
    ("claim", 0, dict(action="pass")),
    ("claim", 1, dict(action="discard", tile="AX2")),
    ("draw", 0, dict(action="ready")),
    ("game_end", 0, dict(action="discard", tile="AX1")),
])
def test_invalid_actions_return_error_without_mutating_authority(stage, seat, action):
    async def run():
        game, server = await stage_fixture(stage)
        route_server(game, server)
        socket = server.user_id_to_connection[game.players[seat].user_id].websocket
        before = game.build_state(seat, sync_mode="reconnect")
        try:
            await route(game, server, seat, **action)
            assert socket.packets[-1]["type"] == "tips"
            assert socket.packets[-1]["success"] is False
            after = game.build_state(seat, sync_mode="reconnect")
            for key in ("hand", "players", "action_tick", "phase", "events"):
                assert after[key] == before[key], key
        finally:
            await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("source", list(HongqueStatus))
@pytest.mark.parametrize("target", list(HongqueStatus))
def test_state_machine_all_edges_preserve_version_on_rejected_or_duplicate_transition(source, target):
    machine = HongqueStateMachine()
    machine.status = source
    if target != source and target not in ALLOWED_TRANSITIONS[source]:
        with pytest.raises(RuntimeError):
            machine.transition(target)
        assert machine.status == source and machine.version == 0 and machine.history == []
    else:
        machine.transition(target)
        assert machine.status == target
        assert machine.version == int(source != target)


@pytest.mark.parametrize("cleanup_fails", [False, True])
def test_main_loop_fault_requests_cleanup_without_secondary_unhandled_error(cleanup_fails):
    async def run():
        game, server = await stage_fixture("turn")
        game.game_loop_hongque = AsyncMock(side_effect=RuntimeError("injected game failure"))
        if cleanup_fails:
            server.gamestate_manager.cleanup_game_state_complete.side_effect = RuntimeError("injected cleanup failure")
        await game.run_game_loop()
        server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(
            gamestate_id=game.gamestate_id, reason="runtime_error")
        await game.cleanup_game_state()
    asyncio.run(run())


def test_cancellation_preserved_and_all_humans_offline_closes_once():
    async def run():
        game, server = await stage_fixture("turn")
        game.game_loop_hongque = AsyncMock(side_effect=asyncio.CancelledError)
        with pytest.raises(asyncio.CancelledError):
            await game.run_game_loop()
        for player in game.players[:3]:
            await game.player_disconnect(player.user_id)
        server.gamestate_manager.cleanup_game_state_complete.assert_not_awaited()
        await game.player_disconnect(game.players[3].user_id)
        server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
        assert game.close_reason == "all_humans_offline"
        await game.cleanup_game_state()
    asyncio.run(run())
