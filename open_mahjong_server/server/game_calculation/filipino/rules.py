"""Ging's 2010 sixteen-tile rules; decisions are in other/rules/filipino.md.

All amounts are integer game points, never currency. A quarter-payment is one
point and the configurable ordinary hand value defaults to four points.
"""
from collections import Counter
from dataclasses import asdict, dataclass, fields
from functools import lru_cache

SUB_RULE = "filipino/classic16"
VERSION = "classic16-ging2010-v1"
TILES = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
FLOWERS = tuple(range(41, 48)) + tuple(range(51, 59))
PHYSICAL = tuple(t for t in TILES + tuple(range(41, 48)) for _ in range(4)) + tuple(range(51, 59))


@dataclass(frozen=True)
class Rules:
    base_points: int = 4
    self_draw_multiplier: int = 2
    discard_payment: str = "all"
    seven_pairs: bool = True
    pair_quads_as_two: bool = False
    ambitions: bool = True
    opening_no_flowers: bool = True
    opening_one_flower: bool = False
    extra_flower_thresholds: bool = False
    secret_points: int = 1
    kong_points: int = 2
    sagasa_points: int = 2
    dice_double: bool = False
    dealer_mode: str = "winner_roll"

    @classmethod
    def from_dict(cls, data=None):
        if data is None:
            return cls()
        if not isinstance(data, dict):
            raise ValueError("菲律宾详细配置须为对象")
        data = dict(data)
        version = data.pop("rule_version", VERSION)
        if version != VERSION:
            raise ValueError("不支持的菲律宾规则版本")
        allowed = {f.name for f in fields(cls)}
        if set(data) - allowed:
            raise ValueError("未知菲律宾选项: " + ", ".join(sorted(set(data) - allowed)))
        default = cls()
        for key, value in data.items():
            expected = type(getattr(default, key))
            if type(value) is not expected:
                raise ValueError(f"{key} 类型错误")
        result = cls(**data)
        if not 1 <= result.base_points <= 1000 or result.self_draw_multiplier not in (1, 2):
            raise ValueError("无效的基准分或自摸倍数")
        if result.discard_payment not in ("all", "discarder") or result.dealer_mode not in ("winner_roll", "rotate", "winner"):
            raise ValueError("无效的支付或庄家规则")
        if any(not 0 <= getattr(result, k) <= 100 for k in ("secret_points", "kong_points", "sagasa_points")):
            raise ValueError("杠奖励须为0至100整数")
        return result

    def snapshot(self):
        return {**asdict(self), "rule_version": VERSION}


def meld_tiles(code):
    if not isinstance(code, str) or len(code) < 3 or code[0] not in "skgG":
        raise ValueError("非法副露编码")
    try:
        tile = int(code[1:])
    except ValueError as error:
        raise ValueError("非法副露牌") from error
    if tile not in TILES or code[0] == "s" and not 2 <= tile % 10 <= 8:
        raise ValueError("菲律宾副露只能包含数牌")
    return (tile - 1, tile, tile + 1) if code[0] == "s" else (tile,) * (3 if code[0] == "k" else 4)


@lru_cache(maxsize=32768)
def partitions(tiles, count):
    if not tiles:
        return ((),) if count == 0 else ()
    if len(tiles) != count * 3 or count <= 0:
        return ()
    first = tiles[0]
    choices = []
    if tiles.count(first) >= 3:
        choices.append(("k", first, (first,) * 3))
    if first % 10 <= 7 and first + 1 in tiles and first + 2 in tiles:
        choices.append(("s", first + 1, (first, first + 1, first + 2)))
    result = []
    for kind, value, used in choices:
        rest = list(tiles)
        for tile in used:
            rest.remove(tile)
        result.extend(((kind, value),) + tail for tail in partitions(tuple(rest), count - 1))
    return tuple(result)


@dataclass(frozen=True)
class Shape:
    kind: str
    pair: int = 0
    groups: tuple = ()


