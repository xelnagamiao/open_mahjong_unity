from __future__ import annotations

from collections import Counter
from functools import lru_cache
from itertools import combinations

from .w_rules import classify_meld, winning_partitions, kong_candidates, _subtract

FAN_VALUES = {
    "花龙": 3, "清龙": 6, "双同刻": 1, "三同刻": 3, "四同刻": 6,
    "双同顺": 1, "三同顺": 3, "四同顺": 6, "四连顺": 3,
    "清一色": 18, "混一色": 12, "清双色": 9, "混双色": 6, "三色": 3,
    "清一数": 18, "二数": 12, "三数": 6, "四数": 3, "全带幺": 2,
    "天和": 18, "海底": 2, "门清": 1, "对对和": 12, "碰碰和": 2, "平和": 1,
    "金龙": 6, "二金": 3, "三金": 1, "彩虹": 6, "双虹会": 12,
}


def _best_disjoint(candidates):
    candidates = tuple(candidates)
    @lru_cache(None)
    def solve(index, used):
        if index == len(candidates):
            return 0, ()
        best = solve(index + 1, used)
        mask, name = candidates[index]
        if not used & mask:
            score, chosen = solve(index + 1, used | mask)
            option = score + FAN_VALUES[name], (name,) + chosen
            if option > best:
                best = option
        return best
    return solve(0, 0)[1]


def _combination_fans(shapes):
    expanded = [shape for shape in shapes if shape.base_kind != "pair"
                for _ in range(2 if shape.is_double else 1)]
    numbers = [tuple(sorted(int(tile[2]) for tile in set(shape.tiles))) for shape in expanded]
    families = {"triplet": [], "sequence": [], "consecutive": [], "dragon": []}
    for kind, word in (("triplet", "刻"), ("sequence", "顺")):
        indexes = [index for index, shape in enumerate(expanded) if shape.base_kind == kind]
        for size, prefix in ((2, "双"), (3, "三"), (4, "四")):
            for group in combinations(indexes, size):
                keys = [numbers[i][0] if kind == "triplet" else numbers[i] for i in group]
                if len(set(keys)) == 1:
                    families[kind].append((sum(1 << i for i in group), f"{prefix}同{word}"))
    sequences = [index for index, shape in enumerate(expanded) if shape.base_kind == "sequence"]
    for group in combinations(sequences, 4):
        shapes_key = {(len(numbers[i]), expanded[i].number_step) for i in group}
        starts = sorted(numbers[i][0] for i in group)
        if len(shapes_key) == 1 and starts[1] > starts[0] and len({b - a for a, b in zip(starts, starts[1:])}) == 1:
            families["consecutive"].append((sum(1 << i for i in group), "四连顺"))
    for size in (1, 2, 3):
        for group in combinations(sequences, size):
            ranks = sorted(rank for i in group for rank in numbers[i])
            if ranks != list(range(1, 10)) or len({expanded[i].number_step for i in group}) != 1:
                continue
            colours = {tile[0] for i in group for tile in expanded[i].tiles}
            name = "清龙" if len(colours) == 1 else "花龙"
            families["dragon"].append((sum(1 << i for i in group), name))
    return Counter(name for options in families.values() for name in _best_disjoint(options))


