from collections import Counter
from dataclasses import asdict, dataclass
from functools import lru_cache

from ..jiandan.decompose import Meld, find_standard_decompositions

RULE = "nanjing"
SUB_RULE = "nanjing/mil2024"
VERSION = "mil-nanjing-2024-v1"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
WINDS = (41, 42, 43, 44)
TILES = NUMBERS + WINDS
FLOWERS = (45, 46, 47) + tuple(range(51, 59))
PHYSICAL = tuple(t for t in TILES + (45, 46, 47) for _ in range(4)) + tuple(range(51, 59))
PATTERNS = {"门清": 10, "混一色": 20, "碰碰和": 20, "清一色": 30,
            "无花果": 20, "七对": 40, "龙七对": 90, "全求独钓": 60,
            "花开": 10, "杠开": 20, "抢杠": 0, "压绝": 20}
ACCIDENTAL = {"无花果", "花开", "杠开", "抢杠", "压绝"}


def normalize_config(raw=None):
    if raw is None:
        raw = {}
    if not isinstance(raw, dict) or set(raw) - {"rule_version"} or raw.get("rule_version", VERSION) != VERSION:
        raise ValueError("南京麻将仅支持 MIL 2024 规则")
    return {"rule_version": VERSION}


def parse_meld(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "kgG":
        raise ValueError("南京麻将不能吃牌")
    tile = int(code[1:])
    if tile not in TILES:
        raise ValueError("花牌不能组成手牌面子")
    return Meld("triplet" if code[0] == "k" else "kong",
                (tile,) * (3 if code[0] == "k" else 4), concealed=code[0] == "G", declared=True)


def valid_tiles(hand, melds=(), complete=True):
    if not isinstance(hand, (list, tuple)) or not isinstance(melds, (list, tuple)):
        return None
    if len(melds) > 4 or len(hand) + 3 * len(melds) != (14 if complete else 13):
        return None
    if any(type(t) is not int or t not in TILES for t in hand):
        return None
    try:
        parsed = tuple(parse_meld(m) for m in melds)
    except (ValueError, TypeError):
        return None
    all_tiles = tuple(hand) + tuple(t for m in parsed for t in m.tiles)
    return None if any(c > 4 for c in Counter(all_tiles).values()) else (parsed, all_tiles)


@lru_cache(maxsize=32768)
def _shapes(hand, codes):
    valid = valid_tiles(hand, codes)
    if valid is None:
        return ()
    results = [("standard", d.pair.head, d.melds)
               for d in find_standard_decompositions(hand, valid[0], allow_quad_split=True)]
    if not codes and all(n % 2 == 0 for n in Counter(hand).values()):
        results.append(("seven_pairs", 0, ()))
    return tuple(results)


def shapes(hand, melds=()):
    return _shapes(tuple(sorted(hand)), tuple(melds)) if valid_tiles(hand, melds) else ()


@lru_cache(maxsize=32768)
def _waits(hand, codes):
    valid = valid_tiles(hand, codes, False)
    if valid is None:
        return frozenset()
    counts = Counter(valid[1])
    return frozenset(t for t in TILES if counts[t] < 4 and _shapes(tuple(sorted(hand + (t,))), codes))


def waiting_tiles(hand, melds=()):
    return set(_waits(tuple(sorted(hand)), tuple(melds))) if valid_tiles(hand, melds, False) else set()


@dataclass(frozen=True)
class ScoreItem:
    name: str
    points: int


@dataclass(frozen=True)
class WinScore:
    shape: str
    patterns: tuple[ScoreItem, ...]
    flowers: tuple[ScoreItem, ...]
    points: int
    uncapped_points: int
    qualified: bool

    @property
    def pattern_points(self):
        return sum(p.points for p in self.patterns)

    @property
    def names(self):
        return {p.name for p in self.patterns}

    @property
    def fan_ids(self):
        return ["NJ|底分|10"] + [f"NJ|{x.name}|{x.points}" for x in self.patterns + self.flowers]

    def as_dict(self):
        return asdict(self)


def _flower_valid(flowers):
    if not isinstance(flowers, (list, tuple)) or any(type(t) is not int or t not in FLOWERS for t in flowers):
        return False
    return all(n <= (4 if t < 50 else 1) for t, n in Counter(flowers).items())


def _narrow_wait(shape, pair, groups, tile, waits):
    if len(waits) != 1 or shape != "standard":
        return False
    if pair == tile:
        return True
    return any(not m.declared and m.kind == "sequence" and
               (tile == m.tiles[1] or m.tiles[0] % 10 == 1 and tile == m.tiles[2]
                or m.tiles[0] % 10 == 7 and tile == m.tiles[0]) for m in groups)


def score_hand(hand, melds=(), flowers=(), *, winning_tile=None, self_draw=False,
               after_flower=False, after_kong=False, rob_kong=False, public_pungs=(),
               pung_claimed=None, ignore_minimum=False, forced_patterns=()):
    if not _flower_valid(flowers) or rob_kong and self_draw:
        return None
    if forced_patterns:
        if any(name not in ("全求独钓", "清一色") for name in forced_patterns):
            return None
        parsed = [parse_meld(m) for m in melds]
        tiles = list(hand) + [t for m in parsed for t in m.tiles]
        suit_count = len({t // 10 for t in tiles if t < 40})
        names = set(forced_patterns)
        if suit_count == 1 and "清一色" not in names:
            names.add("混一色" if any(t in WINDS for t in tiles) else "清一色")
        if not (any(m.startswith("k") for m in melds) if pung_claimed is None else pung_claimed):
            names.add("门清")
        items = tuple(ScoreItem(n, PATTERNS[n]) for n in PATTERNS if n in names)
        extras = (ScoreItem("花牌", len(flowers)),) if flowers else (ScoreItem("无花果", 20),)
        if not flowers:
            items += extras
            extras = ()
        if suit_count == 2 and "清一色" not in names:
            extras += (ScoreItem("缺一门",1),)
        wind_count = sum(m.head in WINDS for m in parsed)
        if wind_count:
            extras += (ScoreItem("风牌刻",wind_count),)
        for code,name,value in (("g","明杠",1),("G","暗杠",2)):
            count = sum(m.startswith(code) for m in melds)
            if count:
                extras += (ScoreItem(name,count*value),)
        raw = 10 + sum(x.points for x in items + extras)
        return WinScore("liability", items, extras, min(120, raw), raw, True)
    if not hand or winning_tile not in hand:
        return None
    valid = valid_tiles(hand, melds)
    if valid is None:
        return None
    before = list(hand)
    before.remove(winning_tile)
    waits = waiting_tiles(before, melds)
    suit_count = len({t // 10 for t in valid[1] if t < 40})
    have_honors = any(t in WINDS for t in valid[1])
    pung_claimed = any(m.startswith("k") for m in melds) if pung_claimed is None else pung_claimed
    candidates = []
    for shape, pair, groups in shapes(hand, melds):
        names = set()
        if suit_count == 1:
            names.add("混一色" if have_honors else "清一色")
        if shape == "seven_pairs":
            names.add("龙七对" if before.count(winning_tile) == 3 else "七对")
        elif len(hand) == 2:
            names.add("全求独钓")
        elif all(m.kind != "sequence" for m in groups):
            names.add("碰碰和")
        if not pung_claimed and shape != "seven_pairs":
            names.add("门清")
        if not flowers:
            names.add("无花果")
        if self_draw and after_kong:
            names.add("杠开")
        elif self_draw and after_flower:
            names.add("花开")
        if rob_kong:
            names.add("抢杠")
        narrow = _narrow_wait(shape, pair, groups, winning_tile, waits)
        if narrow and winning_tile in public_pungs:
            names.add("压绝")
        qualified = bool(names - ACCIDENTAL) or len(flowers) >= 4
        if not qualified and not ignore_minimum:
            continue
        extras = []
        if flowers:
            extras.append(ScoreItem("花牌", len(flowers)))
        if suit_count == 2:
            extras.append(ScoreItem("缺一门", 1))
        if narrow and "压绝" not in names:
            extras.append(ScoreItem("压档", 1))
        wind_sets = sum(m.kind != "sequence" and m.head in WINDS for m in groups)
        if wind_sets:
            extras.append(ScoreItem("风牌刻", wind_sets))
        for code, name, value in (("g", "明杠", 1), ("G", "暗杠", 2)):
            count = sum(m.startswith(code) for m in melds)
            if count:
                extras.append(ScoreItem(name, count * value))
        patterns = tuple(ScoreItem(n, PATTERNS[n]) for n in PATTERNS if n in names)
        raw = 10 + sum(x.points for x in patterns) + sum(x.points for x in extras)
        candidates.append(WinScore(shape, patterns, tuple(extras), min(120, raw), raw, qualified))
    return max(candidates, key=lambda q: (q.qualified, q.points, q.uncapped_points)) if candidates else None


def payments(winner, points, *, payer=None, multiplier=1, responsible=(), seat_order=(0,1,2,3)):
    if winner not in range(4) or points < 0 or payer == winner or payer is not None and payer not in range(4):
        raise ValueError("无效的支付参数")
    payers = sorted(set(responsible)) if responsible else ([payer] if payer is not None else [i for i in range(4) if i != winner])
    if not payers or winner in payers or any(i not in range(4) for i in payers):
        raise ValueError("无效的承包座位")
    if len(seat_order) != 4 or set(seat_order) != set(range(4)):
        raise ValueError("无效的起始座位顺序")
    total = points * multiplier * (3 if payer is None else 1)
    quotient, remainder = divmod(total, len(payers))
    result = [0] * 4
    result[winner] = total
    for index in payers:
        result[index] -= quotient
    if remainder and payer is not None:
        result[payer] -= remainder
    elif remainder:
        for index in [i for i in seat_order if i in payers][:remainder]:
            result[index] -= 1
    return result
