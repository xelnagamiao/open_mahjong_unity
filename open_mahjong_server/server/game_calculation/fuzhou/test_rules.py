from dataclasses import replace

import pytest

from .rules import (FLOWERS, NUMBERS, PHYSICAL, WinContext, can_youjin, flower_points,
                    golden_middle_wait, is_complete, meld_tiles, normalize_config,
                    payments, reserved_tiles, score_hand, valid_tiles, waiting_tiles)


HAND = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 37, 37, 37, 29, 29]


def test_golden_middle_checks_alternative_assignments_of_two_gold_tiles():
    # The new gold can be the 6 in 456 in one decomposition and the natural
    # centre 5 in another. The first assignment must not reject the latter.
    hand = [11]*3 + [21]*3 + [31]*3 + [37]*3 + [29]*2 + [14,15]
    assert golden_middle_wait(hand, jin=15)


def test_pack_and_five_meld_structure():
    assert len(PHYSICAL) == 144
    assert len(FLOWERS) == 15
    assert is_complete(HAND, jin=39)
    assert not is_complete(HAND[:14], jin=39)
    assert not is_complete(HAND + [39] * 3, jin=39)
    assert is_complete(HAND[3:], ["s12"], jin=39)
    assert is_complete(HAND[:-2] + [39, 39], jin=39)
    assert waiting_tiles(HAND[:-1], jin=39) == {29, 39}


@pytest.mark.parametrize("hand,melds", [([11] * 5, []), (HAND[:-1] + [41], []),
    (HAND, ["k39"]), (HAND, ["s11"]), (HAND, ["k41"]), (HAND, ["x12"]),
    ([39] * 4, []), ([True], []), ([11], ["s99"]), ([11], ["sxx"])])
def test_physical_rejection(hand, melds):
    assert not valid_tiles(hand, melds, jin=39)
    assert not is_complete(hand, melds, jin=39)
    assert not waiting_tiles(hand, melds, jin=39)


def test_flowers_are_accounted_separately():
    assert flower_points([41] * 4) == 6
    assert flower_points([51, 52, 53, 54]) == 6
    assert flower_points([55, 56, 57, 58]) == 6
    assert flower_points([41] * 4 + [42, 43] + [51, 52, 53, 54]) == 14
    for flowers in ([41] * 5, [51, 51], [11], [True]):
        with pytest.raises(ValueError):
            flower_points(flowers)
    assert reserved_tiles([(["g11", "G21"], [41] * 4), ([], [51, 52, 53, 54])]) == 25


def test_fixed_quote_and_exclusive_suit():
    q = score_hand(HAND, jin=39)
    assert q["points"] == 45
    assert sum(row["points"] for row in q["items"]) == q["points"]
    assert score_hand(HAND, flowers=[51], jin=39)["points"] == 31
    pure = [11, 12, 13, 14, 15, 16, 17, 18, 19, 11, 11, 11, 14, 14, 14, 19, 19]
    q = score_hand(pure, jin=29)
    assert {r["id"] for r in q["items"]} >= {"pure_suit"}
    assert "mixed_suit" not in {r["id"] for r in q["items"]}
    q = score_hand(pure[:-1] + [29], jin=29)
    assert "mixed_suit" in {r["id"] for r in q["items"]}
    assert "pure_suit" not in {r["id"] for r in q["items"]}


def test_gold_pair_dragon_and_separate_three_gold_declaration():
    pair = HAND[:-2] + [39, 39]
    assert score_hand(pair, jin=39)["points"] == 107
    dragon = HAND[:12] + [39] * 3 + [29, 29]
    q = score_hand(dragon, jin=39)
    assert q["points"] == 168
    assert "gold_dragon" in {r["id"] for r in q["items"]}
    q = score_hand(dragon, jin=39, special="three_gold")
    assert q["points"] == 58
    assert q["pattern_points"] == 40 and q["witness"] is None
    assert score_hand(dragon[:-1], jin=39, special="three_gold") is None
    assert score_hand(pair, jin=39, special="three_gold") is None


