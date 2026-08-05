from __future__ import annotations

from collections import Counter, defaultdict
from itertools import combinations
from typing import Iterable, Sequence

from .rules import MeldShape, classify_meld, kong_win_candidates
from .tile import HongqueTile
from .win_check import winning_decompositions


def _entry(name: str, value: int, count: int = 1) -> dict:
    return {"name": name, "value": value, "count": count, "total": value * count}


def _arithmetic(values: Sequence[int]) -> bool:
    ordered = sorted(set(values))
    if len(ordered) != len(values) or len(ordered) < 2:
        return False
    step = ordered[1] - ordered[0]
    return step > 0 and all(ordered[i + 1] - ordered[i] == step for i in range(len(ordered) - 1))


def _ordered_tiles(shape: MeldShape) -> list[HongqueTile]:
    tiles = [HongqueTile.parse(code) for code in shape.tiles]
    if shape.base_kind == "sequence":
        return sorted(tiles, key=lambda tile: tile.number, reverse=shape.number_step < 0)
    return sorted(tiles, key=lambda tile: tile.colour)


def _merge_entries(entries: Sequence[dict]) -> list[dict]:
    """同名番种合并为一条带 count 的记录（与清顺等按组复计的展示一致）。"""
    merged: dict[str, dict] = {}
    for item in entries:
        name = item["name"]
        if name in merged:
            merged[name]["count"] += 1
            merged[name]["total"] = merged[name]["value"] * merged[name]["count"]
        else:
            merged[name] = dict(item)
    return list(merged.values())


def _same_shape_fans(shapes: Sequence[MeldShape], base_kind: str, names: dict[int, tuple[str, int]]) -> list[dict]:
    """同刻/同顺系列：按“数字形状”分组，组内取最高档（双/三/四）。

    不同分组可以复计：如 AX1-3 与 BX1-3、AX7-9 与 BX7-9 各成一组双同顺，
    则计 2 组双同顺。规则书“每一组牌至多只能计算同顺/同刻/同花一次”
    由“组内只取最高档”满足（同组的牌不会同时计入双/三/四两档）。
    """
    buckets: dict[tuple[int, ...], int] = defaultdict(int)
    for shape in shapes:
        if shape.base_kind != base_kind:
            continue
        key = tuple(sorted(HongqueTile.parse(code).number for code in shape.tiles))
        buckets[key] += 1
    entries: list[dict] = []
    for count in buckets.values():
        eligible = max((n for n in names if count >= n), default=0)
        if not eligible:
            continue
        name, value = names[eligible]
        entries.append(_entry(name, value))
    return _merge_entries(entries)


def _same_colour_layout_fans(shapes: Sequence[MeldShape]) -> list[dict]:
    """同花系列：按（长度，花色对应）分组，组内取最高档，不同分组可复计。"""
    buckets: dict[tuple[int, tuple[int, ...]], int] = defaultdict(int)
    for shape in shapes:
        ordered = _ordered_tiles(shape)
        buckets[(len(ordered), tuple(tile.colour for tile in ordered))] += 1
    entries: list[dict] = []
    for count in buckets.values():
        if count >= 4:
            entries.append(_entry("四同花", 12))
        elif count >= 3:
            entries.append(_entry("三同花", 6))
        elif count >= 2:
            entries.append(_entry("双同花", 2))
    return _merge_entries(entries)


def _consecutive_sequence_fan(shapes: Sequence[MeldShape]) -> dict | None:
    sequences = [shape for shape in shapes if shape.base_kind == "sequence"]
    best = 0
    for count in (4, 3):
        for selected in combinations(sequences, count):
            if len({len(shape.tiles) for shape in selected}) != 1:
                continue
            if len({abs(shape.number_step) for shape in selected}) != 1:
                continue
            starts = [min(HongqueTile.parse(code).number for code in shape.tiles) for shape in selected]
            if _arithmetic(starts):
                best = count
                break
        if best:
            break
    if best == 4:
        return _entry("四连顺", 6)
    if best == 3:
        return _entry("三连顺", 3)
    return None


def _dragon_fan(shapes: Sequence[MeldShape]) -> dict | None:
    sequences = [shape for shape in shapes if shape.base_kind == "sequence"]
    for count in (1, 2, 3):
        for selected in combinations(sequences, count):
            if len({abs(shape.number_step) for shape in selected}) != 1:
                continue
            numbers = [HongqueTile.parse(code).number for shape in selected for code in shape.tiles]
            if len(numbers) == 9 and sorted(numbers) == list(range(1, 10)):
                return _entry("一条龙", 3)
    return None


