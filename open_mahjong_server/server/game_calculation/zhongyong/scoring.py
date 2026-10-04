"""Zung Jung v3.3: independent scoring, with the creator's series/addition rules.

Rules: https://www.zj-mahjong.info/zj33_rules_eng.html
Patterns: https://www.zj-mahjong.info/zj33_patterns_eng.html
Only shared tile/decomposition primitives are reused from the Nanque calculator.
"""
from collections import Counter
from dataclasses import dataclass, field
from ..jiandan.decompose import find_standard_decompositions, parse_melds, is_seven_pairs, is_thirteen_orphans
from ..jiandan.tiles import ALL_TILES, validate_tiles, is_suited, is_terminal, is_honor, is_simple

# id: (display name, points, series). The value-honor series alone is repeatable.
FANS = {
    "pinfu": ("平和", 5, "1.1"), "closed_hand": ("门前清", 5, "1.2"),
    "all_simples": ("断幺九", 5, "1.3"),
    "half_flush": ("混一色", 40, "2.1"), "full_flush": ("清一色", 80, "2.1"),
    "nine_gates": ("九莲宝灯", 480, "2.2"),
    "value_honor": ("番牌", 10, "3.1"),
    "small_three_dragons": ("小三元", 40, "3.2"), "big_three_dragons": ("大三元", 130, "3.2"),
    "small_three_winds": ("小三风", 30, "3.3"), "big_three_winds": ("大三风", 120, "3.3"),
    "small_four_winds": ("小四喜", 320, "3.3"), "big_four_winds": ("大四喜", 400, "3.3"),
    "all_honors": ("字一色", 320, "3.4"),
    "all_triplets": ("对对和", 30, "4.1"),
    "two_concealed_triplets": ("二暗刻", 5, "4.2"), "three_concealed_triplets": ("三暗刻", 30, "4.2"),
    "four_concealed_triplets": ("四暗刻", 125, "4.2"),
    "one_kong": ("一杠", 5, "4.3"), "two_kongs": ("二杠", 20, "4.3"),
    "three_kongs": ("三杠", 120, "4.3"), "four_kongs": ("四杠", 480, "4.3"),
    "pure_double_chow": ("一般高", 10, "5.1"), "twice_pure_double_chow": ("两般高", 60, "5.1"),
    "pure_triple_chow": ("一色三同顺", 120, "5.1"), "pure_quadruple_chow": ("一色四同顺", 480, "5.1"),
    "mixed_triple_chow": ("三色同顺", 35, "6.1"),
    "small_three_suit_triplets": ("三色小同刻", 30, "6.2"), "three_suit_triplets": ("三色同刻", 120, "6.2"),
    "full_straight": ("一气通贯", 40, "7.1"),
    "three_consecutive_triplets": ("三连刻", 100, "7.2"), "four_consecutive_triplets": ("四连刻", 200, "7.2"),
    "mixed_outside_hand": ("混全带幺", 40, "8.1"), "pure_outside_hand": ("纯全带幺", 50, "8.1"),
    "mixed_terminals": ("混幺九", 100, "8.1"), "pure_terminals": ("清幺九", 400, "8.1"),
    "haitei": ("海底捞月", 10, "9.1"), "houtei": ("河底捞鱼", 10, "9.1"),
    "rinshan": ("岭上开花", 10, "9.2"), "chankan": ("抢杠", 10, "9.3"),
    "heavenly_win": ("天和", 155, "9.4"), "earthly_win": ("地和", 155, "9.4"),
    "thirteen_orphans": ("十三幺九", 160, "10.1"), "seven_pairs": ("七对子", 30, "10.2"),
}

@dataclass(frozen=True)
class HandContext:
    hand_tiles: list[int] | tuple[int, ...]
    meld_codes: list[str] | tuple[str, ...] = field(default_factory=tuple)
    winning_tile: int | None = None
    win_source: str = "self_draw"
    seat_wind: int = 41
    pre_win_tiles: list[int] | tuple[int, ...] | None = None
    heavenly_win: bool = False
    earthly_win: bool = False
    haitei: bool = False
    houtei: bool = False
    rinshan: bool = False
    chankan: bool = False

