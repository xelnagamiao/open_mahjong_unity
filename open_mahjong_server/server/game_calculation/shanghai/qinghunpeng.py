"""《上海清混碰规则》：起和番、花数和承包结算，与敲麻独立。"""

from collections import Counter
from functools import lru_cache

NUMBERS = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10))
WINDS = (41, 42, 43, 44)
DRAGONS = (45, 46, 47)
HONORS = WINDS + DRAGONS
TILES = NUMBERS + HONORS
FLOWERS = tuple(range(51, 59))
SUB_RULE = "shanghai/qinghunpeng"


def meld_tiles(code):
    if not isinstance(code, str) or len(code) < 3 or code[0] not in "skgG":
        raise ValueError("非法面子")
    tile = int(code[1:])
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError("非法顺子")
        return (tile - 1, tile, tile + 1)
    if tile not in TILES:
        raise ValueError("花牌不能组成面子")
    return (tile,) * (4 if code[0] in "gG" else 3)


def valid_tiles(hand, melds):
    if len(hand) + 3 * len(melds) != 14 or len(melds) > 4:
        return None
    if any(type(tile) is not int or tile not in TILES for tile in hand):
        return None
    try:
        tiles = list(hand) + [tile for code in melds for tile in meld_tiles(code)]
    except (ValueError, TypeError):
        return None
    return None if any(count > 4 for count in Counter(tiles).values()) else tiles


@lru_cache(maxsize=32768)
def _sets(tiles):
    if not tiles:
        return ((),)
    first = tiles[0]
    result = []
    if tiles.count(first) >= 3:
        rest = list(tiles)
        for _ in range(3):
            rest.remove(first)
        result.extend((f"k{first}",) + tail for tail in _sets(tuple(rest)))
    if first in NUMBERS and first % 10 <= 7 and first + 1 in tiles and first + 2 in tiles:
        rest = list(tiles)
        for tile in (first, first + 1, first + 2):
            rest.remove(tile)
        result.extend((f"s{first + 1}",) + tail for tail in _sets(tuple(rest)))
    return tuple(result)


def decompositions(hand, melds=()):
    if valid_tiles(hand, melds) is None:
        return ()
    result = []
    for pair, count in Counter(hand).items():
        if count >= 2:
            rest = sorted(hand)
            rest.remove(pair)
            rest.remove(pair)
            result.extend((pair, sets) for sets in _sets(tuple(rest)))
    return tuple(result)


def starting_pattern(tiles, sets, *, unstructured=False):
    all_pungs = not unstructured and all(code[0] != "s" for code in sets)
    suits = {tile // 10 for tile in tiles if tile in NUMBERS}
    if not suits:
        return ("全风向", 40) if all_pungs else ("乱风向", 20)
    if len(suits) == 1:
        mixed = any(tile in HONORS for tile in tiles)
        if all_pungs:
            return ("混碰", 10) if mixed else ("清碰", 20)
        return ("混一色", 1) if mixed else ("清一色", 10)
    return ("碰碰和", 1) if all_pungs else (None, 0)


def waits(hand, melds=()):
    """只返回具备起和番的听牌；1花牌保留为仅自摸候选。"""
    if len(hand) + 3 * len(melds) != 13:
        return set()
    return {tile for tile in TILES if score(list(hand) + [tile], melds, self_draw=True)}


def melds_allow_starting_pattern(melds):
    """公开顺子与多种数牌花色不能同时存在；否则至少仍可做清、混或碰。"""
    try:
        tiles = [tile for code in melds for tile in meld_tiles(code)]
    except (ValueError, TypeError):
        return False
    return (all(code[0] != "s" for code in melds)
            or len({tile // 10 for tile in tiles if tile in NUMBERS}) <= 1)


def physical_flower_count(flowers, melds=(), concealed_sets=()):
    total = len(flowers)
    for code in tuple(melds) + tuple(concealed_sets):
        kind, tile = code[0], int(code[1:])
        if kind in "kgG":
            total += 2 if tile in DRAGONS else 1 if tile in WINDS else 0
            total += 2 if kind == "G" else 1 if kind == "g" else 0
    return total


def score(hand, melds=(), flowers=(), *, self_draw=False, replacement=False,
          last_tile=False, rob_kong=False):
    tiles = valid_tiles(hand, melds)
    if tiles is None:
        return None
    decomposed = list(decompositions(hand, melds))
    if all(tile in HONORS for tile in tiles):
        # 乱风向不要求四面子一将；即使全为对子也按乱风向，不开放七对番。
        decomposed.append((None, None))
    extras = []
    if len(melds) == 4 and all(code[0] != "G" for code in melds):
        extras.append("大吊车")
    if self_draw and replacement:
        extras.append("杠头开花")
    if last_tile:
        extras.append("海底捞月")
    if rob_kong:
        extras.append("抢杠")
    candidates = []
    for pair, sets in decomposed:
        pattern, base = starting_pattern(tiles, tuple(melds) + (sets or ()), unstructured=sets is None)
        if not base:
            continue  # 偶然番不能单独起和。
        physical = physical_flower_count(flowers, melds, sets or ())
        points = max(base, 10) if extras else base
        points = points if points >= 10 else min(10, points + physical)
        if points == 1 and not self_draw:
            continue
        labels = [pattern] + extras
        if points < 10 or (base == 1 and not extras):
            labels.append(f"花数（{physical}花）")
        if base == 1 and not extras and points == 10:
            labels.append("勒子封顶")
        candidates.append({"is_win": True, "base_score": points, "flowers": points,
                           "base_flowers": base, "physical_flowers": physical,
                           "fan_names": labels, "pair": pair, "concealed_sets": list(sets or ())})
    return max(candidates, key=lambda item: (item["base_score"], item["base_flowers"]), default=None)


def payments(winner, points, *, discarder=None, partners=(), rob_kong=False):
    partners = set(partners) - {winner}
    charges = {index: points for index in range(4) if index != winner} if discarder is None else {
        discarder: points * (3 if rob_kong else 1)
    }
    # 抢杠时每个承包家另付三份；杠者兼承包家则共付六份。
    contract_shares = 3 if rob_kong else (2 if discarder is None else 1)
    for partner in partners:
        charges[partner] = charges.get(partner, 0) + points * contract_shares
    result = {index: 0 for index in range(4)}
    for payer, charge in charges.items():
        result[payer] -= charge
        result[winner] += charge
    return result
