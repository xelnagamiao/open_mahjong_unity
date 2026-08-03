from __future__ import annotations

from collections import Counter, defaultdict
from itertools import combinations
from typing import Iterable, Sequence

from .rules import MeldShape, classify_meld
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


def _same_shape_fan(shapes: Sequence[MeldShape], base_kind: str, names: dict[int, tuple[str, int]]) -> dict | None:
    buckets: dict[tuple[int, ...], int] = defaultdict(int)
    for shape in shapes:
        if shape.base_kind != base_kind:
            continue
        key = tuple(sorted(HongqueTile.parse(code).number for code in shape.tiles))
        buckets[key] += 1
    maximum = max(buckets.values(), default=0)
    eligible = max((count for count in names if maximum >= count), default=0)
    if not eligible:
        return None
    name, value = names[eligible]
    return _entry(name, value)


def _same_colour_layout_fan(shapes: Sequence[MeldShape]) -> dict | None:
    buckets: dict[tuple[int, tuple[int, ...]], int] = defaultdict(int)
    for shape in shapes:
        ordered = _ordered_tiles(shape)
        buckets[(len(ordered), tuple(tile.colour for tile in ordered))] += 1
    maximum = max(buckets.values(), default=0)
    if maximum >= 4:
        return _entry("四同花", 12)
    if maximum >= 3:
        return _entry("三同花", 6)
    if maximum >= 2:
        return _entry("双同花", 2)
    return None


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
    clean_sequences = sum(shape.base_kind == "sequence" and len(shape.tiles) >= 4 for shape in shapes)
    clean_triplets = sum(shape.base_kind == "triplet" and len(shape.tiles) >= 4 for shape in shapes)
    if clean_sequences:
        fans.append(_entry("清顺", 1, clean_sequences))
    if clean_triplets:
        fans.append(_entry("清刻", 1, clean_triplets))

    dragon = _dragon_fan(shapes)
    if dragon:
        fans.append(dragon)
    same_triplet = _same_shape_fan(shapes, "triplet", {2: ("双同刻", 2), 3: ("三同刻", 6), 4: ("四同刻", 12)})
    if same_triplet:
        fans.append(same_triplet)
    same_sequence = _same_shape_fan(shapes, "sequence", {2: ("双同顺", 2), 3: ("三同顺", 6), 4: ("四同顺", 12)})
    if same_sequence:
        fans.append(same_sequence)
    same_colour = _same_colour_layout_fan(shapes)
    if same_colour:
        fans.append(same_colour)
    consecutive = _consecutive_sequence_fan(shapes)
    if consecutive:
        fans.append(consecutive)

    colour_counts = Counter(tile.colour for tile in tiles)
    max_colour_count = max(colour_counts.values(), default=0)
    if max_colour_count == 9:
        fans.append(_entry("九归一", 6))
    elif max_colour_count in (7, 8):
        fans.append(_entry("七归一", 3))

    rainbow_count = sum(shape.is_rainbow for shape in shapes)
    if rainbow_count >= 2:
        fans.append(_entry("双虹会", 12))
    elif rainbow_count == 1:
        fans.append(_entry("彩虹", 6))

    distinct_colours = set(colour_counts)
    all_fourteen = len(distinct_colours) == 14
    if all_fourteen:
        fans.append(_entry("全彩", 12))
    elif len(distinct_colours) == len(tiles):
        fans.append(_entry("光谱", 6))
    elif len(distinct_colours) == 2:
        fans.append(_entry("双色", 12))
    elif len(distinct_colours) == 3:
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
    if numbers and set(numbers).issubset({1, 9}):
        fans.append(_entry("全带幺", 2))

    is_heavenly = self_draw and before_first_discard and concealed
    if is_heavenly:
        fans.append(_entry("天和", 18))
    elif concealed:
        fans.append(_entry("门清", 1))
    if self_draw and wall_empty:
        fans.append(_entry("海底", 2))
    # 清一数明确“不计碰碰和”：保留清一数 18 番，排除碰碰和 3 番。
    if all_triplets and len(numbers) != 1:
        fans.append(_entry("碰碰和", 3))
    if shapes and all(shape.base_kind == "sequence" for shape in shapes):
        fans.append(_entry("平和", 1))
    if len(groups) == 1:
        fans.append(_entry("金龙", 6))
    elif len(groups) == 2:
        fans.append(_entry("二金", 3))
    elif len(groups) == 3:
        fans.append(_entry("三金", 1))

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
    if not results:
        return None
    return max(results, key=lambda result: (result["points"], result["fan_total"], result["base"]))
