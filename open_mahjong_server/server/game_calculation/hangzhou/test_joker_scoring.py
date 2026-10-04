"""Book-based fixtures against the real shared joker solver, never a stub."""

import json

import pytest

from .rules import JOKER, WinContext, can_baotou, evaluate_win, waiting_tiles


STANDARD = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 45, 45]
PLAIN_JOKER = [11, 12, JOKER, 14, 15, 16, 21, 22, 23, 31, 32, 33, 45, 45]
SIX_PAIRS = [11, 11, 14, 14, 21, 21, 24, 24, 31, 31, 34, 34]
FOUR_MELDS = ["s12", "s15", "s22", "s35"]


@pytest.mark.parametrize("count", range(5))
def test_zero_to_four_physical_jokers_and_assignment_never_mutates_hand(count):
    hand = list(STANDARD)
    for index in (0, 3, 6, 9)[:count]:
        hand[index] = JOKER
    original = list(hand)
    score = evaluate_win(hand, win_tile=45)
    assert score is not None
    assert score.shape == "standard"
    assert ("no_joker" in score.fan_ids) == (count == 0)
    assert hand == original
    assert score.decomposition["physical_tiles"] == tuple(hand)
    assert len(score.decomposition["assignment"]) == 14
    for physical, logical in zip(hand, score.decomposition["assignment"]):
        assert physical == JOKER or physical == logical
    assert json.loads(json.dumps(score.as_dict()))["rule_version"].startswith("mil-hangzhou")


def test_plain_and_no_joker_base_fans():
    score = evaluate_win(PLAIN_JOKER, win_tile=45)
    assert score.fan_ids == ("plain",)
    assert (score.raw_fan, score.points) == (0, 1)
    score = evaluate_win(STANDARD)
    assert score.fan_ids == ("no_joker",)
    assert (score.raw_fan, score.points) == (1, 2)


def test_only_self_draw_wins_and_no_rob_kong_or_discard_win():
    assert evaluate_win(STANDARD, context=WinContext(self_drawn=False)) is None


@pytest.mark.parametrize("draw", [11, 19, 21, 29, 31, 39, 41, 45, 46, 47])
def test_baotou_four_melds_accepts_every_draw_domain(draw):
    before = STANDARD[:12] + [JOKER]
    assert can_baotou(before)
    result = evaluate_win(before + [draw], win_tile=draw,
                          context=WinContext(pre_draw_tiles=before))
    assert "baotou" in result.fan_ids
    assert result.raw_fan == 1


@pytest.mark.parametrize("draw", [11, 19, 41, JOKER])
def test_baotou_six_pairs_and_seven_pairs_are_both_counted(draw):
    before = SIX_PAIRS + [JOKER]
    assert can_baotou(before)
    result = evaluate_win(before + [draw], win_tile=draw)
    assert result.shape == "seven_pairs"
    assert result.fan_ids == ("seven_pairs", "baotou")
    assert result.raw_fan == 3 and result.points == 8


def test_baotou_removes_exactly_one_joker_and_keeps_remaining_jokers_active():
    before = [11, 12, JOKER, 14, 15, 16, 21, 22, 23, 31, 32, 33, JOKER]
    assert can_baotou(before)
    assert "baotou" in evaluate_win(before + [41], win_tile=41).fan_ids
    pair_before = [11, 14] + [21, 21, 24, 24, 31, 31, 34, 34] + [JOKER] * 3
    assert can_baotou(pair_before)


def test_open_short_hand_baotou_and_all_joker_pair():
    assert can_baotou([JOKER], FOUR_MELDS)
    for draw in (28, JOKER):
        score = evaluate_win([JOKER, draw], FOUR_MELDS, draw)
        assert score.fan_ids == ("baotou",)
        assert score.points == 2


def test_joker_only_in_winning_draw_does_not_retroactively_make_baotou():
    assert "baotou" not in evaluate_win(PLAIN_JOKER, win_tile=JOKER).fan_ids
    assert not can_baotou(STANDARD[:-1])
    assert not can_baotou(PLAIN_JOKER[:-1])
    assert not can_baotou(PLAIN_JOKER)
    assert not can_baotou(None)


@pytest.mark.parametrize("count", range(5))
def test_consecutive_cai_piao_stacks_then_caps_at_four(count):
    before = STANDARD[:12] + [JOKER]
    score = evaluate_win(before + [41], win_tile=41,
                         context=WinContext(cai_piao_count=count, pre_draw_tiles=before))
    assert score.raw_fan == 1 + count
    assert score.fan_total == min(4, 1 + count)
    assert score.points == 2 ** min(4, 1 + count)
    assert ("cai_piao" in score.fan_ids) == bool(count)
    if count:
        assert score.fans[-1].fan == count


def test_discarding_joker_without_baotou_is_not_cai_piao():
    score = evaluate_win(PLAIN_JOKER, win_tile=45, context=WinContext(cai_piao_count=2))
    assert score.fan_ids == ("plain",)


