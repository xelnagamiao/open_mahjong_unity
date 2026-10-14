"""Cheah/Sloper 84-tile rules, with the published platform rulings in docs.

Physical fly tiles never change identity. The shared solver only supplies
structural witnesses; this module owns fan selection and non-exponential pay.
"""
from collections import Counter
from dataclasses import dataclass, asdict
from functools import lru_cache

from ..jokers import JokerPolicy, ExternalMeld, iter_winning_shapes, structural_waits

RULE_VERSION = "cheah-sloper-2018-platform-1"
SUB_RULE = "malaysia/traditional84"
DOTS = tuple(range(21, 30))
WINDS = tuple(range(41, 45))
DRAGONS = (45, 46, 47)
TILES = DOTS + WINDS + DRAGONS
FLOWERS = tuple(range(51, 59))
ANIMALS = (61, 62, 63, 64)
FACES = (71, 72, 73, 74)
BONUS = FLOWERS + ANIMALS + FACES
FLY = 81
PHYSICAL = TILES + (FLY,)
SETS = (FLOWERS[:4], FLOWERS[4:], ANIMALS, FACES)
POLICY = JokerPolicy(joker_tiles=(FLY,), logical_tiles=TILES,
    physical_tiles=PHYSICAL, physical_limits=((FLY, 4),),
    shapes=("standard", "seven_pairs"))


def tile_pool():
    return [t for t in PHYSICAL for _ in range(4)] + list(BONUS)


def normalize_config(value=None):
    if value is None:
        value = {}
    if not isinstance(value, dict) or set(value) - {"multiple_max"}:
        raise ValueError("马来西亚三人馆规仅支持 multiple_max")
    multiple = value.get("multiple_max", False)
    if type(multiple) is not bool:
        raise ValueError("multiple_max 必须为布尔值")
    return {"multiple_max": multiple}


@dataclass(frozen=True)
class Meld:
    kind: str
    physical: tuple
    logical: tuple
    source: int = -1
    concealed: bool = False

    def __post_init__(self):
        object.__setattr__(self, "physical", tuple(self.physical))
        object.__setattr__(self, "logical", tuple(self.logical))
        n = 4 if self.kind == "kong" else 3
        if self.kind not in ("sequence", "triplet", "kong") or len(self.physical) != n or len(self.logical) != n:
            raise ValueError("无效副露")
        if self.kind == "kong" and FLY in self.physical:
            raise ValueError("飞不能用于杠")
        if self.physical.count(FLY) > (1 if self.kind == "sequence" else 2):
            raise ValueError("副露用飞数量超限")
        if self.kind == "sequence":
            if sorted(self.logical) not in [list(range(t, t + 3)) for t in DOTS[:-2]]:
                raise ValueError("吃牌须为筒子连续三张")
        elif len(set(self.logical)) != 1:
            raise ValueError("碰杠须为同一牌种")
        if any(l not in TILES or (p != FLY and p != l) for p, l in zip(self.physical, self.logical)):
            raise ValueError("实体牌与逻辑牌不相符")

    def external(self):
        return ExternalMeld(self.kind, self.physical, self.logical, self.concealed)

    def code(self):
        prefix = "s" if self.kind == "sequence" else "G" if self.concealed else "g" if self.kind == "kong" else "k"
        return prefix + str(sorted(self.logical)[1] if self.kind == "sequence" else self.logical[0])

    def as_dict(self):
        return asdict(self)


@dataclass(frozen=True)
class Item:
    id: str
    name: str
    fan: int


@dataclass(frozen=True)
class Quote:
    fan: int
    points: int
    items: tuple
    maximums: tuple
    assignment: tuple = ()
    automatic: bool = False

    def as_dict(self):
        return asdict(self)

    @property
    def labels(self):
        return [f"MY|{x.id}|{x.fan}|{x.name}" for x in self.items] + [f"MY|max|满番|{x}" for x in self.maximums]


