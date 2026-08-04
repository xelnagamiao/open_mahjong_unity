from .scoring import best_win_result


def test_scoring_uses_rulebook_base_times_fan_sum() -> None:
    result = best_win_result(
        ["AX1", "AX2", "AX3"],
        [],
        self_draw=True,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    assert result["base"] == 5  # 3 base + 2 concealed
    assert {fan["name"]: fan["total"] for fan in result["fans"]} == {
        "全纯色": 1,
        "三数": 6,
        "平和": 1,
        "金龙": 6,
        "门清": 1,
    }
    assert result["fan_total"] == 15
    assert result["points"] == 75


def test_zero_fan_hand_is_exactly_one_point() -> None:
    result = best_win_result(
        ["DY7", "EY4", "FY1"],
        [
            {"kind": "sequence", "tiles": ["DX3", "DX4", "DX5"]},
            {"kind": "triplet", "tiles": ["BX3", "BY3", "CX3"]},
            {"kind": "triplet", "tiles": ["EX1", "EY1", "FX1"]},
        ],
        self_draw=False,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    assert result["fan_total"] == 0
    assert result["points"] == 1


def test_rulebook_1008_point_example() -> None:
    # Page 8, first example: all 14 colour levels of number 1 form one long
    # triplet.  It scores 16 base, 63 fan and 1008 points.
    hand = [f"{letter}{half}1" for letter in "ABCDEFG" for half in "XY"]
    result = best_win_result(
        hand,
        [],
        self_draw=True,
        before_first_discard=True,
        wall_empty=False,
    )
    assert result is not None
    assert result["base"] == 16
    assert result["fan_total"] == 63
    assert result["points"] == 1008
    assert {fan["name"] for fan in result["fans"]} == {
        "天和", "清一数", "全彩", "金龙", "彩虹", "全带幺", "清刻",
    }


def test_same_number_pair_scores_with_exposed_groups_without_becoming_a_meld() -> None:
    result = best_win_result(
        ["BX5", "CY5"],
        [{"kind": "sequence", "tiles": ["AX1", "AX2", "AX3"]}],
        self_draw=True,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    assert result["pair"] == ["BX5", "CY5"]
    assert result["groups"] == [["AX1", "AX2", "AX3"]]
