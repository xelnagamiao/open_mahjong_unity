"""Shared Python entry points exercised with memory-only connections."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .HangzhouGameState import HangzhouGameState
from .test_flow import state
from .test_contracts import fake_room_manager
from ...room.hangzhou_room import HangzhouRoomValidator
from ...game_calculation.hangzhou.rules import RULE_VERSION


def test_registered_game_looks_up_and_cleans_all_indexes():
    from ..gamestate_manager import GameStateManager
    async def run():
        s = state()
        data = {"room_id": s.room_id, "instance_id": "hangzhou-test"}
        rooms = SimpleNamespace(rooms={s.room_id: data}, active_game_room_ids={}, finish_custom_game_room=AsyncMock())
        server = SimpleNamespace(room_manager=rooms, user_id_to_connection={})
        manager = GameStateManager(server)
        s.cleanup_game_state = AsyncMock()
        manager.register_game(s, data)
        assert manager.room_id_to_HangzhouGameState[s.room_id] is s
        assert manager.get_game_state_by_room_id(s.room_id) is s
        assert all(manager.get_game_state_by_user_id(p.user_id) is s for p in s.player_list)
        await manager.cleanup_game_state_complete(gamestate_id=s.gamestate_id)
        assert manager.get_game_state_by_room_id(s.room_id) is None
        assert not rooms.active_game_room_ids
        assert all(manager.get_game_state_by_user_id(p.user_id) is None for p in s.player_list)
        s.cleanup_game_state.assert_awaited_once()
    asyncio.run(run())


@pytest.mark.parametrize("config", [{"game_round": "bad"}, {"game_round": True}, {"round_timer": "bad"},
                                   {"step_timer": None}, {"random_seed": "bad"}, {"tips": "false"}, {"use_flowers": True}])
def test_event_room_rejects_invalid_fields_before_mutation(config):
    from ...room.room_manager import RoomManager
    async def run():
        manager, _ = fake_room_manager()
        manager.room_validators = {"hangzhou": HangzhouRoomValidator}
        result = await RoomManager.create_empty_event_room(manager, "event-test", "hangzhou", config, broadcast=False)
        assert not result.success and not manager.rooms
    asyncio.run(run())


def test_event_room_preserves_flags_and_fixed_version():
    from ...room.room_manager import RoomManager
    async def run():
        manager, _ = fake_room_manager()
        manager.room_validators = {"hangzhou": HangzhouRoomValidator}
        result = await RoomManager.create_empty_event_room(manager, "event-test", "hangzhou",
            {"game_round": 3, "tips": False, "count_tips": True, "pointer_tips": False,
             "tourist_limit": True, "allow_spectator": False}, broadcast=False)
        assert result.success
        room = manager.rooms[123456]
        assert room["game_round"] == 3 and room["count_tips"] and not room["pointer_tips"]
        assert room["tourist_limit"] and not room["allow_spectator"] and not room["tips"]
        assert room["detailed_config"] == {"rule_version": RULE_VERSION}
    asyncio.run(run())


def test_actual_manager_starts_hangzhou_and_router_delivers_authenticated_action():
    from ..test_room_lifecycle import make_server, make_room, USERS, parked_loop
    from ..gamestate_router import handle_gamestate_message
    async def run():
        server = make_server(USERS)
        room = make_room(USERS, "hangzhou")
        server.room_manager.rooms["1"] = room
        with patch.object(HangzhouGameState, "run_game_loop", parked_loop):
            result = await server.gamestate_manager.start_game(str(USERS[0]), "1")
            assert result is None, result
            s = server.gamestate_manager.get_game_state_by_room_id("1")
            assert isinstance(s, HangzhouGameState)
            s.initialize_round();s.open_action_window(s.opening_window())
            p=s.player_list[0]
            connection=server.players[str(USERS[0])]
            message=dict(type="gamestate/hangzhou/cut_tile",gamestate_id=s.gamestate_id,
                         action_tick=s.server_action_tick,TileId=p.hand_tiles[-1],cutClass=True,cutIndex=13)
            await handle_gamestate_message(server, str(USERS[0]), message, connection.websocket)
            assert s.action_queues[0].qsize()==1
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=s.gamestate_id)
            assert not server.gamestate_manager.room_id_to_HangzhouGameState
    asyncio.run(run())
