"""Shared registration contracts, using fake rooms and no running services."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_flow import state
from .test_contracts import fake_room_manager
from ...room.guizhou_room import GuizhouRoomValidator


def test_guizhou_index_registration_lookup_and_cleanup():
    from ..gamestate_manager import GameStateManager
    async def run():
        s = state()
        data = {"room_id": s.room_id, "instance_id": "test-instance"}
        rooms = SimpleNamespace(rooms={s.room_id: data}, active_game_room_ids={}, finish_custom_game_room=AsyncMock())
        server = SimpleNamespace(room_manager=rooms, user_id_to_connection={})
        manager = GameStateManager(server)
        s.cleanup_game_state = AsyncMock()
        manager.register_game(s, data)
        assert manager.room_id_to_GuizhouGameState[s.room_id] is s
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
def test_event_room_rejects_invalid_guizhou_fields_before_mutation(config):
    from ...room.room_manager import RoomManager
    async def run():
        manager, _ = fake_room_manager()
        manager.room_validators = {"guizhou": GuizhouRoomValidator}
        result = await RoomManager.create_empty_event_room(manager, "event-test", "guizhou", config, broadcast=False)
        assert not result.success and not manager.rooms
    asyncio.run(run())


def test_event_room_preserves_guizhou_flags_and_version():
    from ...room.room_manager import RoomManager
    async def run():
        manager, _ = fake_room_manager()
        manager.room_validators = {"guizhou": GuizhouRoomValidator}
        result = await RoomManager.create_empty_event_room(manager, "event-test", "guizhou",
            {"game_round": 3, "tips": False, "count_tips": True, "pointer_tips": False, "tourist_limit": True, "allow_spectator": False}, broadcast=False)
        assert result.success
        room = manager.rooms[123456]
        assert room["game_round"] == 3 and room["count_tips"] and not room["pointer_tips"]
        assert room["tourist_limit"] and not room["allow_spectator"] and not room["tips"]
        assert room["detailed_config"] == {"rule_version": "mil-guizhou-2023-om1"}
    asyncio.run(run())
