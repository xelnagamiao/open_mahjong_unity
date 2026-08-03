"""
高性能罗伯特——国标专用陪打启发式（决策纯函数）。

来源：哈基明（github.com/baisebaoma）于 2026-08-01 晚开发群讨论并定稿的版本。
完整自测画像与主番频率见同目录 GUOBIAO_HEURISTIC_BOT.md；kobalab 向听声明见 ATTRIBUTION.md。

决策骨架：
  - 向听优先；结构听但不足 minFan 视为距合法听差 1
  - 合法听进张只计够番的等待；仅自摸听按 TSUMO_ONLY_UKEIRE_WEIGHT 打折
  - 听牌并列：少仅自摸听种 → 多可荣听种
  - 番牌碰可持平向听；门清合法一摸进张提升可持平开副露
  - 绝张假想、尾巡 near16、海底妙手假想番
  - 七对向听严格更优时不鸣；不求人门前不死守

假想番通过 injectable scorer 调用本项目 GB_hepai_check。
仅面向 guobiao/standard；变种规则暂未支持。
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Dict, Hashable, List, Optional, Sequence, Set, Tuple

from .guobiao_shanten import (
    ALL_TILE_IDS,
    Counts,
    add_tile,
    copy_counts,
    counts_from_tiles,
    effective_tiles,
    guobiao_shanten,
    live_copies,
    normalize_tile,
    pack_adjust,
    pack_counts,
    remove_tile,
    shanten_qidui,
    xiangting_yiban,
)
from .smart_bot_logic import count_melds

DEFAULT_MIN_FAN = 8
FAN_UKEIRE_SLACK = 1
TSUMO_ONLY_UKEIRE_WEIGHT = 0.35
THIN_LEGAL_UKEIRE = 2
NEAR_EXHAUST_WALL = 16

WIND_IDS = (41, 42, 43, 44)  # 东南西北
DRAGON_IDS = (45, 46, 47)  # 中发白

_SHARED_TINGPAI = None

# FanScorer(hand14, combos, way_to_hepai, win_tile) -> (score, fan_names)
FanScorer = Callable[[List[int], List[str], List[str], int], Tuple[int, List[str]]]


@dataclass
class HeuristicContext:
    """决策上下文（红acted 可见信息）。"""
    hand: List[int]
    combination_tiles: List[str]
    visible: Counts  # tile_id -> 已见张数（含自己手牌）
    wall_left: int
    min_fan: int = DEFAULT_MIN_FAN
    round_wind: int = 0  # 0东 1南 2西 3北
    seat_wind: int = 0  # 相对庄：0东 … 3北
    flower_count: int = 0
    scorer: Optional[FanScorer] = None
    # 假想番缓存：concealed+combos+win_tile+way 相关 → 非花番（未过 min_fan 门闩）
    fan_memo: Optional[Dict[Tuple, int]] = None


def counts_key(counts: Counts) -> bytes:
    """稳定手牌计数键（bytes 打包，对齐向听缓存键）。"""
    return pack_counts(counts)


@dataclass
class DiscardScore:
    shanten: int
    max_wait_fan: int = 0
    ukeire: float = 0.0
    ron_ukeire: float = 0.0
    tsumo_only_kinds: int = 0
    ron_wait_kinds: int = 0
    shed_priority: float = 0.0


@dataclass
class ClaimPlan:
    action: str  # peng / chi_left / chi_mid / chi_right / gang
    shanten_after: int
    ukeire_after: float
    max_wait_fan: int
    fan_direction: bool = False
    claimed_tile: int = 0


def _meld_count(combs: Sequence[str]) -> int:
    return count_melds(list(combs))


def winning_shanten(score: DiscardScore) -> int:
    if score.shanten == 0 and score.max_wait_fan == 0:
        return 1
    return score.shanten


def is_cheap_tenpai(score: DiscardScore) -> bool:
    return score.shanten == 0 and score.max_wait_fan == 0


def dominant_number_suit(counts: Counts) -> Optional[int]:
    suit_counts = {1: 0, 2: 0, 3: 0}
    for tid, c in counts.items():
        suit = tid // 10
        if suit in suit_counts:
            suit_counts[suit] += c
    ranked = sorted(suit_counts.items(), key=lambda x: -x[1])
    top_suit, top = ranked[0]
    second = ranked[1][1]
    if top >= 8 and top > second:
        return top_suit
    return None


def flush_lean_shed_bonus(tile_id: int, counts: Counts) -> float:
    dom = dominant_number_suit(counts)
    if dom is None:
        return 0.0
    suit = tile_id // 10
    if suit not in (1, 2, 3) or suit == dom:
        return 0.0
    return 1.5 if counts.get(tile_id, 0) == 1 else 0.75


def shed_priority(tile_id: int, counts: Counts) -> float:
    tid = normalize_tile(tile_id)
    suit = tid // 10
    num = tid % 10
    if suit == 4:
        if counts.get(tid, 0) >= 2:
            base = 0.4
        else:
            base = 3.0 if tid in DRAGON_IDS else 3.6
    else:
        if num in (1, 9):
            base = 3.0
        elif num in (2, 8):
            base = 2.0
        else:
            base = 0.0
    return base + flush_lean_shed_bonus(tid, counts)


def is_better_discard(a: DiscardScore, b: DiscardScore, near_exhaust: bool = False) -> bool:
    aw, bw = winning_shanten(a), winning_shanten(b)
    if aw != bw:
        return aw < bw
    ac, bc = is_cheap_tenpai(a), is_cheap_tenpai(b)
    if ac != bc:
        return not ac
    if a.shanten != b.shanten:
        return a.shanten < b.shanten

    both_legal = aw == 0 and bw == 0
    ukeire_gap = abs(a.ukeire - b.ukeire)
    slack = 0 if (both_legal and (near_exhaust or min(a.ukeire, b.ukeire) <= THIN_LEGAL_UKEIRE)) else FAN_UKEIRE_SLACK
    if ukeire_gap > slack:
        return a.ukeire > b.ukeire
    if a.max_wait_fan != b.max_wait_fan:
        return a.max_wait_fan > b.max_wait_fan
    if a.ukeire != b.ukeire:
        return a.ukeire > b.ukeire
    if a.ron_ukeire != b.ron_ukeire:
        return a.ron_ukeire > b.ron_ukeire
    if both_legal and a.tsumo_only_kinds != b.tsumo_only_kinds:
        return a.tsumo_only_kinds < b.tsumo_only_kinds
    if both_legal and a.ron_wait_kinds != b.ron_wait_kinds:
        return a.ron_wait_kinds > b.ron_wait_kinds
    return a.shed_priority > b.shed_priority


def _wind_label(idx: int) -> str:
    return ("东", "南", "西", "北")[idx % 4]


def _tingpai_checker():
    """进程内单例：与 action_check.refresh_waiting_tiles 同源。"""
    global _SHARED_TINGPAI
    if _SHARED_TINGPAI is not None:
        return _SHARED_TINGPAI
    try:
        from ....game_calculation.gb_tingpai_check import Chinese_Tingpai_Check
    except ImportError:
        from game_calculation.gb_tingpai_check import Chinese_Tingpai_Check  # type: ignore
    _SHARED_TINGPAI = Chinese_Tingpai_Check()
    return _SHARED_TINGPAI


def tenpai_wait_tiles(
    concealed: Counts,
    combos: Sequence[str],
    n_melds: int,
) -> List[int]:
    """结构听种：优先 GB_tingpai（对齐 waiting_tiles / 「和单张」门闩）。

    `effective_tiles` 会跳过手内已 4 枚的牌种，但 GB_tingpai 仍可能把它们算进
    waiting_tiles。若只用 effective_tiles 判独听，会误加「和单张」把假听当合法听。
    """
    hand: List[int] = []
    for tid, c in concealed.items():
        if c > 0:
            hand.extend([int(tid)] * int(c))
    try:
        waits = _tingpai_checker().tingpai_check(hand, list(combos))
        if waits:
            return sorted(int(w) for w in waits)
    except Exception:
        pass
    return list(effective_tiles(concealed, n_melds))


def build_way(
    win_type: str,
    ctx: HeuristicContext,
    wall_left: int,
    juezhang: bool,
    single_wait: bool = False,
) -> List[str]:
    way: List[str] = ["花牌"] * max(0, ctx.flower_count)
    if win_type == "tsumo":
        way.append("自摸")
        if wall_left == 0:
            way.append("last_deal")
    else:
        way.append("点和")
        if wall_left <= 1:
            way.append("last_cut")
    way.append(f"场风{_wind_label(ctx.round_wind)}")
    way.append(f"自风{_wind_label(ctx.seat_wind)}")
    if juezhang:
        way.append("和绝张")
    # 与 OMC singleWait / action_check.waiting_tiles==1 对齐：
    # Chinese_Hepai_Check 仅在 way 含「和单张」时计边张/嵌张/单吊将。
    if single_wait:
        way.append("和单张")
    return way


def _non_flower_fan(score: int, fan_list: Sequence[str], flower_count: int) -> int:
    """剔除花牌番后的番分（花牌名通常含「花」或以张数计入）。"""
    # 本项目：花牌每张 1 番，way 里 "花牌"*n，score 含花；合法听用不含花的番分判断。
    return max(0, int(score) - max(0, flower_count))


def _fan_memo_key(
    concealed: Counts,
    combos: Sequence[str],
    win_tile: int,
    win_type: str,
    juezhang: bool,
    wall_left: int,
    round_wind: int,
    seat_wind: int,
    flower_count: int,
    single_wait: bool,
) -> Tuple:
    return (
        counts_key(concealed),
        tuple(combos),
        win_tile,
        win_type,
        juezhang,
        wall_left,
        round_wind,
        seat_wind,
        flower_count,
        single_wait,
    )


def hypothetical_fan(
    ctx: HeuristicContext,
    concealed: Counts,
    combos: List[str],
    win_tile: int,
    win_type: str,
    juezhang: bool = False,
    single_wait: bool = False,
) -> int:
    """返回够 minFan 的非花番分，否则 0。"""
    if ctx.scorer is None:
        return 0
    wall = ctx.wall_left
    memo = ctx.fan_memo
    memo_key = None
    if memo is not None:
        memo_key = _fan_memo_key(
            concealed,
            combos,
            win_tile,
            win_type,
            juezhang,
            wall,
            ctx.round_wind,
            ctx.seat_wind,
            ctx.flower_count,
            single_wait,
        )
        cached = memo.get(memo_key)
        if cached is not None:
            return cached if cached >= ctx.min_fan else 0

    hand_tiles: List[int] = []
    for tid, c in concealed.items():
        hand_tiles.extend([tid] * c)
    way = build_way(win_type, ctx, wall, juezhang, single_wait=single_wait)
    # Fresh combos copy: scorer/hepai historically mutated combination_list.
    combos_arg = list(combos)
    try:
        # Chinese_Hepai_Check 要求完整 14 张（含和牌）才会走一般型/七对等拆解；
        # 传 13 张时自摸路径恒为 (0,[])，会把「仅不求人够 8 番」的合法听打成假听。
        # 与 action_check.check_hepai 一致：荣和/自摸均 hand=13+和牌；自摸 vs 点和只靠 way。
        # （OMC getGuobiaoFanScore 同样始终 hand=[...concealed, winTile]。）
        score, fans = ctx.scorer(hand_tiles + [win_tile], combos_arg, way, win_tile)
    except Exception:
        return 0
    value = _non_flower_fan(score, fans, ctx.flower_count)
    if memo is not None and memo_key is not None:
        memo[memo_key] = value
    if value < ctx.min_fan:
        return 0
    return value


def analyze_live_waits(
    ctx: HeuristicContext,
    remaining: Counts,
    combos: List[str],
    n_melds: int,
) -> Dict[str, float]:
    waits = tenpai_wait_tiles(remaining, combos, n_melds)
    # 结构听种唯一 → 和单张（边嵌吊）；必须与 action_check.waiting_tiles 同源
    single_wait = len(waits) == 1
    max_fan = 0
    qualifying = 0.0
    ron_ukeire = 0.0
    tsumo_only_kinds = 0
    ron_wait_kinds = 0
    for w in waits:
        live = max(0, 4 - ctx.visible.get(w, remaining.get(w, 0)))
        if live <= 0:
            continue
        visible_outside = ctx.visible.get(w, 0) - remaining.get(w, 0)
        juezhang = visible_outside == 3
        ron = hypothetical_fan(
            ctx, remaining, combos, w, "ron", juezhang, single_wait=single_wait
        )
        tsumo = hypothetical_fan(
            ctx, remaining, combos, w, "tsumo", juezhang, single_wait=single_wait
        )
        v = max(ron, tsumo)
        if v > max_fan:
            max_fan = v
        if ron > 0:
            qualifying += live
            ron_ukeire += live
            ron_wait_kinds += 1
        elif tsumo > 0:
            qualifying += live * TSUMO_ONLY_UKEIRE_WEIGHT
            tsumo_only_kinds += 1
    return {
        "max_fan": float(max_fan),
        "qualifying_ukeire": qualifying,
        "ron_ukeire": ron_ukeire,
        "tsumo_only_kinds": float(tsumo_only_kinds),
        "ron_wait_kinds": float(ron_wait_kinds),
    }


def score_discard(ctx: HeuristicContext, discard_tile: int) -> DiscardScore:
    hand_counts = counts_from_tiles(ctx.hand)
    n_melds = _meld_count(ctx.combination_tiles)
    remaining = remove_tile(hand_counts, discard_tile)
    shanten = guobiao_shanten(remaining, n_melds)
    eff = effective_tiles(remaining, n_melds)
    if shanten == 0:
        waits = analyze_live_waits(ctx, remaining, list(ctx.combination_tiles), n_melds)
        return DiscardScore(
            shanten=shanten,
            max_wait_fan=int(waits["max_fan"]),
            ukeire=waits["qualifying_ukeire"],
            ron_ukeire=waits["ron_ukeire"],
            tsumo_only_kinds=int(waits["tsumo_only_kinds"]),
            ron_wait_kinds=int(waits["ron_wait_kinds"]),
            shed_priority=shed_priority(discard_tile, hand_counts),
        )
    return DiscardScore(
        shanten=shanten,
        max_wait_fan=0,
        ukeire=float(live_copies(eff, remaining, ctx.visible)),
        ron_ukeire=0.0,
        shed_priority=shed_priority(discard_tile, hand_counts),
    )


def qualifying_wait_weight(
    ctx: HeuristicContext,
    remaining: Counts,
    combos: List[str],
    n_melds: int,
) -> float:
    waits = tenpai_wait_tiles(remaining, combos, n_melds)
    single_wait = len(waits) == 1
    best = 0.0
    for w in waits:
        live = max(0, 4 - ctx.visible.get(w, remaining.get(w, 0)))
        if live <= 0:
            continue
        visible_outside = ctx.visible.get(w, 0) - remaining.get(w, 0)
        juezhang = visible_outside == 3
        if (
            hypothetical_fan(
                ctx, remaining, combos, w, "ron", juezhang, single_wait=single_wait
            )
            >= ctx.min_fan
        ):
            return 1.0
        if (
            hypothetical_fan(
                ctx, remaining, combos, w, "tsumo", juezhang, single_wait=single_wait
            )
            >= ctx.min_fan
        ):
            best = TSUMO_ONLY_UKEIRE_WEIGHT
    return best


def qualifying_tenpai_ukeire_one_draw(
    ctx: HeuristicContext,
    remaining: Counts,
    combos: List[str],
    n_melds: int,
    qualifies_memo: Optional[Dict[Hashable, float]] = None,
    shanten_memo: Optional[Dict[Hashable, int]] = None,
) -> float:
    structural = guobiao_shanten(remaining, n_melds)
    draw_types = effective_tiles(remaining, n_melds) if structural == 1 else list(ALL_TILE_IDS)
    ukeire = 0.0
    for draw in draw_types:
        live = max(0, 4 - ctx.visible.get(draw, remaining.get(draw, 0)))
        if live <= 0:
            continue
        after_draw = add_tile(remaining, draw)
        next_visible = dict(ctx.visible)
        next_visible[draw] = next_visible.get(draw, remaining.get(draw, 0)) + 1
        sub_ctx = HeuristicContext(
            hand=[],
            combination_tiles=combos,
            visible=next_visible,
            wall_left=max(0, ctx.wall_left - 1),
            min_fan=ctx.min_fan,
            round_wind=ctx.round_wind,
            seat_wind=ctx.seat_wind,
            flower_count=ctx.flower_count,
            scorer=ctx.scorer,
            fan_memo=ctx.fan_memo,
        )
        best_w = 0.0
        for disc, cnt in after_draw.items():
            if cnt <= 0 or disc == draw:
                continue
            after_disc = remove_tile(after_draw, disc)
            ck = pack_counts(after_disc)
            if shanten_memo is not None:
                s = shanten_memo.get(ck)
                if s is None:
                    s = guobiao_shanten(after_disc, n_melds)
                    shanten_memo[ck] = s
            else:
                s = guobiao_shanten(after_disc, n_melds)
            if s != 0:
                continue
            qkey: Hashable = (draw, ck)
            if qualifies_memo is not None and qkey in qualifies_memo:
                w = qualifies_memo[qkey]
            else:
                w = qualifying_wait_weight(sub_ctx, after_disc, combos, n_melds)
                if qualifies_memo is not None:
                    qualifies_memo[qkey] = w
            if w > best_w:
                best_w = w
            if best_w >= 1.0:
                break
        ukeire += live * best_w
    return ukeire


def thick_one_shanten_ukeire_after_one_draw(
    remaining: Counts,
    n_melds: int,
    visible: Counts,
    shanten_memo: Optional[Dict[Hashable, int]] = None,
) -> float:
    """死一向听重塑：一摸后能摸到多「厚」的一向听。

    必须与主决策相同的完整 guobiao_shanten（含特殊型）。原先 specials=False
    会在「仅特殊型可进张」的死形上把厚度打成 0，改变逃逸排序。
    就地 ±1 + 增量 pack 仅作加速，语义与全量枚举一致。
    """
    total = 0.0
    base_packed = pack_counts(remaining)
    for draw in effective_tiles(remaining, n_melds, specials=True):
        live = max(0, 4 - visible.get(draw, remaining.get(draw, 0)))
        if live <= 0:
            continue
        after_draw = add_tile(remaining, draw)
        packed_draw = pack_adjust(base_packed, draw, 1)
        next_vis = dict(visible)
        next_vis[draw] = next_vis.get(draw, remaining.get(draw, 0)) + 1
        best = 0.0
        # 只扫手里实际有的牌种；增量 pack + 就地 ±1 避免反复 dict/全表打包
        disc_kinds = [tid for tid, cnt in after_draw.items() if cnt > 0 and tid != draw]
        for disc in disc_kinds:
            packed_disc = pack_adjust(packed_draw, disc, -1)
            memo_ck: Hashable = (1, packed_disc)
            c_before = after_draw[disc]
            after_draw[disc] = c_before - 1
            if after_draw[disc] <= 0:
                del after_draw[disc]
            if shanten_memo is not None:
                s = shanten_memo.get(memo_ck)
                if s is None:
                    s = guobiao_shanten(
                        after_draw, n_melds, specials=True, packed=packed_disc
                    )
                    shanten_memo[memo_ck] = s
            else:
                s = guobiao_shanten(
                    after_draw, n_melds, specials=True, packed=packed_disc
                )
            if s > 1:
                after_draw[disc] = c_before
                continue
            if s == 0:
                after_draw[disc] = c_before
                best = 24.0
                break
            u = live_copies(
                effective_tiles(after_draw, n_melds, specials=True, packed=packed_disc),
                after_draw,
                next_vis,
            )
            after_draw[disc] = c_before
            if u > best:
                best = float(u)
        total += live * best
    return total


def is_value_honour(tile_id: int, round_wind: int, seat_wind: int) -> bool:
    tid = normalize_tile(tile_id)
    if tid in DRAGON_IDS:
        return True
    if tid not in WIND_IDS:
        return False
    wind_idx = tid - 41
    return wind_idx == round_wind or wind_idx == seat_wind


def claim_matches_fan_direction(
    claim_type: str,
    claimed: int,
    concealed: Counts,
    combos_after: List[str],
) -> bool:
    all_tiles: List[int] = []
    for c in combos_after:
        if len(c) < 2:
            continue
        try:
            base = normalize_tile(int(c[1:]))
        except ValueError:
            continue
        sign = c[0]
        if sign in ("s", "S"):
            all_tiles.extend([base - 1, base, base + 1])
        elif sign in ("k", "K"):
            all_tiles.extend([base] * 3)
        elif sign in ("g", "G"):
            all_tiles.extend([base] * 4)
    for tid, cnt in concealed.items():
        all_tiles.extend([tid] * cnt)
    total = len(all_tiles)
    claimed = normalize_tile(claimed)

    for suit in (1, 2, 3):
        in_suit = sum(
            1 for t in all_tiles
            if t // 10 == suit or t // 10 == 4
        )
        if in_suit < total - 2:
            continue
        if claimed // 10 == suit or claimed // 10 == 4:
            return True

    if claim_type in ("peng", "gang") and claimed in DRAGON_IDS:
        return True
    if claim_type in ("peng", "gang"):
        blocks = 0
        for c in combos_after:
            if c and c[0] in ("k", "K", "g", "G"):
                blocks += 1
        for cnt in concealed.values():
            if cnt >= 2:
                blocks += 1
        if blocks >= 5:
            return True
    return False


def _combo_string_for_claim(action: str, claimed: int) -> str:
    claimed = normalize_tile(claimed)
    if action == "peng":
        return f"k{claimed}"
    if action == "gang":
        return f"g{claimed}"
    # chi: mid tile encoding like s12 for 123
    if action == "chi_left":
        mid = claimed - 1  # claimed is high of sequence? chi_left needs claimed-2,claimed-1 + claimed → mid=claimed-1
        return f"s{claimed - 1}"
    if action == "chi_right":
        return f"s{claimed + 1}"
    if action == "chi_mid":
        return f"s{claimed}"
    return f"k{claimed}"


def _hand_after_claim(hand: List[int], action: str, claimed: int) -> Optional[List[int]]:
    claimed = normalize_tile(claimed)
    test = [normalize_tile(t) for t in hand]
    if action == "peng":
        if test.count(claimed) < 2:
            return None
        test.remove(claimed)
        test.remove(claimed)
        return test
    if action == "gang":
        if test.count(claimed) < 3:
            return None
        for _ in range(3):
            test.remove(claimed)
        return test
    if action == "chi_left":
        need = [claimed - 2, claimed - 1]
    elif action == "chi_mid":
        need = [claimed - 1, claimed + 1]
    elif action == "chi_right":
        need = [claimed + 1, claimed + 2]
    else:
        return None
    for n in need:
        if n not in test:
            return None
        test.remove(n)
    return test


def evaluate_claim(
    ctx: HeuristicContext,
    action: str,
    claimed: int,
    shanten_before: int,
) -> Optional[ClaimPlan]:
    hand = [normalize_tile(t) for t in ctx.hand]
    n_melds = _meld_count(ctx.combination_tiles)
    after_hand = _hand_after_claim(hand, action, claimed)
    if after_hand is None:
        return None

    combo = _combo_string_for_claim(action, claimed)
    combos_after = list(ctx.combination_tiles) + [combo]
    n_after = n_melds + 1
    remaining = counts_from_tiles(after_hand)
    claimed = normalize_tile(claimed)

    if action == "gang":
        shanten_after = guobiao_shanten(remaining, n_after)
        ukeire = float(live_copies(effective_tiles(remaining, n_after), remaining, ctx.visible))
        max_fan = 0
        if shanten_after == 0:
            waits = analyze_live_waits(ctx, remaining, combos_after, n_after)
            max_fan = int(waits["max_fan"])
            ukeire = waits["qualifying_ukeire"]
        best_remaining = remaining
    else:
        # 鸣后必切：用完整 analyze_live_waits 字段走 is_better_discard
        #（含 ron_ukeire / L-D 仅自摸种 / L-H 可荣种），与主切牌路径一致。
        near_exhaust = ctx.wall_left < NEAR_EXHAUST_WALL
        shanten_after = 99
        ukeire = -1.0
        max_fan = -1
        best_ron_ukeire = -1.0
        best_tsumo_only = 0
        best_ron_kinds = 0
        best_remaining = remaining
        for tid in list(remaining.keys()):
            if remaining.get(tid, 0) <= 0:
                continue
            after_disc = remove_tile(remaining, tid)
            s = guobiao_shanten(after_disc, n_after)
            if s > shanten_after:
                continue
            u = float(live_copies(effective_tiles(after_disc, n_after), after_disc, ctx.visible))
            fan = 0
            ron_u = 0.0
            tsumo_only = 0
            ron_kinds = 0
            if s == 0:
                waits = analyze_live_waits(ctx, after_disc, combos_after, n_after)
                fan = int(waits["max_fan"])
                u = waits["qualifying_ukeire"]
                ron_u = waits["ron_ukeire"]
                tsumo_only = int(waits["tsumo_only_kinds"])
                ron_kinds = int(waits["ron_wait_kinds"])
            cand = DiscardScore(
                shanten=s,
                max_wait_fan=fan,
                ukeire=u,
                ron_ukeire=ron_u,
                tsumo_only_kinds=tsumo_only,
                ron_wait_kinds=ron_kinds,
            )
            best = DiscardScore(
                shanten=shanten_after if shanten_after < 99 else 8,
                max_wait_fan=max(0, max_fan),
                ukeire=max(0.0, ukeire),
                ron_ukeire=max(0.0, best_ron_ukeire),
                tsumo_only_kinds=best_tsumo_only,
                ron_wait_kinds=best_ron_kinds,
            )
            if shanten_after >= 99 or is_better_discard(cand, best, near_exhaust):
                shanten_after = s
                ukeire = u
                max_fan = fan
                best_ron_ukeire = ron_u
                best_tsumo_only = tsumo_only
                best_ron_kinds = ron_kinds
                best_remaining = after_disc

    value_honour_pon = (
        action == "peng"
        and shanten_before > 0
        and is_value_honour(claimed, ctx.round_wind, ctx.seat_wind)
    )
    advances = (
        shanten_after <= shanten_before
        if (action == "gang" or value_honour_pon)
        else shanten_after < shanten_before
    )

    # 仅明顺/明刻/明杠算已副露；暗杠 G 仍门清（对齐 OMC open meld 判定）
    was_closed = not any(
        c and c[0] in ("s", "S", "k", "K", "g") for c in ctx.combination_tiles
    )
    if (
        not advances
        and was_closed
        and shanten_before > 0
        and shanten_after == shanten_before
        and action != "gang"
    ):
        before_u = qualifying_tenpai_ukeire_one_draw(
            ctx, counts_from_tiles(hand), list(ctx.combination_tiles), n_melds
        )
        after_u = qualifying_tenpai_ukeire_one_draw(
            ctx, best_remaining, combos_after, n_after
        )
        if after_u > before_u:
            advances = True
            ukeire = after_u

    if not advances:
        return None

    if shanten_after == 0 and max_fan < ctx.min_fan:
        before_u = qualifying_tenpai_ukeire_one_draw(
            ctx, counts_from_tiles(hand), list(ctx.combination_tiles), n_melds
        )
        after_u = qualifying_tenpai_ukeire_one_draw(
            ctx, best_remaining, combos_after, n_after
        )
        if after_u <= before_u:
            return None
        ukeire = after_u

    # 推进到一向听但一摸无法进合法听 = 死一向听陷阱。
    # 弃牌侧已有死形→二向听逃逸；鸣牌若仍跳进死形，会提前锁薄听/耗壁。
    # （OMC 弃牌死形逃逸同源；鸣牌侧补同构闸，避免「向听好看、合法进张为零」。）
    if shanten_after == 1:
        after_u = qualifying_tenpai_ukeire_one_draw(
            ctx, best_remaining, combos_after, n_after
        )
        if after_u <= 0:
            return None
        ukeire = after_u

    return ClaimPlan(
        action=action,
        shanten_after=shanten_after,
        ukeire_after=max(0.0, ukeire),
        max_wait_fan=max(0, max_fan),
        fan_direction=claim_matches_fan_direction(
            action, claimed, best_remaining, combos_after
        ),
        claimed_tile=claimed,
    )


def is_better_claim(a: ClaimPlan, b: ClaimPlan) -> bool:
    if a.shanten_after != b.shanten_after:
        return a.shanten_after < b.shanten_after
    gap = abs(a.ukeire_after - b.ukeire_after)
    if gap > FAN_UKEIRE_SLACK:
        return a.ukeire_after > b.ukeire_after
    if a.max_wait_fan != b.max_wait_fan:
        return a.max_wait_fan > b.max_wait_fan
    if a.ukeire_after != b.ukeire_after:
        return a.ukeire_after > b.ukeire_after
    if a.fan_direction != b.fan_direction:
        return a.fan_direction
    return False


def choose_best_discard(ctx: HeuristicContext) -> Optional[int]:
    """返回最优切牌 tile_id；手牌空则 None。"""
    hand = [normalize_tile(t) for t in ctx.hand if normalize_tile(t) <= 47 and normalize_tile(t) // 10 != 5]
    if not hand:
        return None
    # 本手决策内共享假想番 / 向听 memo（对齐 OMC qualifiesMemo/shantenMemo）
    if ctx.fan_memo is None:
        ctx.fan_memo = {}
    near_exhaust = ctx.wall_left < NEAR_EXHAUST_WALL
    candidates: List[Tuple[int, Counts, DiscardScore]] = []
    seen: Set[int] = set()
    hand_counts = counts_from_tiles(hand)
    for tid in hand:
        if tid in seen:
            continue
        seen.add(tid)
        score = score_discard(ctx, tid)
        candidates.append((tid, remove_tile(hand_counts, tid), score))

    best = candidates[0]
    for cand in candidates[1:]:
        if is_better_discard(cand[2], best[2], near_exhaust):
            best = cand

    if winning_shanten(best[2]) == 1:
        one_step = [c for c in candidates if winning_shanten(c[2]) == 1]
        best_legal = -1.0
        n_melds = _meld_count(ctx.combination_tiles)
        qualifies_memo: Dict[Hashable, float] = {}
        shanten_memo: Dict[Hashable, int] = {}
        for cand in one_step:
            legal = qualifying_tenpai_ukeire_one_draw(
                ctx,
                cand[1],
                list(ctx.combination_tiles),
                n_melds,
                qualifies_memo=qualifies_memo,
                shanten_memo=shanten_memo,
            )
            better = legal > best_legal or (
                legal == best_legal and is_better_discard(cand[2], best[2], near_exhaust)
            )
            if better:
                best = cand
                best_legal = legal

        if best_legal <= 0:
            best_reshape = None
            best_progress = -1.0
            shanten_memo2: Dict[Hashable, int] = {}
            for cand in candidates:
                if winning_shanten(cand[2]) != 2:
                    continue
                progress = thick_one_shanten_ukeire_after_one_draw(
                    cand[1], n_melds, ctx.visible, shanten_memo=shanten_memo2
                )
                if progress > best_progress or (
                    progress == best_progress
                    and best_reshape is not None
                    and is_better_discard(cand[2], best_reshape[2], near_exhaust)
                ) or (progress == best_progress and best_reshape is None):
                    best_reshape = cand
                    best_progress = progress
            if best_reshape is not None:
                best = best_reshape

    return best[0]


def should_open_qidui_protect(hand: Sequence[int], n_melds: int) -> bool:
    if n_melds != 0:
        return False
    counts = counts_from_tiles(hand)
    q = shanten_qidui(counts)
    y = xiangting_yiban(counts, 0)
    return q < y


def choose_claim(
    ctx: HeuristicContext,
    action_list: Sequence[str],
    cut_tile: int,
) -> str:
    """从鸣牌候选中选最优；默认 pass。"""
    if should_open_qidui_protect(ctx.hand, _meld_count(ctx.combination_tiles)):
        return "pass"

    hand_counts = counts_from_tiles(ctx.hand)
    n_melds = _meld_count(ctx.combination_tiles)
    shanten_before = guobiao_shanten(hand_counts, n_melds)

    best: Optional[ClaimPlan] = None
    for action in action_list:
        if action not in ("peng", "gang", "chi_left", "chi_mid", "chi_right"):
            continue
        plan = evaluate_claim(ctx, action, cut_tile, shanten_before)
        if plan is None:
            continue
        if best is None or is_better_claim(plan, best):
            best = plan
    return best.action if best else "pass"


def _winning_after_counts(
    ctx: HeuristicContext,
    remaining: Counts,
    combos: List[str],
    n_melds: int,
) -> Tuple[int, float]:
    """返回 (winning_shanten, qualifying_ukeire)。"""
    s = guobiao_shanten(remaining, n_melds)
    if s != 0:
        return s, 0.0
    waits = analyze_live_waits(ctx, remaining, combos, n_melds)
    max_fan = int(waits["max_fan"])
    ukeire = float(waits["qualifying_ukeire"])
    if max_fan == 0:
        return 1, ukeire
    return 0, ukeire


def choose_closed_kan(
    ctx: HeuristicContext,
    *,
    allow_angang: bool,
    allow_jiagang: bool,
    baseline_discard: Optional[int] = None,
) -> Optional[Tuple[str, int]]:
    """暗杠/加杠：对齐 OMC —— 相对最优切不恶化 winningShanten，不削合法听进张。

    返回 ("angang"|"jiagang", tile_id) 或 None（应切牌）。
    若已算过最优切，传入 baseline_discard 避免重复 choose_best_discard。
    """
    if not allow_angang and not allow_jiagang:
        return None
    hand = [
        normalize_tile(t)
        for t in ctx.hand
        if normalize_tile(t) <= 47 and normalize_tile(t) // 10 != 5
    ]
    if not hand:
        return None

    best_disc = baseline_discard if baseline_discard is not None else choose_best_discard(ctx)
    if best_disc is None:
        return None
    base_score = score_discard(ctx, best_disc)
    baseline_winning = winning_shanten(base_score)
    baseline_ukeire = base_score.ukeire if baseline_winning == 0 else -1.0
    baseline_raw = base_score.shanten

    hand_counts = counts_from_tiles(hand)
    n_melds = _meld_count(ctx.combination_tiles)
    combos = list(ctx.combination_tiles)

    if allow_angang:
        for tid, cnt in list(hand_counts.items()):
            if cnt < 4:
                continue
            after = dict(hand_counts)
            del after[tid]
            combos_after = combos + [f"G{tid}"]
            after_winning, after_ukeire = _winning_after_counts(
                ctx, after, combos_after, n_melds + 1
            )
            after_raw = guobiao_shanten(after, n_melds + 1)
            if after_winning > baseline_winning:
                continue
            if baseline_winning == 0 and after_winning == 0 and after_ukeire < baseline_ukeire:
                continue
            if baseline_winning > 0 and after_raw > baseline_raw:
                continue
            return ("angang", tid)

    if allow_jiagang:
        for c in combos:
            if not c or c[0] != "k":
                continue
            try:
                ktile = normalize_tile(int(c[1:]))
            except ValueError:
                continue
            if hand_counts.get(ktile, 0) <= 0:
                continue
            after = remove_tile(hand_counts, ktile)
            combos_after = [f"g{ktile}" if x == c else x for x in combos]
            after_winning, after_ukeire = _winning_after_counts(
                ctx, after, combos_after, n_melds
            )
            after_raw = guobiao_shanten(after, n_melds)
            if after_winning > baseline_winning:
                continue
            if baseline_winning == 0 and after_winning == 0 and after_ukeire < baseline_ukeire:
                continue
            if baseline_winning > 0 and after_raw > baseline_raw:
                continue
            return ("jiagang", ktile)

    return None


def count_visible_from_game(game_state, player_index: int) -> Counts:
    """从对局状态统计可见牌（含自己手牌）。"""
    visible: Counts = {}
    for p in game_state.player_list:
        for t in getattr(p, "discard_tiles", []):
            tid = normalize_tile(t)
            if 11 <= tid <= 47 and tid // 10 != 5:
                visible[tid] = visible.get(tid, 0) + 1
        for c in getattr(p, "combination_tiles", []):
            if len(c) < 2:
                continue
            sign = c[0]
            try:
                base = normalize_tile(int(c[1:]))
            except ValueError:
                continue
            if sign in ("s", "S"):
                for tid in (base - 1, base, base + 1):
                    visible[tid] = visible.get(tid, 0) + 1
            elif sign in ("k", "K"):
                visible[base] = visible.get(base, 0) + 3
            elif sign in ("g", "G"):
                visible[base] = visible.get(base, 0) + 4
            elif sign == "q":
                visible[base] = visible.get(base, 0) + 2
    me = game_state.player_list[player_index]
    for t in getattr(me, "hand_tiles", []):
        tid = normalize_tile(t)
        if 11 <= tid <= 47 and tid // 10 != 5:
            visible[tid] = visible.get(tid, 0) + 1
    return visible


_SHARED_CHECKER = None
_SHARED_SCORER: Optional[FanScorer] = None


def make_default_scorer() -> FanScorer:
    """使用仓库内 Chinese_Hepai_Check（进程内单例）。

    hepai_check / fan_count 会就地改 way（门风圈风相同、暗转明等）；
    此处对 hand/combos/way 先拷贝再算。故意不做跨决策全局检番结果缓存，
    以免与就地副作用耦合后改变决策。
    """
    global _SHARED_CHECKER, _SHARED_SCORER
    if _SHARED_SCORER is not None:
        return _SHARED_SCORER
    try:
        from ....game_calculation.guobiao_hepai_check import Chinese_Hepai_Check
    except ImportError:
        from game_calculation.guobiao_hepai_check import Chinese_Hepai_Check  # type: ignore

    checker = Chinese_Hepai_Check()
    _SHARED_CHECKER = checker

    def score(hand, combos, way, get_tile):
        # hepai_decompose 对每个拆解拷贝 way，避免 fan_count 的暗转明/门风圈风
        # 就地改写串到后续拆解；取最高番（与 hepai_check 的 max 语义一致）。
        results = checker.hepai_decompose(list(hand), list(combos), list(way), get_tile)
        if not results:
            return 0, []
        best = results[0]
        return checker.filter_zero_value_fans(best["score"], best["fan_list"])

    _SHARED_SCORER = score
    return score


def context_from_game(game_state, player_index: int, scorer: Optional[FanScorer] = None) -> HeuristicContext:
    player = game_state.player_list[player_index]
    dealer = getattr(game_state, "dealer_index", 0)
    seat = (player_index - dealer) % 4
    round_no = getattr(game_state, "current_round", 1)
    if round_no <= 4:
        round_wind = 0
    elif round_no <= 8:
        round_wind = 1
    elif round_no <= 12:
        round_wind = 2
    else:
        round_wind = 3
    return HeuristicContext(
        hand=list(player.hand_tiles),
        combination_tiles=list(getattr(player, "combination_tiles", [])),
        visible=count_visible_from_game(game_state, player_index),
        wall_left=len(getattr(game_state, "tiles_list", [])),
        min_fan=int(getattr(game_state, "hepai_limit", DEFAULT_MIN_FAN) or DEFAULT_MIN_FAN),
        round_wind=round_wind,
        seat_wind=seat,
        flower_count=len(getattr(player, "huapai_list", [])),
        scorer=scorer or make_default_scorer(),
        fan_memo={},
    )
