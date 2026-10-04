"""MIL《推倒和麻将（推广）竞赛规则（试行2024版）》七至九节。

报听不是和牌门槛；番值相加，最多32番，支付基数为2+番数。
结构枚举只借用现有十三张求解器的四面子/十三幺拆分，不借用港麻计分。
全不靠按本书的16选14，不能用要求九数牌或七字齐全的其他规则替代。
"""

from collections import Counter
from functools import lru_cache
from itertools import permutations

from ..hongkong.models import HongKongRules, TILES, NUMBERS, HONORS, ORPHANS
from ..hongkong.solver import decompositions, parse_meld

SUB_RULE = "guangdong/tuidao_mil2024"
EDITION = "mil-tuidao-2024-om1"
STRUCTURE = HongKongRules(sub_rule="hongkong/qingzhang")
FANS = {
    "门清": 2, "平和": 2, "断幺": 2, "报听": 2,
    "碰碰和": 6, "全带幺": 6, "大吊车": 6, "混一色": 6,
    "抢杠": 8, "海底": 8, "杠上开花": 8, "全不靠": 8, "清龙": 8,
    "天听": 16, "七对": 16, "清一色": 16, "豪华七对": 24,
    "天和": 32, "十三幺": 32, "大三元": 32, "字一色": 32, "四暗刻": 32, "大四喜": 32,
}
KNITTED_SETS = tuple(frozenset(HONORS) | {s*10+r for s, ranks in enumerate(p, 1) for r in ranks}
                     for p in permutations(((1, 4, 7), (2, 5, 8), (3, 6, 9))))


def valid_tiles(hand, melds=(), *, complete=True):
    if not isinstance(hand, (tuple, list)) or not isinstance(melds, (tuple, list)):
        return False
    if len(melds) > 4 or len(hand) != 3*(4-len(melds)) + (2 if complete else 1):
        return False
    if any(type(t) is not int or t not in TILES for t in hand):
        return False
    try:
        physical = Counter(hand)
        physical.update(t for code in melds for t in parse_meld(code).tiles)
        return max(physical.values(), default=0) <= 4
    except (TypeError, ValueError):
        return False


def special_shape(hand, melds=()):
    if melds or not valid_tiles(hand, melds):
        return None
    counts = Counter(hand)
    if set(counts) == ORPHANS and sorted(counts.values()) == [1]*12+[2]:
        return "十三幺"
    if all(n % 2 == 0 for n in counts.values()):
        return "豪华七对" if 4 in counts.values() else "七对"
    if len(counts) == 14 and any(set(counts) <= permitted for permitted in KNITTED_SETS):
        return "全不靠"
    return None


@lru_cache(maxsize=32768)
def _waits(hand, melds):
    if not valid_tiles(hand, melds, complete=False):
        return frozenset()
    return frozenset(t for t in TILES if is_complete(hand+(t,), melds))


def waits(hand, melds=()):
    if not valid_tiles(hand, melds, complete=False):
        return set()
    return set(_waits(tuple(sorted(hand)), tuple(melds)))


def is_complete(hand, melds=()):
    return valid_tiles(hand, melds) and bool(special_shape(hand, melds)
        or decompositions(hand, melds, STRUCTURE))