def test_kong_draw_fan_requires_a_real_declared_kong():
    assert evaluate_win(STANDARD, context=WinContext(after_kong=True)) is None
    tail = [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45]
    for meld in ("g19", "G19"):
        result = evaluate_win(tail, [meld], 45, WinContext(after_kong=True))
        assert result.fan_ids == ("kong_draw", "no_joker")
        assert result.points == 4


def test_kong_draw_baotou_and_cai_piao_all_stack_with_cap():
    before = [11, 12, 13, 21, 22, 23, 31, 32, 33, JOKER]
    score = evaluate_win(before + [41], ["G19"], 41,
                         WinContext(after_kong=True, cai_piao_count=3, pre_draw_tiles=before))
    assert score.fan_ids == ("baotou", "cai_piao", "kong_draw")
    assert score.raw_fan == 5 and score.fan_total == 4 and score.points == 16


def test_normal_seven_pairs_and_best_shape_when_standard_also_possible():
    hand = [11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17]
    score = evaluate_win(hand)
    assert score.shape == "seven_pairs"
    assert score.fan_ids == ("seven_pairs", "no_joker")
    assert score.points == 8


def test_luxury_requires_four_physical_identical_tiles_not_substituted_fourth():
    luxury = [11] * 4 + [14] * 2 + [21] * 2 + [24] * 2 + [31] * 2 + [34] * 2
    score = evaluate_win(luxury)
    assert score.fan_ids == ("luxury_seven_pairs", "no_joker")
    assert score.raw_fan == 5 and score.points == 16
    fake = [11] * 3 + [14] * 2 + [21] * 2 + [24] * 2 + [31] * 2 + [34] * 2 + [JOKER]
    score = evaluate_win(fake, win_tile=34)
    assert score.fan_ids == ("seven_pairs",)
    assert score.points == 4


def test_four_whites_luxury_only_when_they_are_literal_white_pairs():
    natural = [JOKER] * 4 + [11] * 2 + [14] * 2 + [21] * 2 + [24] * 2 + [31] * 2
    score = evaluate_win(natural, win_tile=31)
    assert "luxury_seven_pairs" in score.fan_ids
    assert score.decomposition["closed_logical_tiles"].count(JOKER) == 4
    assert all(physical == logical for physical, logical in
               zip(natural, score.decomposition["assignment"]))
    used_as_substitutes = [JOKER] * 4 + [11, 14, 21, 24] + [31] * 2 + [34] * 2 + [37] * 2
    score = evaluate_win(used_as_substitutes, win_tile=37)
    assert score.fan_ids == ("seven_pairs",)
    assert score.points == 4


def test_open_hand_and_kong_cannot_be_seven_pairs():
    result = evaluate_win([JOKER, 28], FOUR_MELDS, 28)
    assert result.shape == "standard"
    assert "seven_pairs" not in result.fan_ids


def test_waits_include_joker_and_exclude_fifth_physical_copy():
    assert waiting_tiles(STANDARD[:-1]) == {45, JOKER}
    # Three 11 exposed plus one in hand: another physical 11 is impossible,
    # but a joker can complete that pair without inventing a fifth real tile.
    assert waiting_tiles([11], ["k11", "s22", "s25", "s32"]) == {JOKER}
    assert waiting_tiles([11], ["G11", "s22", "s25", "s32"]) == set()
    assert waiting_tiles(None) == set()


def test_hangzhou_has_no_unpublished_logical_four_copy_cap():
    hand = [11, 12, 13, 21, 22, 23, 31, 32, 33] + [41] * 4 + [JOKER]
    result = evaluate_win(hand, win_tile=41)
    assert result is not None
    assert result.decomposition["closed_logical_tiles"].count(41) == 5
    assert result.decomposition["physical_tiles"].count(41) == 4


def test_no_unpublished_thirteen_orphans_or_four_joker_automatic_win():
    orphans = [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47, 11]
    assert evaluate_win(orphans) is None
    four_jokers = [11, 14, 17, 21, 24, 27, 31, 34, 37, 41] + [JOKER] * 4
    assert evaluate_win(four_jokers) is None


def test_actual_pre_draw_multiset_is_checked_not_a_caller_baotou_flag():
    correct = STANDARD[:-1]
    assert evaluate_win(STANDARD, context=WinContext(pre_draw_tiles=correct)) is not None
    wrong = list(correct)
    wrong[0] = 19
    assert evaluate_win(STANDARD, context=WinContext(pre_draw_tiles=wrong)) is None
    invalid = [True] * 13
    assert evaluate_win(STANDARD, context=WinContext(pre_draw_tiles=invalid)) is None


@pytest.mark.parametrize("winning_tile", [False, 51, 19, "45"])
def test_invalid_or_absent_winning_tile(winning_tile):
    assert evaluate_win(STANDARD, win_tile=winning_tile) is None


def test_no_win_and_invalid_input_do_not_create_a_score():
    assert evaluate_win([11, 13, 15, 17, 19, 21, 23, 25, 27, 29, 31, 33, 35, 37]) is None
    assert evaluate_win(None) is None
    assert evaluate_win(STANDARD[:-1]) is None
    with pytest.raises(ValueError):
        evaluate_win(STANDARD, context={"baotou": True})
