"""上海垃圾和：基础番择高、花分、七对子及全风向。

四张相同牌可作两对，大吊车包含暗杠；自摸默认 +5/+10，可选再加一底（5分）。
"""
from collections import Counter

from .qinghunpeng import NUMBERS, HONORS, TILES, FLOWERS, valid_tiles, decompositions

SUB_RULE = "shanghai/lajihe"
SELF_DRAW_EXTRA_BASE = "self_draw_extra_base"


def flower_points(count):
    if count < 3:
        return 0
    if count < 5:
        return 5
    if count < 7:
        return 10
    return (count - 4) * 5


def physical_flowers(flowers, melds, sets):
    total = len(flowers)
    for code in tuple(melds) + tuple(sets):
        kind, tile = code[0], int(code[1:])
        if kind in "kgG":
            total += int(tile in HONORS) + (2 if kind == "G" else 1 if kind == "g" else 0)
    return total


def score(hand, melds=(), flowers=(), *, self_draw=False, replacement=False, rob_kong=False,
          self_draw_extra_base=False):
    tiles = valid_tiles(hand, melds)
    if tiles is None:
        return None
    candidates = []
    honors = all(tile in HONORS for tile in tiles)
    suits = {tile // 10 for tile in tiles if tile in NUMBERS}
    mixed = any(tile in HONORS for tile in tiles)
    for pair, sets in decompositions(hand, melds):
        pungs = all(code[0] != "s" for code in tuple(melds) + sets)
        if honors:
            pattern, base, fan = "全风向碰碰和", 20, 2
        elif len(suits) == 1 and pungs:
            pattern, base, fan = ("混碰", 25, 0) if mixed else ("清碰", 20, 1)
        elif len(suits) == 1:
            pattern, base, fan = ("混一色", 20, 0) if mixed else ("清一色", 25, 0)
        elif pungs:
            pattern, base, fan = "碰碰和", 20, 0
        else:
            pattern, base, fan = "垃圾和", 5, 0
        candidates.append((pattern, base, fan, pair, sets))
    if not melds and all(count % 2 == 0 for count in Counter(hand).values()):
        candidates.append(("七对子", 20, 0, None, ()))
    if honors:
        # 全风向无需四面子一将；手中实际三张字牌仍计刻子花。
        sets = tuple(f"k{tile}" for tile, count in Counter(hand).items() if count >= 3)
        candidates.append(("全风向", 20, 1, None, sets))
    results = []
    for pattern, base, fan, pair, sets in candidates:
        count = physical_flowers(flowers, melds, sets)
        garbage = pattern == "垃圾和"
        # 抢杠按自摸处理，不受垃圾和的点和花数门槛限制。
        if garbage and count < 2 and not (self_draw or rob_kong):
            continue
        labels = [pattern]
        extra = 0
        if rob_kong or self_draw:
            value = (5 if garbage else 10) + (5 if self_draw_extra_base else 0)
            extra += value
            labels.append(f"{'抢杠' if rob_kong else '自摸'}（{value}分）")
        if len(melds) == 4:
            extra += 5
            labels.append("大吊车")
        if self_draw and replacement and not rob_kong:
            extra += 5
            labels.append("杠头开花")
        added = flower_points(count)
        labels.append(f"花数（{count}花）")
        labels.append(f"花分（{added}分）")
        results.append({"is_win": True, "base_score": (base + extra + added) * (1 << fan),
                        "basic_points": base, "extra_points": extra, "flower_points": added,
                        "fan": fan, "physical_flowers": count, "fan_names": labels,
                        "pattern": pattern, "pair": pair, "concealed_sets": list(sets)})
    return max(results, key=lambda item: (item["base_score"], item["basic_points"] * (1 << item["fan"])), default=None)


def waits(hand, melds=()):
    if len(hand) + 3 * len(melds) != 13:
        return set()
    return {tile for tile in TILES if score(list(hand) + [tile], melds, self_draw=True)}


def payments(winner, points, *, discarder=None, rob_kong=False):
    changes = {index: 0 for index in range(4)}
    payers = [discarder] if discarder is not None else [index for index in range(4) if index != winner]
    for payer in payers:
        amount = points * (3 if rob_kong else 1)
        changes[payer] -= amount
        changes[winner] += amount
    return changes
