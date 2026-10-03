"""规则书七至九节的独立牌例；不以实现输出生成期望分值。"""
import pytest

from .qiaoma import decompositions, waits, score, payments, physical_flower_count, contract_patterns


PLAIN = [11, 12, 13, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44, 44]
OPEN_HAND = PLAIN[3:]
OPEN_MELDS = ["s12"]


def scored(hand=PLAIN, melds=(), flowers=(), **kwargs):
    return score(hand, melds, flowers, declared_ready=True, **kwargs)


def test_declaration_required_even_for_complete_hand():
    assert score(PLAIN) is None
    assert scored()["base_score"] == 22  # 无花10花、门清1番。


def test_thirteen_plus_one_and_no_seven_pairs():
    assert waits(PLAIN[:-1]) == {44}
    assert not decompositions([11, 11, 14, 14, 18, 18, 21, 21, 25, 25, 29, 29, 33, 33])
    assert not decompositions(PLAIN + [11, 12, 13])


def test_dragons_are_flowers_not_structural_tiles():
    assert not decompositions(PLAIN[:-2] + [45, 45])
    assert waits(PLAIN[:-1]).isdisjoint({45, 46, 47})


def test_fifth_copy_rejected_across_hand_and_melds():
    assert not decompositions([11, 11, 11, 12, 13, 14, 21, 22, 23, 44, 44], ["k11"])


@pytest.mark.parametrize("flowers,self_draw,expected", [
    ([], False, 11), ([51], False, None), ([51], True, None),
    ([51, 45], False, None), ([51, 45], True, 3),
    ([51, 45, 46], False, 4),
])
def test_plain_hand_minimum_flowers(flowers, self_draw, expected):
    result = scored(OPEN_HAND, OPEN_MELDS, flowers, self_draw=self_draw)
    assert (result["base_score"] if result else None) == expected


def test_accidental_fan_does_not_remove_flower_threshold():
    assert scored(OPEN_HAND, OPEN_MELDS, [51], self_draw=True, replacement=True) is None
    assert scored(OPEN_HAND, OPEN_MELDS, [51, 52], rob_kong=True) is None
    assert scored(OPEN_HAND, OPEN_MELDS, [51, 52], self_draw=True, replacement=True)["base_score"] == 6


@pytest.mark.parametrize("code,expected", [("g11", 1), ("G11", 2), ("k41", 1), ("g41", 2), ("G41", 3)])
def test_flower_table(code, expected):
    assert physical_flower_count([], [code]) == expected


def test_hidden_wind_triplet_counts_and_no_flower_bonus_is_exclusive():
    hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 44, 44]
    result = scored(hand)
    assert result["flowers"] == 1
    assert result["base_score"] == 4


def test_fan_stacking_and_three_fan_cap():
    hand = [11, 11, 11, 13, 13, 13, 15, 15, 15, 17, 17, 17, 19, 19]
    result = scored(hand, self_draw=True, replacement=True)
    assert result["fan"] == 5  # 门清、碰碰和、清一色、杠开。
    assert result["capped_fan"] == 3
    assert result["base_score"] == 88


def test_big_single_wait_and_half_flush():
    result = scored([41, 41], ["s12", "s15", "s18", "k11"], [51])
    assert result["fan"] == 2
    assert result["base_score"] == 8


@pytest.mark.parametrize("kwargs,expected", [
    ({}, {0: 30, 1: -10, 2: -10, 3: -10}),
    ({"discarder": 2}, {0: 10, 1: 0, 2: -10, 3: 0}),
    ({"partners": [1, 3]}, {0: 100, 1: -50, 2: 0, 3: -50}),
    ({"discarder": 2, "partners": [1, 3], "discarder_ready": True}, {0: 30, 1: -10, 2: -10, 3: -10}),
    ({"discarder": 2, "partners": [1, 3]}, {0: 30, 1: 0, 2: -30, 3: 0}),
    ({"discarder": 1, "partners": [1, 3], "discarder_ready": True}, {0: 30, 1: -20, 2: 0, 3: -10}),
    ({"discarder": 1, "partners": [1, 3]}, {0: 30, 1: -30, 2: 0, 3: 0}),
])
def test_zero_sum_contract_payments(kwargs, expected):
    result = payments(0, 10, **kwargs)
    assert result == expected
    assert sum(result.values()) == 0


def test_fourth_meld_can_break_contract_pattern():
    assert contract_patterns(["s12", "s15", "s18"])
    assert not contract_patterns(["s12", "s15", "s18", "s22"])
    assert contract_patterns(["k11", "g21", "k31"]) == {"碰碰和"}
