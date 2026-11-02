"""Exercise Hongque through the real match and GameState lifecycle owners."""
import asyncio
import importlib
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_lifecycle import USERS, server_fixture

hongque = importlib.import_module("server.gamestate.game_hongque.HongqueGameState")
QUEUES = ("hongque_elo_banzhuang", "hongque_elo_quanzhuang")


async def wait_until(predicate):
    async with asyncio.timeout(5):
        while not predicate():
            await asyncio.sleep(.005)


async def create_last_round(queue):
    server = server_fixture()
    server.match_manager.MATCH_FOUND_DELAY_SECONDS = 0
    server.match_manager.winning_queues = dict.fromkeys(USERS, queue)
    server.db_manager.settle_rated_game.return_value = {str(uid): {} for uid in USERS}
    server.db_manager.store_hongque_game_record.return_value = "lifecycle-test"
    server.friend_manager = SimpleNamespace(on_game_end=AsyncMock())
    await server.match_manager._start_game(queue, USERS)
    game = next(iter(server.gamestate_manager.gamestate_id_to_game_state.values()))
    # The constructor still uses the production 8/16-hand configuration. Start
    # the final hand directly to isolate the lifetime boundary from scoring.
    game.current_round = game.max_round
    await wait_until(lambda: game.phase == "turn" and game._in_wait_action)
    for conn in server.user_id_to_connection.values():
        assert conn.websocket.send_json.call_args_list[0].args[0]["type"] == "gamestate/hongque/game_start"
    return server, game


async def exhaust_wall(game):
    game.wall.clear()
    player = game.players[game.current_player_index]
    await game.submit_action(player.user_id, "discard", tile=player.drawn_tile, action_tick=game.action_tick)
    async with asyncio.timeout(5):
        while game.phase != "game_end":
            if game.phase == "claim":
                for seat in list(game.claim_window.pending):
                    await game.submit_action(game.players[seat].user_id, "force_pass", action_tick=game.action_tick)
            await asyncio.sleep(.005)


@pytest.mark.parametrize("queue", QUEUES)
@pytest.mark.parametrize("slow_recipient", ["player", "spectator"])
def test_terminal_delivery_completes_before_match_cleanup(queue, slow_recipient):
    async def run():
        server, game = await create_last_round(queue)
        entered, release = asyncio.Event(), asyncio.Event()
        packets = []

        async def send(payload):
            if payload["type"] in ("gamestate/hongque/game_end", "spectator/record_complete"):
                entered.set()
                await release.wait()
            packets.append(payload)

        socket = SimpleNamespace(send_json=send)
        if slow_recipient == "player":
            server.user_id_to_connection[USERS[0]].websocket = socket
        else:
            game.spectator_manager.spectator_connections[990001] = SimpleNamespace(websocket=socket)
        try:
            with patch.object(hongque, "liuju_ready_wait_seconds", return_value=0):
                await exhaust_wall(game)
                await asyncio.wait_for(entered.wait(), 2)
                await asyncio.sleep(.15)  # Allow the normal action loop to observe END.
                assert game.lifecycle_state == "running"
                assert not game.game_task.done()
                assert server.match_manager.committed_users == set(USERS)
                release.set()
                await asyncio.wait_for(game.game_task, 6)
            assert game.lifecycle_state == "closed" and game.close_reason == "completed"
            assert any(p["type"] == ("gamestate/hongque/game_end" if slow_recipient == "player"
                                     else "spectator/record_complete") for p in packets)
            assert not server.match_manager.committed_users
            assert not server.match_manager.gamestate_to_match
            assert not server.room_manager.match_room_ids
            assert not server.gamestate_manager.user_id_to_game_state
            assert server.match_manager.playing_counts[queue] == 0
            assert not game.spectator_manager.spectator_connections
            server.friend_manager.on_game_end.assert_awaited_once_with(game)
            for uid, conn in server.user_id_to_connection.items():
                if uid == USERS[0] and slow_recipient == "player":
                    sent = packets
                else:
                    sent = [call.args[0] for call in conn.websocket.send_json.call_args_list]
                assert sum(p["type"] == "gamestate/hongque/game_end" for p in sent) == 1
                assert all(p["type"] != "gamestate/closed" for p in sent)
                assert server.match_manager.snapshot(uid)["match_committed"] is False
        finally:
            release.set()
            await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
    asyncio.run(run())