def score_partition(
    concealed_partition: Sequence[Sequence[str]],
    open_melds: Sequence[dict],
    *,
    pair: Sequence[str] = (),
    self_draw: bool,
    before_first_discard: bool,
    wall_empty: bool,
) -> dict:
    groups = [list(group) for group in concealed_partition] + [list(meld.get("tiles", ())) for meld in open_melds]
    shapes = [classify_meld(group) for group in groups]
    if any(shape is None for shape in shapes):
        raise ValueError("invalid group in winning partition")
    shapes = [shape for shape in shapes if shape is not None]
    pair_tiles = [HongqueTile.parse(code) for code in pair]
    if pair_tiles and (len(pair_tiles) != 2 or pair_tiles[0].number != pair_tiles[1].number):
        raise ValueError("winning pair must contain two tiles of the same number")
    tiles = [HongqueTile.parse(code) for group in groups for code in group] + pair_tiles

    base = 3 + sum(max(0, len(group) - 3) for group in groups)
    concealed = not open_melds
    if concealed:
        base += 2

    fans: list[dict] = []
    # Rulebook wording is inclusive: a 清顺 is any pure-colour sequence with
    # three or more tiles, while a 清刻 is any pure-colour triplet with four
    # or more tiles.  Count every qualifying group; these fans are repeatable.
    # 清顺：仅“花色相同”（colour_step 0）且非彩虹的顺子，按组复计。
    clean_sequences = sum(
        shape.base_kind == "sequence"
        and shape.colour_step == 0
        and not shape.is_rainbow
        for shape in shapes
    )
    clean_triplets = sum(
        shape.base_kind == "triplet" and len(shape.tiles) >= 4
        for shape in shapes
    )
    if clean_sequences:
        fans.append(_entry("清顺", 1, clean_sequences))
    if clean_triplets:
        fans.append(_entry("清刻", 1, clean_triplets))

    dragon = _dragon_fan(shapes)
    if dragon:
        fans.append(dragon)
    fans.extend(_same_shape_fans(shapes, "triplet", {2: ("双同刻", 2), 3: ("三同刻", 6), 4: ("四同刻", 12)}))
    fans.extend(_same_shape_fans(shapes, "sequence", {2: ("双同顺", 2), 3: ("三同顺", 6), 4: ("四同顺", 12)}))
    fans.extend(_same_colour_layout_fans(shapes))
    consecutive = _consecutive_sequence_fan(shapes)
    if consecutive:
        fans.append(consecutive)

    # 花色计数：
    # - 清一色/双色/三色：按“纯色覆盖”计数。纯色牌覆盖 1 个基础色；
    #   半色牌覆盖相邻 2 个基础色（如 AY=红橙 覆盖 红+橙）。因此
    #   “红+红橙+橙”（1 12 2）为两种颜色→双色；而“红橙+橙+橙黄”
    #   （2 23 4）覆盖 红/橙/黄 三种颜色→三色，不是双色。
    # - 七归一/九归一/光谱/全彩：14 级颜色各自独立计数（7 张 AX 才算
    #   七归一，AX+AY 不能合并）。
    colour_counts = Counter(tile.colour for tile in tiles)  # 14 级
    covered_colours = set()
    for tile in tiles:
        covered_colours.update(tile.primary_colours)
    max_colour_level_count = max(colour_counts.values(), default=0)
    if max_colour_level_count >= 9:
        fans.append(_entry("九归一", 6))
    elif max_colour_level_count >= 7:
        fans.append(_entry("七归一", 3))

    rainbow_count = sum(shape.is_rainbow for shape in shapes)
    if rainbow_count >= 2:
        fans.append(_entry("双虹会", 12))
    elif rainbow_count == 1:
        fans.append(_entry("彩虹", 6))

    distinct_levels = len(colour_counts)
    colour_count = len(covered_colours)
    if colour_count == 1:
        fans.append(_entry("清一色", 18))
    elif distinct_levels == 14:
        fans.append(_entry("全彩", 12))
    elif distinct_levels == len(tiles):
        fans.append(_entry("光谱", 6))
    elif colour_count == 2:
        fans.append(_entry("双色", 12))
    elif colour_count == 3:
        fans.append(_entry("三色", 6))
    if tiles and all(tile.colour % 2 == 0 for tile in tiles):
        fans.append(_entry("全纯色", 1))
    if tiles and all(tile.colour % 2 == 1 for tile in tiles):
        fans.append(_entry("全半色", 1))

    numbers = sorted({tile.number for tile in tiles})
    all_triplets = all(shape.base_kind == "triplet" for shape in shapes)
    if len(numbers) == 1:
        fans.append(_entry("清一数", 18))
    elif len(numbers) == 2:
        fans.append(_entry("二数", 12))
    elif len(numbers) == 3 and _arithmetic(numbers):
        fans.append(_entry("三数", 6))
    elif len(numbers) == 4 and _arithmetic(numbers):
        fans.append(_entry("四数", 3))
    # 全带幺：每组牌均含数字 1 或 9 的牌（按规则书“牌组”判定，而非全体手牌）。
    if groups and all(
        any(HongqueTile.parse(code).number in (1, 9) for code in group)
        for group in groups
    ):
        fans.append(_entry("全带幺", 2))

    is_heavenly = self_draw and before_first_discard and concealed
    if is_heavenly:
        fans.append(_entry("天和", 18))
    elif concealed:
        fans.append(_entry("门清", 1))
    if self_draw and wall_empty:
        fans.append(_entry("海底", 2))
    # 清一数明确“不计碰碰和”：保留清一数 18 番，排除碰碰和 3 番。
    # 清一数、二数均不计碰碰和。
    if all_triplets and len(numbers) > 2:
        fans.append(_entry("碰碰和", 3))
    # 平和：仅由顺子构成。彩虹组本质也是顺子（长顺子），不影响平和；
    # 因此按 base_kind 判断，彩虹顺子同样计入平和，彩虹刻子（刻子类）不计。
    if shapes and all(shape.base_kind == "sequence" for shape in shapes):
        fans.append(_entry("平和", 1))
    if len(groups) == 1:
        fans.append(_entry("金龙", 6))
    elif len(groups) == 2:
        fans.append(_entry("二金", 3))
    elif len(groups) == 3:
        fans.append(_entry("三金", 1))

    # 番种从大到小展示。
    fans.sort(key=lambda fan: fan["value"], reverse=True)
    fan_total = sum(fan["total"] for fan in fans)
    return {
        "partition": [list(group) for group in concealed_partition],
        "pair": list(pair),
        "groups": groups,
        "base": base,
        "fans": fans,
        "fan_total": fan_total,
        # Rulebook 1.6: a zero-fan winning hand is worth exactly one point;
        # otherwise points are base multiplied by the fan sum.
        "points": 1 if fan_total == 0 else base * fan_total,
        "concealed": concealed,
    }


