"""Singapore scoring. Physical tiles and payments never depend on client claims.

Source: SPGG Mahjong Rules & Regulations, January 2024, pp. 2–5.
The platform freezes the amended limit at five tai; see docs/singapore-spgg2024.md.
"""
from collections import Counter
from functools import lru_cache

from ..jokers import TILES, NUMBERS, HONORS, ORPHANS, JokerPolicy, iter_winning_shapes
from ..jokers import structural_waits as solve_waits

RULE = "singapore"
SUB_RULE = "singapore/spgg2024"
VERSION = "spgg-2024-om1"
FLOWERS = tuple(range(51, 59))
ANIMALS = (61, 62, 63, 64)  # cat, mouse, rooster, centipede
BONUS_TILES = FLOWERS + ANIMALS
WALL = tuple(t for t in TILES for _ in range(4)) + BONUS_TILES
POLICY = JokerPolicy(shapes=("standard", "thirteen_orphans"))
CONFIG = {"rule_version": VERSION, "fan_cap": 5, "dead_wall": 15}


def bonus_fans(flowers, seat):
    tiles = set(flowers)
    result = []
    for start, name in ((51, "四季"), (55, "四君子")):
        if set(range(start, start + 4)) <= tiles:
            result.append((name, 2))
        elif start + seat in tiles:
            result.append(("正花", 1))
    animals = tiles.intersection(ANIMALS)
    if len(animals) == 4:
        result.append(("四动物", 5))
    elif animals:
        result.append(("动物", len(animals)))
    return result


def detail(fans, *, limit=False, orphans=False, pinghu=False, automatic=False):
    raw = sum(value for _, value in fans)
    tai = min(5, raw)
    return {"is_win": tai >= 1, "tai": tai, "raw_tai": raw, "fan_names": [f"{name}（{value}台）" for name, value in fans],
            "fan_items": list(fans), "limit": limit, "orphans": orphans, "pinghu": pinghu,
            "automatic": automatic, "rule_version": VERSION}


def automatic_meld_win(melds):
    pungs = {int(c[1:]) for c in melds if c and c[0] in "kgG"}
    if {45, 46, 47} <= pungs:
        return detail([("大三元自动和", 5)], limit=True, automatic=True)
    if {41, 42, 43, 44} <= pungs:
        return detail([("大四喜自动和", 5)], limit=True, automatic=True)
    return None


@lru_cache(maxsize=16384)
def structural_waits(hand, melds=()):
    return solve_waits(hand, melds, POLICY)


