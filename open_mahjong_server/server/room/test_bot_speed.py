import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .room_manager import RoomManager
from .room_router import handle_room_message
from ..gamestate.public.ai.pacing import bot_delay, configure_bot_pacing
from ..gamestate.public.ai.auto_cut_ai import auto_cut_action


def manager():
    result = RoomManager.__new__(RoomManager)
    result.rooms = {"123": {"player_list": [100, 101, 2], "ready_list": [101],
                            "is_game_running": False}}
    result.game_server = NS(players={"host": NS(user_id=100, current_room_id="123"),
                                     "guest": NS(user_id=101, current_room_id="123"),
                                     "outside": NS(user_id=102, current_room_id="999")})
    result._broadcast_room_info = AsyncMock()
    return result


@pytest.mark.parametrize("speed,delay", [("instant", 0.0), ("fast", 0.5), ("medium", 1.0), ("slow", 1.5), ("standard", 0.5)])
def test_host_speed_is_room_owned_and_keeps_ready_state(speed, delay):
    room_manager = manager()
    assert asyncio.run(room_manager.set_bot_speed("host", "123", speed)) is None
    room = room_manager.rooms["123"]
    assert room["ready_list"] == [101]
    room_manager._broadcast_room_info.assert_awaited_once_with("123")
    # All robot types, including robots seated later, read the new game snapshot.
    state = NS()
    configure_bot_pacing(state, dict(room))
    assert bot_delay(state) == delay


@pytest.mark.parametrize("connection,room_id,speed,running", [
    ("guest", "123", "fast", False), ("outside", "123", "fast", False),
    ("missing", "123", "fast", False), ("host", "missing", "fast", False),
    ("host", "123", "fast", True), ("host", "123", [], False),
    ("host", "123", None, False), ("host", "123", 0, False),
    ("host", "123", "turbo", False),
])
def test_speed_rejects_invalid_or_unauthorized_changes(connection, room_id, speed, running):
    room_manager = manager()
    room_manager.rooms["123"]["is_game_running"] = running
    response = asyncio.run(room_manager.set_bot_speed(connection, room_id, speed))
    assert response.success is False
    assert "bot_speed" not in room_manager.rooms["123"]
    room_manager._broadcast_room_info.assert_not_awaited()


def test_speed_request_broadcasts_authoritative_value_to_every_human():
    async def run():
        room_manager = manager()
        server = room_manager.game_server
        server.room_manager = room_manager
        server.user_id_to_connection = {
            uid: NS(websocket=NS(send_json=AsyncMock())) for uid in (100, 101)
        }
        del room_manager._broadcast_room_info  # Exercise the real room broadcaster.
        host_socket = server.user_id_to_connection[100].websocket
        await handle_room_message(server, "host", {
            "type": "room/set_bot_speed", "room_id": "123", "bot_speed": "slow",
        }, host_socket)
        for connection in server.user_id_to_connection.values():
            message = connection.websocket.send_json.await_args.args[0]
            assert message["type"] == "room/refresh_room_info"
            assert message["room_info"]["bot_speed"] == "slow"
            assert message["room_info"]["ready_list"] == [101]
    asyncio.run(run())


@pytest.mark.parametrize("speed,delay", [("instant", 0.0), ("fast", 0.5), ("medium", 1.0), ("slow", 1.5)])
def test_real_bot_wait_uses_the_selected_speed(monkeypatch, speed, delay):
    async def run():
        sleep = AsyncMock()
        send = AsyncMock()
        monkeypatch.setattr("server.gamestate.public.ai.pacing.asyncio.sleep", sleep)
        monkeypatch.setattr("server.gamestate.public.ai.auto_cut_ai.get_ai_action", send)
        state = NS(bot_speed=speed, room_rule="riichi", claim_protection=False,
                   server_action_tick=1, waiting_players_list=[0],
                   player_list=[NS(user_id=0, username="test", hand_tiles=[11], has_draw_slot=True, tag_list=[])])
        await auto_cut_action(state, 0, ["cut"], "waiting_hand_action")
        send.assert_awaited_once()
        if delay == 0:
            sleep.assert_not_awaited()
        else:
            sleep.assert_awaited_once()
            assert delay - 0.03 <= sleep.await_args.args[0] <= delay
    asyncio.run(run())


def test_instant_does_not_add_an_ai_post_meld_sleep(monkeypatch):
    async def run():
        sleep, send = AsyncMock(), AsyncMock()
        monkeypatch.setattr("server.gamestate.public.ai.pacing.asyncio.sleep", sleep)
        monkeypatch.setattr("server.gamestate.public.ai.auto_cut_ai.get_ai_action", send)
        state = NS(bot_speed="instant", room_rule="qingque", claim_protection=True,
                   server_action_tick=1, waiting_players_list=[0],
                   player_list=[NS(user_id=0, username="test", hand_tiles=[11], has_draw_slot=True, tag_list=[])])
        await auto_cut_action(state, 0, ["cut"], "onlycut_after_action")
        send.assert_awaited_once()
        sleep.assert_not_awaited()
    asyncio.run(run())
