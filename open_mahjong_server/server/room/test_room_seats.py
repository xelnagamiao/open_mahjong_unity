import asyncio
from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

import pytest

from .room_manager import RoomManager
from .room_router import handle_room_message
from .room_seats import get_seats


def manager():
    result = RoomManager.__new__(RoomManager)
    result.rooms = {"123": {
        "room_id": "123", "instance_id": "test", "room_rule": "guobiao",
        "sub_rule": "guobiao/standard", "max_player": 4, "player_list": [100],
        "host_user_id": 100, "host_name": "user100", "has_password": False,
        "player_settings": {100: {"username": "user100"}}, "ready_list": [],
    }}
    players = {str(uid): NS(user_id=uid, username=f"user{uid}",
                            current_room_id="123" if uid == 100 else None,
                            websocket=NS(send_json=AsyncMock())) for uid in range(100, 105)}
    result.game_server = NS(
        players=players, user_id_to_connection={p.user_id: p for p in players.values()},
        db_manager=NS(get_user_settings=lambda uid: {"username": f"user{uid}"}),
        gamestate_manager=NS(is_user_in_active_game=lambda uid: False),
    )
    result._broadcast_room_info = AsyncMock()
    result.destroy_room = AsyncMock(side_effect=lambda rid: result.rooms.pop(rid))
    return result


@pytest.mark.parametrize("method,bot_id", [
    ("add_bot_to_room", 0), ("add_smart_bot_to_room", 2), ("add_guobiao_heuristic_bot_to_room", 3),
])
@pytest.mark.parametrize("seat", [2, 3])
def test_bot_enters_clicked_seat_without_filling_gap(method, bot_id, seat):
    room_manager = manager()
    response = asyncio.run(getattr(room_manager, method)("100", "123", seat))
    assert response.success
    room = room_manager.rooms["123"]
    expected = [100, -1, -1, -1]
    expected[seat] = bot_id
    assert room["seat_list"] == expected
    assert room["player_list"] == [100, bot_id]


@pytest.mark.parametrize("seat", [-1, 4, "2", True, 1.0, [], 0])
def test_invalid_or_occupied_seat_does_not_add_bot(seat):
    room_manager = manager()
    response = asyncio.run(room_manager.add_bot_to_room("100", "123", seat))
    assert response.success is False
    assert room_manager.rooms["123"]["player_list"] == [100]
    assert get_seats(room_manager.rooms["123"]) == [100, -1, -1, -1]
    room_manager._broadcast_room_info.assert_not_awaited()


def test_legacy_requests_fill_first_gap_and_duplicate_bots_remove_exact_seat():
    async def run():
        room_manager = manager()
        room = room_manager.rooms["123"]
        await room_manager.add_bot_to_room("100", "123", 3)
        await room_manager.add_bot_to_room("100", "123", 1)
        assert (await room_manager.kick_player_from_room("100", "123", 0, 3)).success
        assert room["seat_list"] == [100, 0, -1, -1]
        assert 0 in room["player_settings"]
        assert not (await room_manager.kick_player_from_room("100", "123", 0, 3)).success
        await room_manager.add_smart_bot_to_room("100", "123")
        assert room["seat_list"] == [100, 0, 2, -1]
        await room_manager.kick_player_from_room("100", "123", 0)
        assert 0 not in room["player_settings"]
    asyncio.run(run())


def test_host_transfers_by_arrival_order_without_moving_people_or_bots():
    async def run():
        room_manager = manager()
        room = room_manager.rooms["123"]
        await room_manager.add_bot_to_room("100", "123", 1)
        await room_manager.join_room("101", "123", "")
        await room_manager.join_room("102", "123", "")
        assert room["seat_list"] == [100, 0, 101, 102]
        await room_manager.leave_room("100", "123")
        assert room["seat_list"] == [-1, 0, 101, 102]
        assert room["host_user_id"] == 101
        await room_manager.join_room("100", "123", "")
        assert room["seat_list"] == [100, 0, 101, 102]
        assert room["host_user_id"] == 101
        assert (await room_manager.set_player_ready("100", "123", True)) is None
        assert not room_manager.all_players_ready(room)
        assert (await room_manager.set_player_ready("102", "123", True)) is None
        assert room_manager.all_players_ready(room)
        assert (await room_manager.set_bot_speed("101", "123", "slow")) is None
        assert not (await room_manager.set_bot_speed("100", "123", "slow")).success
        await room_manager.leave_room("101", "123")
        assert room["host_user_id"] == 102
        assert room["seat_list"] == [100, 0, -1, 102]
        await room_manager.leave_room("102", "123")
        assert room["host_user_id"] == 100
        assert room["seat_list"] == [100, 0, -1, -1]
        await room_manager.leave_room("100", "123")
        room_manager.destroy_room.assert_awaited_once_with("123")
    asyncio.run(run())


