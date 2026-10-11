"""MIL 沈阳2025，第7—11页的牌型与逐付款者结算。"""
from collections import Counter
from dataclasses import dataclass
from functools import lru_cache

from ..hongkong.models import TILES, NUMBERS, ORPHANS, DRAGONS, WINDS
from ..hongkong.solver import _partitions, _key, parse_meld as normal_meld

SUB_RULE = "shenyang/mil2025"
VERSION = "mil-shenyang-2025-om1"
SPECIAL = {"Ydragon": tuple(DRAGONS), "Ywind": tuple(WINDS)}
FANS = {"庄": 1, "点炮": 1, "单吊/边张/夹和": 1, "四归一": 1,
        "杠上开花": 1, "海底": 1, "中/发/白刻": 1, "明杠": 1,
        "长毛": 1, "闭门": 1, "听牌": 1, "手把一": 2, "抢杠和": 2,
        "杠流": 2, "三家闭": 2, "暗杠": 2, "中/发/白明杠": 2,
        "喜杠": 2, "旋风杠": 2, "和中/发/白": 2, "飘和": 3,
        "中/发/白暗杠": 4, "清一色": 4, "闭大四": 4, "七对": 4}


@dataclass(frozen=True)
class Meld:
    kind: str
    tiles: tuple
    concealed: bool = False


@lru_cache(maxsize=256)
def parse_meld(code):
    if code in SPECIAL:
        return Meld(code, SPECIAL[code])
    m = normal_meld(code)
    return Meld(m.kind, m.tiles, m.concealed)


def valid_hand(hand, codes=(), *, complete=False):
    try:
        melds = tuple(parse_meld(c) for c in codes)
    except (ValueError, TypeError):
        return None
    if len(melds) > 4 or len(hand) != 13 + int(complete) - 3 * len(melds):
        return None
    if any(type(t) is not int or t not in TILES for t in hand):
        return None
    counts = Counter(hand)
    counts.update(t for m in melds for t in m.tiles)
    return melds if all(n <= 4 for n in counts.values()) else None


