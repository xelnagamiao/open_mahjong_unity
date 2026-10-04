import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from .wenzhou_room import (EDITION, SUB_RULE, WenzhouRoomValidator, create_wenzhou_room,
                           handle_create_wenzhou_room, normalize_wenzhou_config)


class WenzhouRoomConfigTests(unittest.TestCase):
    def test_defaults_and_all_switch_values(self):
        for toggle in ("tips", "count_tips", "pointer_tips", "tourist_limit", "allow_spectator"):
            for value in (False, True):
                config = WenzhouRoomValidator(room_name=" 温州 ", **{toggle: value})
                self.assertEqual(config.room_name, "温州")
                self.assertIs(getattr(config, toggle), value)
                self.assertEqual(config.sub_rule, SUB_RULE)
                self.assertEqual(config.detailed_config, {"edition": EDITION})
        self.assertEqual(normalize_wenzhou_config({}), {"edition": EDITION})
        self.assertEqual(normalize_wenzhou_config(), {"edition": EDITION})
        self.assertEqual(normalize_wenzhou_config({"edition": EDITION}), {"edition": EDITION})

    def test_timer_round_boundaries(self):
        for field, lo, hi in (("game_round", 1, 4), ("round_timer", 0, 1000), ("step_timer", 0, 100)):
            for value in (lo, hi):
                self.assertEqual(getattr(WenzhouRoomValidator(room_name="温州", **{field: value}), field), value)
            for value in (lo-1, hi+1, True, 1.5, "1", None):
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    WenzhouRoomValidator(room_name="温州", **{field: value})

    def test_reject_unpublished_rules(self):
        for value in ([], False, "", {"edition": "future"}, {"joker_melds": True}):
            with self.subTest(value=value), self.assertRaises(ValueError):
                normalize_wenzhou_config(value)
        for key in ("use_flowers", "open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he"):
            for value in (True, "false", 0):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    WenzhouRoomValidator(room_name="温州", **{key: value})
        with self.assertRaises(ValueError):
            WenzhouRoomValidator(room_name="温州", sub_rule="wenzhou/folk")
        with self.assertRaises(ValueError):
            WenzhouRoomValidator(room_name=" ")
        for key in ("tips", "count_tips", "pointer_tips", "tourist_limit", "allow_spectator"):
            with self.assertRaises(ValueError):
                WenzhouRoomValidator(room_name="温州", **{key: "false"})


class WenzhouRoomLifecycleTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.connection = SimpleNamespace(user_id=12345678, username="测试温州", current_room_id=None)
        self.manager = SimpleNamespace(
            game_server=SimpleNamespace(players={"player": self.connection},
                db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "测试温州"})),
            _reject_room_entry_conflicts=lambda *_: None,
            _normalize_event_id=lambda event: event,
            _validate_event_for_room=lambda *_: None,
            _generate_room_id=lambda: 12345,
            _apply_event_fields=lambda room, event: room.update(event_id=event),
            _broadcast_room_info=AsyncMock(), rooms={}, room_passwords={})

    async def test_create_and_read_back(self):
        for password in ("", "test"):
            self.connection.current_room_id = None
            response = await create_wenzhou_room(self.manager, "player", room_name="温州", password=password,
                gameround=3, roundTimerValue=0, stepTimerValue=100, count_tips=True, pointer_tips=False,
                tourist_limit=True, allow_spectator=False, random_seed="0" * 62 + "7b", event_id="venue")
            self.assertTrue(response.success)
            room = response.room_info
            self.assertEqual(room["room_rule"], "wenzhou")
            self.assertEqual(room["sub_rule"], SUB_RULE)
            self.assertEqual(room["detailed_config"], {"edition": EDITION})
            self.assertEqual(room["game_round"], 3)
            self.assertFalse(room["use_flowers"])
            self.assertEqual(room["has_password"], bool(password))
            self.assertEqual(room["player_settings"][12345678]["username"], "测试温州")
            self.assertEqual(self.connection.current_room_id, 12345)
            self.assertEqual(room["event_id"], "venue")
            self.assertTrue(room["is_player_set_random_seed"])
        self.assertEqual(self.manager.room_passwords[12345], "test")

    async def test_entry_failures_do_not_create_room(self):
        response = await create_wenzhou_room(self.manager, "absent", room_name="温州")
        self.assertFalse(response.success)
        self.connection.user_id = 0
        self.assertFalse((await create_wenzhou_room(self.manager, "player", room_name="温州")).success)
        self.connection.user_id = 12345678
        self.connection.current_room_id = 3
        self.assertFalse((await create_wenzhou_room(self.manager, "player", room_name="温州")).success)
        self.connection.current_room_id = None
        sentinel = object()
        self.manager._reject_room_entry_conflicts = lambda *_: sentinel
        self.assertIs(await create_wenzhou_room(self.manager, "player", room_name="温州"), sentinel)
        self.manager._reject_room_entry_conflicts = lambda *_: None
        self.manager._validate_event_for_room = lambda *_: sentinel
        self.assertIs(await create_wenzhou_room(self.manager, "player", room_name="温州"), sentinel)
        self.manager._validate_event_for_room = lambda *_: None
        self.assertFalse((await create_wenzhou_room(self.manager, "player", room_name="温州", use_flowers=True)).success)
        self.manager.game_server.db_manager.get_user_settings = lambda _: None
        self.assertFalse((await create_wenzhou_room(self.manager, "player", room_name="温州")).success)
        self.assertEqual(self.manager.rooms, {})

    async def test_wire_handler_retains_settings(self):
        socket = SimpleNamespace(send_json=AsyncMock())
        await handle_create_wenzhou_room(SimpleNamespace(room_manager=self.manager), "player",
            {"roomname": "温州", "gameround": 2, "detailed_config": {"edition": EDITION}, "use_flowers": False}, socket)
        response = socket.send_json.await_args.args[0]
        self.assertTrue(response["success"])
        self.assertEqual(response["room_info"]["game_round"], 2)
        self.assertEqual(response["room_info"]["detailed_config"]["edition"], EDITION)


if __name__ == "__main__":
    unittest.main()