def bonus_items(bonuses, seat):
    if seat not in (0, 1, 2):
        raise ValueError("三人麻将仅有东南西三席")
    counts = Counter(bonuses)
    if any(t not in BONUS + (FLY,) or n > (4 if t == FLY else 1) for t, n in counts.items()):
        raise ValueError("花牌库存无效")
    result = []
    def add(key, name, count):
        if count:
            result.append(Item(key, name, count))
    add("faces", "人头", sum(counts[t] for t in FACES))
    add("animals", "动物", sum(counts[t] for t in ANIMALS))
    add("fly_flower", "飞当花", counts[FLY])
    for group, key, name in zip(SETS, ("seasons", "flowers", "animals_set", "faces_set"), ("四季齐", "四花齐", "四兽齐", "四人头齐")):
        add(key, name, int(all(counts[t] for t in group)))
    add("own_bonus", "正花", counts[51 + seat] + counts[55 + seat])
    add("four_bonus", "四号花", counts[54] + counts[58])
    return result


def honor_items(pungs, seat):
    result = []
    for t in sorted(pungs):
        value = int(t in (41, 44) or t in DRAGONS) + int(t == 41 + seat)
        if value:
            result.append(Item("honor_" + str(t), {41:"东风刻",42:"南风刻",43:"西风刻",44:"北风刻",45:"红中刻",46:"白板刻",47:"发财刻"}[t], value))
    return result


def side_fan(hand, melds, bonuses, seat):
    """Losers compare only source fan entries 1-11, plus declared fly flowers.

    Incomplete concealed hands use natural triplets only; wild assignments are
    fixed only for published melds. This avoids assigning the same fly twice.
    """
    pungs = [t for t, n in Counter(hand).items() if t in WINDS + DRAGONS and n >= 3]
    pungs.extend(m.logical[0] for m in melds if m.kind in ("triplet", "kong"))
    return sum(i.fan for i in bonus_items(bonuses, seat) + honor_items(pungs, seat))


def fly_count(hand, melds, bonuses):
    return list(hand).count(FLY) + list(bonuses).count(FLY) + sum(m.physical.count(FLY) for m in melds)


def _valid_supply(hand, melds, bonuses):
    counts = Counter(hand)
    counts.update(bonuses)
    for m in melds:
        counts.update(m.physical)
    return all(t in PHYSICAL + BONUS and n <= (1 if t in BONUS else 4) for t, n in counts.items())


