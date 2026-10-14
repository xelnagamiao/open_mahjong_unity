import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from server.game_calculation.changsha.changsha_hepai_check import Changsha_Hepai_Check
from server.gamestate.game_changsha.action_check import check_hepai, check_action_after_cut
from server.gamestate.game_changsha.test_changsha_rules import DummyPlayer
from server.room.room_router import handle_create_Changsha_room
from server.room.room_validators import ChangshaRoomValidator
from server.response import Response


class ChangshaKamoTest(unittest.TestCase):
    hand = [12, 13, 14, 22, 23, 24, 31, 32, 33, 27, 28, 29, 25, 25]

    def make_state(self, enabled, self_draw=False):
        checker = Changsha_Hepai_Check()
        players = [DummyPlayer(i, [11]) for i in range(4)]
        players[1].hand_tiles = list(self.hand if self_draw else self.hand[:-1])
        return SimpleNamespace(
            player_list=players, current_player_index=1 if self_draw else 0,
            small_hu_self_draw_only=enabled, result_dict={}, player_passed_hu_base={},
            dihe_possible=False, last_draw_was_gang=False, tiles_list=[31, 32, 33],
            calculation_service=SimpleNamespace(
                Changsha_hepai_check=checker.hepai_check,
                Changsha_tingpai_check=lambda hand, melds: {25},
            ),
        )

    def test_small_hu_ron_is_filtered_only_when_enabled(self):
        for enabled in (False, True):
            state = self.make_state(enabled)
            actions = check_action_after_cut(state, 25)
            self.assertEqual("hu_first" in actions[1], not enabled)
            self.assertEqual("hu_first" in state.result_dict, not enabled)

    def test_small_hu_self_draw_remains_allowed_even_after_pass(self):
        state = self.make_state(True, self_draw=True)
        state.player_passed_hu_base[1] = 100
        actions = {i: [] for i in range(4)}
        check_hepai(state, actions, 25, 1, "handgot")
        self.assertEqual(actions[1], ["hu_self"])
        self.assertEqual(state.result_dict["hu_self"][1], ["小胡"])

    def test_context_big_hu_ron_and_rob_kong_remain_allowed(self):
        for context in ("dihe", "gang", "qianggang", "sea_bottom"):
            state = self.make_state(True)
            state.dihe_possible = context == "dihe"
            state.last_draw_was_gang = context == "gang"
            if context == "sea_bottom":
                state.tiles_list = []
            actions = {i: [] for i in range(4)}
            check_hepai(state, actions, 25, 1, "qianggang" if context == "qianggang" else "dianhe")
            self.assertEqual(actions[1], ["hu_first"], context)
            self.assertNotEqual(state.result_dict["hu_first"][1], ["小胡"])

    def test_room_validator_defaults_off_and_requires_boolean(self):
        base = dict(room_name="长沙", game_round=1, round_timer=0, step_timer=5)
        self.assertFalse(ChangshaRoomValidator(**base).small_hu_self_draw_only)
        self.assertTrue(ChangshaRoomValidator(**base, small_hu_self_draw_only=True).small_hu_self_draw_only)
        with self.assertRaises(ValueError):
            ChangshaRoomValidator(**base, small_hu_self_draw_only="false")

    def test_real_room_request_route_forwards_kamo_setting(self):
        server = SimpleNamespace(players={}, create_Changsha_room=AsyncMock(return_value=Response(type="room/create_room_done", success=True, message="created")))
        socket = SimpleNamespace(send_json=AsyncMock())
        message = dict(roomname="长沙", gameround=1, password="", roundTimerValue=0, stepTimerValue=5, tips=False, small_hu_self_draw_only=True)
        asyncio.run(handle_create_Changsha_room(server, "connection", message, socket))
        self.assertIs(server.create_Changsha_room.call_args.kwargs["small_hu_self_draw_only"], True)
        socket.send_json.assert_awaited_once()