def test_flower_win_shape_exception_and_suppression():
    flowers = [tile for tile in range(41, 46) for _ in range(4)]
    incomplete = HAND[:-1] + [18]
    assert not is_complete(incomplete, jin=39)
    quote = score_hand(incomplete, flowers=flowers, jin=39, special="flower",
                       context=WinContext(heavenly=True, youjin=2))
    assert quote["pattern_points"] == 60
    assert quote["points"] == 105
    assert score_hand([11], flowers=flowers[:-1], jin=39, special="flower") is None
    assert score_hand(HAND, jin=39, special="unknown") is None


def test_context_additions_and_double_youjin_stacks_gold_pair():
    pair = HAND[:-2] + [39, 39]
    q = score_hand(pair, jin=39, flowers=[51], context=WinContext(youjin=2, dealer_streak=2))
    assert q["points"] == 15 + 1 + 2 + 6 + 15 + 120 + 60
    ids = {row["id"] for row in q["items"]}
    assert "gold_pair" in ids
    q = score_hand(HAND, jin=39, context=WinContext(source="rob_kong", winning_tile=29,
        after_ready_kong=True, heavenly=True, qiangjin=True, golden_middle=True))
    assert q["points"] == 45 + 15 + 10 + 40 + 30 + 40
    assert score_hand(HAND, jin=39, context=WinContext(source="discard", winning_tile=39)) is None
    assert score_hand(HAND, jin=39, context=WinContext(source="invalid")) is None
    assert score_hand(HAND[:-1], jin=39) is None


def test_youjin_and_golden_middle_candidates():
    pair = HAND[:-2] + [39, 39]
    assert 39 in can_youjin(pair, jin=39)
    assert not can_youjin(HAND, jin=39)
    held = [11, 11, 11, 21, 22, 23, 31, 32, 33, 35, 35, 35, 29, 29, 16, 18]
    assert golden_middle_wait(held, jin=17)
    assert not golden_middle_wait(held, jin=19)
    assert not golden_middle_wait([17] * 4, jin=17)


@pytest.mark.parametrize("winner", range(4))
def test_payments_each_seat(winner):
    changes = payments(winner, 47)
    assert changes[winner] == 141 and sum(changes) == 0
    for payer in range(4):
        if payer == winner:
            with pytest.raises(ValueError):
                payments(winner, 47, payer)
        else:
            result = payments(winner, 47, payer)
            assert result[payer] == -47 and result[winner] == 47 and sum(result) == 0


def test_strict_config_and_accounting_inputs():
    assert normalize_config()["base_score"] == 15
    assert normalize_config({"base_score": 20})["base_score"] == 20
    for raw in ([], {"base_score": True}, {"base_score": 0}, {"x": 1}, {"rule_version": "other"}):
        with pytest.raises(ValueError):
            normalize_config(raw)
    for args in ((True, 10), (0, 0), (0, True), (4, 10), (0, 10, True)):
        with pytest.raises(ValueError):
            payments(*args)
    with pytest.raises(ValueError):
        score_hand(HAND, jin=39, base_score=0)
    with pytest.raises(ValueError):
        score_hand(HAND, jin=39, context=WinContext(dealer_streak=-1))
    assert meld_tiles("G11") == (11,) * 4


def test_five_pungs_and_five_exposed_meld_pair_score_all_seventeen_tiles():
    pungs = [11]*3 + [21]*3 + [31]*3 + [18]*3 + [28]*3 + [23]*2
    q = score_hand(pungs, jin=39)
    assert q['points'] == 15 + 30 + 15
    assert 'all_pungs' in {row['id'] for row in q['items']}
    melds = ['k11','k21','k31','k18','k28']
    q = score_hand([23,23], melds, jin=39)
    assert q['points'] == 15 + 30 + 15 + 60
    assert {'single_pair','all_pungs'} <= {row['id'] for row in q['items']}
    assert score_hand([23,23], melds[:-1], jin=39) is None
    assert score_hand([23,23,24], melds, jin=39) is None
    assert score_hand([23,23], ['k39',*melds[1:]], jin=39) is None


def test_golden_centre_requires_an_available_natural_winning_gold():
    assert not golden_middle_wait([17]*3 + HAND[:12] + [29], jin=17)
    assert not golden_middle_wait(HAND[:-1], jin=17)
    assert score_hand(HAND[:-1]+[41], jin=39) is None
