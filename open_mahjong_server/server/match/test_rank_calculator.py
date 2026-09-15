import pytest

from server.match.rank_calculator import apply_pt, calculate_pt, can_play_tier, get_rank_index


def test_first_and_second_use_format_multiplier():
    assert calculate_pt("beginner", "quanzhuang", 1, "2级") == 24
    assert calculate_pt("beginner", "banzhuang", 1, "2级") == 16.8
    assert calculate_pt("beginner", "dongfeng", 1, "2级") == 11.76
    assert calculate_pt("beginner", "quanzhuang", 2, "2级") == 6
    assert calculate_pt("beginner", "banzhuang", 2, "2级") == 4.2
    assert calculate_pt("beginner", "dongfeng", 2, "2级") == 2.94
    assert calculate_pt("advanced", "quanzhuang", 1, "四段") == 84


def test_third_and_fourth_also_use_format_multiplier():
    assert calculate_pt("beginner", "quanzhuang", 3, "2级") == -4.5
    assert calculate_pt("beginner", "banzhuang", 3, "2级") == round(-15 * 0.3 * 0.7, 2)
    assert calculate_pt("beginner", "dongfeng", 3, "2级") == round(-15 * 0.3 * 0.49, 2)
    assert calculate_pt("beginner", "quanzhuang", 4, "2级") == -10.5
    assert calculate_pt("beginner", "banzhuang", 4, "2级") == round(-15 * 0.7 * 0.7, 2)
    assert calculate_pt("beginner", "dongfeng", 4, "2级") == round(-15 * 0.7 * 0.49, 2)
    assert calculate_pt("advanced", "dongfeng", 4, "四段") == round(-95 * 0.7 * 0.49, 2)


def test_can_play_tier_rank_and_pass():
    assert can_play_tier("10级", "beginner")
    assert can_play_tier("九段", "beginner")
    assert not can_play_tier("10级", "intermediate")
    assert can_play_tier("2级", "intermediate")
    assert can_play_tier("10级", "intermediate", is_intermediate_qualified=True)
    assert not can_play_tier("10级", "intermediate", is_beginner_qualified=True)
    assert not can_play_tier("七段", "intermediate")
    assert not can_play_tier("七段", "intermediate", is_intermediate_qualified=True)
    assert not can_play_tier("2级", "advanced")
    assert can_play_tier("四段", "advanced")
    assert can_play_tier("2级", "advanced", is_advanced_qualified=True)
    assert not can_play_tier("九段", "mcrpl")
    assert can_play_tier("10级", "mcrpl", is_mcrpl_qualified=True)


@pytest.mark.parametrize("score,pt,expected", [
    (6999, 0.99, ("九段", 6999.99)),
    (6999.99, 0.01, ("十段", 100)),
    (6916, 84, ("十段", 100)),
    (6999, 84, ("十段", 100)),
    (7100, 0, ("十段", 100)),
])
def test_ninth_dan_promotes_at_7000_and_discards_terminal_overflow(score, pt, expected):
    assert apply_pt("九段", score, pt) == expected


@pytest.mark.parametrize("score", [-100, 0, 100, 3200, 10000])
@pytest.mark.parametrize("pt", [-10000, -126, 0, 108, 10000])
def test_tenth_dan_is_always_fixed_and_never_changes_rank(score, pt):
    assert apply_pt("十段", score, pt) == ("十段", 100)


@pytest.mark.parametrize("tier", ["beginner", "intermediate", "advanced", "mcrpl"])
@pytest.mark.parametrize("game_type", ["dongfeng", "banzhuang", "quanzhuang"])
@pytest.mark.parametrize("position", [1, 2, 3, 4])
def test_tenth_dan_earns_and_loses_no_pt(tier, game_type, position):
    assert calculate_pt(tier, game_type, position, "十段") == 0


def test_tenth_dan_tier_access_uses_highest_rank():
    assert get_rank_index("十段") > get_rank_index("九段")
    assert can_play_tier("十段", "beginner")
    assert can_play_tier("十段", "advanced")
    assert not can_play_tier("十段", "intermediate", is_intermediate_qualified=True)
    assert not can_play_tier("十段", "mcrpl")
    assert can_play_tier("十段", "mcrpl", is_mcrpl_qualified=True)


def test_nonterminal_promotion_and_demotion_are_preserved():
    assert apply_pt("八段", 3199, 2) == ("九段", 3201)
    assert apply_pt("九段", 3200, -126) == ("九段", 3074)
    assert apply_pt("九段", 1, -1) == ("九段", 0)
    assert apply_pt("九段", 0, -0.01) == ("八段", 1600)
    assert apply_pt("10级", 0, -100) == ("10级", 0)
    assert apply_pt("10级", 0, 40) == ("8级", 0)
    assert apply_pt("八段", 3199, 10000) == ("十段", 100)
