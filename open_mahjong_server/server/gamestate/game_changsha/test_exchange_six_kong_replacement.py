import unittest

from server.gamestate.game_changsha.test_exchange_six import make_state


class ExchangeSixKongReplacementTests(unittest.IsolatedAsyncioTestCase):
    def state(self):
        state = make_state()
        state.exchange_six.actual_draw_counts = {seat: 2 for seat in range(4)}
        return state


    async def test_concealed_replacement_sends_exact_four_tiles_and_draw_origin(self):
        for drawn_target in (False, True):
            with self.subTest(drawn_target=drawn_target):
                state = self.state()
                player = state.player_list[0]
                player.hand_tiles = ([12] * 3 + [21, 22, 23, 24, 25, 26, 31, 32, 33, 35] + [12]
                                     if drawn_target else [12] * 4 + [21, 22, 23, 24, 25, 26, 31, 32, 33, 35])
                player.has_draw_slot = True
                await state.exchange_six.declare_kong(0, 12, formal=False)
                self.assertEqual(player.combination_tiles, ["G12"])
                self.assertEqual(player.hand_tiles.count(12), 0)
                messages = state.game_server.user_id_to_connection[100].websocket.messages
                wire = [message["do_action_info"] for message in messages
                        if message.get("do_action_info", {}).get("action_list") == ["buzhang"]][-1]
                self.assertEqual(wire["combination_mask"], [0, 12] * 4)
                self.assertEqual(wire["combination_target"], "G12")
                self.assertEqual(wire["is_mo_gang"], drawn_target)
