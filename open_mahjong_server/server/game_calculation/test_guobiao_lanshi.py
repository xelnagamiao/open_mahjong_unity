from .guobiao_lanshi_hepai_check import Lanshi_Hepai_Check
from ..gamestate.game_guobiao.lanshi_scoring import calculate_lanshi_score_changes


def test_lanshi_value_table_and_cap():
    checker = Lanshi_Hepai_Check()
    assert checker.count_model_dict["xiaosanyuan"] == 16
    assert checker._score(["dasixi", "qingyaojiu"])[0] == 100


def test_qixing_pairs_scores_100_only():
    checker = Lanshi_Hepai_Check()
    hand = [41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47]
    score, fans = checker.hepai_check(hand, [], ["点和", "自风东", "场风东"], 47)
    assert score == 100
    assert fans == ["七星对"]


def test_four_identical_tiles_are_not_two_seven_pair_pairs():
    checker = Lanshi_Hepai_Check()
    # 旧国标七对逻辑会把四张相同牌错误地当作两对；即使另有一般型，不能报七对。
    hand = [11, 11, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16]
    _score, fans = checker.hepai_check(hand, [], ["点和", "自风东", "场风东"], 16)
    assert "七对" not in fans


def test_special_five_excludes_all_other_fans():
    checker = Lanshi_Hepai_Check()
    hand = [11, 12, 13, 11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45]
    score, fans = checker.hepai_check(hand, [], ["自摸", "妙手回春", "自风东", "场风东"], 45)
    assert (score, fans) == (5, ["妙手回春"])


def test_shunwang_requires_two_identical_relations():
    checker = Lanshi_Hepai_Check()
    assert checker._same_relation_twice([(1, 1), (1, 4), (1, 1), (1, 4)])
    assert not checker._same_relation_twice([(1, 1), (1, 4), (2, 1), (2, 4)])


def test_shunhuan_and_low_sequence_matching_reference():
    checker = Lanshi_Hepai_Check()
    consecutive = [(1, 1), (1, 4), (2, 1), (2, 4)]
    terminal = [(1, 1), (1, 7), (2, 1), (2, 7)]
    invalid = [(1, 1), (1, 4), (2, 1), (2, 5)]

    assert checker._shunhuan(consecutive)
    assert checker._low_sequence_fans(consecutive) == ["lianliu", "lianliu"]
    assert checker._shunhuan(terminal)
    assert checker._low_sequence_fans(terminal) == ["laoshaofu", "laoshaofu"]
    assert not checker._shunhuan(invalid)
    assert checker._low_sequence_fans(invalid) == ["lianliu"]


def test_lanshi_four_player_payments():
    assert calculate_lanshi_score_changes(range(4), 0, 5) == {0: 30, 1: -10, 2: -10, 3: -10}
    assert calculate_lanshi_score_changes(range(4), 0, 5, 1) == {0: 30, 1: -20, 2: -5, 3: -5}