@dataclass(frozen=True)
class ScoreResult:
    is_win: bool
    points: int = 0
    fan_ids: tuple = ()
    fan_names: tuple = ()
    raw_points: int = 0
    decomposition: object = None

def _finish(ids, decomp=None):
    series = {}
    for key in ids:
        name, value, group = FANS[key]
        if group == "3.1":
            continue
        if group not in series or value > FANS[series[group]][1]:
            series[group] = key
    selected = list(series.values()) + [key for key in ids if key == "value_honor"]
    listed = [key for key in selected if FANS[key][1] >= 320]
    if listed:
        selected = [max(listed, key=lambda key: FANS[key][1])]
    selected.sort(key=lambda key: (-FANS[key][1], key))
    raw = sum(FANS[key][1] for key in selected)
    points = raw if listed else min(320, max(1, raw))
    return ScoreResult(True, points, tuple(selected) or ("chicken_hand",),
                       tuple(FANS[key][0] for key in selected) or ("鸡和",), raw, decomp)

def _composition(ctx, tiles, special=None):
    ids = []
    if all(is_simple(t) for t in tiles): ids.append("all_simples")
    suits = {t // 10 for t in tiles if is_suited(t)}
    honors = any(is_honor(t) for t in tiles)
    if len(suits) == 1: ids.append("half_flush" if honors else "full_flush")
    if not suits: ids.append("all_honors")
    if all(is_terminal(t) for t in tiles): ids.append("pure_terminals")
    elif special != "thirteen_orphans" and all(is_terminal(t) or is_honor(t) for t in tiles):
        ids.append("mixed_terminals")
    for key in ("heavenly_win", "earthly_win", "haitei", "houtei", "rinshan", "chankan"):
        if getattr(ctx, key): ids.append(key)
    return ids

def _concealed_counts(ctx, melds, pair):
    concealed = sum(m.is_triplet_like and m.concealed for m in melds)
    if ctx.win_source not in {"discard", "rob_kong"}:
        return concealed
    # Freedom of count: the winning tile may complete any undeclared unit
    # containing it. Prefer a pair/sequence when it preserves concealed pungs.
    tile = ctx.winning_tile
    if tile == pair.head or any(not m.declared and m.kind == "sequence" and tile in m.tiles for m in melds):
        return concealed
    return concealed - int(any(not m.declared and m.is_triplet_like and tile in m.tiles for m in melds))

def score_hand(ctx):
    concealed = tuple(sorted(ctx.hand_tiles))
    validate_tiles(concealed)
    declared = parse_melds(ctx.meld_codes)
    if any(not m.is_set for m in declared): return ScoreResult(False)
    physical = concealed + tuple(t for m in declared for t in m.tiles)
    if any(n > 4 for n in Counter(physical).values()): return ScoreResult(False)
    validate_tiles(physical)
    candidates = []
    if not declared:
        for key, check in (("seven_pairs", is_seven_pairs), ("thirteen_orphans", is_thirteen_orphans)):
            if check(concealed): candidates.append(_finish(_composition(ctx, physical, key) + [key]))
    for decomp in find_standard_decompositions(concealed, declared, allow_quad_split=True):
        ids = _composition(ctx, physical)
        melds, pair = decomp.melds, decomp.pair
        if all(not m.declared or (m.kind == "kong" and m.concealed) for m in melds): ids.append("closed_hand")
        sequences = [m for m in melds if m.kind == "sequence"]
        pungs = [m for m in melds if m.is_triplet_like]
        if len(sequences) == 4: ids.append("pinfu")
        if len(pungs) == 4: ids.append("all_triplets")
        ids += ["value_honor" for m in pungs if m.head in (45, 46, 47, ctx.seat_wind)]
        dragons = {m.head for m in pungs if m.head in (45, 46, 47)}
        winds = {m.head for m in pungs if 41 <= m.head <= 44}
        if len(dragons) == 3: ids.append("big_three_dragons")
        elif len(dragons) == 2 and pair.head in (45, 46, 47): ids.append("small_three_dragons")
        if len(winds) == 4: ids.append("big_four_winds")
        elif len(winds) == 3: ids.append("small_four_winds" if 41 <= pair.head <= 44 else "big_three_winds")
        elif len(winds) == 2 and 41 <= pair.head <= 44: ids.append("small_three_winds")
        n = _concealed_counts(ctx, melds, pair)
        if n >= 2: ids.append({2:"two_concealed_triplets",3:"three_concealed_triplets",4:"four_concealed_triplets"}[n])
        n = sum(m.kind == "kong" for m in declared)
        if n: ids.append({1:"one_kong",2:"two_kongs",3:"three_kongs",4:"four_kongs"}[n])
        identical = Counter(m.head for m in sequences)
        maximum = max(identical.values(), default=0)
        if maximum == 4: ids.append("pure_quadruple_chow")
        elif maximum == 3: ids.append("pure_triple_chow")
        elif list(identical.values()).count(2) == 2: ids.append("twice_pure_double_chow")
        elif maximum == 2: ids.append("pure_double_chow")
        for rank in range(1, 10):
            if all(s * 10 + rank in {m.head for m in sequences} for s in (1,2,3)): ids.append("mixed_triple_chow")
            heads = {m.head for m in pungs}
            matches = [s for s in (1,2,3) if s * 10 + rank in heads]
            if len(matches) == 3: ids.append("three_suit_triplets")
            elif len(matches) == 2 and pair.head % 10 == rank and pair.head // 10 in {1,2,3} - set(matches):
                ids.append("small_three_suit_triplets")
        for suit in (1,2,3):
            seq = {m.head % 10 for m in sequences if m.head // 10 == suit}
            trip = {m.head % 10 for m in pungs if m.head // 10 == suit}
            if {1,4,7} <= seq: ids.append("full_straight")
            if any(set(range(start,start+4)) <= trip for start in range(1,7)): ids.append("four_consecutive_triplets")
            elif any(set(range(start,start+3)) <= trip for start in range(1,8)): ids.append("three_consecutive_triplets")
        if all(any(is_terminal(t) or is_honor(t) for t in unit.tiles) for unit in decomp.all_units):
            ids.append("mixed_outside_hand")
        if all(any(is_terminal(t) for t in unit.tiles) for unit in decomp.all_units):
            ids.append("pure_outside_hand")
        before = list(ctx.pre_win_tiles) if ctx.pre_win_tiles is not None else list(concealed)
        if ctx.pre_win_tiles is None and ctx.winning_tile in before: before.remove(ctx.winning_tile)
        if not declared and len(before) == 13 and ctx.winning_tile is not None:
            suit = ctx.winning_tile // 10
            if suit in (1,2,3) and sorted(before) == [suit * 10 + n for n in (1,1,1,2,3,4,5,6,7,8,9,9,9)]:
                ids.append("nine_gates")
        candidates.append(_finish(ids, decomp))
    return max(candidates, key=lambda r: (r.points, r.raw_points), default=ScoreResult(False))

def tingpai_check(hand_tiles, meld_codes=()):
    # Shape only: no minimum score, so avoid scoring 34 full candidates per draw.
    tiles = list(hand_tiles)
    declared = parse_melds(meld_codes or ())
    counts = Counter(tiles)
    counts.update(t for m in declared for t in m.tiles)
    if any(n > 4 for n in counts.values()): return set()
    result = set()
    for tile in ALL_TILES:
        if counts[tile] >= 4: continue
        hand = tiles + [tile]
        if (not declared and (is_seven_pairs(hand) or is_thirteen_orphans(hand))) or find_standard_decompositions(hand, declared, allow_quad_split=True):
            result.add(tile)
    return result
