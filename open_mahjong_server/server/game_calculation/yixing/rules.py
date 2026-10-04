"""宜兴麻将规则书，2026-10-02 用户底本。

花数资格先于胡底及特殊倍数；固定牌型已含一花胡底（用户确认）。
复用现有四面子分解器，但牌型、风刻花、胜张归属与支付由本规则负责。
"""

from collections import Counter
from dataclasses import asdict, dataclass
from functools import lru_cache

from ..jiandan.decompose import Meld, find_standard_decompositions

RULE_VERSION = "yixing-user-20261002-om2"
SUB_RULE = "yixing/standard"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
WINDS = (41, 42, 43, 44)
DRAGONS = (45, 46, 47)
HONORS = WINDS + DRAGONS
TILES = NUMBERS + HONORS
FLOWERS = tuple(range(51, 59))
PATTERN_FLOWERS = {"混一色": 6, "碰碰胡": 6, "门清": 4, "清一色": 8, "七小对": 10, "全风板": 13}


def normalize_config(raw=None):
    if raw is None:
        raw = {}
    if not isinstance(raw, dict) or set(raw) - {"seven_pairs"}:
        raise ValueError("宜兴馆规仅支持七小对开关")
    value = raw.get("seven_pairs", False)
    if type(value) is not bool:
        raise ValueError("七小对开关必须为布尔值")
    return {"seven_pairs": value}


