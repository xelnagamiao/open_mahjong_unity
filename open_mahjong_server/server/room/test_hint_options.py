"""Independent fan/count options survive room creation and game serialization."""
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from .room_manager import RoomManager
from ..response import GameInfo
from ..gamestate.game_riichi.RiichiGameState import RiichiGameState
from ..gamestate.game_riichi.boardcast import _build_base_game_info


class HintOptionsTests(unittest.IsolatedAsyncioTestCase):
    async def test_all_rules_preserve_each_hint_combination(self):
        rules = ("GB", "Qingque", "Changsha", "Jiandan", "Hongque",
                 "Classical", "Sichuan", "Taiwan", "Riichi")
        for rule in rules:
            for fan, count in ((False, False), (True, False), (False, True), (True, True)):
                with self.subTest(rule=rule, fan=fan, count=count):
                    player = SimpleNamespace(user_id=1, username="host", current_room_id=None)
                    server = SimpleNamespace(
                        players={"connection": player},
                        db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
                        gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False),
                        match_manager=None,
                    )
                    manager = RoomManager(server)
                    manager._generate_room_id = lambda: "123456"
                    manager._broadcast_room_info = AsyncMock()
                    response = await getattr(manager, f"create_{rule}_room")(
                        "connection", "提示测试", 1, "", 20, 5, fan, count_tips=count,
                    )
                    self.assertTrue(response.success, response.message)
                    self.assertEqual(response.room_info["tips"], fan)
                    self.assertEqual(response.room_info["count_tips"], count)
                    self.assertEqual(manager.rooms["123456"]["count_tips"], count)
                    manager._broadcast_room_info.assert_awaited_once_with("123456")
                    if rule == "Riichi":
                        state = RiichiGameState(server, response.room_info, None, None, "test")
                        payload = _build_base_game_info(state)
                        self.assertEqual(payload["tips"], fan)
                        self.assertEqual(payload["count_tips"], count)

    def test_game_info_serializes_count_only_and_legacy_defaults(self):
        payload = dict(room_id=123456, gamestate_id="test", tips=False,
                       current_player_index=0, action_tick=0, max_round=1, tile_count=70,
                       current_round=1, step_time=5, round_time=20, room_type="custom",
                       room_rule="riichi", players_info=[])
        self.assertFalse(GameInfo(**payload).count_tips)
        encoded = GameInfo(**payload, count_tips=True).model_dump()
        self.assertFalse(encoded["tips"])
        self.assertTrue(encoded["count_tips"])
