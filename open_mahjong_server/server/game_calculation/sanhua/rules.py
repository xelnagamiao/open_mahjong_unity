from collections import Counter
from functools import lru_cache

from ..jokers import JokerPolicy, ORPHANS, TILES, iter_winning_shapes
from .wall import RED, special_tiles

SUB_RULE = "new_chinese/sanhua"
VERSION = "sanhua-2026-11"
PAIRS = ("七对", "豪华七对", "双豪华七对", "三豪华七对")
VALUES = {"小屁和": 0, "杠上开花": 1, "硬和": 1, **dict(zip(PAIRS, range(3, 7))),
          "碰碰和": 3, "清一色": 3, "将一色": 3, "全求人": 3, "十三幺": 10,
          "明杠": 1, "暗杠": 2, "补杠": 1, "红中杠": 1, "癞子杠": 2, "皮子杠": 1, "开口番": 0.5}


def table_fans(melds, specials, joker, added=()):
    result = []
    for code in melds:
        if code.startswith("G"):
            result.append("暗杠")
        elif code.startswith("g"):
            result.append("补杠" if int(code[1:]) in added else "明杠")
    result.extend("红中杠" if t == RED else "癞子杠" if t == joker else "皮子杠" for t in specials)
    if melds:
        result.append("开口番")
    return result


@lru_cache(maxsize=8192)
def _variants(hand, melds, indicator):
    skin, joker = special_tiles(indicator)
    if len(hand) + 3 * len(melds) != 14 or any(t not in TILES for t in hand):
        return ()
    counts = Counter(hand)
    if max(counts.values(), default=0) > 4:
        return ()
    variants = set()
    if not melds and all(t in ORPHANS or t == joker for t in hand):
        natural = Counter(t for t in hand if t != joker)
        for pair in ORPHANS:
            target = Counter(ORPHANS)
            target[pair] += 1
            if natural <= target:
                variants.add(("十三幺", False, False, False, False))
                break
    if skin in counts or RED in counts:
        return tuple(variants)
    domain = tuple(t for t in TILES if t not in (skin, RED))
    policy = JokerPolicy(joker_tiles=(joker,), logical_tiles=domain, shapes=("standard", "seven_pairs"), closed_cap=4)
    for shape in iter_winning_shapes(hand, melds, policy, equivalence="logical_structure"):
        logical = shape.logical_tiles
        pure = all(t < 40 and t // 10 == logical[0] // 10 for t in logical)
        jiang = all(t < 40 and t % 10 in (2, 5, 8) for t in logical)
        if shape.kind == "seven_pairs":
            name = PAIRS[min(3, sum(n == 4 for n in Counter(hand).values()))]
            variants.add((name, pure, jiang, False, False))
        else:
            pung = all(g.kind in ("triplet", "kong") for g in shape.melds)
            eye = shape.pair < 40 and shape.pair % 10 in (2, 5, 8)
            variants.add(("standard", pure, jiang, pung, eye))
    return tuple(sorted(variants))


def variants(hand, melds, indicator):
    return _variants(tuple(sorted(hand)), tuple(melds), indicator)


def score(hand, melds, indicator, winner, payer, table, *, declared=False, liable=None, replacement=False, rob=False):
    """Price each legal interpretation against the three actual seats."""
    _, joker = special_tiles(indicator)
    best = None
    opened = "开口番" in table[winner]
    for kind, pure, jiang, pung, eye in variants(hand, melds, indicator):
        if declared and not pure or rob and kind != "十三幺":
            continue
        if kind == "十三幺":
            return dict(fan_names=[kind], pair_fans=[0 if i == winner else 10 for i in range(4)],
                        changes=[600 if i == winner else -200 for i in range(4)], points=600, gold=False)
        names = [] if kind == "standard" else [kind]
        if pung and opened:
            names.append("碰碰和")
        if pure and (declared or len(melds) < 2):
            names.append("清一色")
        if jiang:
            names.append("将一色")
        if payer is not None and len(melds) == 4 and all(m[0] != "G" for m in melds) and (pung or eye):
            names.append("全求人")
        big = bool(names)
        if not big:
            if not opened or not eye or hand.count(joker) > 1:
                continue
            names.append("小屁和")
        if replacement and payer is None:
            names.append("杠上开花")
        if joker not in hand:
            names.append("硬和")
        names.extend(table[winner])
        own = sum(VALUES[n] for n in names)
        pairs = [0 if i == winner else min(6, own + sum(VALUES[n] for n in row)) for i, row in enumerate(table)]
        if (max(pairs) if payer is None else pairs[payer]) < 4:
            continue
        gold = (payer is None or big) and all(f >= 6 for i, f in enumerate(pairs) if i != winner)
        changes = [0] * 4
        for i, fan in enumerate(pairs):
            if i == winner or payer is not None and not big and payer != i:
                continue
            points = 60 if gold else 40 if fan >= 6 else 20 if fan >= 5 else 10 if fan >= 4 else 0
            points *= 1 + int(i == liable and declared) + int(i == payer and "全求人" in names)
            changes[i] = -points
        changes[winner] = -sum(changes)
        result = dict(fan_names=names, pair_fans=pairs, changes=changes, points=changes[winner], gold=gold)
        if best is None or (result["points"], own) > best[:2]:
            best = (result["points"], own, result)
    return best[2] if best else None
