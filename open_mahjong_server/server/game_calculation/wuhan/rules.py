from collections import Counter
from copy import deepcopy
from functools import lru_cache

from ..jokers import TILES, NUMBERS, JokerPolicy, iter_winning_shapes

VERSION = "mil2025"
SUB_RULE = "wuhan/mil2025"
FENG = (41, 42, 43, 44, 47, 46)
JIANG = tuple(t for t in NUMBERS if t % 10 in (2, 5, 8))
RED = 45
POINTS = {6: 10, 7: 20, 8: 40, 9: 60, 10: 80}


def normalize_config(value):
    if value is None:
        return {"rule_version": VERSION}
    if not isinstance(value, dict) or set(value) - {"rule_version"} or value.get("rule_version", VERSION) != VERSION:
        raise ValueError("武汉麻将仅支持 MIL 2025 标准规")
    return {"rule_version": VERSION}


def special_tiles(indicator):
    if type(indicator) is not int or indicator not in TILES:
        raise ValueError("无效的武汉癞子指示牌")
    if indicator < 40:
        base, rank = divmod(indicator, 10)
        joker = base * 10 + rank % 9 + 1
        previous = base * 10 + (rank - 2) % 9 + 1
        return joker, (previous, indicator, RED)
    effective = 44 if indicator == RED else indicator
    index = FENG.index(effective)
    return FENG[(index + 1) % 6], tuple(dict.fromkeys((FENG[(index - 1) % 6], effective, RED)))


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or not code[1:].isdigit():
        return ()
    tile = int(code[1:])
    if tile not in TILES:
        return ()
    if code[0] == "s" and tile < 40 and 2 <= tile % 10 <= 8:
        return (tile - 1, tile, tile + 1)
    if code[0] in "kgG":
        return (tile,) * (3 if code[0] == "k" else 4)
    return ()


def open_count(melds):
    return sum(code[0] != "G" for code in melds)


def valid_hand(hand, melds, indicator, *, complete=True):
    if len(melds) > 4 or len(hand) + 3 * len(melds) != (14 if complete else 13):
        return False
    if any(type(t) is not int or t not in TILES for t in hand):
        return False
    joker, skins = special_tiles(indicator)
    counts = Counter(hand)
    for code in melds:
        tiles = meld_tiles(code)
        if not tiles or any(t == joker or t in skins for t in tiles):
            return False
        counts.update(tiles)
    return max(counts.values(), default=0) <= 4


@lru_cache(maxsize=64)
def policy(indicator):
    joker, skins = special_tiles(indicator)
    return JokerPolicy(joker_tiles=(joker,), physical_tiles=TILES,
        logical_tiles=tuple(t for t in TILES if t not in skins), closed_cap=4)


def _special_shapes(hand, melds, joker, skins):
    exposed = [t for code in melds for t in meld_tiles(code)]
    ordinary = [t for t in hand if t != joker] + exposed
    for name, domain in (("风一色", FENG), ("将一色", JIANG)):
        targets = [t for t in domain if t not in skins]
        if targets and all(t in targets for t in ordinary):
            natural = joker in targets or joker not in hand
            logical = [t if t != joker or natural else targets[0] for t in hand]
            yield dict(fan_names=[name], hard=natural, logical_hand=logical,
                       pair=0, pair_seed=True, kind=name)


