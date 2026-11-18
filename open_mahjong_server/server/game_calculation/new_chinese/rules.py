import json
from collections import Counter
from functools import lru_cache
from pathlib import Path

from ..compact_counter import pack_counter
from ..hongkong.models import DRAGONS, HONORS, NUMBERS, ORPHANS, TILES, WINDS, Shape
from ..hongkong.solver import _partitions, parse_meld

SUB_RULE = "new_chinese/standard"
RULE_VERSION = "new-chinese-2026-11"
DESCRIPTION = "新中式麻将是融合了立直麻将，国标麻将和东北麻将的宝牌衍生出的一种玩法，取精华去糟粕，已有十年以上的实战验证基础，兼具竞技性与娱乐性，牌型难度和对应的番数做过大量优化使其合理，欢迎加入，微信15513635592"
CATALOG = json.loads(Path(__file__).with_name("catalog.json").read_text(encoding="utf-8"))
VALUES = {row["name"]: row["fan"] for row in CATALOG}
LIMITS = {row["name"] for row in CATALOG if row["level"].startswith(("神级", "役满"))}
GREEN = {32, 33, 34, 36, 38, 47}
BASE = {
    "discard": ((2, 1), (4, 2), (20, 10), (20, 10)),
    "self_draw": ((4, 2), (8, 4), (32, 16), (32, 16)),
    "black": ((9, 6), (18, 12), (60, 40), (60, 40)),
    "bao_draw": ((8, 4), (16, 8), (40, 20), (40, 20)),
    "bao_seen": ((8, 4), (24, 16), (48, 32), (56, 32)),
}
WAIT_FANS = {3: "听三张", 4: "听四张", 5: "听五张", 6: "听六张", 7: "听七张", 8: "听八张"}


def normalize_config(value=None):
    value = {} if value is None else value
    if not isinstance(value, dict) or set(value) - {"rule_version", "exchange_three"}:
        raise ValueError("未知的新中式麻将配置")
    if value.get("rule_version", RULE_VERSION) != RULE_VERSION or type(value.get("exchange_three", False)) is not bool:
        raise ValueError("无效的新中式麻将版本或换三张设置")
    return {"rule_version": RULE_VERSION, "exchange_three": value.get("exchange_three", False)}


@lru_cache(maxsize=8192)
def _external(codes):
    return tuple(parse_meld(code) for code in codes)


@lru_cache(maxsize=16384)
def _candidates(before, codes):
    if len(codes) > 4 or len(before) + 3 * len(codes) != 13 or any(t not in TILES for t in before):
        return {}
    groups = _external(codes)
    physical = Counter(before)
    physical.update(t for group in groups for t in group.tiles)
    if any(n > 4 for n in physical.values()):
        return {}
    result = {}
    for tile in TILES:
        counts = Counter(before)
        counts[tile] += 1
        shapes = []
        if not groups:
            if set(counts) == ORPHANS:
                shapes.append(Shape("orphans"))
            if all(n % 2 == 0 for n in counts.values()):
                shapes.append(Shape("seven_pairs"))
        for pair, n in counts.items():
            if n < 2:
                continue
            counts[pair] -= 2
            for sets in _partitions(pack_counter(counts), 4 - len(groups)):
                melds = groups + sets
                if pair == tile:
                    shapes.append(Shape("standard", pair, melds))
                shapes.extend(Shape("standard", pair, melds, i) for i, meld in enumerate(melds)
                              if not meld.external and tile in meld.tiles)
            counts[pair] += 2
        if shapes:
            result[tile] = tuple(dict.fromkeys(shapes))
    return result


def _run_length(tiles):
    longest = current = 0
    for suit in (1, 2, 3):
        current = 0
        for rank in range(1, 10):
            current = current + 1 if suit * 10 + rank in tiles else 0
            longest = max(longest, current)
    return longest


