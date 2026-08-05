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
        "清一色": 18,
        "全纯色": 1,
        "三数": 6,
        "平和": 1,
        "金龙": 6,
        "全带幺": 2,
        "清顺": 1,
        "门清": 1,
    }
    assert result["fan_total"] == 36
    assert result["points"] == 180


def test_zero_fan_hand_is_exactly_one_point() -> None:
    result = best_win_result(
        [],
        [
            {"kind": "sequence", "tiles": ["AX1", "AY2", "BX3"]},
            {"kind": "sequence", "tiles": ["BY4", "CX5", "CY6"]},
            {"kind": "triplet", "tiles": ["DX7", "DY7", "EX7"]},
            {"kind": "triplet", "tiles": ["FY2", "GX2", "GY2"]},
            {"kind": "triplet", "tiles": ["AY5", "BY5", "CY5"]},
        ],
        self_draw=False,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    assert result["fan_total"] == 0
    assert result["points"] == 1


def test_dual_colour_counts_covered_pure_colours() -> None:
    # AX3 AY3 BX3 × 4：红(AX) + 红橙(AY 覆盖红/橙) + 橙(BX) → 覆盖 {红,橙}=2 → 双色
    result = best_win_result(
        ["AX3", "AY3", "BX3", "AX4", "AY4", "BX4", "AX5", "AY5", "BX5", "AX6", "AY6", "BX6"],
        [],
        self_draw=True,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    names = {fan["name"] for fan in result["fans"]}
    assert "双色" in names
    assert "三色" not in names
    # 七归一按 14 级独立计数：AX/AY/BX 各 4 张，不合并 → 不计七归一
    assert "七归一" not in names
    assert result["fan_total"] == 27
    assert result["points"] == 216


def test_seven_nine_return_count_14_levels_independently() -> None:
    # AX1..9 为同一级（9 张）→ 九归一；AY 不并入 AX
    result = best_win_result(
        ["AX1", "AX2", "AX3", "AX4", "AX5", "AX6", "AX7", "AX8", "AX9", "AY1", "AY2", "AY3"],
        [],
        self_draw=True,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    names = {fan["name"] for fan in result["fans"]}
    assert "九归一" in names
    assert "七归一" not in names
    # AX 覆盖红、AY 覆盖红+橙 → 双色（不是清一色）
    assert "双色" in names
    assert "清一色" not in names


def test_2_23_4_pattern_is_not_dual_colour() -> None:
    # BX(橙)+BY(橙黄 覆盖橙/黄)+DX(绿)+DY(绿青 覆盖绿/青) → 覆盖 4 色
    result = best_win_result(
        ["BX1", "BX2", "BX3", "BY1", "BY2", "BY3", "DX1", "DX2", "DX3", "DY1", "DY2", "DY3"],
        [],
        self_draw=True,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    names = {fan["name"] for fan in result["fans"]}
    assert "双色" not in names
    assert "三色" not in names


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


def test_exposed_groups_with_concealed_group_complete_the_win() -> None:
    result = best_win_result(
        ["AX1", "AX2", "AX3"],
        [{"kind": "sequence", "tiles": ["BX4", "BX5", "BX6"]}],
        self_draw=True,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    assert result["pair"] == []
    assert sorted(tuple(group) for group in result["groups"]) == [
        ("AX1", "AX2", "AX3"),
        ("BX4", "BX5", "BX6"),
    ]
