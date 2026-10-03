"""Room pointer hints survive creation and game serialization; legacy rooms stay enabled."""
import importlib
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from .room_manager import RoomManager
from ..response import GameInfo
from ..match.rank_calculator import queue_type_to_room_config


class PointerTipsTests(unittest.IsolatedAsyncioTestCase):
    async def test_room_creation_and_game_state_roundtrip(self):
        rules = {
            "GB": ("guobiao", "Guobiao"), "Qingque": ("mmcr", "Qingque"),
            "Changsha": ("changsha", "Changsha"), "Jiandan": ("jiandan", "Jiandan"),
            "Hongque": ("hongque", "Hongque"), "Free": ("free", "Free"),
            "Classical": ("classical", "Classical"), "Sichuan": ("sichuan", "Sichuan"),
            "Taiwan": ("taiwan", "Taiwan"), "Riichi": ("riichi", "Riichi"),
        }
        for rule, (folder, class_prefix) in rules.items():
            for enabled in (None, True, False):
                with self.subTest(rule=rule, enabled=enabled):
                    server = SimpleNamespace(
                        players={"connection": SimpleNamespace(user_id=1, username="host", current_room_id=None)},
                        db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
                        gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False),
                        match_manager=None,
                    )
                    manager = RoomManager(server)
                    manager._generate_room_id = lambda: "123456"
                    manager._broadcast_room_info = AsyncMock()
                    args = ("connection", "指针测试", "") if rule == "Free" else ("connection", "指针测试", 1, "", 20, 5, False)
                    kwargs = {} if enabled is None else {"pointer_tips": enabled}
                    response = await getattr(manager, f"create_{rule}_room")(*args, **kwargs)
                    self.assertTrue(response.success, response.message)
                    expected = enabled is not False
                    self.assertEqual(response.room_info["pointer_tips"], expected)
                    self.assertEqual(manager.rooms["123456"]["pointer_tips"], expected)
                    module = importlib.import_module(f"server.gamestate.game_{folder}.{class_prefix}GameState")
                    state = getattr(module, class_prefix + "GameState")(server, response.room_info, None, None, "pointer-test")
                    self.assertEqual(state.pointer_tips, expected)
                    broadcaster = importlib.import_module(f"server.gamestate.game_{folder}.boardcast")
                    if hasattr(broadcaster, "_build_base_game_info"):
                        self.assertEqual(broadcaster._build_base_game_info(state)["pointer_tips"], expected)
                    elif hasattr(broadcaster, "_base_game_info"):
                        self.assertEqual(broadcaster._base_game_info(state)["pointer_tips"], expected)
                    elif hasattr(broadcaster, "game_info_payload"):
                        self.assertEqual(broadcaster.game_info_payload(state, 0)["pointer_tips"], expected)

    def test_legacy_payload_and_spectator_reconnect_field(self):
        payload = dict(room_id=123456, gamestate_id="test", tips=False,
                       current_player_index=0, action_tick=0, max_round=1, tile_count=70,
                       current_round=1, step_time=5, round_time=20, room_type="custom",
                       room_rule="guobiao", players_info=[])
        self.assertTrue(GameInfo(**payload).pointer_tips)
        self.assertFalse(GameInfo(**payload, pointer_tips=False).model_dump()["pointer_tips"])

    def test_ranked_presets_have_exact_timers_and_pointer_policy(self):
        for tier in ("beginner", "intermediate", "advanced", "mcrpl"):
            for length, rounds in (("dongfeng", 1), ("banzhuang", 2), ("quanzhuang", 4)):
                with self.subTest(tier=tier, length=length):
                    config = queue_type_to_room_config(f"{tier}_{length}")
                    self.assertEqual(config["game_round"], rounds)
                    self.assertEqual(config["tips"], tier == "beginner")
                    self.assertEqual(config["open_cuohe"], tier != "beginner")
                    self.assertEqual(config["pointer_tips"], tier != "mcrpl")
                    self.assertEqual(config["step_timer"], 8 if tier == "intermediate" else 5)
                    self.assertEqual(config["round_timer"], 20)
                    self.assertTrue(config["tactical_call"])
