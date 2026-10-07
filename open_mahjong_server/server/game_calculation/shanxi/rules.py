"""MIL《山西麻将（推广）竞赛规则（试行2023版）》第五、七、八节。

牌值是点数，不能使用指数番表。公开面子只有碰和杠；暗手允许顺子。
原始规则书来源见 other/rule/shanxi/README.md。
"""

from collections import Counter
from functools import lru_cache

SUB_RULE = "shanxi/mil2023"
VERSION = "mil-shanxi-2023-om1"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
HONORS = tuple(range(41, 48))  # 原书把东南西北中发白合称“风”，均为10点。
TILES = NUMBERS + HONORS
ORPHANS = frozenset((11, 19, 21, 29, 31, 39) + HONORS)


def tile_points(tile):
    if type(tile) is not int or tile not in TILES:
        raise ValueError("山西牌张须为136张牌中的合法牌种")
    return 10 if tile in HONORS else tile % 10


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "kgG":
        raise ValueError("山西麻将不可吃牌，明牌只接受碰和杠")
    tile = int(code[1:])
    tile_points(tile)
    return (tile,) * (3 if code[0] == "k" else 4)


def _physical(hand, melds, complete):
    if len(melds) > 4 or len(hand) + 3 * len(melds) != (14 if complete else 13):
        return None
    if any(type(t) is not int or t not in TILES for t in hand):
        return None
    try:
        count = Counter(hand)
        count.update(t for code in melds for t in meld_tiles(code))
    except (TypeError, ValueError):
        return None
    return count if all(n <= 4 for n in count.values()) else None


@lru_cache(maxsize=32768)
def _sets(hand):
    if not hand:
        return ((),)
    first = hand[0]
    count = Counter(hand)
    choices = []
    if count[first] >= 3:
        choices.append((f"k{first}", (first,) * 3))
    if first in NUMBERS and first % 10 <= 7 and count[first + 1] and count[first + 2]:
        choices.append((f"s{first + 1}", (first, first + 1, first + 2)))
    result = []
    for code, tiles in choices:
        rest = list(hand)
        for tile in tiles:
            rest.remove(tile)
        result.extend((code,) + tail for tail in _sets(tuple(rest)))
    return tuple(result)


@lru_cache(maxsize=32768)
def _shapes(hand, melds):
    count = _physical(hand, melds, True)
    if count is None:
        return ()
    result = []
    concealed = Counter(hand)
    if not melds:
        if all(n % 2 == 0 for n in concealed.values()):
            result.append(("seven_pairs", ()))
        if set(concealed) == ORPHANS and sorted(concealed.values()) == [1] * 12 + [2]:
            result.append(("orphans", ()))
    for pair, number in concealed.items():
        if number < 2:
            continue
        rest = list(hand)
        rest.remove(pair)
        rest.remove(pair)
        result.extend(("standard", sets) for sets in _sets(tuple(rest)))
    return tuple(result)


def shapes(hand, melds=()):
    return _shapes(tuple(sorted(hand)), tuple(melds))


@lru_cache(maxsize=32768)
def _waits(hand, melds):
    count = _physical(hand, melds, False)
    if count is None:
        return frozenset()
    return frozenset(t for t in TILES if count[t] < 4 and shapes(hand + (t,), melds))


def waits(hand, melds=()):
    """结构听口；是否可以报听、自摸或点和须另外判断。"""
    return set(_waits(tuple(sorted(hand)), tuple(melds)))


def ready_waits(hand, melds=(), public_tiles=()):
    """返回可报听手的全部结构听口，否则为空。

    public_tiles 包含实际可见牌河与各家明牌，不含暗扣报听牌；不能窥视
    其他手牌或牌墙。自己的公开面子已在 public_tiles，不得重复扣除。
    """
    result = waits(hand, melds)
    known = Counter(public_tiles)
    known.update(hand)
    return result if any(tile_points(t) >= 6 and known[t] < 4 for t in result) else set()


def score(hand, melds=(), *, winning_tile, declared_ready, self_draw=False):
    if not declared_ready or type(winning_tile) is not int or winning_tile not in hand:
        return None
    if winning_tile not in TILES or tile_points(winning_tile) < (3 if self_draw else 6):
        return None
    candidates = shapes(hand, melds)
    if not candidates:
        return None
    all_tiles = list(hand) + [t for code in melds for t in meld_tiles(code)]
    flush = len({t // 10 for t in all_tiles}) == 1
    best = (0, "平和")
    for kind, sets in candidates:
        if kind == "orphans":
            candidate = (60, "十三幺")
        elif kind == "seven_pairs":
            candidate = (40, "豪华七对") if hand.count(winning_tile) == 4 else (20, "七对")
        elif any({f"s{s}2", f"s{s}5", f"s{s}8"}.issubset(sets) for s in (1, 2, 3)):
            candidate = (20, "一条龙")
        else:
            candidate = (0, "平和")
        if candidate[0] > best[0]:
            best = candidate
    pattern_points = best[0] + (20 if flush else 0)
    fans = ([] if flush and best[0] == 0 else [best[1]]) + (["清一色"] if flush else [])
    points = tile_points(winning_tile)
    return {"is_win": True, "pattern_points": pattern_points, "tile_points": points,
            "base_score": pattern_points + points, "fan_names": fans + [f"和牌张（{points}分）"],
            "rule_version": VERSION}


def payments(winner=None, *, base_score=0, dealer=0, discarder=None,
             discarder_ready=False, kongs=()):
    """八节：统一在和牌时结杠；未报听放铳者包全桌，流局全部作废。

    kongs 是 (杠家座位, 牌张, 暗杠与否) 序列；点杠来源不影响每家杠分。
    返回和牌、杠牌分别明细，便于牌谱与结算展示。
    """
    win = {i: 0 for i in range(4)}
    kong = dict(win)
    if winner is None:
        return {"win": win, "kong": kong, "total": dict(win), "liable_payer": None}
    if winner not in range(4) or dealer not in range(4) or base_score < 0:
        raise ValueError("非法结算参数")
    if discarder is not None and (discarder not in range(4) or discarder == winner):
        raise ValueError("非法放铳座位")
    liability = discarder if discarder is not None and not discarder_ready else None
    for payer in range(4):
        if payer == winner:
            continue
        premium = winner == dealer or (payer == dealer and (discarder is None or discarder == dealer))
        amount = (base_score + (5 if premium else 0)) * (2 if discarder is None else 1)
        win[winner] += amount
        win[liability if liability is not None else payer] -= amount
    for owner, tile, concealed in kongs:
        if owner not in range(4) or type(concealed) is not bool:
            raise ValueError("非法杠账")
        amount = tile_points(tile) * (2 if concealed else 1)
        if owner == liability:
            continue
        for payer in range(4):
            if payer != owner:
                kong[owner] += amount
                kong[liability if liability is not None else payer] -= amount
    return {"win": win, "kong": kong, "total": {i: win[i] + kong[i] for i in range(4)},
            "liable_payer": liability}
