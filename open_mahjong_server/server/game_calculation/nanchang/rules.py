"""MIL 南昌2026四人单打：实体精牌、字顺与三项独立结算。"""

from collections import Counter
from functools import lru_cache
from itertools import combinations

RULE_VERSION = "mil-nanchang-2026"
SUB_RULE = "nanchang/mil2026"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
HONORS = tuple(range(41, 48))
TILES = NUMBERS + HONORS
SEQUENCES = tuple((t, t+1, t+2) for t in NUMBERS if t % 10 <= 7) + tuple(combinations(range(41, 45), 3)) + ((45, 46, 47),)
SEQUENCES_BY_TILE = {t: tuple(s for s in SEQUENCES if t in s) for t in TILES}
CONFIG = {"rule_version": RULE_VERSION}


def normalize_config(config=None):
    if config is not None and (not isinstance(config, dict) or set(config) - {"rule_version"}
                               or config.get("rule_version", RULE_VERSION) != RULE_VERSION):
        raise ValueError("南昌仅支持MIL2026四人单打标准配置")
    return dict(CONFIG)


def secondary_jing(indicator):
    if indicator in NUMBERS:
        return indicator // 10 * 10 + indicator % 10 % 9 + 1
    order = (41, 42, 43, 44) if indicator in range(41, 45) else (45, 47, 46)
    if indicator not in order:
        raise ValueError("精牌指示牌无效")
    return order[(order.index(indicator) + 1) % len(order)]


def jing_tiles(indicator):
    return indicator, secondary_jing(indicator)


def meld_tiles(code):
    if isinstance(code, dict):
        return tuple(code["physical"])
    if not isinstance(code, str) or len(code) < 2:
        raise ValueError("副露编码无效")
    if code.startswith("s["):
        return tuple(int(t) for t in code[2:-1].split(","))
    if code.startswith("s") and len(code) == 7:
        return tuple(int(code[i:i+2]) for i in (1,3,5))
    tile = int(code[1:])
    return (tile-1, tile, tile+1) if code[0] == "s" else (tile,) * (4 if code[0] in "gG" else 3)


def valid_hand(hand, melds=(), *, complete=True):
    try:
        if len(melds) > 4 or len(hand) != (14 if complete else 13) - 3 * len(melds):
            return False
        physical = list(hand)
        for code in melds:
            group = meld_tiles(code)
            if len(group) not in (3, 4) or not (len(set(group)) == 1 or tuple(sorted(group)) in SEQUENCES):
                return False
            physical.extend(group)
        return all(type(t) is int and t in TILES for t in physical) and max(Counter(physical).values(), default=0) <= 4
    except (ValueError, TypeError, KeyError):
        return False


def _counts(hand, jing, fixed_tile=None):
    natural = list(hand)
    fixed = []
    if fixed_tile is not None:
        natural.remove(fixed_tile)
        fixed = [fixed_tile]
    wild = sum(t in jing for t in natural)
    counts = Counter(t for t in natural if t not in jing)
    counts.update(fixed)
    return tuple(counts[t] for t in TILES), wild


@lru_cache(maxsize=60000)
def _groups(counts, wild, groups, pair, triplets_only=False):
    if wild < 0 or groups < 0 or sum(counts) + wild != groups*3 + pair*2:
        return False
    if not any(counts):
        return True
    i = next(i for i, n in enumerate(counts) if n)
    tile, count = TILES[i], counts[i]
    if pair:
        used = min(2, count)
        remaining = list(counts)
        remaining[i] -= used
        if _groups(tuple(remaining), wild-(2-used), groups, False, triplets_only):
            return True
    if not groups:
        return False
    used = min(3, count)
    remaining = list(counts)
    remaining[i] -= used
    if _groups(tuple(remaining), wild-(3-used), groups-1, pair, triplets_only):
        return True
    if not triplets_only:
        for sequence in SEQUENCES_BY_TILE[tile]:
            remaining, needed = list(counts), 0
            for t in sequence:
                j = TILES.index(t)
                if remaining[j]:
                    remaining[j] -= 1
                else:
                    needed += 1
            if _groups(tuple(remaining), wild-needed, groups-1, pair, False):
                return True
    return False


def _pairs(counts, wild, number=7):
    return sum(counts)+wild == number*2 and sum(n % 2 for n in counts) <= wild


SPACED = tuple(tuple(c) for n in range(4) for c in combinations(range(1, 10), n)
               if all(b-a >= 3 for a, b in zip(c, c[1:])))