@pytest.mark.parametrize("queue", QUEUES)
@pytest.mark.parametrize("recipient", ["player", "delayed", "realtime"])
def test_stalled_terminal_recipient_still_releases_the_match(queue, recipient):
    async def run():
        server, game = await create_last_round(queue)
        async def send(payload):
            if payload["type"].endswith(("/game_end", "/record_complete")):
                await asyncio.Event().wait()
        socket = SimpleNamespace(send_json=send)
        if recipient == "player":
            server.user_id_to_connection[USERS[0]].websocket = socket
        elif recipient == "delayed":
            game.spectator_manager.spectator_connections[990001] = SimpleNamespace(websocket=socket)
            game.spectator_manager.send_timeout = .02
        else:
            server.user_id_to_connection[990001] = SimpleNamespace(websocket=socket)
            game.realtime_spectators = [SimpleNamespace(user_id=990001, host_user_id=USERS[0])]
        try:
            with patch.object(hongque, "liuju_ready_wait_seconds", return_value=0), \
                 patch("server.gamestate.public.connection_delivery.SEND_TIMEOUT_SECONDS", .02):
                await exhaust_wall(game)
                await asyncio.wait_for(game.game_task, 6)
            assert game.close_reason == "completed" and game.lifecycle_state == "closed"
            assert not server.match_manager.committed_users
            assert not server.match_manager.gamestate_to_match
            assert not server.gamestate_manager.user_id_to_game_state
            assert not server.room_manager.match_room_ids
            assert all(not server.match_manager.snapshot(uid)["match_committed"] for uid in USERS)
        finally:
            await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
    asyncio.run(run())


@pytest.mark.parametrize("queue", QUEUES)
def test_finalization_error_is_observed_by_main_task_and_releases_match(queue):
    async def run():
        server, game = await create_last_round(queue)
        try:
            with patch.object(hongque, "liuju_ready_wait_seconds", return_value=0), \
                 patch.object(hongque, "persist_and_remember", side_effect=RuntimeError("injected terminal failure")):
                await exhaust_wall(game)
                await asyncio.wait_for(game.game_task, 3)
            assert game.close_reason == "runtime_error" and game.lifecycle_state == "closed"
            assert not server.match_manager.committed_users
            assert not server.gamestate_manager.gamestate_id_to_game_state
            for conn in server.user_id_to_connection.values():
                sent = [call.args[0] for call in conn.websocket.send_json.call_args_list]
                assert sum(p["type"] == "gamestate/closed" for p in sent) == 1
        finally:
            await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
    asyncio.run(run())


@pytest.mark.parametrize("queue", QUEUES)
def test_closing_during_final_round_broadcast_cancels_terminal_work(queue):
    async def run():
        server, game = await create_last_round(queue)
        entered = asyncio.Event()
        broadcast = game.broadcast_state

        async def blocked_terminal_state():
            if game.phase == "game_end":
                entered.set()
                await asyncio.Event().wait()
            await broadcast()

        try:
            with patch.object(game, "broadcast_state", side_effect=blocked_terminal_state), \
                 patch.object(hongque, "liuju_ready_wait_seconds", return_value=0), \
                 patch.object(hongque, "persist_and_remember") as persist:
                await exhaust_wall(game)
                await asyncio.wait_for(entered.wait(), 2)
                await asyncio.sleep(.15)  # Main task is awaiting the last round task.
                await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
                persist.assert_not_called()
            assert game.game_task.done()
            assert not server.match_manager.committed_users
            assert not server.gamestate_manager.gamestate_id_to_game_state
        finally:
            await server.gamestate_manager.cleanup_game_state_complete(game.gamestate_id)
    asyncio.run(run())
