"""换六张算牌：先确认手牌能胡，再累计牌型和胡牌方式的大胡数。"""
from collections import Counter

from .changsha_hepai_check import (
    VALID_TILES, BIG_HU_WEIGHTS, _expand_all, _expand_meld,
    _is_standard_shape, _is_all_triplets, _seven_pairs_type,
    _is_flush, _is_quanqiuren, _append_context_fans,
)


TERMINALS = frozenset((11, 19, 21, 29, 31, 39))
JIANG_TILES = frozenset(tile for tile in VALID_TILES if tile % 10 in (2, 5, 8))
WAY_FANS = frozenset(("天胡", "地胡", "海底", "杠上开花", "杠上炮", "抢杠胡"))


def big_weight(fans):
    return sum(BIG_HU_WEIGHTS.get(fan, 2 if fan in ("无对十三将", "全幺九") else 1)
               for fan in fans if fan != "小胡")


def base_score(weight):
    return 2 if weight == 0 else 8 * 2 ** (weight - 1)


def hand_fans(hand, melds):
    """先检查实际牌数和每种最多四张；拆牌时，一个杠按一组面子计算。"""
    if len(melds) > 4 or len(hand) != 14 - 3 * len(melds):
        return []
    if any(not meld or meld[0] not in ("s", "S", "k", "K", "g", "G")
           or not _expand_meld(meld) for meld in melds):
        return []
    tiles = _expand_all(hand, melds)
    counts = Counter(tiles)
    if any(tile not in VALID_TILES or count > 4 for tile, count in counts.items()):
        return []
    fans = []
    shape = _is_standard_shape(hand, melds)
    pairs = _seven_pairs_type(hand, melds)
    if (shape or pairs != "none") and _is_flush(tiles):
        fans.append("清一色")
    if _is_all_triplets(hand, melds):
        fans.append("碰碰胡")
    if _is_quanqiuren(hand, melds) and all(meld[0] in ("s", "k", "g") for meld in melds):
        fans.append("全求人")
    if counts and all(tile in JIANG_TILES for tile in counts):
        fans.append("将将胡")
    pair_name = {"normal": "七小对", "luxury": "豪华七小对",
                 "double_luxury": "双豪华七小对", "triple_luxury": "三豪华七小对"}.get(pairs)
    if pair_name:
        fans.append(pair_name)
    if not melds and len(hand) == 14 and all(counts.get(tile) == 1 for tile in TERMINALS):
        remaining = [tile for tile in hand if tile not in TERMINALS]
        if all(tile in JIANG_TILES for tile in remaining):
            if len(set(remaining)) == 7:
                fans.append("十三将")
            elif len(set(remaining)) == 8:
                fans.append("无对十三将")
    if counts and all(tile in TERMINALS for tile in counts):
        fans.append("全幺九")
    return fans


def evaluate(hand, melds, ways=(), *, reported_ready=False):
    if len(hand) != 14 - 3 * len(melds):
        return 0, []
    if any(not meld or meld[0] not in ("s", "S", "k", "K", "g", "G")
           or not _expand_meld(meld) for meld in melds):
        return 0, []
    all_tiles = _expand_all(hand, melds)
    if any(tile not in VALID_TILES or count > 4 for tile, count in Counter(all_tiles).items()):
        return 0, []
    fans = hand_fans(hand, melds)
    shape = _is_standard_shape(hand, melds)
    # 地胡、海底等只加大胡数，不能让非二五八将的小胡变得可胡。
    if not fans and not _is_standard_shape(hand, melds, jiang_pair=True):
        if not (reported_ready and shape):
            return 0, []
    if not fans:
        fans = ["小胡"]
    _append_context_fans(fans, True, list(ways))
    if len(fans) > 1 and "小胡" in fans:
        fans.remove("小胡")
    return base_score(big_weight(fans)), fans


def waiting_tiles(hand, melds, *, reported_ready=False):
    if len(hand) != 13 - 3 * len(melds):
        return set()
    counts = Counter(_expand_all(hand, melds))
    return {tile for tile in VALID_TILES if counts[tile] < 4
            and evaluate(hand + [tile], melds, reported_ready=reported_ready)[0] > 0}


def group_kong_wins(results, kong_fan):
    """完整手牌牌型相同的放一组，不同的分别算；保留公开牌原来的顺序。"""
    groups = {}
    for tile, fans in results:
        patterns = tuple(sorted(fan for fan in fans if fan not in WAY_FANS and fan != "小胡"))
        context = tuple(fan for fan in fans if fan in WAY_FANS and fan != kong_fan)
        key = (patterns, context)
        if key not in groups:
            groups[key] = {"patterns": list(patterns), "context": list(context), "tiles": [], "fans": list(patterns) + list(context)}
        groups[key]["tiles"].append(tile)
        groups[key]["fans"].append(kong_fan)
    return list(groups.values())