def _lan(counts, wild):
    if any(n > 1 for n in counts) or sum(counts)+wild != 14:
        return False
    present = {t for t, n in zip(TILES, counts) if n}
    capacity = 7
    for suit in (1, 2, 3):
        ranks = {t % 10 for t in present if t // 10 == suit}
        completions = [len(c) for c in SPACED if ranks <= set(c)]
        if not completions:
            return False
        capacity += max(completions)
    return capacity >= 14


def shapes(hand, melds=(), *, indicator=11, self_draw=True, winning_tile=None, natural=False):
    if not valid_hand(hand, melds):
        return set()
    fixed = winning_tile if not self_draw else None
    if fixed is not None and fixed not in hand:
        return set()
    counts, wild = _counts(hand, () if natural else jing_tiles(indicator), fixed)
    result = set()
    if _groups(counts, wild, 4-len(melds), True):
        result.add("standard")
    if all(len(set(meld_tiles(m))) == 1 for m in melds) and _groups(counts, wild, 4-len(melds), True, True):
        result.add("all_triplets")
    if not melds:
        if _pairs(counts, wild):
            result.add("seven_pairs")
        if _lan(counts, wild):
            result.add("thirteen_lan")
    return result


def is_complete(hand, melds=(), **kwargs):
    return bool(shapes(hand, melds, **kwargs))


def is_jingdiao(hand, melds=(), *, indicator):
    if not valid_hand(hand, melds, complete=False):
        return False
    jing = jing_tiles(indicator)
    for tile in set(hand) & set(jing):
        remaining = list(hand)
        remaining.remove(tile)
        counts, wild = _counts(remaining, jing)
        if _groups(counts, wild, 4-len(melds), False) or (not melds and _pairs(counts, wild, 6)):
            return True
    return False


@lru_cache(maxsize=4096)
def _waits(hand, melds, indicator):
    counts = Counter(hand)
    for m in melds:
        counts.update(meld_tiles(m))
    return tuple(t for t in TILES if counts[t] < 4 and is_complete(hand+(t,), melds, indicator=indicator))


def waiting_tiles(hand, melds=(), *, indicator):
    if not valid_hand(hand, melds, complete=False):
        return set()
    return set(_waits(tuple(sorted(hand)), tuple(melds), indicator))


def win_payments(winner, fan, *, dealer=0, payer=None, german=0, jingdiao=False, fixed=0):
    changes, rows = [0]*4, []
    for index in range(4):
        if index == winner:
            continue
        applied_fan = min(6, fan + int(payer is None) + int(winner == dealer or index == dealer) + int(index == payer))
        extra = (german + 5*int(jingdiao)) if payer is None or index == payer else 0
        amount = fixed or (2**applied_fan + extra)
        changes[index] -= amount
        changes[winner] += amount
        rows.append(dict(payer=index, receiver=winner, fan=applied_fan, extra=0 if fixed else extra, points=amount))
    return changes, rows


def score_hand(hand, melds=(), *, indicator, winning_tile=None, self_draw=True,
               discarded=(), other_has_jing=True, declared_jingdiao=False, after_kong=False,
               heavenly=False, earthly=False, winner=1, dealer=0, payer=None):
    winning_tile = hand[-1] if winning_tile is None and hand else winning_tile
    wild_shapes = shapes(hand, melds, indicator=indicator, self_draw=self_draw, winning_tile=winning_tile)
    if not wild_shapes:
        return None
    jing = jing_tiles(indicator)
    before = list(hand)
    before.remove(winning_tile)
    fixed_name, fixed_points = None, 0
    if heavenly:
        special = any(is_jingdiao(hand[:i]+hand[i+1:], melds, indicator=indicator)
                      for i, t in enumerate(hand) if t not in jing)
        fixed_name, fixed_points = ("天和精吊", 40) if special else ("天和", 20)
    elif earthly:
        special = is_jingdiao(before, melds, indicator=indicator)
        fixed_name, fixed_points = ("地和精吊", 40) if special else ("地和", 20)
    if fixed_points:
        changes, rows = win_payments(winner, 0, dealer=dealer, payer=payer, fixed=fixed_points)
        return dict(fan=0, qualifying_fan=1, fan_names=[fixed_name], fan_values={fixed_name:fixed_points},
                    points=changes[winner], payments=rows, changes=changes, fixed=fixed_points, german=0,
                    jingdiao=False, effective_fan=6, shape=next(iter(sorted(wild_shapes))))
    naturals = shapes(hand, melds, indicator=indicator, self_draw=self_draw, winning_tile=winning_tile, natural=True)
    common = {}
    for name, tile, value in (("正精",jing[0],2), ("副精",jing[1],1)):
        count = list(discarded).count(tile)
        if count:
            common[name] = count*value
    if after_kong and self_draw:
        common["杠开"] = 1
    jingdiao = declared_jingdiao and self_draw and is_jingdiao(before, melds, indicator=indicator)
    if jingdiao:
        common["精吊"] = 1
    candidates = []
    for shape in sorted(wild_shapes):
        fans, german = dict(common), 0
        if shape in naturals:
            fans["德国" if other_has_jing else "德中德"] = 1 if other_has_jing else 2
            german = 5 if other_has_jing else 10
        if shape == "all_triplets":
            fans["大七对"] = 2
        elif shape == "seven_pairs":
            fans["德国七对" if shape in naturals else "小七对"] = 2 if shape in naturals else 1
        elif shape == "thirteen_lan":
            seven = all(t in hand for t in HONORS)
            fans["七星十三烂" if seven else "十三烂"] = 2 if seven else 1
        fan = sum(fans.values())
        changes, rows = win_payments(winner, fan, dealer=dealer, payer=None if self_draw else payer,
                                     german=german, jingdiao=jingdiao)
        effective = min(6, fan + int(winner == dealer) + (0.5 if self_draw or payer == dealer else 0))
        candidates.append(dict(fan=fan, qualifying_fan=fan, fan_names=list(fans), fan_values=fans,
                               points=changes[winner], payments=rows, changes=changes, fixed=0,
                               german=german, jingdiao=jingdiao, effective_fan=effective, shape=shape))
    return max(candidates, key=lambda d:(d["points"], d["fan"], d["shape"]))


def jing_value(tile, indicator):
    return 2 if tile == indicator else 1 if tile == secondary_jing(indicator) else 0


def chongguan(base):
    return base * (base-3) if base >= 5 else base


def jing_payments(owned, *, indicator, transfers=(), disabled=()):
    bases = [sum(jing_value(t, indicator) for t in tiles) for tiles in owned]
    sole = next((i for i,b in enumerate(bases) if b), None) if sum(bool(b) for b in bases) == 1 else None
    charges, ledger = [[0]*4 for _ in range(4)], []
    for receiver, base in enumerate(bases):
        if receiver in disabled:
            continue
        relevant = [x for x in transfers if x["receiver"] == receiver]
        current = base-sum(jing_value(x["tile"], indicator) for x in relevant)
        if current < 0:
            raise ValueError("精牌责任记录与实体归属不一致")
        for payer in range(4):
            if payer != receiver:
                charges[receiver][payer] = chongguan(base)
        for event in relevant:
            donor, value = event["payer"], jing_value(event["tile"], indicator)
            extra = chongguan(current+value)-chongguan(current)-value
            current += value
            for payer in range(4):
                if payer not in (receiver, donor):
                    charges[receiver][payer] -= extra
                    charges[receiver][donor] += extra
            ledger.append(dict(kind="冲关承包",receiver=receiver,payer=donor,extra=extra))
        if receiver == sole:
            extra = chongguan(base)
            donor = relevant[-1]["payer"] if relevant else None
            for payer in range(4):
                if payer != receiver:
                    charges[receiver][donor if donor is not None else payer] += extra
            ledger.append(dict(kind="霸王精",receiver=receiver,payer=donor,extra=extra))
    changes = [0]*4
    for receiver in range(4):
        for payer, amount in enumerate(charges[receiver]):
            changes[receiver] += amount
            changes[payer] -= amount
    return dict(bases=bases, sole=sole, charges=charges, changes=changes, liability=ledger)


def kong_payments(actor, kind, tile, *, indicator):
    unit = 10 if tile in jing_tiles(indicator) else 2 if kind == "angang" else 1
    return [unit*3 if i == actor else -unit for i in range(4)]


def match_results(scores):
    result = []
    for score in scores:
        rank = 1+sum(other>score for other in scores)
        tied = sum(other==score for other in scores)
        points = sum(5-place for place in range(rank,rank+tied))/tied
        result.append(dict(rank=rank,competition_points=points,score=score))
    return result