def score_hand(hand, melds=(), bonuses=(), *, seat=0, winning_tile=None,
               self_draw=False, after_flower=False, after_kong=False,
               consecutive_kongs=0, rob_kong=False, first_tile=False,
               last_tile=False, opening_four_fly=False, multiple_max=False):
    hand, melds, bonuses = tuple(hand), tuple(melds), tuple(bonuses)
    if seat not in (0, 1, 2) or not _valid_supply(hand, melds, bonuses):
        return None
    if any(t not in PHYSICAL for t in hand):
        return None
    if opening_four_fly and hand.count(FLY) == 4 and not melds:
        return Quote(10, 300, (), ("起手四飞",), automatic=True)
    if winning_tile is not None and winning_tile not in hand:
        return None
    best = None
    # Scoring uses logical groups and self-draw status; it never depends on the
    # chosen winning component. Full witnesses retain the <=2 fly group rule.
    for shape in iter_winning_shapes(hand, [m.external() for m in melds], policy=POLICY, winning_tile=winning_tile):
        if any(g.kind == "triplet" and g.physical.count(FLY) == 3 for g in shape.groups):
            continue
        pungs = {g.tile for g in shape.melds if g.kind in ("triplet", "kong")}
        items = bonus_items(bonuses, seat) + honor_items([g.tile for g in shape.melds if g.kind in ("triplet", "kong")], seat)
        logical = shape.logical_tiles
        dots = any(t in DOTS for t in logical)
        honors = any(t in WINDS + DRAGONS for t in logical)
        if dots:
            items.append(Item("mixed_suit" if honors else "pure_suit", "混一色" if honors else "清一色", 1 if honors else 3))
        if len(pungs & set(DRAGONS)) == 2 and shape.pair in DRAGONS:
            items.append(Item("small_dragons", "小三元", 3))
        if shape.kind == "seven_pairs":
            items.append(Item("seven_pairs", "七对", 1))
        elif len(shape.melds) == 4:
            if all(g.kind == "sequence" for g in shape.melds) and shape.pair in DOTS:
                items.append(Item("all_chows", "平和", 1))
            if all(g.kind in ("triplet", "kong") for g in shape.melds):
                items.append(Item("all_pungs", "碰碰和", 2))
        if self_draw and after_flower:
            items.append(Item("flower_win", "补花和", 1))
        if self_draw and after_kong:
            items.append(Item("kong_win", "杠上开花", 1))
        if rob_kong:
            items.append(Item("rob_kong", "抢杠", 1))
        fan = sum(x.fan for x in items)
        maximums = []
        if not bonuses:
            maximums.append("无花")
        if len(pungs & set(WINDS)) == 4:
            maximums.append("大四喜")
        elif len(pungs & set(WINDS)) == 3 and shape.pair in WINDS:
            maximums.append("小四喜")
        if set(DRAGONS) <= pungs:
            maximums.append("大三元")
        if sum(g.kind == "kong" for g in shape.melds) == 4:
            maximums.append("四杠")
        if self_draw and consecutive_kongs >= 2:
            maximums.append("连杠和")
        if self_draw and len(shape.concealed_pungs(True)) == 4:
            maximums.append("四暗刻")
        if fly_count(hand, melds, bonuses) == 4:
            maximums.append("四飞")
        if not dots:
            maximums.append("字一色")
        if set(BONUS) <= set(bonuses):
            maximums.append("十六花")
        if first_tile:
            maximums.append("首张和")
        if last_tile:
            maximums.append("末张和")
        if self_draw and not melds and not honors:
            c = Counter(shape.closed_logical_tiles)
            if c[21] >= 3 and c[29] >= 3 and all(c[t] >= 1 for t in DOTS[1:-1]):
                maximums.append("九莲宝灯")
        if fan >= 10:
            maximums.append("十番以上")
        if fan < 5 and not maximums:
            continue
        points = 100 + (200 * (len(maximums) if multiple_max else 1) if maximums else fan * 20)
        quote = Quote(fan, points, tuple(items), tuple(maximums), shape.assignment)
        if best is None or (quote.points, quote.fan, quote.assignment) > (best.points, best.fan, best.assignment):
            best = quote
    return best


@lru_cache(maxsize=4096)
def waits(hand, melds=()):
    return structural_waits(hand, [m.external() for m in melds], policy=POLICY)


def transfer(changes, payer, receiver, amount):
    if payer == receiver or payer not in range(3) or receiver not in range(3) or type(amount) is not int or amount < 0:
        raise ValueError("无效转分")
    changes[payer] -= amount
    changes[receiver] += amount


def payments(winner, quote, sides, flies, *, payer=None, liable=None):
    if winner not in range(3) or len(sides) != 3 or len(flies) != 3 or payer == winner or liable == winner:
        raise ValueError("无效三人结算")
    changes, ledger = [0, 0, 0], []
    def pay(a, b, value, reason):
        if value:
            transfer(changes, a, b, value)
            ledger.append(dict(payer=a, receiver=b, points=value, reason=reason))
    losers = [i for i in range(3) if i != winner]
    for i in losers:
        pay(liable if liable is not None else i, winner, quote.points * (2 if payer is None or payer == i else 1), "包牌和分" if liable is not None else "和分")
    if not quote.maximums:
        a, b = sorted(losers, key=lambda i: (sides[i], i))
        value = (sides[b] - sides[a] if sides[a] >= 5 else sides[b]) if sides[b] >= 5 else 0
        pay(a, b, value * 20, "输家花分")
    for a in range(3):
        for b in range(a + 1, 3):
            delta = (flies[b] - flies[a]) * 50
            pay(a if delta >= 0 else b, b if delta >= 0 else a, abs(delta), "飞牌分")
    return changes, ledger
