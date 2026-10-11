"""直接以规则书的四个例子为计分依据。"""
from collections import Counter

import pytest

from . import patterns as p


@pytest.mark.parametrize("hand,expected", [
    ([11]*3+[12]*3+[13]*3+[14]*3+[25]*2, 5),
    ([11]*4+[12]*4+[13]*2+[14]*2+[25]*2, 8),
    ([11]*4+[12]*2+[13]*2+[14]*2+[15]*2+[26]*2, 9),
    ([t for t in range(11,18) for _ in range(2)], 20),
])
def test_rulebook_examples(hand, expected):
    assert max(c["big_points"] for c in p.big_hu_candidates(hand)) == expected


def test_deck_and_fa_is_not_a_structural_tile():
    wall = [t for t in p.WALL_TILES for _ in range(4)]
    assert len(wall) == 120 and set(Counter(wall).values()) == {4}
    assert p.FA not in p.TILES and not set(range(41,45)) & set(wall)
    assert not p.decompositions([11,12,13,21,22,23,31,32,33,17,18,19,47,47])


@pytest.mark.parametrize("code", ["s19", "s45", "k47", "g47", "G47", "k41", "x11", "kno", None])
def test_invalid_meld(code):
    assert p.valid_tiles([11,12,13,21,22,23,31,32,33,45,45], [code]) is None


def test_seven_pairs_and_four_copy_limit():
    hand = [11]*4+[12]*2+[23]*2+[24]*2+[35]*2+[45]*2
    assert p.is_seven_pairs(hand)
    assert 11 in p.structural_waits(tuple(hand[1:]))
    assert not p.is_seven_pairs([11]*6+[12]*2+[23]*2+[24]*2+[45]*2)
    assert not p.structural_waits((11,12))
    assert p.valid_tiles([11]*3+[22]*3+[33]*3+[45]*2,["k11"]) is None


def test_luxury_and_chains_do_not_reuse_pairs():
    hand = [11]*4+[12]*4+[13]*4+[25]*2
    candidate = next(c for c in p.big_hu_candidates(hand) if c["seven_pairs"])
    assert candidate["big_points"] == 10
    assert candidate["big_fans"].count(("豪华",2)) == 3
    assert not any(name == "潇洒" for name,_ in candidate["big_fans"])


def test_two_disjoint_chains():
    hand = [t for t in (11,12,13,25,26,27,45) for _ in range(2)]
    candidate = next(c for c in p.big_hu_candidates(hand) if c["seven_pairs"])
    assert candidate["big_points"] == 8
    assert candidate["big_fans"].count(("潇洒",2)) == 2


def test_straight_and_flush_stack():
    hand = [11,12,13,14,15,16,17,18,19,11,11,11,12,12]
    assert max(c["big_points"] for c in p.big_hu_candidates(hand)) == 6
    hand = [11,12,13,14,15,16,17,18,19,45,45,45,46,46]
    assert max(c["big_points"] for c in p.big_hu_candidates(hand)) == 3


def test_open_and_concealed_kongs_count_as_pungs():
    candidates = p.big_hu_candidates([25,25], ["k11","G12","g13","k14"])
    assert len(candidates) == 1 and candidates[0]["big_points"] == 5
