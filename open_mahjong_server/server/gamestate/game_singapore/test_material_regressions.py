"""SPGG A12 refers to the discard pool, excluding exposed meld tiles."""
import asyncio

import pytest

from .test_state import make_state, drawn
from ...game_calculation.singapore.test_rules import PLAIN


@pytest.mark.parametrize('still_in_river', [False, True])
def test_fresh_discard_ignores_a_previous_copy_now_claimed_into_a_meld(still_in_river):
    async def run():
        s = make_state()
        drawn(s.player_list[0], PLAIN)
        s.current_player_index = 0
        s.sg_last_drawer = 0
        s.sg_last_draw_count = 5
        s.sg_discarded.add(11)
        s.player_list[2].combination_tiles = ['s12']
        s.player_list[2].combination_mask = [[1,11,0,12,0,13]]
        s.player_list[1].discard_tiles = [11] if still_in_river else []
        await s.execute_cut(0, dict(TileId=11, cutClass=False, cutIndex=0))
        assert s.player_list[0].discard_tiles[-1] == 11
        assert s.sg_fresh_discard is (not still_in_river)
    asyncio.run(run())


@pytest.mark.parametrize('bonus', [51,52,53,54,55,56,57,58,61,62,63,64])
def test_spgg_replacement_does_not_treat_winds_or_dragons_as_flowers(bonus):
    async def run():
        s = make_state()
        hand = [11,12,13,21,22,23,41,42,43,44,45,46,47,bonus]
        drawn(s.player_list[0], hand)
        s.current_player_index = 0
        s.tiles_list = [19] * 40
        await s.execute_buhua(0)
        assert s.player_list[0].huapai_list == [bonus]
        assert sorted(s.player_list[0].hand_tiles) == sorted(hand[:-1] + [19])
        assert s.player_list[0].has_draw_slot
    asyncio.run(run())