def shapes(hand, codes=(), rules=None):
    rules = rules or Rules()
    if len(codes) > 5 or len(hand) != 17 - 3 * len(codes):
        return ()
    if any(type(t) is not int or t not in TILES for t in hand):
        return ()
    try:
        all_tiles = list(hand) + [t for code in codes for t in meld_tiles(code)]
    except (ValueError, TypeError):
        return ()
    if any(n > 4 for n in Counter(all_tiles).values()):
        return ()
    counts = Counter(hand)
    result = []
    for pair, n in counts.items():
        if n < 2:
            continue
        rest = list(sorted(hand))
        rest.remove(pair)
        rest.remove(pair)
        result.extend(Shape("standard", pair, groups) for groups in partitions(tuple(rest), 5 - len(codes)))
    # Seven pairs plus one chow/pung is explicitly described by Ging. A single
    # declared group is accepted; no second group fits this seventeen-tile shape.
    if rules.seven_pairs and len(codes) <= 1:
        groups = [()] if codes else [("k", t) for t, n in counts.items() if n >= 3]
        if not codes:
            groups += [("s", t + 1) for t in TILES if t % 10 <= 7 and all(counts[x] for x in (t, t + 1, t + 2))]
        for group in groups:
            rest = counts.copy()
            if group:
                rest.subtract(meld_tiles(group[0] + str(group[1])))
            rest = +rest
            valid = all(n % 2 == 0 for n in rest.values()) if rules.pair_quads_as_two else len(rest) == 7 and set(rest.values()) == {2}
            if sum(rest.values()) == 14 and valid:
                result.append(Shape("seven_pairs", groups=(group,) if group else ()))
    return tuple(result)


@lru_cache(maxsize=8192)
def _waits(hand, codes, rules):
    return frozenset(t for t in TILES if shapes(hand + (t,), codes, rules))


def waits(hand, codes=(), rules=None):
    return set(_waits(tuple(sorted(hand)), tuple(codes), rules or Rules()))


@dataclass(frozen=True)
class Quote:
    points: int
    items: tuple
    shape: str

    @property
    def fan_ids(self):
        return [f"PH|{name}|{value}" for name, value in self.items]


def score(hand, codes=(), flowers=(), *, winning_tile, self_draw=False, discard_count=99, rules=None):
    rules = rules or Rules()
    candidates = shapes(hand, codes, rules)
    if not candidates or winning_tile not in hand:
        return None
    previous = list(hand)
    previous.remove(winning_tile)
    waiting = waits(previous, codes, rules)
    quotes = []
    exposed = [(c[0], int(c[1:])) for c in codes]
    for shape in candidates:
        items = [("和牌", rules.base_points)]
        if rules.ambitions:
            groups = exposed + list(shape.groups)
            if not flowers:
                items.append(("无花", 1))
            if not any(c[0] in "skg" for c in codes):
                items.append(("门清", 1))
            if len(codes) == 5 and all(c[0] != "G" for c in codes):
                items.append(("全露", 1))
            if shape.kind == "seven_pairs":
                items.append(("七对一组", 2))
            if shape.kind == "standard" and all(k == "s" for k, _ in groups):
                items.append(("全顺", 1))
            if shape.kind == "standard" and all(k != "s" for k, _ in groups):
                items.append(("全刻", 1))
            physical = list(hand) + [t for c in codes for t in meld_tiles(c)]
            if len({t // 10 for t in physical}) == 1:
                items.append(("清一色", 1))
            if any(set(range(s * 10 + 1, s * 10 + 10)) <= set(physical) for s in (1, 2, 3)):
                items.append(("一至九", 1))
            if discard_count < 5:
                items.append(("第五张前", 2))
            concealed = list(shape.groups)
            if len(waiting) == 1:
                middle = any(k == "s" and t == winning_tile for k, t in concealed)
                items.append(("嵌张" if middle else "独听", 1))
            elif shape.kind == "standard" and any(k == "k" and t == winning_tile for k, t in concealed) and Counter(previous)[shape.pair] >= 2:
                items.append(("双碰", 1))
        quotes.append(Quote(sum(v for _, v in items), tuple(items), shape.kind))
    return max(quotes, key=lambda q: q.points)


def payments(winner, points, *, payer=None, self_draw=False, rules=None, multiplier=1):
    rules = rules or Rules()
    if type(winner) is not int or winner not in range(4) or (not self_draw and (type(payer) is not int or payer not in range(4) or payer == winner)):
        raise ValueError("非法结算座位")
    changes = [0] * 4
    for index in range(4):
        if index == winner or not self_draw and rules.discard_payment == "discarder" and index != payer:
            continue
        amount = points * multiplier * (rules.self_draw_multiplier if self_draw else 1)
        changes[index] -= amount
        changes[winner] += amount
    return changes