def score_partition(partition, open_melds=(), *, all_pairs=False, self_draw=False,
                    heavenly=False, last_tile=False):
    groups = [list(group) for group in partition] + [list(meld["tiles"]) for meld in open_melds]
    shapes = [classify_meld(group) for group in groups]
    if not groups or any(shape is None for shape in shapes):
        return None
    fans = Counter() if all_pairs else _combination_fans(shapes)
    tiles = [tile for group in groups for tile in group]
    colours = {tile[0] for tile in tiles}
    numbers = sorted({int(tile[2]) for tile in tiles})
    coloured = colours - {"H"}
    if len(colours) == 1:
        fans["清一色"] = 1
    elif "H" in colours and len(coloured) == 1:
        fans["混一色"] = 1
    elif "H" not in colours and len(colours) == 2:
        fans["清双色"] = 1
    elif "H" in colours and len(coloured) == 2:
        fans["混双色"] = 1
    elif len(colours) == 3:
        fans["三色"] = 1
    if len(numbers) == 1:
        fans["清一数"] = 1
    elif len(numbers) == 2:
        fans["二数"] = 1
    elif len(numbers) in (3, 4) and len({b - a for a, b in zip(numbers, numbers[1:])}) == 1:
        fans["三数" if len(numbers) == 3 else "四数"] = 1
    terminal_groups = [(tile, tile) for tile in set(tiles)] if all_pairs else groups
    if all(any(tile[2] in "19" for tile in group) for group in terminal_groups):
        fans["全带幺"] = 1
    if self_draw and heavenly and not open_melds:
        fans["天和"] = 1
    elif not open_melds:
        fans["门清"] = 1
    if self_draw and last_tile:
        fans["海底"] = 1
    if all_pairs:
        fans["对对和"] = 1
    elif all(shape.base_kind == "triplet" for shape in shapes) and len(numbers) > 2:
        fans["碰碰和"] = 1
    elif all(shape.base_kind == "sequence" for shape in shapes):
        fans["平和"] = 1
    if 1 <= len(groups) <= 3:
        fans[{1: "金龙", 2: "二金", 3: "三金"}[len(groups)]] = 1
    rainbows = sum(2 if shape.is_double else 1 for shape in shapes if shape.is_rainbow)
    if rainbows:
        fans["双虹会" if rainbows >= 2 else "彩虹"] = 1
    base = 3 + sum(max(0, len(group) - 3) for group in groups)
    entries = [{"name": name, "value": value, "count": fans[name], "total": value * fans[name]}
               for name, value in FAN_VALUES.items() if fans[name]]
    fan_total = sum(fan["total"] for fan in entries)
    pair = next((group for group in partition if len(group) == 2), ()) if not all_pairs else ()
    return {"partition": [list(group) for group in partition], "pair": list(pair), "groups": groups,
            "base": base, "fans": entries, "fan_total": fan_total,
            "points": base * fan_total if fan_total else 1, "concealed": not open_melds,
            "all_pairs": all_pairs}


def score_hand(hand, open_melds=(), *, self_draw=False, heavenly=False, last_tile=False,
               allow_kong_win=False, minimum_fan=0, minimum_points=0, _seen=None):
    seen = set() if _seen is None else _seen
    state = (tuple(sorted(hand)), tuple(tuple(sorted(meld["tiles"])) for meld in open_melds))
    if state in seen:
        return None
    seen.add(state)
    results = [score_partition(groups, open_melds, all_pairs=all_pairs,
                              self_draw=self_draw, heavenly=heavenly, last_tile=last_tile)
               for groups, all_pairs in winning_partitions(hand, open_melds)]
    if allow_kong_win and self_draw:
        for candidate in kong_candidates(hand, open_melds):
            remaining = _subtract(hand, candidate["hand_tiles"])
            melds = [dict(meld) for meld in open_melds]
            melds[candidate["meld_index"]]["tiles"] = candidate["tiles"]
            result = score_hand(remaining, melds, self_draw=True, last_tile=last_tile,
                                allow_kong_win=True, minimum_fan=minimum_fan,
                                minimum_points=minimum_points, _seen=seen)
            if result is not None:
                results.append({**result, "kong_win": candidate,
                                "kong_path": [candidate, *result.get("kong_path", ())]})
    return max((result for result in results if result is not None
                and result["fan_total"] >= minimum_fan and result["points"] >= minimum_points),
               key=lambda result: (result["points"], result["fan_total"], result["base"]), default=None)


def best_win_result(hand, open_melds=(), *, self_draw=False, before_first_discard=False,
                    wall_empty=False, allow_kong_win=False, minimum_fan=0, minimum_points=0):
    return score_hand(hand, open_melds, self_draw=self_draw, heavenly=before_first_discard,
                      last_tile=wall_empty, allow_kong_win=allow_kong_win,
                      minimum_fan=minimum_fan, minimum_points=minimum_points)
