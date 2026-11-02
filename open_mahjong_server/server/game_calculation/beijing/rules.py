from collections import Counter
from functools import lru_cache
from itertools import combinations_with_replacement

RULE_VERSION = "beijing-v1"
SUB_RULE = "beijing/chipengti"
DESCRIPTION = "北京麻将（吃碰提）是北京地区的民间麻将规则，其特点为有字牌、无花牌、有万能牌、非门前清状态不可点和，番种体系较为简单。本文参考网络资料与线下实践梳理北京麻将的基本玩法，体现北京麻将核心特征，便于各地麻将爱好者了解北京麻将主要框架。"
TILES = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10)) + tuple(range(41, 48))
INDEX = {tile: i for i, tile in enumerate(TILES)}
SEQUENCES = tuple((t, t + 1, t + 2) for t in TILES if t < 40 and t % 10 <= 7)
SEQUENCES_BY_TILE = {t: tuple(s for s in SEQUENCES if t in s) for t in TILES}
CONFIG = dict(rule_version=RULE_VERSION, hun_mode="up", three_hun_self_draw=False, single_call=False,
              dingzhuang=False, four_hun_win=False, multi_ron=False, fan_zimo=False, fan_dealer=False,
              fan_repeat=False, fan_all_pungs=False, fan_all_waits=False, fan_kong=False,
              ron_all_pay=False, half_hun=False, pao_kong=False, pao_four_calls=False)


def normalize_config(value=None):
    if value is None:
        return dict(CONFIG)
    if not isinstance(value, dict) or set(value) - CONFIG.keys():
        raise ValueError("北京麻将配置包含未知选项")
    result = {**CONFIG, **value}
    if result["rule_version"] != RULE_VERSION or result["hun_mode"] not in ("up", "down", "self"):
        raise ValueError("北京麻将规则版本或混儿模式无效")
    for key in CONFIG.keys() - {"rule_version", "hun_mode"}:
        if type(result[key]) is not bool:
            raise ValueError(f"{key} 必须为布尔值")
    return result


def hun_tile(indicator, mode="up"):
    if type(indicator) is not int or indicator not in TILES or mode not in ("up", "down", "self"):
        raise ValueError("混儿皮儿或模式无效")
    if mode == "self":
        return indicator
    cycle = tuple(indicator // 10 * 10 + n for n in range(1, 10)) if indicator < 40 else (
        (41, 42, 43, 44) if indicator < 45 else (45, 47, 46))
    return cycle[(cycle.index(indicator) + (1 if mode == "up" else -1)) % len(cycle)]


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "skgG":
        raise ValueError("副露编码无效")
    tile = int(code[1:])
    if tile not in TILES:
        raise ValueError("副露牌张无效")
    if code[0] == "s":
        group = (tile - 1, tile, tile + 1)
        if group not in SEQUENCES:
            raise ValueError("吃牌组合无效")
        return group
    return (tile,) * (4 if code[0] in "gG" else 3)


def valid_hand(hand, melds=(), *, indicator=11, mode="up", complete=True):
    try:
        hun = hun_tile(indicator, mode)
        if len(melds) > 4 or len(hand) + 3 * len(melds) != (14 if complete else 13):
            return False
        groups = [meld_tiles(m) for m in melds]
        if any(hun in group for group in groups):
            return False
        counts = Counter([*hand, *(t for group in groups for t in group)])
        return all(type(t) is int and t in TILES and n <= (3 if t == indicator else 4)
                   for t, n in counts.items())
    except (ValueError, TypeError):
        return False


@lru_cache(maxsize=50000)
def _groups(counts, wild, remaining):
    if wild < 0 or remaining < 0 or sum(counts) + wild != 3 * remaining:
        return ()
    if remaining == 0:
        return ((),)
    first = next((i for i, n in enumerate(counts) if n), None)
    if first is None:
        choices = tuple((t,) * 3 for t in TILES) + SEQUENCES
    else:
        tile = TILES[first]
        choices = ((tile,) * 3,) + SEQUENCES_BY_TILE[tile]
    result = set()
    for group in choices:
        rest, missing = list(counts), 0
        for tile in group:
            i = INDEX[tile]
            if rest[i]:
                rest[i] -= 1
            else:
                missing += 1
        for tail in _groups(tuple(rest), wild - missing, remaining - 1):
            result.add(tuple(sorted((group,) + tail)))
    return tuple(sorted(result))


