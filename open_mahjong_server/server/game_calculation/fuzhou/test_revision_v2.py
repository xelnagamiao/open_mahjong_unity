"""Examples derived from the revised book's formulas and physical tile rules."""
import pytest

from .rules import (VERSION, WinContext, flower_points, is_complete,
                    normalize_config, reserved_tiles, score_hand)
from .test_rules import HAND


@pytest.mark.parametrize('base', [10, 15, 20])
def test_every_base_uses_the_same_additive_formula(base):
    quote = score_hand(HAND, flowers=[41, 42], jin=39, base_score=base,
                       context=WinContext(dealer_streak=2))
    assert quote['points'] == base + 2 + 6
    assert quote['pattern_points'] == 0
    assert quote['rule_version'] == VERSION == 'fuzhou-user-20261015-v2'
    assert normalize_config()['base_score'] == 15


@pytest.mark.parametrize('flowers,groups', [([51,52,53],0),([51,52,53,54],1),
    ([55,56,57,58],1),([51,52,53,54,55,56,57,58],2),([41]*4+[51,52,53,54],2)])
def test_complete_flower_groups_add_points_and_dead_wall_once(flowers,groups):
    assert flower_points(flowers) == len(flowers) + 2 * groups
    assert reserved_tiles([([], flowers)]) == 18 + 2 * groups
    assert reserved_tiles([([], flowers), (['g11','G21'],[])]) == 21 + 2 * groups


def test_indicator_claim_allows_the_actual_fourth_gold_but_never_a_fifth():
    hand = HAND[:12] + [39]*4 + [29]
    assert not is_complete(hand, jin=39)
    assert is_complete(hand, jin=39, indicator_claimed=True)
    quote = score_hand(hand, jin=39, context=WinContext(qiangjin=True, winning_tile=39))
    assert quote is not None and 'qiangjin' in {p['id'] for p in quote['items']}
    assert score_hand(hand, jin=39) is None
    assert not is_complete([39]*5 + HAND[:12], jin=39, indicator_claimed=True)


def test_special_wins_still_require_completed_draw_and_all_flowers_replaced():
    flowers = [t for t in range(41,46) for _ in range(4)]
    assert score_hand(HAND[:-1], flowers=flowers, jin=39, special='flower') is None
    assert score_hand(HAND[:-1]+[47], flowers=flowers, jin=39, special='flower') is None
    quote = score_hand(HAND, flowers=flowers, jin=39, special='flower')
    assert quote['points'] == 15 + 30 + 60
    assert {p['name'] for p in quote['items']} >= {'花和'}


def test_best_decomposition_chooses_gold_dragon_over_gold_pair():
    # The gold triplet is worth 120; the alternative gold pair only 60.
    hand = HAND[:12] + [39]*3 + [29,29]
    quote = score_hand(hand, jin=39)
    assert quote['points'] == 15 + 3 + 30 + 120
    assert quote['pattern_points'] == 150
    assert 'gold_dragon' in {p['id'] for p in quote['items']}
    assert 'gold_pair' not in {p['id'] for p in quote['items']}
