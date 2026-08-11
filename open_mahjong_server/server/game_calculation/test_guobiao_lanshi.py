from pathlib import Path

import pytest

from . import guobiao_lanshi_hepai_check as lanshi_module
from .guobiao_lanshi_hepai_check import Lanshi_Hepai_Check
from ..gamestate.game_guobiao.lanshi_scoring import calculate_lanshi_score_changes


WAY_DIANHE = ["点和", "自风东", "场风东"]


def test_lanshi_checkers_are_source_level_independent():
    assert Lanshi_Hepai_Check.__bases__ == (object,)
    server_source = Path(lanshi_module.__file__).read_text(encoding="utf-8")
    assert "guobiao_hepai_check import" not in server_source
    assert "Chinese_Hepai_Check" not in server_source

    repo_root = Path(__file__).resolve().parents[3]
    unity_source = (repo_root / "open_mahjong_unity/Assets/Scripts/GameScene/Calculation/CalculationScript/Guobiao/GBhepaiLanshi.cs").read_text(encoding="utf-8")
    assert ": Chinese_Hepai_Check" not in unity_source
    assert "new Chinese_Hepai_Check" not in unity_source
    assert "LanshiPlayerTiles" in unity_source


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


def test_two_dragon_triplets_are_not_reported_as_two_terminal_triplets():
    checker = Lanshi_Hepai_Check()
    hand = [35, 35, 36, 37, 37, 38, 38, 39, 45, 45, 45, 47, 47, 47]
    score, fans = checker.hepai_check(hand, [], ["点和", "自风东", "场风西"], 47)
    assert score == 10
    assert fans == ["双箭刻", "混一色", "门前清"]
    assert all("幺九刻" not in fan for fan in fans)


def test_regular_five_points_remove_robbing_kong_occasional_fan():
    checker = Lanshi_Hepai_Check()
    # 22p、中、南均为碰出的刻子；和 8p。南既非门风也非圈风。
    hand = [11, 11, 26, 27, 28]
    combinations = ["k22", "k47", "k42"]
    # 调用方只传抢杠和；和绝张必须由计番器作为伴随番补齐。
    way = ["点和", "抢杠和", "自风东", "场风西"]
    score, fans = checker.hepai_check(hand, combinations, way, 28)
    assert score == 5
    assert fans == ["和绝张", "箭刻", "幺九刻*1"]
    assert "抢杠和" not in fans


def test_self_draw_companion_is_added_for_relevant_occasional_fans():
    checker = Lanshi_Hepai_Check()
    # 清龙 6 + 自摸 1 = 7，因常规番达到起和线，删除偶然番。
    hand = [11, 12, 13, 14, 15, 16, 17, 18, 19, 23, 24, 25, 33, 33]
    for occasional in ("天和", "妙手回春", "杠上开花"):
        score, fans = checker.hepai_check(hand, [], [occasional, "自风东", "场风西"], 19)
        assert score == 7
        assert fans == ["清龙", "自摸"]
        assert occasional not in fans


def test_self_draw_companion_does_not_stack_when_occasional_fan_is_used():
    checker = Lanshi_Hepai_Check()
    # 常规番不足 5 分时，虽然先补齐自摸用于判线，最终仍只显示偶然番。
    hand = [11, 12, 13, 14, 15, 16, 27, 28, 29, 31, 31, 31, 45, 45]
    score, fans = checker.hepai_check(hand, [], ["妙手回春", "自风东", "场风东"], 45)
    assert (score, fans) == (5, ["妙手回春"])


@pytest.mark.parametrize(
    ("occasional", "expected_score", "expected_fans"),
    [
        ("妙手回春", 7, ["清龙", "自摸"]),
        ("海底捞月", 6, ["清龙"]),
        ("杠上开花", 7, ["清龙", "自摸"]),
        ("抢杠和", 8, ["清龙", "和绝张"]),
        ("天和", 7, ["清龙", "自摸"]),
        ("地和", 6, ["清龙"]),
    ],
)
def test_all_occasional_fans_are_removed_above_the_starting_line(
    occasional, expected_score, expected_fans
):
    checker = Lanshi_Hepai_Check()
    hand = [11, 12, 13, 14, 15, 16, 17, 18, 19, 23, 24, 25, 33, 33]
    assert checker.hepai_check(hand, [], [occasional, "自风东", "场风西"], 19) == (
        expected_score,
        expected_fans,
    )


