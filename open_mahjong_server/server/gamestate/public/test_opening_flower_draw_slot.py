import unittest
from types import SimpleNamespace

from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoPlayer
from server.gamestate.public.hand_slot_utils import (
    clear_draw_slot,
    resolve_timeout_cut,
    retain_opening_first_player_draw_slot,
)


class OpeningFlowerDrawSlotTest(unittest.TestCase):
    def test_guobiao_normal_draw_records_explicit_source(self):
        player = GuobiaoPlayer(1, "p0", [11], 60)

        player.get_tile([29])

        self.assertTrue(player.has_draw_slot)
        self.assertEqual(player.last_drawn_tile, 29)

    def test_guobiao_replacement_draw_records_explicit_source(self):
        player = GuobiaoPlayer(1, "p0", [11], 60)
        state = SimpleNamespace(backward_tiles_list_type="single")

        player.get_gang_tile([21], state)

        self.assertTrue(player.has_draw_slot)
        self.assertEqual(player.last_drawn_tile, 21)

    def test_opening_cleanup_retains_only_first_players_slot(self):
        players = [GuobiaoPlayer(i + 1, f"p{i}", [11 + i], 60) for i in range(4)]
        state = SimpleNamespace(backward_tiles_list_type="single")
        for index, player in enumerate(players):
            player.player_index = index
            player.get_gang_tile([21 + index], state)

        retain_opening_first_player_draw_slot(players, 0)

        self.assertTrue(players[0].has_draw_slot)
        self.assertEqual(players[0].last_drawn_tile, 21)
        for player in players[1:]:
            self.assertFalse(player.has_draw_slot)
            self.assertIsNone(player.last_drawn_tile)

    def test_opening_timeout_uses_retained_replacement_as_moqie(self):
        player = GuobiaoPlayer(1, "p0", [11, 29], 60)
        player.has_draw_slot = True
        player.last_drawn_tile = 29

        self.assertEqual(
            resolve_timeout_cut(player, opening_first_discard=True),
            (29, True),
        )

    def test_opening_timeout_without_replacement_uses_maximum_hand_cut(self):
        player = GuobiaoPlayer(1, "p0", [12, 41, 29], 60)

        self.assertEqual(
            resolve_timeout_cut(player, opening_first_discard=True),
            (41, False),
        )

    def test_consuming_slot_clears_explicit_source(self):
        player = GuobiaoPlayer(1, "p0", [11, 29], 60)
        player.has_draw_slot = True
        player.last_drawn_tile = 29

        clear_draw_slot(player)

        self.assertFalse(player.has_draw_slot)
        self.assertIsNone(player.last_drawn_tile)


if __name__ == "__main__":
    unittest.main()
