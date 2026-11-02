"""A delayed spectator cannot keep a match alive or outlive its final record."""
import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .spectator_manager import SpectatorManager


def manager():
    game = NS(gamestate_id="old-game", room_id="1", player_list=[NS(user_id=101)])
    result = SpectatorManager(game, delay=0)
    result.game_title = {"rule": "guobiao"}
    result.round_headers = {1: {"timestamp": 0, "data": {"round_index": 1}}}
    result.round_ticks = {1: []}
    result.current_round_index = 1
    return result


def test_stalled_final_recipient_does_not_block_other_viewers_or_close(monkeypatch):
    async def run():
        spectator = manager()
        monkeypatch.setattr(spectator, "send_timeout", .02, raising=False)
        async def stall(_):
            await asyncio.Event().wait()
        spectator.spectator_connections = {
            201: NS(websocket=NS(send_json=stall)),
            202: NS(websocket=NS(send_json=AsyncMock())),
        }
        fast = spectator.spectator_connections[202].websocket.send_json
        await asyncio.wait_for(spectator.send_final_record_and_close(), .5)
        fast.assert_awaited_once()
        assert fast.call_args.args[0]["type"] == "spectator/record_complete"
        assert not spectator.spectator_connections
        assert not spectator.spectator_send_tasks
    asyncio.run(run())


def test_inflight_update_stops_before_final_record():
    async def run():
        spectator = manager()
        started, stopped = asyncio.Event(), asyncio.Event()
        final_after_stop = []
        async def send(packet):
            if packet["type"] == "spectator/record_update":
                started.set()
                try:
                    await asyncio.Event().wait()
                finally:
                    stopped.set()
            elif packet["type"] == "spectator/record_complete":
                final_after_stop.append(stopped.is_set())
        socket = NS(send_json=AsyncMock(side_effect=send))
        await spectator.add_spectator(201, NS(websocket=socket))
        spectator.record_tick(["c", 21, "F"])
        await asyncio.wait_for(started.wait(), 1.5)
        await spectator.send_final_record_and_close()
        final = [c.args[0] for c in socket.send_json.call_args_list
                 if c.args[0]["type"] == "spectator/record_complete"]
        assert len(final) == 1 and stopped.is_set()
        assert final_after_stop == [True], "final record raced a live incremental writer"
        # Exceptions from websocket.send_json are normally isolated from the game.
        assert socket.send_json.await_args_list[-1].args[0]["type"] == "spectator/record_complete"
    asyncio.run(run())


@pytest.mark.parametrize("blocked_type", ["spectator/add_spectator", "spectator/record_init"])
def test_initial_delivery_finishes_before_final_and_never_restarts_a_closed_loop(blocked_type):
    async def run():
        spectator = manager()
        started, release = asyncio.Event(), asyncio.Event()
        messages = []
        async def send(packet):
            if packet["type"] == blocked_type:
                started.set()
                await release.wait()
            messages.append(packet["type"])
        join = asyncio.create_task(spectator.add_spectator(201, NS(websocket=NS(send_json=send))))
        await started.wait()
        finish = asyncio.create_task(spectator.send_final_record_and_close())
        await asyncio.sleep(0)
        release.set()
        try:
            await asyncio.wait_for(asyncio.gather(join, finish), .5)
            assert messages == ["spectator/add_spectator", "spectator/record_init", "spectator/record_complete"]
            assert not spectator.spectator_connections
            assert not spectator.spectator_send_tasks
        finally:
            await spectator.cleanup()
    asyncio.run(run())


def test_join_after_close_cannot_start_another_stream():
    async def run():
        spectator = manager()
        await spectator.send_final_record_and_close()
        socket = NS(send_json=AsyncMock())
        try:
            await spectator.add_spectator(201, NS(websocket=socket))
            assert not spectator.spectator_connections
            assert not spectator.spectator_send_tasks
            assert all(c.args[0].get("success") is False for c in socket.send_json.call_args_list)
        finally:
            await spectator.cleanup()
    asyncio.run(run())


def test_failed_initial_ack_releases_subscription():
    async def run():
        spectator = manager()
        await spectator.add_spectator(201, NS(websocket=NS(send_json=AsyncMock(side_effect=ConnectionError))))
        assert not spectator.spectator_connections
        assert not spectator.spectator_progress
        assert not spectator.spectator_send_tasks
    asyncio.run(run())


@pytest.mark.parametrize("close", ["remove", "cleanup"])
def test_removing_pending_join_keeps_request_handler_alive(close):
    async def run():
        spectator = manager()
        started = asyncio.Event()
        async def stall(_):
            started.set()
            await asyncio.Event().wait()
        join = asyncio.create_task(spectator.add_spectator(201, NS(websocket=NS(send_json=stall))))
        await started.wait()
        if close == "remove":
            await spectator.remove_spectator(201)
        else:
            await spectator.cleanup()
        await asyncio.wait_for(join, .5)
        assert not join.cancelled()
        assert not spectator.spectator_connections and not spectator.spectator_send_tasks
    asyncio.run(run())


def test_update_failure_removes_its_own_stream_without_self_await():
    async def run():
        spectator = manager()
        async def send(packet):
            if packet["type"] == "spectator/record_update":
                raise ConnectionError("closed")
        await spectator.add_spectator(201, NS(websocket=NS(send_json=send)))
        delivery = spectator.spectator_send_tasks[201]
        spectator.record_tick(["c", 21, "F"])
        await asyncio.wait_for(delivery, 1.5)
        assert not spectator.spectator_connections
        assert not spectator.spectator_send_tasks
    asyncio.run(run())


def test_concurrent_rejoins_keep_only_the_latest_subscription():
    async def run():
        spectator = manager()
        writing, stopping, release = asyncio.Event(), asyncio.Event(), asyncio.Event()
        async def send(packet):
            if packet['type'] == 'spectator/record_update':
                writing.set()
                try:
                    await asyncio.Event().wait()
                finally:
                    stopping.set()
                    await release.wait()
        await spectator.add_spectator(201, NS(websocket=NS(send_json=send)))
        spectator.record_tick(['c', 21, 'F'])
        await asyncio.wait_for(writing.wait(), 1.5)
        first_connection = NS(websocket=NS(send_json=AsyncMock()))
        latest_connection = NS(websocket=NS(send_json=AsyncMock()))
        first = asyncio.create_task(spectator.add_spectator(201, first_connection))
        await stopping.wait()
        latest = asyncio.create_task(spectator.add_spectator(201, latest_connection))
        await asyncio.sleep(0)
        await asyncio.sleep(0)
        release.set()
        await asyncio.wait_for(asyncio.gather(first, latest), .5)
        writers = [task for task in asyncio.all_tasks()
                   if task.get_coro().__qualname__ == 'SpectatorManager._delivery_loop']
        try:
            assert spectator.spectator_connections[201] is latest_connection
            assert len(writers) == 1
            assert spectator.spectator_send_tasks[201] is writers[0]
        finally:
            await spectator.cleanup()
            await spectator._stop_tasks(writers)
    asyncio.run(run())
