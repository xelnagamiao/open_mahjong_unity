"""Riichi starting points survive both room creation paths and reach the game."""
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from .room_manager import RoomManager
from .room_validators import RiichiRoomValidator
from ..gamestate.game_riichi.RiichiGameState import RiichiGameState


class RiichiStartingScoreTests(unittest.IsolatedAsyncioTestCase):
    def manager(self):
        server = SimpleNamespace(
            players={"connection": SimpleNamespace(user_id=10001, username="host", current_room_id=None)},
            db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
            gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False),
            match_manager=None,
        )
        manager = RoomManager(server)
        manager._generate_room_id = lambda: "123456"
        manager._broadcast_room_info = AsyncMock()
        manager._validate_event_for_room = lambda *_: None
        return manager, server

    async def test_custom_room_default_override_and_legacy_langyong(self):
        for sub_rule, score, expected in [
            ("riichi/standard", None, 25000), ("riichi/standard", 30000, 30000),
            ("riichi/langyong", None, 50000), ("riichi/langyong", 30000, 30000),
        ]:
            with self.subTest(sub_rule=sub_rule, score=score):
                manager, server = self.manager()
                result = await manager.create_Riichi_room(
                    "connection", "starting points", 2, "", 20, 5, False,
                    sub_rule=sub_rule, starting_score=score,
                )
                self.assertTrue(result.success, result.message)
                self.assertEqual(result.room_info["starting_score"], expected)
                state = RiichiGameState(server, result.room_info, None, None, "test")
                self.assertEqual([state._player_starting_score(i) for i in range(4)], [expected] * 4)

    async def test_event_room_and_invalid_requests(self):
        manager, server = self.manager()
        result = await manager.create_empty_event_room("event-123", "riichi", {"starting_score": 30000}, broadcast=False)
        self.assertTrue(result.success, result.message)
        self.assertEqual(result.room_info["starting_score"], 30000)
        state = RiichiGameState(server, result.room_info, None, None, "test")
        self.assertEqual(state._starting_score(), 30000)
        for value in [True, "30000", 0, 999, 25001, 30000.5, 1000001]:
            with self.subTest(value=value):
                manager, _ = self.manager()
                result = await manager.create_empty_event_room("event-123", "riichi", {"starting_score": value}, broadcast=False)
                self.assertFalse(result.success)
                self.assertEqual(manager.rooms, {})
                result = await manager.create_Riichi_room("connection", "invalid", 2, "", 20, 5, False, starting_score=value)
                self.assertFalse(result.success)
                self.assertEqual(manager.rooms, {})

    def test_bounds(self):
        for score in [1000, 25000, 30000, 50000, 1000000]:
            config = RiichiRoomValidator(room_name="test", game_round=2, round_timer=20, step_timer=5, starting_score=score)
            self.assertEqual(config.starting_score, score)