def _base_fans(tiles, melds, *, declared_ready, heavenly_ready, self_draw,
               first_draw, rob_kong, last_discard, replacement):
    fans = set()
    if not any(code[0] != "G" for code in melds):
        fans.add("门清")
    if all(t in NUMBERS and t % 10 not in (1, 9) for t in tiles):
        fans.add("断幺")
    suits = {t//10 for t in tiles if t in NUMBERS}
    if not suits:
        fans.add("字一色")
    elif len(suits) == 1:
        fans.add("混一色" if any(t in HONORS for t in tiles) else "清一色")
    if declared_ready:
        fans.add("天听" if heavenly_ready else "报听")
    if rob_kong and not self_draw:
        fans.add("抢杠")
    if last_discard and not self_draw and not rob_kong:
        fans.add("海底")
    if replacement and self_draw:
        fans.add("杠上开花")
    if first_draw and self_draw:
        fans.add("天和")
        fans.discard("门清")
    return fans


def score(hand, melds=(), *, winning_tile=None, self_draw=False, declared_ready=False,
          heavenly_ready=False, first_draw=False, rob_kong=False, last_discard=False,
          replacement=False):
    if not valid_tiles(hand, melds) or (winning_tile is not None and winning_tile not in hand):
        return None
    physical = list(hand) + [t for code in melds for t in parse_meld(code).tiles]
    base = _base_fans(physical, melds, declared_ready=declared_ready, heavenly_ready=heavenly_ready,
                     self_draw=self_draw, first_draw=first_draw, rob_kong=rob_kong,
                     last_discard=last_discard, replacement=replacement)
    candidates = []
    special = special_shape(hand, melds)
    if special:
        # All special shapes necessarily have no calls/kongs, so do not double-count 门清.
        candidates.append((base - {"门清"}) | {special})
    for shape in decompositions(hand, melds, STRUCTURE, winning_tile or 0):
        if shape.kind != "standard":
            continue
        fans = set(base)
        pungs = {m.tile for m in shape.melds if m.kind != "sequence"}
        if all(m.kind != "sequence" for m in shape.melds):
            fans.add("碰碰和")
        if all(m.kind == "sequence" for m in shape.melds) and shape.pair in NUMBERS:
            fans.add("平和")
        if shape.pair in ORPHANS and all(any(t in ORPHANS for t in m.tiles) for m in shape.melds):
            fans.add("全带幺")
        if len(hand) == 2:
            fans.add("大吊车")
        sequences = {m.tile for m in shape.melds if m.kind == "sequence"}
        if any({s*10+2, s*10+5, s*10+8} <= sequences for s in (1, 2, 3)):
            fans.add("清龙")
        if {45, 46, 47} <= pungs:
            fans.add("大三元")
        if {41, 42, 43, 44} <= pungs:
            fans.add("大四喜")
            fans.discard("碰碰和")
        concealed = len(shape.concealed_pungs(self_draw))
        if concealed == 4:
            fans.add("四暗刻")
            fans.difference_update(("碰碰和", "门清"))
        if "字一色" in fans:
            fans.discard("全带幺")
        candidates.append(fans)
    if not candidates:
        return None
    fans = max(candidates, key=lambda f: (sum(FANS[n] for n in f), tuple(sorted(f))))
    names = [n for n in FANS if n in fans]
    raw = sum(FANS[n] for n in names)
    return {"is_win": True, "fan_names": names, "fan_values": {n: FANS[n] for n in names},
            "fan": min(32, raw), "raw_fan": raw, "base_score": 2+min(32, raw), "edition": EDITION}


def payments(winner, fan, discarder=None):
    if type(winner) is not int or winner not in range(4) or type(fan) is not int or fan < 0:
        raise ValueError("无效的和牌结算")
    if discarder is not None and (type(discarder) is not int or discarder not in range(4) or discarder == winner):
        raise ValueError("无效的放铳座位")
    base = 2+min(32, fan)
    result = {i: 0 for i in range(4)}
    for payer in ([discarder] if discarder is not None else [i for i in range(4) if i != winner]):
        amount = base*(2 if discarder is not None else 1)
        result[payer] -= amount
        result[winner] += amount
    return result


def kong_payments(player, kind, *, payer=None, drawn=True):
    if type(player) is not int or player not in range(4) or kind not in ("direct", "added", "concealed"):
        raise ValueError("无效的杠分结算")
    if kind == "direct" and (type(payer) is not int or payer not in range(4) or payer == player):
        raise ValueError("无效的点杠座位")
    result = {i: 0 for i in range(4)}
    if kind == "added" and not drawn:
        return result
    for other in ([payer] if kind == "direct" else [i for i in range(4) if i != player]):
        amount = 1 if kind == "added" else 2
        result[other] -= amount
        result[player] += amount
    return result
