import random

import pytest

from .qinghunpeng_bot import best_cut, choose_claim, shanten
from ...game_calculation.shanghai.qinghunpeng import TILES, score


@pytest.mark.parametrize('hand,melds,expected', [
    ([11,12,13,14,15,16,17,18,19,41,41,41,42,42], [], -1),
    ([11,11,11,21,21,21,31,31,31,41,41,41,42,42], [], -1),
    ([11,12,13,21,22,23,31,32,33,41,41,41,42,42], [], 5),
    ([11,12,13,14,15,16,41,41,41,42], ['s18'], 0),
    ([11,11,21,21,31,31,41,41], ['s15','s25'], 99),
    ([41,41,41,41,42,43,43,44,44,45,45,46,47,47], [], -1),
])
def test_only_legal_starting_patterns_are_complete(hand, melds, expected):
    assert shanten(hand, melds) == expected


def test_cut_advances_mixed_suit_instead_of_ordinary_illegal_ready_hand():
    hand = [11,12,13,14,15,16,17,18,41,41,41,42,42,29]
    tile, index = best_cut(hand, [], [0]*34)
    assert tile == 29 and hand[index] == 29
    assert shanten(hand[:index]+hand[index+1:]) == 0


def test_claim_does_not_break_all_pungs_route_with_foreign_chow():
    hand = [11,11,21,21,31,31,41,41,42,42,24,26,47]
    assert choose_claim(hand, [], [0]*34, ['chi_mid','pass'],25) == 'pass'


def test_completed_shapes_agree_with_actual_rule_calculator():
    rng = random.Random(20260929)
    wall = [tile for tile in TILES for _ in range(4)]
    for _ in range(300):
        hand = rng.sample(wall,14)
        assert (shanten(hand) == -1) == bool(score(hand, self_draw=True))