def best_win_result(
    hand: Iterable[str],
    open_melds: Sequence[dict],
    *,
    self_draw: bool,
    before_first_discard: bool,
    wall_empty: bool,
    allow_kong_win: bool = False,
) -> dict | None:
    results = [
        score_partition(
            decomposition["groups"],
            open_melds,
            pair=decomposition["pair"],
            self_draw=self_draw,
            before_first_discard=before_first_discard,
            wall_empty=wall_empty,
        )
        for decomposition in winning_decompositions(hand, open_melds)
    ]
    if allow_kong_win:
        # 杠和拆解：自摸的牌可并入明牌完成和牌。按“杠后虚拟状态”计分，
        # 展示时仍保留真实手牌与明牌（由调用方决定 winning_hand/melds）。
        hand_codes = [HongqueTile.parse(code).code for code in hand]
        for candidate in kong_win_candidates(hand_codes, open_melds):
            rest = list(hand_codes)
            for code in candidate["hand_tiles"]:
                rest.remove(code)
            melds_after = [dict(meld) for meld in open_melds]
            melds_after[candidate["meld_index"]]["tiles"] = list(candidate["tiles"])
            results.extend(
                score_partition(
                    decomposition["groups"],
                    melds_after,
                    pair=decomposition["pair"],
                    self_draw=self_draw,
                    before_first_discard=before_first_discard,
                    wall_empty=wall_empty,
                )
                for decomposition in winning_decompositions(rest, melds_after)
            )
    if not results:
        return None
    return max(results, key=lambda result: (result["points"], result["fan_total"], result["base"]))
