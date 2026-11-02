import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

from .connection_delivery import send_bounded
from .settlement_sync import send_game_packet


def test_scoping_does_not_mutate_shared_payload_or_relabel_a_previous_game():
    async def run():
        socket = NS(send_json=AsyncMock())
        payload = {"type": "gamestate/guobiao/game_end", "game_end_info": {"salt": "fixture"}}
        await send_game_packet(NS(gamestate_id="first"), socket, payload)
        await send_game_packet(NS(gamestate_id="second"), socket, payload)
        first, second = [call.args[0] for call in socket.send_json.call_args_list]
        assert "gamestate_id" not in payload
        assert first["gamestate_id"] == "first" and second["gamestate_id"] == "second"
        await send_game_packet(NS(gamestate_id="second"), socket, first)
        assert socket.send_json.call_args.args[0]["gamestate_id"] == "first"
        assert socket.send_json.call_args.args[0]["game_end_info"] == payload["game_end_info"]
    asyncio.run(run())


def test_lifecycle_cancellation_is_not_swallowed_as_a_send_timeout():
    async def run():
        started, stopped = asyncio.Event(), asyncio.Event()
        async def send(_):
            started.set()
            try:
                await asyncio.Event().wait()
            finally:
                stopped.set()
        task = asyncio.create_task(send_game_packet(NS(gamestate_id="game"), NS(send_json=send),
            {"type": "gamestate/hongque/game_end"}))
        await started.wait()
        task.cancel()
        result = await asyncio.gather(task, return_exceptions=True)
        assert isinstance(result[0], asyncio.CancelledError)
        assert stopped.is_set()
    asyncio.run(run())


def test_socket_timeout_cancels_only_the_send_and_owner_continues():
    async def run():
        stopped = asyncio.Event()
        async def send(_):
            try:
                await asyncio.Event().wait()
            finally:
                stopped.set()
        try:
            await send_bounded(NS(send_json=send), {}, timeout=.01)
        except TimeoutError:
            pass
        else:
            raise AssertionError("blocked socket did not time out")
        assert stopped.is_set()
        assert not asyncio.current_task().cancelling()
        await asyncio.sleep(0)
    asyncio.run(run())