@pytest.mark.parametrize("偶然番", ("妙手回春", "海底捞月", "杠上开花", "抢杠和", "天和", "地和"))
def test_all_occasional_fans_stand_alone_below_the_starting_line(偶然番):
    checker = Lanshi_Hepai_Check()
    hand = [11, 12, 13, 14, 15, 16, 27, 28, 29, 31, 31, 31, 45, 45]
    assert checker.hepai_check(hand, [], [偶然番, "自风东", "场风西"], 45) == (5, [偶然番])


@pytest.mark.parametrize(
    ("hand", "required", "forbidden"),
    [
        ([45, 45, 45, 46, 46, 46, 47, 47, 47, 11, 12, 13, 22, 22], "大三元", {"小三元", "双箭刻", "箭刻"}),
        ([45, 45, 45, 46, 46, 46, 47, 47, 11, 12, 13, 21, 22, 23], "小三元", {"大三元", "双箭刻", "箭刻"}),
        ([35, 35, 36, 37, 37, 38, 38, 39, 45, 45, 45, 47, 47, 47], "双箭刻", {"大三元", "小三元", "箭刻"}),
        ([45, 45, 45, 11, 12, 13, 21, 22, 23, 31, 32, 33, 44, 44], "箭刻", {"大三元", "小三元", "双箭刻"}),
    ],
)
def test_dragon_hierarchy_is_mutually_exclusive(hand, required, forbidden):
    _score, fans = Lanshi_Hepai_Check().hepai_check(hand, [], ["点和", "自风东", "场风西"], hand[-1])
    assert required in fans
    assert forbidden.isdisjoint(fans)


def test_inputs_are_not_mutated_by_scoring():
    checker = Lanshi_Hepai_Check()
    hand = [11, 11, 26, 27, 28]
    combinations = ["k22", "k47", "k42"]
    way = ["点和", "抢杠和", "自风东", "场风西"]
    snapshots = (list(hand), list(combinations), list(way))
    checker.hepai_check(hand, combinations, way, 28)
    assert (hand, combinations, way) == snapshots


def test_best_score_is_selected_from_multiple_decompositions():
    checker = Lanshi_Hepai_Check()
    hand = [11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17]
    decompositions = checker.hepai_decompose(hand, [], WAY_DIANHE, 17)
    assert len(decompositions) >= 2
    assert checker.hepai_check(hand, [], WAY_DIANHE, 17) == (
        decompositions[0]["score"],
        decompositions[0]["fan_list"],
    )
    keys = {
        (tuple(sorted(item["combinations"])), tuple(item["fan_keys"]), item["score"])
        for item in decompositions
    }
    assert len(keys) == len(decompositions)


def test_incomplete_hand_is_not_scored():
    checker = Lanshi_Hepai_Check()
    assert checker.hepai_check([11, 12, 13], [], WAY_DIANHE, 13) == (0, [])
    assert checker.hepai_decompose([11, 12, 13], [], WAY_DIANHE, 13) == []


def test_low_sequence_fans_follow_non_cyclic_combination_rule():
    # 四副顺子最多形成三条无环关系，不把同一关系循环使用。
    fans = Lanshi_Hepai_Check._sequence_fans(["S12", "S22", "S15", "S35"])
    assert len(fans) == 3
    assert fans.count("xixiangfeng") == 2
    assert fans.count("lianliu") == 1


def test_lanshi_four_player_payments():
    assert calculate_lanshi_score_changes(range(4), 0, 5) == {0: 30, 1: -10, 2: -10, 3: -10}
    assert calculate_lanshi_score_changes(range(4), 0, 5, 1) == {0: 30, 1: -20, 2: -5, 3: -5}
