"""The fast numeric quote must match the full physical decomposition scorer."""
import random

import pytest

from . import rules as book
from .test_rules import HAND


def assert_same(hand, melds=(), *, jin=39, context=None):
    full = book.score_hand(hand, melds, jin=jin, context=context)
    fast = book.score_hand(hand, melds, jin=jin, context=context, include_witness=False)
    assert full is not None
    assert fast['witness'] is None
    assert {k:v for k,v in full.items() if k != 'witness'} == {k:v for k,v in fast.items() if k != 'witness'}


@pytest.mark.parametrize('jin', [11, 17, 29, 39])
def test_numeric_quote_matches_random_physical_decompositions(jin):
    rng = random.Random(727+jin)
    checked = 0
    while checked < 50:
        hand, melds = [], []
        for _ in range(5):
            tile = rng.choice(book.NUMBERS)
            code = rng.choice(['k','s','s','G','g'])
            if code == 's': tile = tile//10*10+rng.randrange(2,9)
            tiles = book.meld_tiles(f'{code}{tile}')
            if rng.randrange(3) == 0 and jin not in tiles:
                melds.append(f'{code}{tile}')
            else:
                hand.extend(tiles[:3] if code in 'gG' else tiles)
        pair = rng.choice(book.NUMBERS);hand.extend([pair,pair])
        for i in rng.sample(range(len(hand)), rng.randrange(min(3,len(hand))+1)):
            hand[i] = jin
        if not book.valid_tiles(hand, melds, jin=jin):continue
        assert_same(hand, melds, jin=jin)
        checked += 1


def test_three_gold_full_hand_discard_matrix_matches_exhaustive_scoring():
    drawn = HAND[:12]+[39]*3+[29,29]
    for discard in set(drawn):
        hand=list(drawn);hand.remove(discard)
        for tile in book.waiting_tiles(hand, jin=39):
            assert_same(hand+[tile], context=book.WinContext(winning_tile=tile))


@pytest.mark.parametrize('gold_count', [2,3,4])
def test_gold_group_and_all_pungs_coexistence(gold_count):
    hands = [HAND[:12]+[39]*gold_count+[29]*(5-gold_count),
             [11]*3+[21]*3+[31]*3+[37]*3+[39]*gold_count+[29]*(5-gold_count)]
    for hand in hands:
        assert_same(hand, context=book.WinContext(qiangjin=True,winning_tile=39))


def test_numeric_only_path_does_not_enumerate_wildcard_assignments(monkeypatch):
    def forbidden(*args, **kwargs):
        raise AssertionError('A numeric waiting quote must not enumerate all physical assignments')
    monkeypatch.setattr(book, 'iter_winning_shapes', forbidden)
    quote=book.score_hand(HAND[:12]+[39]*3+[29,29],jin=39,include_witness=False)
    assert quote['points'] == 168
