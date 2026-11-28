"""青雀高性能罗伯特（user_id=3）：平方打点期望 + 青雀全番型深势能 + 全铳制打点对攻防守。

青雀计分公式为 base_point = round(fan^2)，自摸三家各付 1x（净赢 +3x），
荣和全铳制（放铳者独赔 -3x）。v3.1 在 v2.1 精确向听与平方前瞻基础上升级：
1. 补全青雀独有高番势能（三步高/三连刻/八归/镜数映数/四聚连数/满庭芳/高级七对）；
2. 1 向听前瞻按潜力优先排序进张，避免高番进张被活张截断；
3. 基于胜率×平方打点的精确 EV 拆小听升级；
4. 基于对手预估打点（清混一色/对对/多杠）的全铳制攻防平衡。
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Sequence, Tuple

from ...game_calculation.qingque.hepai import qingque_hepai_check
from ...game_calculation.qingque.scoring import qingque_get_base_point
from ...game_calculation.qingque.tingpai import qingque_tingpai_check
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import (
    shanten_qidui,
    shanten_shisanyao,
    xiangting_yiban,
)
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import (
    _BOT_DELAY,
    _RON_HU_ACTIONS,
    _handle_buhua_round,
    _wait_until_actionable,
)
from ..public.ai.smart_bot_logic import should_accept_hu
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class

_WIND_WAY = ("自风东", "自风南", "自风西", "自风北")
_WIND_IDS = (41, 42, 43, 44)
_DRAGON_IDS = (45, 46, 47)
_TERMINAL_IDS = frozenset((11, 19, 21, 29, 31, 39))
_YAOJIU_IDS = frozenset((11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47))

_V2_LOW_TENPAI_AVG_PTS = 125.0
_V2_HIGH_TENPAI_BONUS_WALL = 26

_V3_LOW_TENPAI_AVG_PTS = 170.0
_V3_HIGH_TENPAI_BONUS_WALL = 22


def _norm(tile: int) -> int:
    return int(tile) % 100


def _payout(fan: float) -> float:
    if fan < 1.0:
        return 0.0
    return float(qingque_get_base_point(fan) * 3)


def _meld_tiles(code: str) -> Tuple[int, ...]:
    if not code or len(code) < 2:
        return ()
    try:
        base = _norm(int(code[1:]))
    except ValueError:
        return ()
    kind = code[0]
    if kind in ("s", "S"):
        return (base - 1, base, base + 1)
    if kind in ("k", "K"):
        return (base, base, base)
    if kind in ("g", "G"):
        return (base, base, base, base)
    return ()


@dataclass(frozen=True)
class QingqueOpponentView:
    """对手公开信息快照（青雀暗杠中间两张亮牌，牌种对所有视角公开）。"""

    discards: Tuple[int, ...] = ()
    melds: Tuple[str, ...] = ()


@dataclass
class QingqueContext:
    hand: List[int]
    melds: List[str]
    visible: Dict[int, int]
    seat_wind: int = 0
    wall_left: int = 60
    opponents: Tuple[QingqueOpponentView, ...] = ()
    fan_memo: Dict[Tuple, float] = field(default_factory=dict)
    shanten_memo: Dict[Tuple, int] = field(default_factory=dict)
    enable_v3: bool = False


def count_visible_from_game(game_state, player_index: int) -> Dict[int, int]:
    """统计所有公开可见牌 + 自己手牌。"""
    vis: Dict[int, int] = {}
    for p in game_state.player_list:
        for t in getattr(p, "discard_tiles", ()):
            tid = _norm(t)
            if 11 <= tid <= 47 and tid // 10 != 5:
                vis[tid] = vis.get(tid, 0) + 1
        for code in getattr(p, "combination_tiles", ()):
            for tid in _meld_tiles(code):
                if 11 <= tid <= 47 and tid // 10 != 5:
                    vis[tid] = vis.get(tid, 0) + 1
    me = game_state.player_list[player_index]
    for t in getattr(me, "hand_tiles", ()):
        tid = _norm(t)
        if 11 <= tid <= 47 and tid // 10 != 5:
            vis[tid] = vis.get(tid, 0) + 1
    return vis


def context_from_game(game_state, player_index: int, *, enable_v3: bool = False) -> QingqueContext:
    me = game_state.player_list[player_index]
    opponents = tuple(
        QingqueOpponentView(
            discards=tuple(_norm(t) for t in getattr(p, "discard_tiles", ())),
            melds=tuple(getattr(p, "combination_tiles", ())),
        )
        for p in game_state.player_list
        if p.player_index != player_index
    )
    return QingqueContext(
        hand=[_norm(t) for t in getattr(me, "hand_tiles", ()) if 11 <= _norm(t) <= 47 and _norm(t) // 10 != 5],
        melds=list(getattr(me, "combination_tiles", ())),
        visible=count_visible_from_game(game_state, player_index),
        seat_wind=player_index % 4,
        wall_left=len(getattr(game_state, "tiles_list", ())),
        opponents=opponents,
        enable_v3=enable_v3,
    )


def _shanten_key(counts: Dict[int, int], meld_count: int) -> Tuple:
    return (meld_count, tuple(sorted((t, c) for t, c in counts.items() if c > 0)))


def qingque_shanten(
    counts: Dict[int, int],
    meld_count: int,
    memo: Optional[Dict[Tuple, int]] = None,
) -> int:
    key = None
    if memo is not None:
        key = _shanten_key(counts, meld_count)
        cached = memo.get(key)
        if cached is not None:
            return cached
    d = xiangting_yiban(counts, meld_count)
    if meld_count == 0:
        d = min(d, shanten_qidui(counts), shanten_shisanyao(counts))
    if memo is not None and key is not None:
        memo[key] = d
    return d


def _candidate_draws(counts: Dict[int, int], meld_count: int) -> List[int]:
    cands = set()
    for t, c in counts.items():
        if c <= 0:
            continue
        if c < 4:
            cands.add(t)
        suit, num = t // 10, t % 10
        if suit in (1, 2, 3):
            for d in (-2, -1, 1, 2):
                n2 = num + d
                if 1 <= n2 <= 9:
                    nid = suit * 10 + n2
                    if counts.get(nid, 0) < 4:
                        cands.add(nid)
    if meld_count == 0 and shanten_shisanyao(counts) <= 3:
        for y in _YAOJIU_IDS:
            if counts.get(y, 0) < 4:
                cands.add(y)
    return sorted(cands)


def _live_remaining(tile: int, visible: Dict[int, int], held_count: int) -> int:
    return max(0, 4 - max(visible.get(tile, 0), held_count))


def _hypothetical_fan(
    ctx: QingqueContext,
    hand_13: Sequence[int],
    melds: Sequence[str],
    win_tile: int,
    self_draw: bool,
) -> float:
    key = (tuple(sorted(hand_13)), tuple(melds), win_tile, self_draw, ctx.seat_wind, ctx.wall_left == 0)
    cached = ctx.fan_memo.get(key)
    if cached is not None:
        return cached
    way = [_WIND_WAY[ctx.seat_wind % 4]]
    if self_draw:
        way.append("自摸")
        if ctx.wall_left == 0:
            way.append("海底捞月")
    elif ctx.wall_left == 0:
        way.append("河底捞鱼")
    fan, _ = qingque_hepai_check(list(hand_13), list(melds), way, win_tile)
    val = float(fan) if fan >= 1.0 else 0.0
    ctx.fan_memo[key] = val
    return val


@dataclass(frozen=True)
class TenpaiEval:
    live_waits: int
    total_ev: float
    avg_pts: float
    max_pts: float
    max_fan: float


def evaluate_tenpai(
    ctx: QingqueContext,
    hand_13: Sequence[int],
    melds: Sequence[str],
    visible: Optional[Dict[int, int]] = None,
) -> TenpaiEval:
    """精确评估 0 向听手牌的活张平方打点期望。"""
    vis = visible if visible is not None else ctx.visible
    waits = qingque_tingpai_check(list(hand_13), list(melds))
    if not waits:
        return TenpaiEval(0, 0.0, 0.0, 0.0, 0.0)
    held = Counter(hand_13)
    live_total = 0
    ev_sum = 0.0
    max_pts = 0.0
    max_fan = 0.0
    for w in waits:
        rem = _live_remaining(w, vis, held.get(w, 0))
        if rem <= 0:
            continue
        fan_ron = _hypothetical_fan(ctx, hand_13, melds, w, False)
        fan_tsumo = _hypothetical_fan(ctx, hand_13, melds, w, True)
        best_fan = max(fan_ron, fan_tsumo)
        if best_fan < 1.0:
            continue
        pts_ron = _payout(fan_ron)
        pts_tsumo = _payout(fan_tsumo)
        if pts_ron > 0.0:
            eff_pts = 0.72 * pts_ron + 0.28 * pts_tsumo
            eff_live = rem
        else:
            eff_pts = pts_tsumo
            eff_live = rem * 0.32
        live_total += rem
        ev_sum += eff_live * eff_pts
        p_hi = max(pts_ron, pts_tsumo)
        if p_hi > max_pts:
            max_pts = p_hi
        if best_fan > max_fan:
            max_fan = best_fan
    if live_total <= 0 or ev_sum <= 0.0:
        return TenpaiEval(0, 0.0, 0.0, 0.0, 0.0)
    return TenpaiEval(
        live_waits=live_total,
        total_ev=ev_sum,
        avg_pts=ev_sum / live_total,
        max_pts=max_pts,
        max_fan=max_fan,
    )


# ── 多方向做大牌势能评估（v3.1 全番型深度势能 + v2.1 基准势能） ───────────────

def hand_yaku_potential(
    hand_13: Sequence[int],
    melds: Sequence[str],
    seat_wind: int,
    *,
    deep_v3: bool = True,
) -> float:
    """O(1) 评估手牌朝青雀高打点番型（200~800+分）聚合的势能分。"""
    all_tiles: List[int] = list(hand_13)
    for m in melds:
        all_tiles.extend(_meld_tiles(m))
    if not all_tiles:
        return 0.0

    counts = Counter(all_tiles)
    hand_counts = Counter(hand_13)
    suit_cnt = {1: 0, 2: 0, 3: 0, 4: 0}
    num_cnt = Counter()
    suit_nums = {1: set(), 2: set(), 3: set()}
    for t, c in counts.items():
        s = t // 10
        if s in suit_cnt:
            suit_cnt[s] += c
        if s in (1, 2, 3):
            n = t % 10
            num_cnt[n] += c
            suit_nums[s].add(n)

    pot = 0.0

    # 1. 清一色 / 混一色（177~700+分核心主轴）
    dom_suit = max((1, 2, 3), key=lambda s: suit_cnt[s])
    dom_n = suit_cnt[dom_suit]
    hon_n = suit_cnt[4]
    if dom_n >= 10:
        pot += (3.8 if deep_v3 else 3.2) + (dom_n - 10) * (1.15 if deep_v3 else 0.9)
    elif deep_v3 and dom_n == 9 and hon_n == 0:
        pot += 1.8
    elif dom_n + hon_n >= 10:
        pot += (2.0 if deep_v3 else 1.6) + (dom_n + hon_n - 10) * (0.68 if deep_v3 else 0.55)
    elif dom_n + hon_n == 9:
        pot += 0.95 if deep_v3 else 0.7

    # 2. 对对和 / 三暗刻 / 七对 / 叠对 / 八归（186~550+分）
    has_seq_meld = any(m and m[0] in ("s", "S") for m in melds)
    if not has_seq_meld:
        pairs = sum(1 for c in hand_counts.values() if c == 2)
        trips = sum(1 for c in hand_counts.values() if c >= 3)
        open_trips = sum(1 for m in melds if m and m[0] in ("k", "K", "g", "G"))
        total_groups = pairs + trips + open_trips
        if total_groups >= 5:
            pot += (1.7 if deep_v3 else 1.4) + (trips + open_trips) * (0.8 if deep_v3 else 0.65)
        elif trips + open_trips >= 2:
            pot += (trips + open_trips) * (0.7 if deep_v3 else 0.55)
        if not melds and pairs + trips >= 5:
            pot += (1.6 if deep_v3 else 1.3) + (pairs + trips - 5) * (0.9 if deep_v3 else 0.75)

    # v3.1 新增：四张同牌（八归 552分 / 暗杠 57分 / 叠对）与高级七对连对
    if deep_v3:
        fours = sum(1 for c in counts.values() if c == 4)
        if fours >= 1:
            pot += 0.9 * fours + (1.4 if fours >= 2 else 0.0)
        if not melds:
            for s in (1, 2, 3):
                p_nums = sorted(t % 10 for t, c in hand_counts.items() if c >= 2 and t // 10 == s)
                adj = sum(1 for i in range(len(p_nums) - 1) if p_nums[i + 1] - p_nums[i] == 1)
                if adj >= 2:
                    pot += 0.75 * adj

    # 3. 混带幺 / 清带幺 / 混幺九（183~500+分）
    yaojiu_n = sum(c for t, c in counts.items() if t in _YAOJIU_IDS)
    edge_compat = sum(
        c for t, c in counts.items()
        if t in _YAOJIU_IDS or (t // 10 in (1, 2, 3) and t % 10 in (2, 3, 7, 8))
    )
    if yaojiu_n >= 6 and edge_compat >= 11:
        pot += (1.8 if deep_v3 else 1.4) + (yaojiu_n - 6) * 0.42

    # 4. 青雀特色数字番：四聚(243) / 三聚 / 连数(321) / 满庭芳(399) / 三色同刻(624) / 镜数映数(258~327)
    if num_cnt:
        best_win4 = max(
            sum(num_cnt[n] for n in range(start, start + 4))
            for start in range(1, 7)
        )
        if best_win4 >= 10:
            pot += (2.1 if deep_v3 else 1.5) + (best_win4 - 10) * 0.6
        elif best_win4 == 9:
            pot += 0.9 if deep_v3 else 0.6
        max_same_num = max(num_cnt.values())
        if max_same_num >= 5:
            pot += (1.2 if deep_v3 else 0.9) + (max_same_num - 5) * 0.55
        if deep_v3:
            # 镜数 / 映数（对称数字对 {1,9},{2,8},{3,7},{4,6} 高度对称）
            sym_pairs = sum(min(num_cnt[k], num_cnt[10 - k]) for k in (1, 2, 3, 4))
            if sym_pairs >= 5 and hon_n == 0:
                pot += 1.1 + (sym_pairs - 5) * 0.45
            # 三连刻（同花色连续 3 刻子，507分）与三步高（384分）
            for s in (1, 2, 3):
                trip_nums = [n for n in range(1, 10) if counts.get(s * 10 + n, 0) >= 2]
                for k in range(1, 8):
                    if all(counts.get(s * 10 + n, 0) >= 2 for n in (k, k + 1, k + 2)):
                        pot += 1.35
                        break
                # 连续顺子重叠（一般高/三步高/三连环）
                if suit_cnt[s] >= 7:
                    dense = sum(1 for n in range(2, 9) if counts.get(s * 10 + n, 0) >= 2)
                    if dense >= 3:
                        pot += 0.7

    # 5. 一气贯通 / 三步高 / 三连环（168~384分）
    for s in (1, 2, 3):
        if len(suit_nums[s]) >= 7:
            pot += (1.35 if deep_v3 else 1.1) + (len(suit_nums[s]) - 7) * 0.45

    # 6. 三色同顺 / 三色同刻 / 三色贯通（108~624分）
    common_3 = suit_nums[1] & suit_nums[2] & suit_nums[3]
    if len(common_3) >= 2:
        pot += (0.95 if deep_v3 else 0.7) + (len(common_3) - 2) * 0.5
        if deep_v3:
            # 三色同刻（同一数字在三门各有 >=2 张）
            for n in common_3:
                if sum(1 for s in (1, 2, 3) if counts.get(s * 10 + n, 0) >= 2) == 3:
                    pot += 1.6

    # 7. 镜同 / 双龙会（两门数字高度重合，156~180分）
    for s1, s2 in ((1, 2), (1, 3), (2, 3)):
        if suit_cnt[s1] >= 5 and suit_cnt[s2] >= 5:
            overlap = len(suit_nums[s1] & suit_nums[s2])
            if overlap >= 4:
                pot += (1.05 if deep_v3 else 0.8) + (overlap - 4) * 0.4

    # 8. 番牌刻 / 风牌三刻 / 三元牌
    seat_wind_tid = 41 + (seat_wind % 4)
    for h in (seat_wind_tid, 45, 46, 47):
        c = counts.get(h, 0)
        if c >= 3:
            pot += 0.45
        elif c == 2:
            pot += 0.18
    if sum(1 for w in _WIND_IDS if counts.get(w, 0) >= 3) >= 2:
        pot += 1.8 if deep_v3 else 1.5
    if sum(1 for d in _DRAGON_IDS if counts.get(d, 0) >= 3) >= 2:
        pot += 2.1 if deep_v3 else 1.8

    if not melds:
        pot += 0.75 if deep_v3 else 0.65

    return pot


# ── 全铳制三层防守（含对手预估打点） ──────────────────────────────────────────

def _opponent_flush_suit(opp: QingqueOpponentView) -> Optional[int]:
    if len(opp.melds) < 2:
        return None
    suits = set()
    for code in opp.melds:
        for t in _meld_tiles(code):
            if 11 <= t <= 39:
                suits.add(t // 10)
    if len(suits) != 1:
        return None
    s = next(iter(suits))
    same_disc = sum(1 for t in opp.discards if 11 <= t <= 39 and t // 10 == s)
    return s if same_disc <= 2 else None


def opponent_threat(opp: QingqueOpponentView, wall_left: int) -> float:
    """对手大牌威胁度 [0.0, 1.0]。"""
    m = len(opp.melds)
    if wall_left > 60 and m == 0:
        return 0.0
    progress = max(0.0, min(1.0, (84 - wall_left) / 72.0))
    base = 0.32 * (progress ** 1.45) + 0.24 * min(3, m)
    mult = 1.0
    if _opponent_flush_suit(opp) is not None:
        mult *= 1.45
    if m >= 2 and all(c and c[0] in ("k", "K", "g", "G") for c in opp.melds):
        mult *= 1.35
    if any(c and c[0] in ("g", "G") for c in opp.melds):
        mult *= 1.15
    return min(1.0, base * mult)


def opponent_expected_payout(opp: QingqueOpponentView) -> float:
    """基于公开副露估算对手和牌预期打点（用于全铳制攻防盈亏比判断）。"""
    m = len(opp.melds)
    if _opponent_flush_suit(opp) is not None:
        return 340.0
    if m >= 2 and all(c and c[0] in ("k", "K", "g", "G") for c in opp.melds):
        return 310.0
    if m >= 2:
        return 230.0
    return 180.0


def tile_danger_for_opponent(
    tile: int,
    opp: QingqueOpponentView,
    visible: Dict[int, int],
) -> float:
    tid = _norm(tile)
    if tid in opp.discards:
        return 0.1
    suit = tid // 10
    if suit == 4:
        seen = visible.get(tid, 0)
        if seen >= 3:
            return 0.2
        if seen == 2:
            return 0.5
        if seen == 1:
            return 0.9
        return 1.6
    num = tid % 10
    if num in (4, 5, 6):
        danger = 1.85
    elif num in (3, 7):
        danger = 1.45
    elif num in (2, 8):
        danger = 1.05
    else:
        danger = 0.75
    if num in (1, 2) and visible.get(suit * 10 + 3, 0) >= 4:
        danger *= 0.45
    elif num in (8, 9) and visible.get(suit * 10 + 7, 0) >= 4:
        danger *= 0.45
    dset = set(opp.discards)
    if (num >= 4 and (suit * 10 + num - 3) in dset) or (num <= 6 and (suit * 10 + num + 3) in dset):
        danger *= 0.72
    flush_suit = _opponent_flush_suit(opp)
    if flush_suit is not None:
        danger *= 2.6 if suit == flush_suit else 0.35
    return danger


def total_tile_danger(tile: int, ctx: QingqueContext) -> float:
    if not ctx.opponents:
        return 0.0
    total = 0.0
    for opp in ctx.opponents:
        t = opponent_threat(opp, ctx.wall_left)
        if t > 0.0:
            total += t * tile_danger_for_opponent(tile, opp, ctx.visible)
    return total


def max_opponent_threat(ctx: QingqueContext) -> float:
    if not ctx.opponents:
        return 0.0
    return max((opponent_threat(opp, ctx.wall_left) for opp in ctx.opponents), default=0.0)


def max_opponent_payout(ctx: QingqueContext) -> float:
    if not ctx.opponents:
        return 0.0
    return max(
        (opponent_expected_payout(opp) for opp in ctx.opponents if opponent_threat(opp, ctx.wall_left) >= 0.65),
        default=0.0,
    )


# ── 1 向听精确平方打点前瞻 ────────────────────────────────────────────────────

def evaluate_one_shanten_ev(
    ctx: QingqueContext,
    hand_13: Sequence[int],
    melds: Sequence[str],
    max_draws_to_check: int = 9,
) -> Tuple[float, float, int]:
    """评估 1 向听手牌一摸进听后的平方打点期望。"""
    counts = Counter(hand_13)
    n_melds = len(melds)
    draws = _candidate_draws(counts, n_melds)
    dom_suit = max((1, 2, 3), key=lambda s: sum(c for t, c in counts.items() if t // 10 == s))
    advancing: List[Tuple[float, int, int]] = []
    for d in draws:
        live = _live_remaining(d, ctx.visible, counts.get(d, 0))
        if live <= 0:
            continue
        counts[d] = counts.get(d, 0) + 1
        s_after = qingque_shanten(counts, n_melds, ctx.shanten_memo)
        counts[d] -= 1
        if counts[d] <= 0:
            del counts[d]
        if s_after <= 0:
            # v3.1：按“活张 + 高番方向相关性（主花色/成刻/成对）”排序，避免高番进张被截断
            prio = float(live)
            if ctx.enable_v3:
                if d // 10 == dom_suit:
                    prio += 1.2
                if counts.get(d, 0) >= 2:
                    prio += 1.0  # 摸成暗刻/对对
            advancing.append((prio, live, d))
    if not advancing:
        return 0.0, 0.0, 0

    advancing.sort(reverse=True)
    total_ev = 0.0
    weighted_pts_sum = 0.0
    legal_copies = 0
    limit = 12 if ctx.enable_v3 else max_draws_to_check
    live_exp = 0.72 if ctx.enable_v3 else 0.75

    for _, live, d in advancing[:limit]:
        hand_14 = list(hand_13) + [d]
        next_vis = dict(ctx.visible)
        next_vis[d] = next_vis.get(d, counts.get(d, 0)) + 1
        best_draw_ev = 0.0
        best_draw_avg_pts = 0.0
        seen_disc = set()
        c14 = Counter(hand_14)
        for disc in hand_14:
            if disc == d or disc in seen_disc:
                continue
            seen_disc.add(disc)
            c14[disc] -= 1
            if c14[disc] <= 0:
                del c14[disc]
            s_disc = qingque_shanten(c14, n_melds, ctx.shanten_memo)
            c14[disc] = c14.get(disc, 0) + 1
            if s_disc != 0:
                continue
            rem_13 = list(hand_14)
            rem_13.remove(disc)
            te = evaluate_tenpai(ctx, rem_13, melds, visible=next_vis)
            if te.live_waits <= 0:
                continue
            score = (te.live_waits ** live_exp) * te.avg_pts
            if score > best_draw_ev:
                best_draw_ev = score
                best_draw_avg_pts = te.avg_pts
        if best_draw_ev > 0.0:
            total_ev += live * best_draw_ev
            weighted_pts_sum += live * best_draw_avg_pts
            legal_copies += live

    avg_pts = (weighted_pts_sum / legal_copies) if legal_copies > 0 else 0.0
    return total_ev, avg_pts, legal_copies


def _structural_ukeire(
    counts: Dict[int, int],
    n_melds: int,
    visible: Dict[int, int],
    shanten_now: int,
    memo: Dict[Tuple, int],
) -> int:
    total = 0
    for d in _candidate_draws(counts, n_melds):
        live = _live_remaining(d, visible, counts.get(d, 0))
        if live <= 0:
            continue
        counts[d] = counts.get(d, 0) + 1
        s2 = qingque_shanten(counts, n_melds, memo)
        counts[d] -= 1
        if counts[d] <= 0:
            del counts[d]
        if s2 < shanten_now:
            total += live
    return total


# ── 主弃牌决策 ────────────────────────────────────────────────────────────────

@dataclass(frozen=True)
class DiscardPlan:
    tile: int
    shanten: int
    ev_score: float
    avg_pts: float
    ukeire: int
    danger: float


def choose_discard_plan(
    ctx: QingqueContext,
    *,
    allow_one_shanten_lookahead: bool = True,
) -> Optional[DiscardPlan]:
    hand = list(ctx.hand)
    if not hand:
        return None
    n_melds = len(ctx.melds)
    base_counts = Counter(hand)
    unique_tiles = sorted(base_counts.keys())
    v3 = ctx.enable_v3

    # 1. 计算每个切牌后的基础向听、青雀高番势能与危险度
    raw_cands: List[Tuple[int, List[int], int, int, float, float]] = []
    min_shanten = 99
    for tid in unique_tiles:
        rem = list(hand)
        rem.remove(tid)
        rc = Counter(rem)
        s = qingque_shanten(rc, n_melds, ctx.shanten_memo)
        if s < min_shanten:
            min_shanten = s
        pot = hand_yaku_potential(rem, ctx.melds, ctx.seat_wind, deep_v3=v3)
        danger = total_tile_danger(tid, ctx)
        raw_cands.append((tid, rem, s, 0, pot, danger))

    # 2. 精确评估所有 0 向听候选（合法听 + 平方打点期望）
    best_tenpai: Optional[DiscardPlan] = None
    best_tenpai_key: Optional[Tuple] = None
    tenpai_exp = 0.74 if v3 else 0.80
    for tid, rem, s, _, pot, danger in raw_cands:
        if s != 0:
            continue
        te = evaluate_tenpai(ctx, rem, ctx.melds)
        if te.live_waits <= 0:
            continue
        ev_score = (te.live_waits ** tenpai_exp) * te.avg_pts
        key = (round(ev_score, 2), te.live_waits, -round(danger, 2), round(pot, 2))
        if best_tenpai_key is None or key > best_tenpai_key:
            best_tenpai_key = key
            best_tenpai = DiscardPlan(
                tile=tid,
                shanten=0,
                ev_score=ev_score,
                avg_pts=te.avg_pts,
                ukeire=te.live_waits,
                danger=danger,
            )

    low_tenpai_thresh = _V3_LOW_TENPAI_AVG_PTS if v3 else _V2_LOW_TENPAI_AVG_PTS
    bonus_wall = _V3_HIGH_TENPAI_BONUS_WALL if v3 else _V2_HIGH_TENPAI_BONUS_WALL

    can_upgrade_from_low_tenpai = (
        best_tenpai is not None
        and ctx.wall_left >= bonus_wall
        and best_tenpai.avg_pts < low_tenpai_thresh
        and allow_one_shanten_lookahead
    )
    if best_tenpai is not None and not can_upgrade_from_low_tenpai:
        threat = max_opponent_threat(ctx)
        if v3:
            opp_pts = max_opponent_payout(ctx)
            # 全铳制盈亏比防守：自身听牌远小于高危对手预估打点且切极危生张时，改切安全牌兜底
            if (
                ctx.wall_left < 24
                and threat >= 0.72
                and opp_pts > 0.0
                and best_tenpai.avg_pts < 0.42 * opp_pts
                and best_tenpai.danger >= 2.2
            ):
                safest = min(raw_cands, key=lambda x: (x[5], x[2], -x[4]))
                return DiscardPlan(safest[0], safest[2], 0.0, 0.0, 0, safest[5])
        elif ctx.wall_left < 20 and threat >= 0.78 and best_tenpai.avg_pts <= 60.0 and best_tenpai.danger >= 2.6:
            safest = min(raw_cands, key=lambda x: (x[5], x[2], -x[4]))
            return DiscardPlan(safest[0], safest[2], 0.0, 0.0, 0, safest[5])
        return best_tenpai

    # 3. 评估 1 向听候选（精确 1 摸平方打点前瞻）
    one_shanten_cands: List[Tuple[int, List[int], int, float, float]] = []
    for tid, rem, s, _, pot, danger in raw_cands:
        if s == 1 or (s == 0 and (best_tenpai is None or tid != best_tenpai.tile)):
            rc = Counter(rem)
            u = _structural_ukeire(rc, n_melds, ctx.visible, max(1, s), ctx.shanten_memo)
            one_shanten_cands.append((tid, rem, u, pot, danger))

    best_one: Optional[DiscardPlan] = None
    if one_shanten_cands and allow_one_shanten_lookahead and min_shanten <= 1:
        by_u = sorted(one_shanten_cands, key=lambda x: (-x[2], -x[3], x[4]))[:3]
        by_p = sorted(one_shanten_cands, key=lambda x: (-x[3], -x[2], x[4]))[:3]
        shortlist_ids = []
        shortlist = []
        cap = 5 if v3 else 4
        for item in by_p[:3] + by_u[:2] + by_u[2:]:
            if item[0] not in shortlist_ids:
                shortlist_ids.append(item[0])
                shortlist.append(item)
                if len(shortlist) >= cap:
                    break
        threat = max_opponent_threat(ctx)
        opp_pts = max_opponent_payout(ctx) if v3 else 0.0
        best_1_key: Optional[Tuple] = None
        for tid, rem, u, pot, danger in shortlist:
            ev1, avg_pts1, legal_u = evaluate_one_shanten_ev(ctx, rem, ctx.melds)
            if legal_u <= 0:
                continue
            total_1 = ev1 * (1.0 + (0.07 if v3 else 0.05) * pot)
            if v3 and threat >= 0.68 and opp_pts > 0.0:
                # 全铳制高危对手前，轻微折减极危生张的 1 向听 EV，避免白送 300 分大铳
                total_1 -= 32.0 * danger * (opp_pts / 250.0)
            key = (round(total_1, 1), legal_u, -round(danger, 2), u)
            if best_1_key is None or key > best_1_key:
                best_1_key = key
                best_one = DiscardPlan(
                    tile=tid,
                    shanten=1,
                    ev_score=total_1,
                    avg_pts=avg_pts1,
                    ukeire=legal_u,
                    danger=danger,
                )

    # 精确 EV 升级判断：早中盘低分听牌（< 7.4 番）vs 高打点 1 向听做大牌（>= 9 番）
    if best_tenpai is not None:
        if v3 and best_one is not None and best_one.ukeire >= 4:
            if best_one.avg_pts >= max(240.0, best_tenpai.avg_pts * 2.0):
                return best_one
        elif (
            not v3
            and best_one is not None
            and best_one.avg_pts >= max(210.0, best_tenpai.avg_pts * 2.4)
            and best_one.ukeire >= 5
        ):
            return best_one
        return best_tenpai

    if best_one is not None:
        return best_one

    # 4. 2~4 向听（或死 1 向听）：进张速度 × 大牌方向深度势能
    early_factor = max(0.0, min(1.0, (ctx.wall_left - 14) / 56.0))
    pot_weight = (0.36 * early_factor + 0.07) if v3 else (0.30 * early_factor + 0.06)

    threat = max_opponent_threat(ctx)
    fold_wall = 22 if v3 else 18
    fold_threat = 0.74 if v3 else 0.78
    if ctx.wall_left < fold_wall and threat >= fold_threat and min_shanten >= 2:
        safest = min(raw_cands, key=lambda x: (x[5], x[2], -x[4]))
        return DiscardPlan(safest[0], safest[2], 0.0, 0.0, 0, safest[5])

    max_s_eval = min_shanten + (1 if ctx.wall_left >= 34 and min_shanten <= 2 else 0)
    best_far: Optional[DiscardPlan] = None
    best_far_key: Optional[Tuple] = None
    for tid, rem, s, _, pot, danger in raw_cands:
        if s > max_s_eval:
            continue
        rc = Counter(rem)
        u = _structural_ukeire(rc, n_melds, ctx.visible, max(1, s), ctx.shanten_memo)
        step_penalty = 1.0 if s == min_shanten else (0.54 if v3 else 0.50)
        eff_score = step_penalty * (u + 1.5) * (1.0 + pot_weight * pot)
        if threat >= 0.65:
            eff_score -= (0.85 if v3 else 0.45) * danger
        key = (round(eff_score, 2), -s, u, -round(danger, 2))
        if best_far_key is None or key > best_far_key:
            best_far_key = key
            best_far = DiscardPlan(
                tile=tid,
                shanten=s,
                ev_score=eff_score,
                avg_pts=0.0,
                ukeire=u,
                danger=danger,
            )

    if best_far is not None:
        return best_far
    fallback = raw_cands[0]
    return DiscardPlan(fallback[0], fallback[2], 0.0, 0.0, 0, fallback[5])


# ── 手牌动作（暗杠/加杠/切牌） ────────────────────────────────────────────────

def choose_hand_action(
    ctx: QingqueContext,
    actions: Sequence[str],
) -> Tuple[str, Optional[int], int]:
    action_set = set(actions)
    hand = list(ctx.hand)
    if not hand:
        return "pass", None, -1

    baseline = choose_discard_plan(ctx, allow_one_shanten_lookahead=True)
    threat = max_opponent_threat(ctx)

    if "angang" in action_set:
        counts = Counter(hand)
        for tid in sorted(t for t, c in counts.items() if c >= 4):
            rest = [t for t in hand if t != tid]
            sub_ctx = QingqueContext(
                hand=rest,
                melds=ctx.melds + [f"G{tid}"],
                visible=ctx.visible,
                seat_wind=ctx.seat_wind,
                wall_left=ctx.wall_left,
                opponents=ctx.opponents,
                fan_memo=ctx.fan_memo,
                shanten_memo=ctx.shanten_memo,
                enable_v3=ctx.enable_v3,
            )
            s_after = qingque_shanten(Counter(rest), len(sub_ctx.melds), ctx.shanten_memo)
            if baseline is not None and s_after <= baseline.shanten:
                if s_after == 0:
                    te = evaluate_tenpai(sub_ctx, rest, sub_ctx.melds)
                    if te.live_waits > 0 and (te.live_waits ** 0.74) * te.avg_pts >= baseline.ev_score * 0.9:
                        return "angang", tid, -1
                else:
                    return "angang", tid, -1

    if "jiagang" in action_set and threat < 0.70:
        for code in ctx.melds:
            if not code or code[0] != "k":
                continue
            try:
                tid = _norm(int(code[1:]))
            except ValueError:
                continue
            if tid in hand:
                rest = list(hand)
                rest.remove(tid)
                upgraded = [f"g{tid}" if m == code else m for m in ctx.melds]
                s_after = qingque_shanten(Counter(rest), len(upgraded), ctx.shanten_memo)
                if baseline is not None and s_after <= baseline.shanten:
                    return "jiagang", tid, -1

    if "cut" in action_set and baseline is not None:
        cut_idx = len(hand) - 1 if hand[-1] == baseline.tile else hand.index(baseline.tile)
        return "cut", baseline.tile, cut_idx
    return "pass", None, -1


# ── 鸣牌决策（吃/碰/明杠） ────────────────────────────────────────────────────

def choose_claim_action(
    ctx: QingqueContext,
    actions: Sequence[str],
    cut_tile: int,
) -> str:
    action_set = set(actions)
    claim_opts = (
        ("peng", (cut_tile, cut_tile), f"k{cut_tile}", True),
        ("gang", (cut_tile, cut_tile, cut_tile), f"g{cut_tile}", False),
        ("chi_left", (cut_tile - 2, cut_tile - 1), f"s{cut_tile - 1}", True),
        ("chi_mid", (cut_tile - 1, cut_tile + 1), f"s{cut_tile}", True),
        ("chi_right", (cut_tile + 1, cut_tile + 2), f"s{cut_tile + 1}", True),
    )
    available = [opt for opt in claim_opts if opt[0] in action_set]
    if not available:
        return "pass"

    hand = list(ctx.hand)
    n_melds = len(ctx.melds)
    base_counts = Counter(hand)
    s_before = qingque_shanten(base_counts, n_melds, ctx.shanten_memo)
    v3 = ctx.enable_v3
    pot_before = hand_yaku_potential(hand, ctx.melds, ctx.seat_wind, deep_v3=v3)

    threat = max_opponent_threat(ctx)
    endgame_defensive = ctx.wall_left < (22 if v3 else 18) and s_before >= 2 and threat >= (0.74 if v3 else 0.78)

    if n_melds == 0 and shanten_qidui(base_counts) < xiangting_yiban(base_counts, 0):
        return "pass"

    before_tenpai = evaluate_tenpai(ctx, hand, ctx.melds) if s_before == 0 else None
    tenpai_exp = 0.74 if v3 else 0.80
    before_ev = (before_tenpai.live_waits ** tenpai_exp) * before_tenpai.avg_pts if before_tenpai and before_tenpai.live_waits > 0 else 0.0

    best_action = "pass"
    best_rank: Optional[Tuple] = None

    for action, needed, code, need_cut in available:
        rest = list(hand)
        ok = True
        for t in needed:
            if t not in rest:
                ok = False
                break
            rest.remove(t)
        if not ok:
            continue
        next_melds = ctx.melds + [code]
        sub_ctx = QingqueContext(
            hand=rest,
            melds=next_melds,
            visible=ctx.visible,
            seat_wind=ctx.seat_wind,
            wall_left=ctx.wall_left,
            opponents=ctx.opponents,
            fan_memo=ctx.fan_memo,
            shanten_memo=ctx.shanten_memo,
            enable_v3=v3,
        )

        if need_cut and rest:
            plan = choose_discard_plan(sub_ctx, allow_one_shanten_lookahead=False)
            if plan is None:
                continue
            s_after = plan.shanten
            rem_after = list(rest)
            rem_after.remove(plan.tile)
        else:
            s_after = qingque_shanten(Counter(rest), len(next_melds), ctx.shanten_memo)
            rem_after = rest
            plan = None

        if endgame_defensive and s_after > 0:
            continue
        if s_after > s_before:
            continue
        if s_after == s_before and s_before > 0:
            continue

        if s_after == 0:
            te = evaluate_tenpai(sub_ctx, rem_after, next_melds)
            if te.live_waits <= 0:
                continue
            ev_after = (te.live_waits ** tenpai_exp) * te.avg_pts
            # 保护门前清（3.61 番平方倍增效应）：早中盘首次破门清听牌需达高打点（>= 180 分 ≈ 7.8 番）
            min_open_tenpai = 180.0 if v3 else 70.0
            if n_melds == 0 and ctx.wall_left >= 22 and te.avg_pts < min_open_tenpai:
                continue
            if s_before == 0 and ev_after <= before_ev * 1.15:
                continue
            rank = (2, round(ev_after, 1), te.live_waits)
        elif s_after == 1:
            ev1, avg_pts1, legal_u = evaluate_one_shanten_ev(sub_ctx, rem_after, next_melds, max_draws_to_check=7)
            if legal_u <= 0:
                continue
            # 首次破门清进 1 向听：需达 9 番级大牌潜力（>= 240 分）才放弃门前清
            min_open_1s = 240.0 if v3 else 150.0
            if n_melds == 0 and ctx.wall_left >= 24 and avg_pts1 < min_open_1s:
                continue
            rank = (1, round(ev1, 1), legal_u)
        else:
            pot_after = hand_yaku_potential(rem_after, next_melds, ctx.seat_wind, deep_v3=v3)
            # 2 向听破门清：仅在真正成型的大牌主轴（清一色/对对和/混带幺/三色同刻等）时才副露
            min_pot_jump = max(5.2, pot_before + 1.1) if v3 else max(2.6, pot_before + 0.6)
            if n_melds == 0 and pot_after < min_pot_jump:
                continue
            if n_melds >= 1 and pot_after < pot_before - 0.3:
                continue
            u_after = plan.ukeire if plan is not None else 0
            rank = (0, round(pot_after * 10 + u_after, 1), u_after)

        if best_rank is None or rank > best_rank:
            best_rank = rank
            best_action = action

    return best_action


# ── 异步游戏循环入口 ──────────────────────────────────────────────────────────

def should_accept_qingque_hu(
    game_state,
    player_index: int,
    hu_action: str,
    *,
    enable_v3: bool = False,
) -> bool:
    """战略性过小和：早中盘放弃超小和，保留冲刺 240~600+ 大牌的机会。"""
    if not should_accept_hu(game_state, player_index, hu_action):
        return False
    wall_left = len(getattr(game_state, "tiles_list", ()))
    res = getattr(game_state, "result_dict", {}).get(hu_action)
    if not res or len(res) < 1:
        return True
    try:
        fan = float(res[0])
    except (TypeError, ValueError):
        return True
    pts = _payout(fan)
    if not enable_v3:
        if pts <= 12.0 and wall_left >= 32:
            return False
        return True

    if pts <= 15.0 and wall_left >= 22:
        return False
    if hu_action != "hu_self" and pts <= 45.0 and wall_left >= 34:
        return False
    return True


@paced_bot(lambda: _BOT_DELAY)
async def qingque_heuristic_bot_action(
    game_state,
    player_index: int,
    action_list: list,
    game_status: str,
):
    if not await _wait_until_actionable(game_state, player_index):
        return
    expected_tick = getattr(game_state, "server_action_tick", None)
    if not bot_action_is_current(game_state, player_index, expected_tick):
        return
    player = game_state.player_list[player_index]

    if game_status == "waiting_buhua_round":
        await _handle_buhua_round(game_state, player_index, action_list, player)
        return

    if game_status in ("waiting_hand_action", "onlycut_after_action"):
        if "buhua" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "buhua", None, None, None, None)
            return
        ctx = context_from_game(game_state, player_index)
        if "hu_self" in action_list and should_accept_qingque_hu(
            game_state, player_index, "hu_self", enable_v3=ctx.enable_v3
        ):
            await submit_bot_action(get_ai_action, game_state, player_index, "hu_self", None, None, None, None)
            return
        action, tile, cut_index = await run_room_bot_cpu(
            game_state, choose_hand_action, ctx, tuple(action_list)
        )
        if not bot_action_is_current(game_state, player_index, expected_tick):
            return
        if action == "cut" and tile is not None:
            hand = list(player.hand_tiles)
            is_moqie = infer_bot_cut_class(hand, tile, cut_index, draw_slot=has_draw_slot(player))
            await submit_bot_action(get_ai_action, game_state, player_index, "cut", is_moqie, tile, cut_index, None)
        elif action in ("angang", "jiagang"):
            await submit_bot_action(get_ai_action, game_state, player_index, action, None, None, None, tile)
        return

    if game_status == "waiting_action_qianggang":
        for hu in _RON_HU_ACTIONS:
            if hu in action_list and should_accept_hu(game_state, player_index, hu):
                await submit_bot_action(get_ai_action, game_state, player_index, hu, None, None, None, None)
                return
        if "pass" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "pass", None, None, None, None)
        return

    if game_status == "waiting_action_after_cut":
        ctx = context_from_game(game_state, player_index)
        for hu in _RON_HU_ACTIONS:
            if hu in action_list and should_accept_qingque_hu(
                game_state, player_index, hu, enable_v3=ctx.enable_v3
            ):
                await submit_bot_action(get_ai_action, game_state, player_index, hu, None, None, None, None)
                return
        discards = game_state.player_list[game_state.current_player_index].discard_tiles
        cut_tile = discards[-1] if discards else None
        if cut_tile is None:
            if "pass" in action_list:
                await submit_bot_action(get_ai_action, game_state, player_index, "pass", None, None, None, None)
            return
        choice = await run_room_bot_cpu(
            game_state, choose_claim_action, ctx, tuple(action_list), _norm(cut_tile)
        )
        if bot_action_is_current(game_state, player_index, expected_tick):
            await submit_bot_action(get_ai_action, game_state, player_index, choice, None, None, None, None)