def test_empty_event_room_retains_empty_seats_and_accepts_new_host():
    async def run():
        room_manager = manager()
        room = room_manager.rooms["123"]
        room["persist_empty"] = True
        await room_manager.add_smart_bot_to_room("100", "123", 3)
        await room_manager.leave_room("100", "123")
        assert room["seat_list"] == [-1] * 4
        assert room["player_list"] == []
        assert room["host_user_id"] == 0
        await room_manager.join_room("101", "123", "")
        assert room["seat_list"] == [101, -1, -1, -1]
        assert room["host_user_id"] == 101
    asyncio.run(run())


def test_unauthorized_running_and_stale_additions_are_rejected():
    async def run():
        room_manager = manager()
        room = room_manager.rooms["123"]
        assert not (await room_manager.add_bot_to_room("101", "123", 2)).success
        room["is_game_running"] = True
        assert not (await room_manager.add_bot_to_room("100", "123", 2)).success
        room["is_game_running"] = False
        replies = await asyncio.gather(*(room_manager.add_bot_to_room("100", "123", 2) for _ in range(2)))
        assert [r.success for r in replies] == [True, False]
        assert room["seat_list"] == [100, -1, 0, -1]
    asyncio.run(run())


def test_broadcast_and_reconnect_preserve_sparse_seats_for_every_human():
    async def run():
        room_manager = manager()
        del room_manager._broadcast_room_info
        await room_manager.add_smart_bot_to_room("100", "123", 3)
        await room_manager.join_room("101", "123", "")
        for uid in (100, 101):
            connection = room_manager.game_server.user_id_to_connection[uid]
            room = connection.websocket.send_json.await_args.args[0]["room_info"]
            assert room["seat_list"] == [100, 101, -1, 2]
        response = await room_manager.sync_my_room("101")
        assert response.room_info["seat_list"] == [100, 101, -1, 2]
    asyncio.run(run())


@pytest.mark.parametrize("message_type,method", [
    ("room/add_bot", "add_bot_to_room"), ("room/add_smart_bot", "add_smart_bot_to_room"),
    ("room/add_guobiao_heuristic_bot", "add_guobiao_heuristic_bot_to_room"),
    ("room/kick_player", "kick_player_from_room"),
])
def test_router_forwards_seat_index(message_type, method):
    server = NS(**{method: AsyncMock()})
    message = {"type": message_type, "room_id": "123", "seat_index": 3, "target_user_id": 0}
    asyncio.run(handle_room_message(server, "100", message, NS()))
    args = ("100", "123", 0, 3) if message_type == "room/kick_player" else ("100", "123", 3)
    getattr(server, method).assert_awaited_once_with(*args)


def test_start_uses_seat_order_while_host_is_earliest_remaining_human(monkeypatch):
    from ..gamestate import gamestate_manager as game_module

    async def run():
        room_manager = manager()
        room = room_manager.rooms["123"]
        await room_manager.add_bot_to_room("100", "123", 1)
        await room_manager.join_room("101", "123", "")
        await room_manager.join_room("102", "123", "")
        await room_manager.leave_room("100", "123")
        await room_manager.join_room("100", "123", "")
        await room_manager.set_player_ready("100", "123", True)
        await room_manager.set_player_ready("102", "123", True)
        before = deepcopy(room)
        game_server = room_manager.game_server
        game_server.room_manager = room_manager
        game_server.calculation_service = NS()
        games = game_module.GameStateManager(game_server)
        captured = []

        def make_state(server, snapshot, *args):
            captured.append(snapshot)
            return NS(gamestate_id="game", run_game_loop=AsyncMock())

        monkeypatch.setattr(game_module, "GuobiaoGameState", make_state)
        monkeypatch.setattr(game_module, "configure_duplicate_state", Mock())
        monkeypatch.setattr(game_module, "attach_game_frames", Mock())
        games.register_game = Mock()
        games.run_game = AsyncMock()
        assert not (await games.start_game("100", "123")).success
        assert await games.start_game("101", "123") is None
        assert captured[0]["player_list"] == [100, 0, 101, 102]
        assert room["player_list"] == before["player_list"] == [0, 101, 102, 100]
        assert room["seat_list"] == before["seat_list"]
        await asyncio.sleep(0)
    asyncio.run(run())