@lru_cache(maxsize=8192)
def _shapes(hand, melds, indicator):
    if not valid_hand(hand, melds, indicator):
        return ()
    joker, skins = special_tiles(indicator)
    if any(t in skins for t in hand):
        return ()
    candidates = list(_special_shapes(hand, melds, joker, skins))
    seen = set()
    for shape in iter_winning_shapes(hand, melds, policy(indicator)):
        fans = []
        if all(group.kind in ("triplet", "kong") for group in shape.melds):
            fans.append("碰碰和")
        if all(t < 40 for t in shape.logical_tiles) and len({t // 10 for t in shape.logical_tiles}) == 1:
            fans.append("清一色")
        if all(t in JIANG for t in shape.logical_tiles):
            fans.append("将一色")
        if all(t in FENG for t in shape.logical_tiles):
            fans.append("风一色")
        pair = next(group for group in shape.groups if group.kind == "pair")
        seeded = shape.pair in JIANG and any(hand[i] == shape.pair for i in pair.indices)
        key = (tuple(fans), shape.is_natural, seeded)
        if key in seen:
            continue
        seen.add(key)
        candidates.append(dict(fan_names=fans, hard=shape.is_natural,
            logical_hand=list(shape.closed_logical_tiles), pair=shape.pair,
            pair_seed=seeded, kind="standard"))
    return tuple(candidates)


def shapes(hand, melds, indicator):
    return deepcopy(_shapes(tuple(sorted(hand)), tuple(melds), indicator))


@lru_cache(maxsize=8192)
def _waits(hand, melds, indicator):
    if not valid_hand(hand, melds, indicator, complete=False):
        return ()
    counts = Counter(hand)
    for code in melds:
        counts.update(meld_tiles(code))
    return tuple(tile for tile in TILES if counts[tile] < 4 and _shapes(tuple(sorted(hand + (tile,))), melds, indicator))


def waits(hand, melds, indicator):
    return set(_waits(tuple(sorted(hand)), tuple(melds), indicator))


def hand_detail(hand, melds, indicator, *, replacement=False, sea=False, robbing=False):
    joker, _ = special_tiles(indicator)
    candidates = []
    for shape in shapes(hand, melds, indicator):
        names = list(shape["fan_names"])
        if not names and not shape["pair_seed"]:
            continue
        names += [name for flag, name in ((replacement, "杠上花"), (sea, "海底"), (robbing, "抢杠")) if flag]
        if hand.count(joker) >= 2 and not names:
            continue
        gold = "风一色" in names
        big = bool(names)
        candidates.append({**shape, "fan_names": names + (["硬和"] if shape["hard"] else []),
            "big": big, "gold": gold, "shape_fan": (len(names) + 2 if names else 0) + int(shape["hard"])})
    return max(candidates, key=lambda d: (d["gold"], d["shape_fan"]), default=None)


def table_fan(melds, specials, joker, added=()):
    total = open_count(melds)
    for code in melds:
        if code[0] == "G":
            total += 2
        elif code[0] == "g":
            total += 1 if int(code[1:]) in added else 2
    # 原书未列普通皮子杠的番值；只计明文规定的红中与癞子杠。
    return total + sum(2 if t == joker else 1 if t == RED else 0 for t in specials)


def kong_units(melds, specials, joker):
    return sum(2 if code[0] == "G" else 1 if code[0] == "g" else 0 for code in melds) + sum(2 if t == joker else 1 for t in specials)


def point_value(fan):
    return POINTS.get(min(fan, 10), 0)


def settlement(detail, winner, payer, table, indicator, *, previous_gold=None, liabilities=()):
    joker, _ = special_tiles(indicator)
    own = table[winner]
    fan = {}
    for index, row in enumerate(table):
        if index == winner:
            continue
        fan[index] = (table_fan(own["melds"], own["specials"], joker, own.get("added", ()))
            + table_fan(row["melds"], row["specials"], joker, row.get("added", ())) + detail["shape_fan"]
            + int(not detail["big"] and (winner == 0 or index == 0))
            + int(payer is None or index == payer))
    if not detail["gold"] and min(fan.values()) < 6:
        return None
    gold = detail["gold"] or min(fan.values()) >= 10
    unopened = [i for i, row in enumerate(table) if i != winner and not open_count(row["melds"])
                and not row["specials"] and not any(c[0] == "G" for c in row["melds"])]
    pays = {}
    for index in fan:
        multiplier = 2 if joker in FENG or joker in JIANG or gold and previous_gold in (winner, index) else 1
        penalty = 4 if len(unopened) == 3 else 2 if index in unopened else 1
        pays[index] = (100 if gold else point_value(fan[index])) * multiplier * penalty
    total = sum(pays.values())
    liable = sorted(set(liabilities) - {winner})
    if liable:
        part, extra = divmod(total, len(liable))
        pays = {i: part + int(n < extra) for n, i in enumerate(liable)}
    changes = {i: -pays.get(i, 0) for i in range(4)}
    changes[winner] = total
    return dict(changes=changes, fan_by_player=fan, payments=pays, gold=gold,
                total=total, liabilities=liable, fan=min(fan.values()), fan_names=detail["fan_names"])