def _nine_gates(hand, melds):
    if melds or len(hand) != 14 or any(t not in NUMBERS for t in hand):
        return False
    suits = {t // 10 for t in hand}
    if len(suits) != 1:
        return False
    counts = Counter(t % 10 for t in hand)
    return counts[1] >= 3 and counts[9] >= 3 and all(counts[r] for r in range(2, 9))


def score(hand, melds=(), flowers=(), *, winning_tile=None, self_draw=False,
          seat=0, round_wind=41, replacement=False, kong_chain=0, last_tile=False,
          heavenly=False, earthly=False):
    if seat not in range(4) or round_wind not in (41, 42, 43, 44):
        return None
    if any(type(t) is not int or t not in BONUS_TILES for t in flowers) or len(set(flowers)) != len(flowers):
        return None
    hand, melds = tuple(hand), tuple(melds)
    candidates = []
    for shape in iter_winning_shapes(hand, melds, POLICY, winning_tile=winning_tile):
        if shape.kind == "thirteen_orphans":
            candidates.append(detail([("十三幺", 5)], limit=True, orphans=True))
            continue
        groups = shape.melds
        tiles = shape.logical_tiles
        pair = shape.pair
        pungs = {g.tile for g in groups if g.kind in ("triplet", "kong")}
        dragons, winds = pungs.intersection((45, 46, 47)), pungs.intersection((41, 42, 43, 44))
        all_pungs = all(g.kind != "sequence" for g in groups)
        limit = None
        if heavenly: limit = "天胡"
        elif earthly: limit = "地胡"
        elif len(dragons) == 3: limit = "大三元"
        elif len(winds) == 4: limit = "大四喜"
        elif all(t in HONORS for t in tiles): limit = "字一色"
        elif all_pungs and self_draw and all(c.startswith("G") for c in melds): limit = "四暗刻"
        elif sum(g.kind == "kong" for g in groups) == 4: limit = "四杠"
        elif all(t in NUMBERS and t % 10 in (1, 9) for t in tiles): limit = "清幺九"
        elif _nine_gates(hand, melds): limit = "九莲宝灯"
        if limit:
            candidates.append(detail([(limit, 5)], limit=True))
            continue
        fans = bonus_fans(flowers, seat)
        if len(dragons) == 2 and pair in (45, 46, 47):
            fans.append(("小三元", 3))
        elif dragons:
            fans.append(("三元刻", len(dragons)))
        if len(winds) == 3 and pair in (41, 42, 43, 44):
            fans.append(("小四喜", 3 + int(seat + 41 == round_wind and round_wind in winds)))
        else:
            if seat + 41 in winds: fans.append(("门风刻", 1))
            if round_wind in winds: fans.append(("圈风刻", 1))
        if all_pungs: fans.append(("碰碰胡", 2))
        suits = {t // 10 for t in tiles if t in NUMBERS}
        all_green = all(t in (32, 33, 34, 36, 38, 47) for t in tiles)
        full_flush = len(suits) == 1 and not any(t in HONORS for t in tiles)
        if all_green: fans.append(("绿一色", 4))
        elif full_flush: fans.append(("清一色", 4))
        elif len(suits) == 1: fans.append(("混一色", 2))
        if all_pungs and pair in NUMBERS and pair % 10 in (1, 9) and all(t in ORPHANS for t in tiles):
            fans.append(("混幺九", 2))
        pinghu = all(g.kind == "sequence" for g in groups) and pair not in (45, 46, 47, 41 + seat, round_wind)
        if pinghu and not self_draw:
            if winning_tile is None: pinghu = False
            else:
                before = list(hand); before.remove(winning_tile)
                winning_group = shape.groups[shape.winning_group] if shape.winning_group >= 0 else None
                multiple = len(structural_waits(tuple(sorted(before)), melds)) >= 2
                allowed_wait = winning_group is not None and (winning_group.kind == "pair" or
                    (winning_group.kind == "sequence" and winning_tile != winning_group.tile and
                     not (winning_tile % 10 == 3 and min(winning_group.tiles) % 10 == 1) and
                     not (winning_tile % 10 == 7 and max(winning_group.tiles) % 10 == 9)))
                pinghu = multiple and allowed_wait
        if pinghu:
            if not flowers and full_flush:
                fans = [(n, v) for n, v in fans if n != "清一色"] + [("清一色平胡", 5)]
            else: fans.append(("平胡", 1 if flowers else 4))
        if replacement: fans.append(("杠杠和" if kong_chain >= 2 else "补牌和", 3 if kong_chain >= 2 else 1))
        if last_tile: fans.append(("海底", 1))
        value = detail(fans, pinghu=pinghu and not flowers)
        if value["is_win"]: candidates.append(value)
    return max(candidates, key=lambda d: (d["tai"], d["pinghu"], d["raw_tai"]), default=None)


def payments(winner, value, *, payer=None, liable=None):
    tai = value["tai"]
    if not 1 <= tai <= 5 or winner not in range(4) or payer == winner or liable == winner:
        raise ValueError("非法新加坡结算")
    full = 20 if tai == 4 and value.get("pinghu") else 2 ** tai
    half = full // 2
    changes = {i: 0 for i in range(4)}
    for seat in range(4):
        if seat == winner: continue
        amount = full if payer is None or seat == payer or value.get("orphans") else half
        changes[liable if liable is not None else seat] -= amount
        changes[winner] += amount
    return changes


def marriage_keys(tiles):
    tiles = set(tiles)
    pairs = [(f"花配{i + 1}", frozenset((51 + i, 55 + i)), 1) for i in range(4)]
    pairs += [("猫鼠", frozenset((61, 62)), 1), ("鸡虫", frozenset((63, 64)), 1),
              ("四季", frozenset(range(51, 55)), 2), ("四君子", frozenset(range(55, 59)), 2),
              ("四动物", frozenset(ANIMALS), 2)]
    return {name: (required, points) for name, required, points in pairs if required <= tiles}


def liability_trigger(melds_before, flowers, tile, *, seat, round_wind):
    """Public information only; called before accepting the new exposed meld."""
    exposed = [c for c in melds_before if c and c[0] in "skg"]
    values = [int(c[1:]) for c in exposed]
    pungs = {int(c[1:]) for c in exposed if c[0] != "s"}
    if tile in NUMBERS and sum(v // 10 == tile // 10 for v in values if v in NUMBERS) >= 3: return True
    if tile in NUMBERS and tile % 10 in (1, 9) and sum(v in NUMBERS and v % 10 in (1, 9) for v in pungs) >= 3: return True
    if tile in range(41, 45) and len(pungs.intersection(range(41, 45))) >= 3: return True
    if tile in HONORS and len(pungs.intersection(HONORS)) >= 3: return True
    if tile in (45, 46, 47) and len(pungs.intersection((45, 46, 47))) >= 2: return True
    shown = sum(v for _, v in bonus_fans(flowers, seat))
    shown += len(pungs.intersection((45, 46, 47))) + int(41 + seat in pungs) + int(round_wind in pungs)
    return ((shown >= 3 and tile == round_wind == 41 + seat) or
            (shown >= 4 and tile in (45, 46, 47, 41 + seat, round_wind)))
