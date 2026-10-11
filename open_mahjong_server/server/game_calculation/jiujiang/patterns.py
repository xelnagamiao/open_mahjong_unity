"""规则书的牌型和大胡表；不在此推断行牌条件或输家支付资格。"""
from collections import Counter
from functools import lru_cache

RULE = "jiujiang"
SUB_RULE = "jiujiang/standard"
NUMBERS = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10))
DRAGONS = (45, 46)
TILES = NUMBERS + DRAGONS
FA = 47
WALL_TILES = NUMBERS + (45, FA, 46)


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "skgG":
        raise ValueError("非法面子")
    tile = int(code[1:])
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError("非法顺子")
        return (tile - 1, tile, tile + 1)
    if tile not in TILES:
        raise ValueError("发财和风牌不能组成面子")
    return (tile,) * (4 if code[0] in "gG" else 3)


def valid_tiles(hand, melds=()):
    if len(hand) + 3 * len(melds) != 14 or len(melds) > 4:
        return None
    if any(type(tile) is not int or tile not in TILES for tile in hand):
        return None
    try:
        tiles = list(hand) + [tile for code in melds for tile in meld_tiles(code)]
    except (ValueError, TypeError):
        return None
    return tiles if all(n <= 4 for n in Counter(tiles).values()) else None


@lru_cache(maxsize=32768)
def _sets(tiles):
    if not tiles:
        return ((),)
    first, result = tiles[0], []
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
        if count < 2:
            continue
        rest = sorted(hand)
        rest.remove(pair)
        rest.remove(pair)
        result.extend((pair, sets) for sets in _sets(tuple(rest)))
    return tuple(result)


def is_seven_pairs(hand, melds=()):
    return not melds and valid_tiles(hand, melds) is not None and all(n % 2 == 0 for n in Counter(hand).values())


@lru_cache(maxsize=16384)
def structural_waits(hand, melds=()):
    if len(hand) + 3 * len(melds) != 13:
        return ()
    return tuple(tile for tile in TILES if decompositions(hand + (tile,), melds)
                 or is_seven_pairs(hand + (tile,), melds))


@lru_cache(maxsize=8192)
def _chain_bonus(groups, luxury):
    """对子/刻子只能进入一个豪华或连续组，按总分择高。"""
    if not groups:
        return ()
    first, candidates = groups[0], [_chain_bonus(groups[1:], luxury)]
    if luxury and groups.count(first) >= 2:
        rest = list(groups)
        rest.remove(first)
        rest.remove(first)
        candidates.append((("豪华", 2),) + _chain_bonus(tuple(rest), luxury))
    if first in NUMBERS:
        for length in range(3, 11 - first % 10):
            run = tuple(range(first, first + length))
            if any(tile not in groups for tile in run):
                break
            rest = list(groups)
            for tile in run:
                rest.remove(tile)
            bonus = (("潇洒", 2),) + (("带滚", 1),) * (length - 3)
            candidates.append(bonus + _chain_bonus(tuple(rest), luxury))
    return max(candidates, key=lambda items: sum(value for _, value in items))


def big_hu_candidates(hand, melds=()):
    tiles = valid_tiles(hand, melds)
    if tiles is None:
        return ()
    suits = {tile // 10 for tile in tiles if tile in NUMBERS}
    common = []
    if len(suits) == 1:
        common = [("混一色", 1)] if any(tile in DRAGONS for tile in tiles) else [("清一色", 4)]
    result = []
    for pair, sets in decompositions(hand, melds):
        all_sets, fans = tuple(melds) + sets, list(common)
        for suit in (1, 2, 3):
            if {f"s{suit * 10 + r}" for r in (2, 5, 8)}.issubset(all_sets):
                fans.append(("清龙", 2))
        if all(code[0] != "s" for code in all_sets):
            fans.append(("碰碰胡", 2))
            fans.extend(_chain_bonus(tuple(sorted(int(code[1:]) for code in all_sets)), False))
        result.append({"pair": pair, "sets": sets, "seven_pairs": False, "big_fans": fans,
                       "big_points": sum(value for _, value in fans)})
    if is_seven_pairs(hand, melds):
        groups = tuple(sorted(tile for tile, count in Counter(hand).items() for _ in range(count // 2)))
        fans = common + [("七对", 4)] + list(_chain_bonus(groups, True))
        if len(set(groups)) == 7 and groups[0] in NUMBERS and groups[-1] == groups[0] + 6 and len(suits) == 1:
            fans.append(("连七对", 6))
        result.append({"pair": None, "sets": (), "seven_pairs": True, "big_fans": fans,
                       "big_points": sum(value for _, value in fans)})
    return tuple(result)
