"""原书第9–12页牌例、鬼实体与完整计分见证的真实核心集成。"""

from collections import Counter
from dataclasses import replace

import pytest

from ..config import GHOSTS, TILES, PHYSICAL_TILES
from ..rules import POLICY, is_complete, score, structural_waits, waiting_scores, valid_inventory
from ..scoring import Context
from ...jokers import iter_winning_shapes


def m(digits):
    return [10 + int(digit) for digit in digits]


@pytest.mark.parametrize("hand,melds,target", [
    (m("1234444556677"), [], 14),
    (m("1234556677"), ["k14"], 14),
    (m("1255667799"), ["g13"], 13),
])
def test_book_cap_excludes_exposed_melds(hand, melds, target):
    with_ghost = hand + [55]
    witnesses = list(iter_winning_shapes(with_ghost, melds, POLICY, winning_tile=55))
    assert witnesses and is_complete(with_ghost, melds)
    targets = {shape.winning_logical_tile for shape in witnesses}
    if not melds:
        assert target not in targets and {11, 17} <= targets
        assert target not in structural_waits(hand, melds)
    else:
        assert target in targets
    for shape in witnesses:
        assert max(Counter(shape.closed_logical_tiles).values()) <= 4


def test_virtual_fifth_is_not_accepted_by_boolean_or_score():
    hand = m("123") + [21, 22, 23, 31, 32, 33] + [41] * 4 + [55]
    assert not is_complete(hand)
    assert score(hand, context=Context(self_draw=True)) is None


def test_physical_fifth_and_duplicate_ghost_are_invalid_even_if_four_ghosts():
    assert not valid_inventory([11, 11, 21, 22, 23, 31, 32, 33, 55, 56, 57], ["k11"], 14)
    assert not is_complete([11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 55, 55, 57, 58])
    assert not is_complete([11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 55, 56, 57, 51])


def test_four_ghosts_any_rest_and_unobtainable_waits():
    hand = [11, 14, 17, 21, 24, 27, 31, 34, 37, 41, 55, 56, 57]
    assert 58 in structural_waits(hand)
    assert not set(GHOSTS[:3]) & structural_waits(hand)
    result = score(hand + [58], context=Context(self_draw=True))
    assert result["fan_ids"] == ["four_ghosts"] and result["base_score"] == 12
    assert score(hand + [58], context=Context()) is None  # 打鬼不能被点和。


def test_three_ghosts_orphans_beats_four_ghosts_and_preserves_physical():
    hand = [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 55, 56, 57, 58]
    before = list(hand)
    result = score(hand, context=Context(self_draw=True))
    assert result["shape"] == "thirteen_orphans"
    assert result["fan"] == 17 and result["fan_ids"] == ["self_draw", "thirteen_orphans"]
    assert hand == before and len(result["substitutions"]) == 4
    assert {item["physical"] for item in result["substitutions"]} == set(GHOSTS)


def test_minimum_fan_and_score_are_separate_and_optional_only_for_score():
    hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45, 55]
    assert score(hand, context=Context(self_draw=True)) is None
    detail = score(hand, context=Context(self_draw=True, require_minimum_score=False))
    assert detail["fan"] == 2 and detail["base_score"] == 2
    assert score(hand, winning_tile=45, context=Context(require_minimum_score=False)) is None
    plain = hand[:-1] + [45]
    assert score(plain, context=Context(self_draw=True))["base_score"] == 4
    assert score(plain, context=Context(discarded_ghosts=3)) is None  # 1番即使16分亦不够2番。


def test_no_ghost_and_discarded_ghosts_multiply_as_user_confirmed():
    hand = m("11122233344455")
    detail = score(hand, context=Context(self_draw=True, discarded_ghosts=2))
    assert detail["coefficient"] == 8
    assert detail["base_score"] == detail["fan"] * 8


def test_nine_gates_requires_prewin_shape_not_just_complete_multiset():
    hand = m("11123456789995")
    exact = score(hand, winning_tile=15, context=Context(self_draw=True))
    other = score(hand, winning_tile=11, context=Context(self_draw=True))
    assert "nine_gates" in exact["fan_ids"]
    assert "nine_gates" not in other["fan_ids"]


def test_waits_distinguish_self_draw_ron_threshold_and_copy_safe_cache():
    hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45]
    details = waiting_scores(hand)
    assert details[0] == {"tile": 45, "fan": 2, "score": 4, "ron": False,
                       "ron_fan": 0, "self_draw_fan": 2,
                       "self_draw": True, "ron_score": 0, "self_draw_score": 4}
    assert {item["tile"] for item in details} == set(structural_waits(hand))
    assert all(not item["ron"] and not item["self_draw"] for item in details[1:])
    details[0]["score"] = -1
    assert waiting_scores(hand)[0]["score"] == 4
    assert set(structural_waits(hand)) == {45, 55, 56, 57, 58}


def test_wait_hint_keeps_ron_and_self_draw_fan_and_points_separate():
    hand = [11, 11, 11, 22, 22, 22, 33, 33, 33, 41, 41, 41, 45]
    detail = next(x for x in waiting_scores(hand) if x["tile"] == 45)
    assert detail["ron"] and detail["self_draw"]
    assert (detail["ron_fan"], detail["ron_score"]) == (5, 10)
    assert (detail["self_draw_fan"], detail["self_draw_score"]) == (6, 12)
    assert waiting_scores(hand, passed_base_score=10) == waiting_scores(hand)


def test_structural_wait_below_minimum_is_kept_and_score_toggle_does_not_change_shape():
    hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 55]
    normal = waiting_scores(hand)
    relaxed = waiting_scores(hand, context=Context(require_minimum_score=False))
    assert {item["tile"] for item in normal} == set(structural_waits(hand))
    assert len(normal) == 36
    assert {item["tile"] for item in normal} == {item["tile"] for item in relaxed}
    below = next(item for item in normal if item["tile"] == 45)
    eligible = next(item for item in relaxed if item["tile"] == 45)
    assert not below["ron"] and not below["self_draw"] and below["score"] == 0
    assert not eligible["ron"] and eligible["self_draw"]
    assert (eligible["self_draw_fan"], eligible["self_draw_score"]) == (2, 2)


def test_basic_hints_do_not_predict_accidental_fans_or_passed_win():
    hand = [21, 22, 23, 31, 32, 33, 41, 41, 41, 45]
    accidental = Context(rob_kong=True, last_tile=True, kong_flower=True,
                         heavenly=True, earthly=True)
    baseline = waiting_scores(hand, ["k11"])
    assert baseline
    assert waiting_scores(hand, ["k11"], context=accidental, passed_base_score=100) == baseline
    target = next(item for item in baseline if item["tile"] == 45)
    assert not target["ron"] and not target["self_draw"]
    # 这里只改变提示；实际抢杠/杠上花仍可以达到起和门槛。
    assert score(hand + [45], ["k11"], context=Context(rob_kong=True))["base_score"] == 8
    assert score(hand + [45], ["k11"], context=Context(self_draw=True, kong_flower=True))["base_score"] == 10


@pytest.mark.parametrize("hand,melds", [(None, []), ([], None), ({11, 12}, []), ([True] * 14, []), ([11] * 14, []),
    ([11] * 11, ["s12"]), ([11] * 11, ["G55"]), ([11] * 11, [False])])
def test_invalid_external_inputs_are_rejected(hand, melds):
    assert not is_complete(hand, melds)
    assert score(hand, melds) is None
    assert not structural_waits(hand, melds)
