"""上海敲麻：MIL《上海麻将（推广）竞赛规则（试行2024版）》七至九节。

计分与状态机分开；只接受四面子一将，中发白属于必须补出的花。
"""

from collections import Counter
from functools import lru_cache


NUMBERS = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10))
WINDS = (41, 42, 43, 44)
TILES = NUMBERS + WINDS
FLOWERS = (45, 46, 47, 51, 52, 53, 54, 55, 56, 57, 58)
SUB_RULE = "shanghai/qiaoma"


def meld_tiles(code):
    if not isinstance(code, str) or len(code) < 3 or code[0] not in "skgG":
        raise ValueError("非法面子")
    tile = int(code[1:])
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError("非法顺子")
        return (tile - 1, tile, tile + 1)
    if tile not in TILES:
        raise ValueError("中发白与花牌不能组成面子")
    return (tile,) * (4 if code[0] in "gG" else 3)


@lru_cache(maxsize=32768)
def _sets(tiles):
    if not tiles:
        return ((),)
    first = tiles[0]
    count = Counter(tiles)
    result = []
    if count[first] >= 3:
        rest = list(tiles)
        for _ in range(3):
            rest.remove(first)
        result.extend((f"k{first}",) + tail for tail in _sets(tuple(rest)))
    if first in NUMBERS and first % 10 <= 7 and count[first + 1] and count[first + 2]:
        rest = list(tiles)
        for tile in (first, first + 1, first + 2):
            rest.remove(tile)
        result.extend((f"s{first + 1}",) + tail for tail in _sets(tuple(rest)))
    return tuple(result)


def decompositions(hand, melds=()):
    """返回 (雀头, 暗手面子)；拒绝花牌、第五张牌与特殊牌型。"""
    if len(hand) + 3 * len(melds) != 14 or len(melds) > 4:
        return ()
    if any(type(tile) is not int or tile not in TILES for tile in hand):
        return ()
    try:
        all_tiles = list(hand) + [t for code in melds for t in meld_tiles(code)]
    except (ValueError, TypeError):
        return ()
    if any(count > 4 for count in Counter(all_tiles).values()):
        return ()
    result = []
    for pair, count in sorted(Counter(hand).items()):
        if count < 2:
            continue
        rest = sorted(hand)
        rest.remove(pair)
        rest.remove(pair)
        result.extend((pair, sets) for sets in _sets(tuple(rest)))
    return tuple(result)


def waits(hand, melds=()):
    if len(hand) + 3 * len(melds) != 13:
        return set()
    return {tile for tile in TILES if decompositions(list(hand) + [tile], melds)}


def physical_flower_count(flowers, melds=(), concealed_sets=()):
    """不含底花、无花奖励的花数；风刻花可来自暗手。"""
    total = len(flowers)
    for code in tuple(melds) + tuple(concealed_sets):
        kind, tile = code[0], int(code[1:])
        if kind in "gG":
            total += (2 if kind == "G" else 1) + (1 if tile in WINDS else 0)
        elif kind == "k" and tile in WINDS:
            total += 1
    return total


def score(hand, melds=(), flowers=(), *, declared_ready=False, self_draw=False,
          replacement=False, rob_kong=False, min_fan=0):
    if not declared_ready:
        return None
    candidates = []
    for pair, sets in decompositions(hand, melds):
        fans = []
        if not any(code[0] != "G" for code in melds):
            fans.append(("门清", 1))
        if len(hand) == 2:
            fans.append(("大吊车", 1))
        all_tiles = list(hand) + [t for code in melds for t in meld_tiles(code)]
        suits = {t // 10 for t in all_tiles if t in NUMBERS}
        if len(suits) == 1:
            fans.append(("混一色", 1) if any(t in WINDS for t in all_tiles) else ("清一色", 2))
        if all(code[0] in "kgG" for code in tuple(melds) + sets):
            fans.append(("碰碰和", 1))
        regular_fan = sum(value for _, value in fans)
        flower_count = physical_flower_count(flowers, melds, sets)
        no_flowers = flower_count == 0
        non_base_flowers = 10 if no_flowers else flower_count
        # 偶然番（杠开、抢杠）不能免除二花自摸／三花点和的门槛。
        if not regular_fan and non_base_flowers < (2 if self_draw else 3):
            continue
        if self_draw and replacement:
            fans.append(("杠开", 1))
        if rob_kong:
            fans.append(("抢杠", 1))
        fan = sum(value for _, value in fans)
        if fan < min_fan:
            continue
        capped = min(fan, 3)
        points = (1 + non_base_flowers) * (2 ** capped)
        labels = [name for name, _ in fans]
        labels.append("无花（10花）" if no_flowers else f"花数（{flower_count}花）")
        labels.append("底花（1花）")
        if fan > 3:
            labels.append("三番封顶")
        candidates.append({
            "is_win": True, "fan": fan, "capped_fan": capped,
            "flowers": non_base_flowers, "physical_flowers": flower_count,
            "base_score": points, "fan_names": labels,
            "pair": pair, "concealed_sets": list(sets),
        })
    return max(candidates, key=lambda value: (value["base_score"], value["fan"]), default=None)


def contract_patterns(melds):
    """已公开面子能共同成立的承包番种；第四副破坏全部番种则解除。"""
    if not melds:
        return set()
    tiles = [tile for code in melds for tile in meld_tiles(code)]
    suits = {tile // 10 for tile in tiles if tile in NUMBERS}
    result = {"碰碰和"} if all(code[0] in "kgG" for code in melds) else set()
    if len(suits) <= 1:
        # 三副同色顺子仍可在第四副或将牌加入风牌，构成混一色。
        result.add("混一色")
        if not any(tile in WINDS for tile in tiles):
            result.add("清一色")
    return result


def payments(winner, base_score, *, discarder=None, partners=(), discarder_ready=False):
    """逐家支付；有承包自摸由所有承包者各付五倍，其他家不支付。"""
    partners = set(partners) - {winner}
    result = {index: 0 for index in range(4)}
    if discarder is None:
        charges = {index: base_score * 5 for index in partners} if partners else {
            index: base_score for index in range(4) if index != winner
        }
    elif partners:
        # 点炮身份与承包身份分别计一份；点炮者自己承包时也支付两份。
        charges = {index: base_score for index in partners}
        charges[discarder] = charges.get(discarder, 0) + base_score
        if not discarder_ready:
            charges = {discarder: sum(charges.values())}
    else:
        charges = {discarder: base_score}
    for payer, points in charges.items():
        result[payer] -= points
        result[winner] += points
    return result