@lru_cache(maxsize=30000)
def _shapes(hand, melds, hun, ron_hun=False):
    counts = Counter(hand)
    wild = counts[hun] - int(ron_hun)
    counts[hun] = int(ron_hun)
    natural = tuple(counts[t] for t in TILES)
    fixed = tuple(meld_tiles(m) for m in melds)
    result = set()
    for pair in TILES:
        rest = list(natural)
        used = min(2, rest[INDEX[pair]])
        rest[INDEX[pair]] -= used
        for groups in _groups(tuple(rest), wild - (2 - used), 4 - len(melds)):
            result.add(("standard", (pair, pair), groups + fixed))
    if not melds and len(hand) == 14:
        odd = [t for t in TILES if counts[t] % 2]
        spare = wild - len(odd)
        if spare >= 0 and spare % 2 == 0:
            paired = [t for t in TILES for _ in range((counts[t] + 1) // 2)]
            for extra in combinations_with_replacement(TILES, spare // 2):
                pairs = tuple(sorted(paired + list(extra)))
                result.add(("seven", (), tuple((t, t) for t in pairs)))
    return tuple(sorted(result))


def shapes(hand, melds=(), *, hun, ron_hun=False):
    if (len(hand) + len(melds) * 3 != 14 or len(melds) > 4 or hun not in TILES
            or any(type(t) is not int or t not in TILES for t in hand)):
        return ()
    try:
        return _shapes(tuple(sorted(hand)), tuple(melds), hun, ron_hun)
    except (ValueError, KeyError):
        return ()


@lru_cache(maxsize=20000)
def logical_waits(hand, melds, hun, ron=False):
    if len(hand) + 3 * len(melds) != 13:
        return ()
    return tuple(tile for tile in TILES if shapes((*hand, tile), melds, hun=hun, ron_hun=ron and tile == hun))


def score_hand(hand, melds=(), *, indicator=11, winning_tile=None, config=None, source="self_draw",
               discarded_hun=False, after_kong=False, last_tile=False, heavenly=False,
               earthly=False, tian_ting=False, dealer=False, repeat=0, four_hun=False,
               check_physical=True):
    config = normalize_config(config)
    hun = hun_tile(indicator, config["hun_mode"])
    ron = source in ("discard", "rob_kong")
    if source not in ("self_draw", "discard", "rob_kong") or discarded_hun:
        return None
    if winning_tile not in hand or (check_physical and not valid_hand(hand, melds, indicator=indicator, mode=config["hun_mode"])):
        return None
    closed = all(m.startswith("G") for m in melds)
    wild = hand.count(hun) - int(ron and winning_tile == hun)
    if ron and (not closed or config["three_hun_self_draw"] and wild >= 3):
        return None
    if four_hun:
        if not config["four_hun_win"] or ron or hand.count(hun) != 4:
            return None
        return dict(fan=2, raw_fan=2, points=4, fan_values={"混儿杠": 2}, fan_names=["混儿杠"],
                    substitutions=0, shape="four_hun", logical_tiles=list(hand), hun=hun)
    forms = shapes(hand, melds, hun=hun, ron_hun=ron and winning_tile == hun)
    if not forms:
        return None
    before = list(hand)
    before.remove(winning_tile)
    waits = logical_waits(tuple(sorted(before)), tuple(melds), hun)
    strict_five = winning_tile == 15 and logical_waits(tuple(sorted(before)), tuple(melds), hun, ron=True) == (15,)
    common = {}
    for name, enabled, value in (("门清", closed, 1), ("杠上开花", not ron and after_kong, 1),
            ("海底捞月", last_tile, 1), ("抢杠和", source == "rob_kong", 1),
            ("天和", heavenly and not ron, 2), ("天听", tian_ting, 1), ("地和", earthly, 1),
            ("自摸", config["fan_zimo"] and not ron, 1), ("庄", config["fan_dealer"] and dealer, 1),
            ("连庄", config["fan_repeat"] and repeat > 0, repeat),
            ("混儿悠", config["fan_all_waits"] and len(waits) == 34, 1),
            ("杠", config["fan_kong"], sum(m[0] in "gG" for m in melds))):
        if enabled and value:
            common[name] = value
    best = None
    for kind, pair, groups in forms:
        logical = [*pair, *(t for group in groups for t in group)]
        logical_counts = Counter(logical)
        natural_hun = int(ron and winning_tile == hun)
        substitutions = max(0, wild - max(0, logical_counts[hun] - natural_hun))
        fan = dict(common)
        if substitutions == 0:
            fan["素"] = 1
        if kind == "seven":
            fan["七对"] = 1
            luxury = sum(n // 4 for n in logical_counts.values())
            if luxury:
                fan["豪华七对"] = luxury
        elif any(all((s * 10 + n, s * 10 + n + 1, s * 10 + n + 2) in groups for n in (1, 4, 7)) for s in (1, 2, 3)):
            fan["一条龙"] = 1
        if config["fan_all_pungs"] and kind == "standard" and all(len(set(g)) == 1 for g in groups):
            fan["碰碰和"] = 1
        suits = {t // 10 for t in logical}
        if len(suits) == 1 and next(iter(suits)) in (1, 2, 3):
            fan["清一色"] = 1
        if strict_five and logical_counts[15] > 0:
            fan["捉五魁"] = 1
        raw_fan = sum(fan.values())
        total = max(1, raw_fan - substitutions) if config["half_hun"] and raw_fan >= 1 else raw_fan
        quote = dict(fan=total, raw_fan=raw_fan, points=2 ** total, fan_values=fan, fan_names=list(fan),
                     substitutions=substitutions, shape=kind, logical_tiles=logical, hun=hun)
        if best is None or (total, -substitutions, raw_fan) > (best["fan"], -best["substitutions"], best["raw_fan"]):
            best = quote
    return best


def payments(winner, points, *, payer=None, liability=None, all_pay=False):
    if winner not in range(4) or type(points) is not int or points < 1:
        raise ValueError("结算参数无效")
    changes = [0] * 4
    if liability is not None:
        if liability not in range(4) or liability == winner:
            raise ValueError("包付座位无效")
        changes[liability], changes[winner] = -points * 3, points * 3
    elif payer is not None:
        if payer not in range(4) or payer == winner:
            raise ValueError("放铳座位无效")
        changes[payer], changes[winner] = -points * (3 if all_pay else 1), points * (3 if all_pay else 1)
    else:
        changes = [-points if i != winner else 3 * points for i in range(4)]
    return changes


def match_results(scores):
    return [dict(rank=1 + sum(s > value for s in scores), score=value) for value in scores]
