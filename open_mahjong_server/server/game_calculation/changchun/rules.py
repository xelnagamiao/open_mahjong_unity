"""Pure, deterministic MIL Changchun rules; physical tiles never change identity.

Source: MIL 长春麻将（推广）竞赛规则（试行2024版）pp.3–10.
Special meld wire format: Ckind:physical,...:logical,... . The first three
members establish one meld; subsequent members are individual added kongs.
The encoding preserves the physical one-bamboo when another player robs it.
"""
from collections import Counter
from dataclasses import dataclass
from functools import lru_cache
from itertools import combinations, product

from ..hongkong.models import TILES, NUMBERS, ORPHANS, DRAGONS, WINDS
from ..hongkong.solver import _partitions, _key, parse_meld as parse_normal_meld

SUB_RULE = "changchun/mil2024"
EDITION = "mil-changchun-2024-om1"
ONE_BAMBOO = 31
SPECIAL_DOMAINS = {"yao": (11, 21, 31), "jiu": (19, 29, 39),
                   "wind": WINDS, "dragon": DRAGONS}
SPECIAL_NAMES = {"yao": "幺杠", "jiu": "九杠", "wind": "旋风杠", "dragon": "喜杠"}
FANS = {"门清": 1, "夹/单吊/单和": 1, "飘和": 2, "七对": 3,
        "豪华七对": 4, "冲宝": 2, "摸宝": 1}


@dataclass(frozen=True)
class Meld:
    kind: str
    physical: tuple
    logical: tuple
    concealed: bool = False

    @property
    def special(self):
        return self.kind in SPECIAL_DOMAINS

    @property
    def kong(self):
        return self.special or self.kind == "kong"


def special_code(kind, physical, logical):
    code = f"C{kind}:{','.join(map(str, physical))}:{','.join(map(str, logical))}"
    parse_meld(code)
    return code


@lru_cache(maxsize=4096)
def parse_meld(code):
    if not isinstance(code, str):
        raise ValueError("副露必须为字符串")
    if not code.startswith("C"):
        normal = parse_normal_meld(code)
        return Meld(normal.kind, normal.tiles, normal.tiles, normal.concealed)
    try:
        kind, physical, logical = code[1:].split(":")
        physical = tuple(map(int, physical.split(",")))
        logical = tuple(map(int, logical.split(",")))
        domain = SPECIAL_DOMAINS[kind]
    except (ValueError, KeyError) as exc:
        raise ValueError("无效的特殊杠编码") from exc
    if (len(physical) < 3 or len(physical) != len(logical)
            or len(set(logical[:3])) != 3 or any(t not in domain for t in logical)
            or any(p != q and p != ONE_BAMBOO for p, q in zip(physical, logical))
            or any(t not in TILES for t in physical)
            or max(Counter(physical).values()) > 4):
        raise ValueError("特殊杠的实体牌或替代关系不合法")
    return Meld(kind, physical, logical)


def valid_hand(hand, codes=(), *, complete=False):
    try:
        melds = tuple(parse_meld(code) for code in codes)
    except (ValueError, TypeError):
        return None
    if (len(melds) > 4 or len(hand) != 13 + int(complete) - 3 * len(melds)
            or any(type(t) is not int or t not in TILES for t in hand)
            or len({m.kind for m in melds if m.special}) != sum(m.special for m in melds)):
        return None
    counts = Counter(hand)
    counts.update(t for m in melds for t in m.physical)
    if any(c > 4 for c in counts.values()):
        return None
    return melds