def parse_meld(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "skgG":
        raise ValueError("非法面子")
    tile = int(code[1:])
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError("非法顺子")
        return Meld("sequence", (tile-1, tile, tile+1), concealed=False, declared=True)
    if tile not in TILES:
        raise ValueError("花牌不能组成面子")
    return Meld("triplet" if code[0] == "k" else "kong", (tile,) * (3 if code[0] == "k" else 4),
                concealed=code[0] == "G", declared=True)


def _valid(hand, codes, complete):
    if not isinstance(hand, (list, tuple)) or not isinstance(codes, (list, tuple)):
        return None
    if len(codes) > 4 or len(hand) + 3 * len(codes) != (14 if complete else 13):
        return None
    if any(type(t) is not int or t not in TILES for t in hand):
        return None
    try:
        melds = tuple(parse_meld(code) for code in codes)
    except (ValueError, TypeError):
        return None
    all_tiles = tuple(hand) + tuple(t for m in melds for t in m.tiles)
    return None if any(c > 4 for c in Counter(all_tiles).values()) else (melds, all_tiles)


@lru_cache(maxsize=32768)
def _shapes(hand, codes, seven_pairs):
    valid = _valid(hand, codes, True)
    if valid is None:
        return ()
    melds, _ = valid
    result = [("standard", shape.pair.head, shape.melds)
              for shape in find_standard_decompositions(hand, melds, allow_quad_split=True)]
    if seven_pairs and not codes and all(c % 2 == 0 for c in Counter(hand).values()):
        result.append(("seven_pairs", 0, ()))
    return tuple(result)


def shapes(hand, melds=(), *, seven_pairs=False):
    if _valid(hand, melds, True) is None:
        return ()
    return _shapes(tuple(sorted(hand)), tuple(melds), seven_pairs)


@lru_cache(maxsize=32768)
def _waits(hand, codes, seven_pairs):
    valid = _valid(hand, codes, False)
    if valid is None:
        return frozenset()
    count = Counter(valid[1])
    return frozenset(t for t in TILES if count[t] < 4 and _shapes(tuple(sorted(hand + (t,))), codes, seven_pairs))


def waiting_tiles(hand, melds=(), *, seven_pairs=False):
    if _valid(hand, melds, False) is None:
        return set()
    return set(_waits(tuple(sorted(hand)), tuple(melds), seven_pairs))


@dataclass(frozen=True)
class FlowerItem:
    id: str
    name: str
    flowers: int


@dataclass(frozen=True)
class WinScore:
    shape: str
    patterns: tuple[FlowerItem, ...]
    extras: tuple[FlowerItem, ...]
    qualifying_flowers: int
    base_flowers: int
    multiplier: int
    multipliers: tuple[tuple[str, int], ...]
    points: int
    pair: int
    winning_triplet: int | None

    def as_dict(self):
        return asdict(self)

    @property
    def fan_names(self):
        result = [f"{item.name}（{item.flowers}花）" for item in self.patterns]
        if not self.patterns:
            result.append("胡牌底花（1花）")
        elif len(self.patterns) > 1:
            result.append(f"重复底花（-{len(self.patterns)-1}花）")
        result += [f"{item.name}（{item.flowers}花）" for item in self.extras]
        result += [f"{name}（×{factor}）" for name, factor in self.multipliers]
        return result


def _patterns(shape, all_tiles, melds):
    result = []
    suit_count = len({t // 10 for t in all_tiles if t in NUMBERS})
    if not suit_count:
        result.append("全风板")
    elif suit_count == 1:
        result.append("混一色" if any(t in HONORS for t in all_tiles) else "清一色")
    if shape == "seven_pairs":
        result.append("七小对")
    elif all(m.kind in ("triplet", "kong") for m in melds):
        result.append("碰碰胡")
    if all(m.concealed for m in melds):
        result.append("门清")
    return tuple(FlowerItem(name, name, PATTERN_FLOWERS[name]) for name in result)


def _extra_flowers(melds, flowers, seat_wind, winning_triplet):
    result = [FlowerItem("flower_tiles", "花牌", len(flowers))] if flowers else []
    for index, meld in enumerate(melds):
        if meld.kind not in ("triplet", "kong"):
            continue
        tile = meld.head
        concealed = meld.concealed and index != winning_triplet
        if tile in HONORS:
            value = (2 if tile in DRAGONS or tile == seat_wind else 1) + int(concealed)
            if meld.kind == "kong":
                value += 2
        elif meld.kind == "kong":
            value = 2 if concealed else 1
        else:
            continue
        kind = ("暗" if concealed else "明") + ("杠" if meld.kind == "kong" else "刻")
        name = ("箭牌" if tile in DRAGONS else "门风" if tile == seat_wind else "客风" if tile in WINDS else "数牌") + kind
        result.append(FlowerItem(f"{kind}:{tile}:{index}", name, value))
    return tuple(result)


def score_hand(hand, melds=(), flowers=(), *, winning_tile=None, self_draw=False, seat_wind=41,
               after_kong=False, sea_bottom=False, rob_kong=False, seven_pairs=False):
    if seat_wind not in WINDS or (self_draw and rob_kong):
        return None
    if not isinstance(flowers, (list, tuple)) or any(type(t) is not int or t not in FLOWERS for t in flowers) or len(set(flowers)) != len(flowers):
        return None
    valid = _valid(hand, melds, True)
    if valid is None:
        return None
    if winning_tile is None:
        winning_tile = hand[-1]
    if type(winning_tile) is not int or winning_tile not in hand:
        return None
    candidates = []
    for shape, pair, groups in shapes(hand, melds, seven_pairs=seven_pairs):
        patterns = _patterns(shape, valid[1], groups)
        # A ron tile can complete a pair, sequence or triplet. Enumerate every
        # valid attribution before deciding which concealed honor earns flowers.
        components = [None] if self_draw or shape == "seven_pairs" else (
            ([None] if pair == winning_tile else []) +
            [i if m.kind == "triplet" else None for i, m in enumerate(groups)
             if not m.declared and winning_tile in m.tiles])
        for triplet in set(components):
            extras = _extra_flowers(groups, flowers, seat_wind, triplet)
            fixed = sum(x.flowers for x in patterns) - max(0, len(patterns)-1) if patterns else 1
            base = fixed + sum(x.flowers for x in extras)
            eligible = base - 1
            single = shape == "standard" and len(melds) == 4 and len(hand) == 2
            minimum = 1 if single else 2 if self_draw else 3
            if eligible < minimum:
                continue
            factors = []
            if single:
                factors.append(("大吊车", 2))
            if after_kong and self_draw:
                factors.append(("杠开", 2))
            if sea_bottom:
                factors.append(("海底捞月" if self_draw else "海底放冲包三家", 2 if self_draw else 3))
            if rob_kong:
                factors.append(("抢杠包三家", 3))
            multiplier = 1
            for _, factor in factors:
                multiplier *= factor
            candidates.append(WinScore(shape, patterns, extras, eligible, base, multiplier,
                                       tuple(factors), base*multiplier, pair, triplet))
    return max(candidates, key=lambda x: (x.points, x.base_flowers), default=None)


def payments(winner, points, *, payer=None):
    if type(winner) is not int or winner not in range(4) or type(points) is not int or points <= 0:
        raise ValueError("非法和家或点数")
    if payer is not None and (type(payer) is not int or payer not in range(4) or payer == winner):
        raise ValueError("非法放铳者")
    changes = [0]*4
    for index in range(4) if payer is None else (payer,):
        if index != winner:
            changes[index] -= points
            changes[winner] += points
    return changes