@lru_cache(maxsize=16384)
def _shape_fans(before, codes, tile, shape, self_obtained):
    exposed = _external(codes)
    counts = Counter((*before, tile))
    counts.update(t for m in exposed for t in m.tiles)
    faces = set(counts)
    suits = {t // 10 for t in faces if t < 40}
    honors = {t for t in faces if t in HONORS}
    seven = shape.kind == "seven_pairs"
    standard = shape.kind == "standard"
    melds = shape.melds
    pungs = {m.tile for m in melds if m.kind != "sequence"}
    sequences = Counter(m.tile for m in melds if m.kind == "sequence")
    all_pungs = standard and not sequences
    dark = len(shape.concealed_pungs(self_obtained))
    kongs = {m.tile for m in exposed if m.kind == "kong"}
    n_kongs = len(kongs)
    undeclared = sum(n == 4 and t not in kongs for t, n in counts.items())
    closed = all(m.concealed for m in exposed)
    single = shape.winning_component < 0
    wind_sets = len(pungs.intersection(WINDS))
    dragon_sets = len(pungs.intersection(DRAGONS))
    flags = set()

    def add(name, condition):
        if condition:
            flags.add(name)

    add("大四喜之十八罗汉", set(WINDS) <= kongs)
    add("十八罗汉", n_kongs == 4)
    add("三杠", n_kongs == 3)
    add("双杠", n_kongs == 2)
    add("杠", n_kongs == 1)
    add("四暗刻单骑救主", dark == 4 and single)
    add("四暗刻", dark == 4)
    add("三暗刻", dark >= 3)
    add("双暗刻", dark >= 2)
    add("清幺九", faces <= ORPHANS and not honors)
    add("混幺九", faces <= ORPHANS and bool(honors and suits))
    add("字一色", not suits)
    add("清一色", len(suits) == 1 and not honors)
    add("混一色", len(suits) == 1 and bool(honors))
    add("五门齐", len(suits) == 3 and not honors.isdisjoint(WINDS) and not honors.isdisjoint(DRAGONS))
    add("绿一色", faces <= GREEN)
    add("大四喜", wind_sets == 4)
    add("大四喜字一色", wind_sets == 4 and shape.pair in HONORS)
    add("大四喜混幺九", wind_sets == 4 and shape.pair < 40 and shape.pair in ORPHANS)
    add("小四喜", wind_sets == 3 and shape.pair in WINDS or seven and set(WINDS) <= faces)
    add("三风刻", wind_sets >= 3)
    add("双风刻", wind_sets >= 2)
    add("大三元", dragon_sets == 3)
    add("小三元", dragon_sets == 2 and shape.pair in DRAGONS or seven and set(DRAGONS) <= faces)
    add("双箭刻", dragon_sets >= 2)
    add("全带幺", standard and shape.pair in ORPHANS and all(not ORPHANS.isdisjoint(m.tiles) for m in melds))
    add("门清", closed)
    add("一色三同顺", any(n >= 3 for n in sequences.values()))
    add("一色四同顺", 4 in sequences.values())
    add("三同刻", any(all(s * 10 + rank in pungs for s in (1, 2, 3)) for rank in range(1, 10)))
    add("三色三同顺", any(all(s * 10 + rank in sequences for s in (1, 2, 3)) for rank in range(2, 9)))

    pure_gates = not exposed and len(suits) == 1 and not honors and all(
        counts[suit * 10 + r] >= (3 if r in (1, 9) else 1) for suit in suits for r in range(1, 10))
    if pure_gates:
        base = {r: (3 if r in (1, 9) else 1) for r in range(1, 10)}
        exact = Counter(t % 10 for t in before) == base
        flags.add("九宝莲灯之天衣无缝" if exact else "九宝莲灯之准灯高挂")
    dark_sequences = {m.tile for i, m in enumerate(melds) if m.kind == "sequence" and not m.external
                      and (self_obtained or i != shape.winning_component)}
    for s in suits:
        straight = {s * 10 + r for r in (2, 5, 8)}
        if straight <= sequences.keys() or pure_gates and standard and (
                tile % 10 in (1, 9) or {t % 10 for t in before} == {1, 2, 3, 5, 6, 7, 8, 9} and tile % 10 == 4):
            flags.add("一条龙")
            if straight <= dark_sequences or pure_gates and self_obtained:
                flags.add("暗中一条龙")
    if shape.kind == "orphans":
        flags.add("国士无双十三面" if len(set(before)) == 13 else "十三幺")
    add("四归一", undeclared == 1)
    add("八归", undeclared == 2)
    add("十二归", undeclared == 3)
    if seven:
        add("七福星", faces == set(HONORS))
        run = _run_length(faces)
        for n, name in ((4, "连四对"), (5, "连五对"), (6, "连六对"), (7, "连七对")):
            add(name, run >= n)
        if undeclared:
            flags.add(("龙七对", "双龙七对", "三龙七对")[undeclared - 1])
            if undeclared >= 2 and before.count(tile) == 1:
                flags.add("双龙七对单骑救主" if undeclared == 2 else "三龙七对单骑救主")
    if "大四喜混幺九" in flags:
        flags.difference_update(("大四喜", "混幺九"))
    if "大四喜字一色" in flags:
        flags.difference_update(("大四喜", "字一色"))
    if "绿一色" in flags:
        flags.discard("清一色")
    if "七福星" in flags:
        flags.discard("字一色")
    values = {name: VALUES[name] for name in flags}
    if "大三元" in flags:
        if "字一色" in flags:
            values["大三元"] = 230 - VALUES["字一色"]
        elif "混幺九" in flags:
            values["大三元"] = 200 - VALUES["混幺九"]
    if "清一色" in flags and (closed or all_pungs or seven):
        values["清一色"] = 50 if all_pungs and faces.isdisjoint(ORPHANS) else 80
    return values


@lru_cache(maxsize=8192)
def _analysis(before, codes):
    candidates = _candidates(before, codes)
    groups = _external(codes)
    physical = Counter(before)
    physical.update(t for m in groups for t in m.tiles)
    has_terminal = not ORPHANS.isdisjoint(physical)
    nine = any(all(s * 10 + r in candidates for r in range(1, 10)) for s in (1, 2, 3))
    qualified = {}
    for tile, shapes in candidates.items():
        for self_obtained in (False, True):
            selected = []
            for shape in shapes:
                has_pung = shape.kind != "standard" or any(m.kind != "sequence" for m in shape.melds)
                if (has_terminal or nine) and has_pung or not LIMITS.isdisjoint(
                        _shape_fans(before, codes, tile, shape, self_obtained)):
                    selected.append(shape)
            if selected:
                qualified[tile, self_obtained] = tuple(selected)
    waits = frozenset(t for t, _ in qualified if physical[t] < 4)
    return candidates, qualified, waits, nine


def waits(hand, codes=(), *, structural=False, self_draw=None):
    before, codes = tuple(sorted(hand)), tuple(codes)
    candidates, qualified, legal, _ = _analysis(before, codes)
    if structural:
        counts = Counter(before)
        counts.update(t for m in _external(codes) for t in m.tiles)
        return {t for t in candidates if counts[t] < 4}
    if self_draw is None:
        return set(legal)
    return {t for t in legal if (t, self_draw) in qualified}


def _base(shape, before, codes, source, tile):
    if shape.kind == "seven_pairs":
        return 3
    if shape.kind == "standard" and all(m.kind != "sequence" for m in shape.melds):
        return 2
    if shape.kind == "standard" and shape.winning_component >= 0:
        group = shape.melds[shape.winning_component]
        if group.kind == "sequence" and group.tile == tile:
            return 1
    if shape.kind == "standard" and shape.winning_component < 0:
        if len(before) == 1 and not any(c.startswith("G") for c in codes) or source == "bao_seen" and tile in HONORS:
            return 1
    return 0


def score(hand, codes, winning_tile, source, *, after_kong=False, chain=0, visible=0,
          dealer=False, payer_dealer=False):
    if source not in BASE or winning_tile not in TILES:
        return None
    before, codes = tuple(sorted(hand)), tuple(codes)
    _, qualified, legal, nine = _analysis(before, codes)
    self_obtained = source in ("self_draw", "bao_seen", "bao_draw")
    best, best_key = None, None
    for tile in sorted(legal if source == "bao_draw" else legal.intersection((winning_tile,))):
        for shape in qualified.get((tile, self_obtained), ()):
            fans = dict(_shape_fans(before, codes, tile, shape, self_obtained))
            if "九宝莲灯之准灯高挂" not in fans:
                if nine:
                    fans["准听九"] = VALUES["准听九"]
                elif len(legal) in WAIT_FANS:
                    name = WAIT_FANS[len(legal)]
                    fans[name] = VALUES[name]
            if visible >= 3:
                fans["绝张"] = VALUES["绝张"]
            if after_kong and source in ("self_draw", "bao_draw"):
                fans["杠上开花"] = VALUES["杠上开花"]
            for minimum, name in ((2, "杠上杠"), (3, "三连杠"), (4, "连四杠")):
                if chain >= minimum:
                    fans[name] = VALUES[name]
            total = sum(fans.values())
            base = _base(shape, before, codes, source, tile)
            high, low = BASE[source][base]
            if source == "black":
                points = (high if dealer or payer_dealer else low) + total * (3 if dealer else 2)
            else:
                points = ordinary_points(high, low, total, dealer)
            key = (points, total, -tile, -shape.winning_component)
            if best_key is None or key > best_key:
                best_key = key
                best = dict(fan=total, fans={name: fans[name] for name in VALUES if name in fans},
                            points=points, base=base, represented=tile, source=source, shape=shape.kind)
    return best


def ordinary_points(high, low, fan, dealer):
    return (3 * high if dealer else high + 2 * low) + 3 * fan


def payments(detail, winner, payer=None, dealer=0):
    high, low = BASE[detail["source"]][detail["base"]]
    changes = [0] * 4
    if detail["source"] == "black":
        if payer not in range(4) or payer == winner:
            raise ValueError("点黑必须指定放炮者")
        base = high if dealer in (winner, payer) else low
        changes[payer] = -base - detail["fan"] * (3 if winner == dealer else 2)
    else:
        for i in range(4):
            if i != winner:
                base = high if dealer in (winner, i) else low
                changes[i] = -base - detail["fan"]
    changes[winner] = -sum(changes)
    return changes