@lru_cache(maxsize=32768)
def _shapes(hand, codes, qualify, winning_tile):
    external = valid_hand(hand, codes, complete=True)
    if external is None:
        return ()
    counts, result = Counter(hand), []
    if not external and all(n % 2 == 0 for n in counts.values()):
        result.append(("seven_pairs", 0, ()))
    tiles = hand + tuple(t for m in external for t in m.tiles)
    suits = {t // 10 for t in tiles if t in NUMBERS}
    pure = len(suits) == 1 and all(t in NUMBERS for t in tiles)
    if qualify and (not set(tiles) & ORPHANS or not (suits == {1, 2, 3} or pure)):
        return tuple(result)
    for pair in sorted(t for t, n in counts.items() if n >= 2):
        rest = counts.copy()
        rest[pair] -= 2
        choices = [("standard", partition) for partition in _partitions(_key(rest), 4 - len(external))]
        if winning_tile in DRAGONS and all(rest[t] for t in DRAGONS):
            dragon_rest = rest.copy()
            dragon_rest.subtract(DRAGONS)
            choices += [("dragon", partition) for partition in _partitions(_key(dragon_rest), 3 - len(external))]
        for kind, partition in choices:
            if qualify and kind != "dragon" and pair not in DRAGONS and all(m.kind == "sequence" for m in external + partition):
                continue
            result.append((kind, pair, partition))
    return tuple(result)


def shapes(hand, codes=(), *, qualify=True, winning_tile=0):
    if any(type(tile) is not int for tile in hand):
        return ()
    return _shapes(tuple(sorted(hand)), tuple(codes), qualify, winning_tile)


@lru_cache(maxsize=32768)
def _waits(hand, codes, qualify):
    if valid_hand(hand, codes) is None:
        return frozenset()
    return frozenset(t for t in TILES if _shapes(tuple(sorted(hand + (t,))), codes, qualify, t))


def waits(hand, codes=(), *, qualify=True):
    if any(type(tile) is not int for tile in hand):
        return set()
    return set(_waits(tuple(sorted(hand)), tuple(codes), qualify))


def is_closed(codes, declared_ready=False):
    return all(c.startswith("G") or c in SPECIAL or (declared_ready and c.startswith("g")) for c in codes)


def _single_wait(hand, codes, win, pair, partition, kind):
    before = list(hand)
    before.remove(win)
    if len(waits(before, codes)) != 1:
        return False
    external = tuple(parse_meld(c) for c in codes)
    prior = before + [t for m in external for t in m.tiles]
    if win in ORPHANS and not set(prior) & ORPHANS:
        return True
    if len(waits(before, codes, qualify=False)) != 1:
        return False
    if kind == "dragon":
        return True
    if pair == win:
        return True
    return any(m.kind == "sequence" and (m.tile == win or m.tile % 10 == 2 and win == m.tile + 1
               or m.tile % 10 == 8 and win == m.tile - 1) for m in partition)


def score(hand, codes=(), *, winning_tile, self_draw=True, after_kong=False, last_four=False,
          robbing_kong=False, kong_discard=False, long_hair=False, declared_ready=False,
          borrowed=()):
    if type(winning_tile) is not int or winning_tile not in hand:
        return None
    external = valid_hand(hand, codes, complete=True)
    if external is None:
        return None
    candidates = []
    for kind, pair, partition in shapes(hand, codes, winning_tile=winning_tile):
        if kind == "dragon" and not self_draw:
            continue
        names = []
        if kind == "seven_pairs":
            names.append("七对")
        else:
            if len(hand) == 2:
                names.append("手把一")
            elif _single_wait(hand, codes, winning_tile, pair, partition, kind):
                names.append("单吊/边张/夹和")
            if len(external + partition) + int(kind == "dragon") == 4 and all(m.kind != "sequence" for m in external + partition):
                names.append("飘和")
            if kind == "dragon":
                names.append("和中/发/白")
        tiles = list(hand) + [t for m in external for t in m.tiles]
        if len({t // 10 for t in tiles}) == 1 and all(t in NUMBERS for t in tiles):
            names.append("清一色")
        counts = Counter(hand)
        for code, m in zip(codes, external):
            if code in SPECIAL:
                names.append("喜杠" if code == "Ydragon" else "旋风杠")
            elif m.kind == "kong" or m.kind == "triplet" and m.tiles[0] in borrowed:
                names.append(("中/发/白暗杠" if m.concealed else "中/发/白明杠") if m.tiles[0] in DRAGONS
                             else "暗杠" if m.concealed else "明杠")
            else:
                counts.update(m.tiles)
                if m.kind == "triplet" and m.tiles[0] in DRAGONS:
                    names.append("中/发/白刻")
        names.extend("四归一" for count in counts.values() if count == 4)
        if kind != "seven_pairs":
            names.extend("中/发/白刻" for m in partition if m.kind == "triplet" and m.tile in DRAGONS)
        if after_kong and self_draw:
            names.append("杠上开花")
        if last_four and self_draw:
            names.append("海底")
        if robbing_kong:
            names.append("抢杠和")
        elif kong_discard and not self_draw:
            names.append("杠流")
        elif not self_draw:
            names.append("点炮")
        if long_hair:
            names.append("长毛")
        if declared_ready:
            names.append("听牌")
        raw = sum(FANS[n] for n in names)
        candidates.append(dict(is_win=True, fan_names=names, raw_fan=raw, fan=min(6, raw),
                               base_score=2 ** min(6, raw), shape=kind, edition=VERSION))
    return max(candidates, key=lambda d: (d["raw_fan"], d["shape"])) if candidates else None


def payments(winner, detail, *, dealer=0, discarder=None, closed=(False,) * 4):
    if winner not in range(4) or dealer not in range(4) or len(closed) != 4 or discarder == winner:
        raise ValueError("无效的沈阳结算")
    if discarder is not None and discarder not in range(4):
        raise ValueError("无效的放铳者")
    changes, rows = dict.fromkeys(range(4), 0), []
    all_closed = all(closed)
    three_closed = all(closed[i] for i in range(4) if i != winner)
    for payer in range(4) if discarder is None else (discarder,):
        if payer == winner:
            continue
        names = list(detail["fan_names"])
        if dealer in (winner, payer):
            names.append("庄")
        if all_closed:
            names.append("闭大四")
        else:
            if three_closed:
                names.append("三家闭")
            elif closed[payer]:
                names.append("闭门")
            if closed[winner] and detail["shape"] != "seven_pairs":
                names.append("闭门")
        raw = sum(FANS[n] for n in names)
        points = 2 ** min(6, raw)
        changes[payer] -= points
        changes[winner] += points
        rows.append(dict(payer=payer, fan=min(6, raw), raw_fan=raw, score=points, fan_names=names))
    return changes, rows


def egg_points(tile):
    return 10 if tile == 31 else 5 if tile in range(21, 30) else 0
