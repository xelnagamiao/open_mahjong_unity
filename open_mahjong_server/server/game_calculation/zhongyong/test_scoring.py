import pytest

from .scoring import FANS, HandContext, score_hand, tingpai_check


def score(hand, melds=(), **context):
    return score_hand(HandContext(hand, melds, **context))


def test_official_catalog_has_44_patterns():
    assert len(FANS) == 44


def test_four_concealed_triplets_example_is_additive():
    result = score([12]*3 + [15]*3 + [23]*3 + [37]*3 + [41]*2, winning_tile=41)
    assert result.points == 160
    assert set(result.fan_ids) == {"closed_hand", "all_triplets", "four_concealed_triplets"}


def test_seven_pairs_does_not_include_concealed_hand_and_allows_quad():
    result = score([11]*4 + [22]*2 + [25]*2 + [37]*2 + [41]*2 + [47]*2)
    assert result.points == 30
    assert result.fan_ids == ("seven_pairs",)


def test_special_shapes_cannot_be_built_using_open_melds():
    assert not score([11]*2 + [22]*2 + [25]*2 + [37]*2 + [41]*2 + [47], ["k47"]).is_win


def test_quad_may_split_between_pair_and_sequences():
    hand = [11]*4 + [12]*2 + [13]*2 + [24,25,26,37,38,39]
    assert score(hand).is_win
    assert 11 in tingpai_check(hand[:-1] if hand[-1] == 11 else hand[1:])


def test_chicken_hand_scores_one():
    result = score([11,12,13,24,25,26,37,37], ["k19", "s32"])
    assert result.is_win and result.points == 1
    assert result.fan_ids == ("chicken_hand",)


def test_dragon_pattern_and_each_value_honor_both_count():
    result = score([45]*3+[46]*3+[47]*2+[11,12,13,27,28,29])
    # 40 small dragons + 20 dragons + 40 outside + 5 concealed + 5 two concealed pungs.
    assert result.points == 110
    assert result.fan_ids.count("value_honor") == 2


def test_no_prevailing_wind_only_seat_wind():
    hand=[41]*3+[12,13,14,25,26,27,36,37,38,29,29]
    assert score(hand,seat_wind=41).points - score(hand,seat_wind=42).points == 10


def test_ron_can_assign_winning_tile_to_sequence_instead_of_triplet():
    hand=[12]*4+[13,14]+[25]*3+[38]*3+[41]*2
    result=score(hand,winning_tile=12,win_source="discard")
    assert "three_concealed_triplets" in result.fan_ids


@pytest.mark.parametrize("key", ["haitei","houtei","rinshan","chankan"])
def test_context_bonuses_are_separate_series(key):
    hand=[12,13,14,25,26,27,35,36,37,41,41]
    result=score(hand,["k19"],**{key:True})
    assert key in result.fan_ids and result.points == 10


def test_haitei_and_kong_win_stack():
    result=score([12,13,14,25,26,27,35,36,37,41,41],["g19"],haitei=True,rinshan=True)
    assert result.points == 25


@pytest.mark.parametrize("hand,expected", [
    ([41]*3+[42]*3+[43]*3+[44]*3+[45]*2,400),
    ([11]*3+[19]*3+[21]*3+[29]*3+[31]*2,400),
    ([11]*3+[12,13,14,15,16,17,18]+[19]*3+[15],480),
    ([11]*4+[12]*4+[13]*4+[45]*2,480),
])
def test_listed_limits_count_only_highest_pattern(hand,expected):
    result=score(hand,winning_tile=hand[-1])
    assert result.points == expected and len(result.fan_ids) == 1


def test_compound_limit_caps_at_320():
    result=score([12]*3+[13]*3+[14]*3+[15]*3+[16]*2,winning_tile=16)
    assert result.raw_points > 320 and result.points == 320


def test_four_kongs_is_listed_480():
    assert score([41,41],["G11","g22","g33","G45"]).points == 480


def test_no_fifth_copy_even_across_melds():
    assert not score([11,11,12,13,14,25,26,27,35,36,37],["k11"]).is_win
    assert 11 not in tingpai_check([11,12,13,14,25,26,27,35,36,37],["k11"])


@pytest.mark.parametrize("hand,melds,fan,absent", [
    ([12,13,14,15,16,17,24,25,26,36,37,38,22,22], [], "all_simples", "full_flush"),
    ([12,13,14,22,23,24,32,33,34,45,45], ["k19"], "mixed_triple_chow", "pure_double_chow"),
    ([14]*3+[24]*3+[34]*2+[16,17,18,32,33,34], [], "small_three_suit_triplets", "three_suit_triplets"),
    ([14]*3+[24]*3+[34]*3+[16,17,18,45,45], [], "three_suit_triplets", "small_three_suit_triplets"),
    ([11,12,13,14,15,16,17,18,19,24,25,26,47,47], [], "full_straight", "full_flush"),
    ([12]*3+[13]*3+[14]*3+[24,25,26,45,45], [], "three_consecutive_triplets", "pure_triple_chow"),
    ([41]*3+[42]*3+[43]*2+[12,13,14,25,26,27], [], "small_three_winds", "big_three_winds"),
    ([41]*3+[42]*3+[43]*3+[12,13,14,25,25], [], "big_three_winds", "small_three_winds"),
    ([41]*3+[42]*3+[43]*3+[44]*2+[12,13,14], [], "small_four_winds", "big_three_winds"),
    ([41]*2+[42]*2+[43]*2+[44]*2+[45]*2+[46]*2+[47]*2, [], "all_honors", "seven_pairs"),
    ([11,19,21,29,31,39,41,42,43,44,45,46,47,47], [], "thirteen_orphans", "mixed_terminals"),
    ([11,11,11,19,19,19,41,41,41,22,23,24,47,47], [], "two_concealed_triplets", "four_concealed_triplets"),
    ([12]*3+[23]*3+[41]*2, ["g19","g37"], "two_kongs", "one_kong"),
    ([12]*3+[41]*2, ["g19","g37","g28"], "three_kongs", "two_kongs"),
    ([11,12,13,17,18,19,39,39], ["k21","k29"], "pure_outside_hand", "mixed_outside_hand"),
    ([11]*3+[19]*3+[41]*2, ["k21","k47"], "mixed_terminals", "mixed_outside_hand"),
])
def test_pattern_examples_and_series_exclusions(hand,melds,fan,absent):
    result=score(hand,melds,winning_tile=11 if fan=="two_concealed_triplets" else hand[-1],
                 win_source="discard" if fan=="two_concealed_triplets" else "self_draw")
    assert result.is_win and fan in result.fan_ids
    assert absent not in result.fan_ids


def test_nine_gates_requires_nine_way_wait_before_winning():
    hand=[11]*3+[12,13,14,15,16,17,18]+[19]*3+[15]
    assert score(hand,winning_tile=11).points != 480