def _qualifies_tiles(hand, melds):
    tiles = tuple(hand) + tuple(t for m in melds for t in m.logical)
    return ({t // 10 for t in tiles if t in NUMBERS} == {1, 2, 3}
            and bool(set(tiles) & ORPHANS))


@lru_cache(maxsize=32768)
def _shapes(hand, codes):
    external = valid_hand(hand, codes, complete=True)
    if external is None or not _qualifies_tiles(hand, external):
        return ()
    counts = Counter(hand)
    result = []
    if not external and all(c % 2 == 0 for c in counts.values()):
        result.append(("seven_pairs", 0, ()))
    for pair in sorted(t for t, c in counts.items() if c >= 2):
        rest = counts.copy()
        rest[pair] -= 2
        for partition in _partitions(_key(rest), 4 - len(external)):
            if (pair not in DRAGONS and all(m.kind == "sequence" for m in partition)
                    and all(m.kind == "sequence" for m in external)):
                continue
            result.append(("standard", pair, partition))
    return tuple(result)


def shapes(hand, codes=()):
    return _shapes(tuple(sorted(hand)), tuple(codes))


@lru_cache(maxsize=32768)
def _waits(hand, codes, declaration):
    melds = valid_hand(hand, codes)
    if melds is None or (declaration and not _qualifies_tiles(hand, melds)):
        return frozenset()
    counts = Counter(hand)
    counts.update(t for m in melds for t in m.physical)
    return frozenset(t for t in TILES if counts[t] < 4 and _shapes(tuple(sorted(hand + (t,))), codes))


def waits(hand, codes=(), *, declaration=False):
    return set(_waits(tuple(sorted(hand)), tuple(codes), declaration))


def score(hand, codes=(), *, winning_tile, bao=""):
    """Score every legal decomposition. 庄/点炮/自摸 are payer-specific.

    For 摸宝 without an ordinary complete shape, use the highest legal ready
    shape of the unchanged thirteen-tile hand. `represented_win` explains that
    scoring choice; it never replaces the actual tile in hand/replay.
    """
    if type(winning_tile) is not int or winning_tile not in hand or bao not in ("", "chong", "mo"):
        return None
    external = valid_hand(hand, codes, complete=True)
    if external is None:
        return None
    before = list(hand)
    before.remove(winning_tile)
    if bao == "mo":
        legal_waits = waits(before, codes, declaration=True)
        candidates = [(t, score(before + [t], codes, winning_tile=t)) for t in sorted(legal_waits)]
        candidates = [(t, detail) for t, detail in candidates if detail]
        if not candidates:
            return None
        represented, best = max(candidates, key=lambda item: (item[1]["raw_fan"], -item[0]))
        names = list(best["fan_names"]) + ["摸宝"]
        return _detail(names, best["shape"], represented_win=represented, bao=bao)
    candidates = []
    single = len(waits(before, codes)) == 1
    for kind, pair, partition in shapes(hand, codes):
        names = []
        if kind == "seven_pairs":
            names.append("豪华七对" if before.count(winning_tile) == 3 else "七对")
        else:
            if all(m.concealed for m in external):
                names.append("门清")
            piao = all(m.kind != "sequence" for m in partition) and all(m.kind != "sequence" for m in external)
            if piao:
                # PDF p.10 example 5 explicitly scores 4 triplets + singleton
                # as 飘和2 only; do not add the implied single wait.
                names.append("飘和")
            elif single or pair == winning_tile or any(
                    m.kind == "sequence" and (
                        winning_tile == m.tile or (m.tile % 10 == 2 and winning_tile == m.tile + 1)
                        or (m.tile % 10 == 8 and winning_tile == m.tile - 1))
                    for m in partition):
                names.append("夹/单吊/单和")
        if bao == "chong":
            names.append("冲宝")
        candidates.append(_detail(names, kind, bao=bao))
    return max(candidates, key=lambda detail: (detail["raw_fan"], tuple(detail["fan_names"]))) if candidates else None


def _detail(names, shape, **extra):
    raw = sum(FANS[n] for n in names)
    return {"is_win": True, "fan_names": names, "fan_values": {n: FANS[n] for n in names},
            "raw_fan": raw, "fan": min(6, raw), "base_score": 2 ** min(6, raw),
            "shape": shape, "edition": EDITION, **extra}


def payments(winner, fan, *, dealer=0, discarder=None, bao_seen=True):
    if (type(winner) is not int or winner not in range(4) or type(dealer) is not int
            or dealer not in range(4) or type(fan) is not int or fan < 0
            or discarder is not None and (type(discarder) is not int or discarder not in range(4) or discarder == winner)):
        raise ValueError("无效的和牌结算")
    delta = {i: 0 for i in range(4)}
    breakdown = []
    for payer in range(4):
        if payer == winner:
            continue
        raw = fan + int(dealer in (payer, winner)) + int(discarder is None or payer == discarder)
        amount = 2 ** min(6, raw)
        responsible = discarder if discarder is not None and not bao_seen else payer
        delta[responsible] -= amount
        delta[winner] += amount
        breakdown.append({"payer": payer, "responsible": responsible, "fan": min(6, raw), "score": amount})
    return delta, breakdown


def kong_payments(player, kind, tile=None):
    if type(player) is not int or player not in range(4) or kind not in ("direct", "added", "concealed", "special", "special_added"):
        raise ValueError("无效的杠分结算")
    if kind not in ("special", "special_added") and tile not in TILES:
        raise ValueError("无效的杠牌")
    amount = 2 if kind == "concealed" else 1
    if kind in ("direct", "added", "concealed") and tile in (21, 31, 45, 46, 47):
        amount *= 2
    return {i: amount * 3 if i == player else -amount for i in range(4)}


def initial_special_candidates(hand, codes=()):
    """Canonical identities; equivalent physical choices are emitted once."""
    try:
        existing = {parse_meld(code).kind for code in codes}
    except (ValueError, TypeError):
        return ()
    if any(type(t) is not int or t not in TILES for t in hand) or any(c > 4 for c in Counter(hand).values()):
        return ()
    counts = Counter(hand)
    result = []
    for kind, domain in SPECIAL_DOMAINS.items():
        if kind in existing:
            continue
        for logical in combinations(domain, 3):
            for physical in product(*[(t,) if t == ONE_BAMBOO else (t, ONE_BAMBOO) for t in logical]):
                if not Counter(physical) - counts:
                    result.append(special_code(kind, physical, logical))
    return tuple(sorted(set(result)))


def add_special(code, tile, represented=None):
    meld = parse_meld(code)
    if not meld.special or type(tile) is not int:
        raise ValueError("无效的特殊加杠")
    domain = SPECIAL_DOMAINS[meld.kind]
    logical = represented if represented is not None else (domain[0] if tile == ONE_BAMBOO else tile)
    if logical not in domain or (tile != logical and tile != ONE_BAMBOO):
        raise ValueError("一条以外的牌不能替代特殊杠成员")
    return special_code(meld.kind, meld.physical + (tile,), meld.logical + (logical,))
