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


def test_same_flower_and_same_sequence_fans_are_repeatable() -> None:
    # AX1-3/BX1-3 与 AX7-9/BX7-9：两个双同顺；AX1-3/AX7-9 与 BX1-3/BX7-9：
    # 两个双同花。双同花/双同顺按组复计。
    hand = [
        "AX1", "AX2", "AX3", "AX7", "AX8", "AX9",
        "BX1", "BX2", "BX3", "BX7", "BX8", "BX9",
    ]
    result = best_win_result(
        hand,
        [],
        self_draw=False,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    fan_map = {fan["name"]: fan["total"] for fan in result["fans"]}
    assert fan_map["双同花"] == 4  # 2 组 × 2 番
    assert fan_map["双同顺"] == 4  # 2 组 × 2 番
    same_flower = next(fan for fan in result["fans"] if fan["name"] == "双同花")
    same_sequence = next(fan for fan in result["fans"] if fan["name"] == "双同顺")
    assert same_flower["count"] == 2
    assert same_sequence["count"] == 2


def test_same_triplet_fans_are_repeatable_across_buckets() -> None:
    # 数字 1 与数字 3 各有一对同数刻子 → 两个双同刻（2×2 番）。
    hand = [
        "AX1", "AY1", "BX1", "CY1", "DY1", "EY1",
        "AX3", "AY3", "BX3", "CY3", "DY3", "EY3",
    ]
    result = best_win_result(
        hand,
        [],
        self_draw=False,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    same_triplet = next(fan for fan in result["fans"] if fan["name"] == "双同刻")
    assert same_triplet["value"] == 2
    assert same_triplet["count"] == 2
    assert same_triplet["total"] == 4


def test_rainbow_sequence_still_counts_for_pinghu() -> None:
    # 彩虹组本质是长顺子，不影响平和；该手牌最优拆解含彩虹组，仍应计平和。
    hand = [
        "AY1", "BY2", "CY3", "DY4", "EY5", "FY6", "GY7",
        "AY8", "BY9", "CY8", "DY7", "EY6",
    ]
    result = best_win_result(
        hand,
        [],
        self_draw=False,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    assert any(fan["name"] == "平和" for fan in result["fans"])

    # 彩虹刻子（同数 14 张，规则书 1008 分例）是刻子类，不计平和。
    hand2 = [f"{letter}{half}1" for letter in "ABCDEFG" for half in "XY"]
    result2 = best_win_result(
        hand2,
        [],
        self_draw=True,
        before_first_discard=True,
        wall_empty=False,
    )
    assert result2 is not None
    assert "平和" not in {fan["name"] for fan in result2["fans"]}


def test_same_triplet_counts_groups_regardless_of_length() -> None:
    # 同刻不要求各组张数相等：数字 1 的刻子 3/3/4 算三同刻，
    # 3/3/3/4 与 3/3/4/4 都算四同刻（只看同数字刻子的个数）。
    def meld(*codes):
        return {"kind": "triplet", "tiles": list(codes)}

    cases = [
        (
            [meld("AX1", "AY1", "BX1"), meld("BY1", "CX1", "CY1"),
             meld("DX1", "DY1", "EX1", "EY1")],
            "三同刻",
            6,
        ),
        (
            [meld("AX1", "AY1", "BX1"), meld("BY1", "CX1", "CY1"),
             meld("DX1", "DY1", "EX1"), meld("EY1", "FX1", "FY1", "GX1")],
            "四同刻",
            12,
        ),
        (
            [meld("AX1", "AY1", "BX1"), meld("BY1", "CX1", "CY1"),
             meld("DX1", "DY1", "EX1", "EY1"), meld("FX1", "FY1", "GX1", "GY1")],
            "四同刻",
            12,
        ),
    ]
    for melds, expected_name, expected_total in cases:
        result = best_win_result(
            [],
            melds,
            self_draw=False,
            before_first_discard=False,
            wall_empty=False,
        )
        assert result is not None
        same = [fan for fan in result["fans"] if fan["name"] in ("双同刻", "三同刻", "四同刻")]
        assert len(same) == 1
        assert same[0]["name"] == expected_name
        assert same[0]["total"] == expected_total


def test_same_flower_matches_opposite_direction_sequences() -> None:
    # 同花按“长度 + 花色对应”匹配，方向由顺子自身决定：
    # 递增顺子 AX1 AY2 BX3 BY4 与递减顺子 BY1 BX2 AY3 AX4 花色对应相同，
    # 应互为双同花；客户端 OrderedTiles 若不反转递减顺子会漏算。
    hand = [
        "AX1", "AY2", "BX3", "BY4",
        "BY1", "BX2", "AY3", "AX4",
        "CX1", "CY2", "DX3", "DY4",
        "DY1", "DX2", "CY3", "CX4",
    ]
    result = best_win_result(
        hand,
        [],
        self_draw=False,
        before_first_discard=False,
        wall_empty=False,
    )
    assert result is not None
    same_flower = [fan for fan in result["fans"] if fan["name"] == "双同花"]
    assert len(same_flower) == 1
    assert same_flower[0]["count"] == 2
    assert same_flower[0]["total"] == 4
