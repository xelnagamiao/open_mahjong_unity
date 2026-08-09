from .guobiao_lanshi_hepai_check import Lanshi_Hepai_Check
from ..gamestate.game_guobiao.lanshi_scoring import calculate_lanshi_score_changes


WAY_DIANHE = ["点和", "自风东", "场风东"]


def test_lanshi_value_table_and_cap():
    checker = Lanshi_Hepai_Check()
    assert checker.count_model_dict["xiaosanyuan"] == 16
    assert checker._score(["dasixi", "qingyaojiu"])[0] == 100


def test_qixing_pairs_is_the_only_reported_limit_fan():
    checker = Lanshi_Hepai_Check()
    hand = [41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47]
    score, fans = checker.hepai_check(hand, [], WAY_DIANHE, 47)
    assert score == 100
    assert fans == ["七星对"]


def test_four_identical_tiles_are_not_two_seven_pair_pairs():
    checker = Lanshi_Hepai_Check()
    hand = [11, 11, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16]
    _score, fans = checker.hepai_check(hand, [], WAY_DIANHE, 16)
    assert "七对" not in fans


def test_occasional_fan_replaces_regular_total_below_five():
    checker = Lanshi_Hepai_Check()
    # 常规番：连六、幺九刻、门前清、自摸，共 4 分。
    hand = [11, 12, 13, 14, 15, 16, 27, 28, 29, 31, 31, 31, 45, 45]
    score, fans = checker.hepai_check(hand, [], ["自摸", "妙手回春", "自风东", "场风东"], 45)
    assert (score, fans) == (5, ["妙手回春"])


def test_occasional_fan_is_removed_when_regular_total_reaches_five():
    checker = Lanshi_Hepai_Check()
    hand = [11, 12, 13, 14, 15, 16, 17, 18, 19, 23, 24, 25, 33, 33]
    score, fans = checker.hepai_check(hand, [], ["自摸", "妙手回春", "自风东", "场风东"], 19)
    assert score == 8
    assert fans == ["清龙", "门前清", "自摸"]


def test_user_example_qinglong_does_not_report_zero_laoshaofu():
    # 33m 123456789s 345p，和 1s。
    checker = Lanshi_Hepai_Check()
    hand = [13, 13, 31, 32, 33, 34, 35, 36, 37, 38, 39, 23, 24, 25]
    score, fans = checker.hepai_check(hand, [], WAY_DIANHE, 31)
    assert score == 7
    assert fans == ["清龙", "门前清"]
    assert all("老少副" not in fan and "*0" not in fan for fan in fans)


def test_user_example_qinglong_does_not_count_its_lianliu():
    # 123456789p 234s 77s，和 8p。
    checker = Lanshi_Hepai_Check()
    hand = [21, 22, 23, 24, 25, 26, 27, 28, 29, 32, 33, 34, 37, 37]
    score, fans = checker.hepai_check(hand, [], WAY_DIANHE, 28)
    assert score == 7
    assert fans == ["清龙", "门前清"]
    assert "连六*1" not in fans


def test_qinglong_allows_one_low_fan_using_the_fourth_sequence():
    # 清龙的三副顺子不计固有连六/老少副；额外一副 123 可与 123 计一般高。
    fans = Lanshi_Hepai_Check._sequence_fans(["S12", "S15", "S18", "S12"])
    assert fans == ["qinglong", "yibangao"]


def test_three_colour_same_sequence_does_not_count_internal_xixiangfeng():
    fans = Lanshi_Hepai_Check._sequence_fans(["S16", "S26", "S36"])
    assert fans == ["sansetongshun"]


def test_low_sequence_fans_follow_non_cyclic_combination_rule():
    # 四副顺子最多形成三条无环关系，不把同一关系循环使用。
    fans = Lanshi_Hepai_Check._sequence_fans(["S12", "S22", "S15", "S35"])
    assert len(fans) == 3
    assert fans.count("xixiangfeng") == 2
    assert fans.count("lianliu") == 1


def test_lanshi_four_player_payments():
    assert calculate_lanshi_score_changes(range(4), 0, 5) == {0: 30, 1: -10, 2: -10, 3: -10}
    assert calculate_lanshi_score_changes(range(4), 0, 5, 1) == {0: 30, 1: -20, 2: -5, 3: -5}
