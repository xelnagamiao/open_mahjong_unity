"""川麻高性能罗伯特（user_id=3）：指数番 EV + 刮风下雨 + 查叫防花猪 + 2v2/复式队友配合。

覆盖全部川麻子规则：
- 血战到底（sichuan/standard）、血战换三张（sichuan/sbr_exchange）
- 血流成河（sichuan/xueliu 等）
- 川麻 2v2 双打（sichuan/doubles）、血流双打（sichuan/xueliu_doubles）
- 川麻 2v2 复式（sichuan/doubles_duplicate, sichuan/xueliu_doubles_duplicate）

严格仅读取合法公开信息（绝不窥视队友或对手暗手牌）。
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field
from itertools import combinations
from typing import Dict, List, Optional, Sequence, Tuple

from ...game_calculation.sichuan.sichuan_hepai_check import Sichuan_Hepai_Check
from ...game_calculation.sichuan.xueliu_hepai_check import Xueliu_Hepai_Check
from ..public.ai.bot_executor import bot_action_is_current, run_room_bot_cpu
from ..public.ai.get_action import get_ai_action
from ..public.ai.guobiao_shanten import xiangting_yiban
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _wait_until_actionable
from ..public.ai.smart_bot_logic import count_visible_tiles, tile_to_34
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class
from .xueliu_bot import shanten as _sichuan_shanten

_SICHUAN_CHECKER = Sichuan_Hepai_Check()
_XUELIU_CHECKER = Xueliu_Hepai_Check()
_ALL_SUITS = (1, 2, 3)


def _norm(tile: int) -> int:
    return int(tile) % 100


def _meld_suit(code: str) -> int:
    if not code or len(code) < 2:
        return 0
    try:
        return _norm(int(code[1:])) // 10
    except ValueError:
        return 0


def _meld_tile(code: str) -> int:
    if not code or len(code) < 2:
        return 0
    try:
        return _norm(int(code[1:]))
    except ValueError:
        return 0


@dataclass(frozen=True)
class SichuanPlayerPublicView:
    """他家公开信息快照（绝不包含暗手牌）。"""

    seat: int
    team_id: Optional[int]
    discards: Tuple[int, ...] = ()
    melds: Tuple[str, ...] = ()
    dingque_suit: int = 0
    is_hu: bool = False
    has_won: bool = False
    win_count: int = 0


@dataclass
class SichuanContext:
    hand: List[int]
    melds: List[str]
    visible_34: Tuple[int, ...]
    meld_count: int = 4  # 血流弃三张为 3，血战/换三张为 4
    dingque_suit: int = 0  # 0 表示未显式定缺（如 SBR 双打）
    post_hu_lock: bool = False
    wall_left: int = 50  # 有效剩余牌墙（复式已含个人独立墙+已胡队友可继承墙）
    fan_cap: int = 5
    is_xueliu: bool = False
    is_team_game: bool = False
    is_duplicate: bool = False
    is_imp_duplicate: bool = False
    team_first_two_end: bool = False
    my_seat: int = 0
    my_team_id: Optional[int] = None
    has_kong_income: bool = False
    players: Tuple[SichuanPlayerPublicView, ...] = ()
    fan_memo: Dict[Tuple, int] = field(default_factory=dict)


def _effective_wall_left(game_state, player_index: int) -> int:
    tiles = getattr(game_state, "tiles_list", ())
    # SBR 复式独立墙：自己墙 + 已胡队友的可继承墙（仅公开张数，绝不读牌面）
    if hasattr(tiles, "remaining_counts"):
        counts = tiles.remaining_counts()
        me = game_state.player_list[player_index]
        orig = getattr(me, "original_player_index", player_index)
        if getattr(game_state, "is_xueliu", False):
            # 血流复式：自身固定8张剩余 + 公共墙估算每人分摊
            common_left = len(getattr(tiles, "common", ()))
            own_left = counts[orig] if 0 <= orig < len(counts) else 0
            return own_left * 4 + common_left
        own_left = counts[orig] if 0 <= orig < len(counts) else 0
        partner_left = 0
        partner = next(
            (p for p in game_state.player_list if getattr(p, "original_player_index", -1) == (orig + 2) % 4),
            None,
        )
        if partner is not None and getattr(partner, "is_hu", False):
            p_orig = getattr(partner, "original_player_index", (orig + 2) % 4)
            if 0 <= p_orig < len(counts):
                partner_left = counts[p_orig]
        # 换算到标准 4 人全局牌墙刻度（单人 14 张 ≈ 全局 56 张）
        return (own_left + partner_left) * 4
    return len(tiles)


def build_visible_34(game_state) -> Tuple[int, ...]:
    vis = list(count_visible_tiles(game_state))
    for p in game_state.player_list:
        for t in getattr(p, "huapai_list", ()):
            idx = tile_to_34(t)
            if 0 <= idx < 34:
                vis[idx] += 1
        if not getattr(game_state, "xueliu_exchange", False):
            for t in getattr(p, "xueliu_throw_tiles", ()):
                idx = tile_to_34(t)
                if 0 <= idx < 34:
                    vis[idx] += 1
    return tuple(vis)


def context_from_game(game_state, player_index: int) -> SichuanContext:
    me = game_state.player_list[player_index]
    is_xueliu = bool(getattr(game_state, "is_xueliu", False))
    meld_count = int(getattr(game_state, "xueliu_meld_count", 3) if is_xueliu else 4)
    is_team = bool(getattr(game_state, "is_team_game", False))
    is_dup = getattr(game_state, "duplicate_match", None) is not None or "duplicate" in str(getattr(game_state, "sub_rule", ""))
    is_imp = is_dup and not is_xueliu
    profile = getattr(game_state, "xueliu_rule_profile", None) or {}
    team_first_two = bool(
        profile.get("team_first_two_end", getattr(game_state, "is_doubles", False) and not is_xueliu)
    )
    my_team = getattr(me, "team_id", None) if is_team else None
    has_kong = any(m and m[0] in ("g", "G") for m in getattr(me, "combination_tiles", ()))
    fan_cap = int(getattr(game_state, "fan_cap", 5) or 5)

    pub_players = tuple(
        SichuanPlayerPublicView(
            seat=p.player_index,
            team_id=getattr(p, "team_id", None) if is_team else None,
            discards=tuple(_norm(t) for t in getattr(p, "discard_tiles", ())),
            melds=tuple(getattr(p, "combination_tiles", ())),
            dingque_suit=int(getattr(p, "dingque_suit", 0) or 0),
            is_hu=bool(getattr(p, "is_hu", False)),
            has_won=bool(getattr(p, "has_won", False)),
            win_count=int(getattr(p, "win_count", 0) or 0),
        )
        for p in game_state.player_list
        if p.player_index != player_index
    )

    return SichuanContext(
        hand=[_norm(t) for t in getattr(me, "hand_tiles", ())],
        melds=list(getattr(me, "combination_tiles", ())),
        visible_34=build_visible_34(game_state),
        meld_count=meld_count,
        dingque_suit=int(getattr(me, "dingque_suit", 0) or 0),
        post_hu_lock=bool(getattr(me, "post_hu_lock", False)),
        wall_left=_effective_wall_left(game_state, player_index),
        fan_cap=fan_cap,
        is_xueliu=is_xueliu,
        is_team_game=is_team,
        is_duplicate=is_dup,
        is_imp_duplicate=is_imp,
        team_first_two_end=team_first_two,
        my_seat=player_index,
        my_team_id=my_team,
        has_kong_income=has_kong,
        players=pub_players,
    )


# ── 对手与队友公开信息推断 ────────────────────────────────────────────────────

def infer_missing_suit(pv: SichuanPlayerPublicView) -> int:
    """从公开定缺、副露与早期弃牌推断某家缺门（0 表示不确定）。"""
    if pv.dingque_suit in _ALL_SUITS:
        return pv.dingque_suit
    meld_suits = {_meld_suit(m) for m in pv.melds if _meld_suit(m) in _ALL_SUITS}
    if len(meld_suits) == 2:
        rem = set(_ALL_SUITS) - meld_suits
        return next(iter(rem))
    if len(pv.discards) >= 3:
        early = pv.discards[:5]
        cnt = Counter(t // 10 for t in early if t // 10 in _ALL_SUITS)
        if cnt:
            top_s, top_c = cnt.most_common(1)[0]
            if top_c >= 3 and top_s not in meld_suits:
                return top_s
    return 0


def infer_flush_suit(pv: SichuanPlayerPublicView) -> int:
    """若对手已有 >=2 副露且全在同一门且该门几乎无弃牌，返回该清一色主花色。"""
    if len(pv.melds) < 2:
        return 0
    meld_suits = {_meld_suit(m) for m in pv.melds if _meld_suit(m) in _ALL_SUITS}
    if len(meld_suits) != 1:
        return 0
    s = next(iter(meld_suits))
    same_disc = sum(1 for t in pv.discards if t // 10 == s)
    return s if same_disc <= 2 else 0


def _partner_view(ctx: SichuanContext) -> Optional[SichuanPlayerPublicView]:
    if not ctx.is_team_game or ctx.my_team_id is None:
        return None
    for pv in ctx.players:
        if pv.team_id == ctx.my_team_id:
            return pv
    return None


def _opponent_views(ctx: SichuanContext) -> List[SichuanPlayerPublicView]:
    if not ctx.is_team_game or ctx.my_team_id is None:
        return [pv for pv in ctx.players if not pv.is_hu]
    return [pv for pv in ctx.players if pv.team_id != ctx.my_team_id and not pv.is_hu]


def _partner_already_hu(ctx: SichuanContext) -> bool:
    pv = _partner_view(ctx)
    return pv is not None and (pv.is_hu or (not ctx.is_xueliu and pv.has_won))


def _opponent_already_hu_count(ctx: SichuanContext) -> int:
    if not ctx.is_team_game or ctx.my_team_id is None:
        return sum(1 for pv in ctx.players if pv.is_hu)
    return sum(1 for pv in ctx.players if pv.team_id != ctx.my_team_id and pv.is_hu)


def opponent_threat(pv: SichuanPlayerPublicView, wall_left: int, opp_hu_pressure: float = 1.0) -> float:
    if pv.is_hu:
        return 0.0
    m = len(pv.melds)
    if wall_left > 48 and m == 0 and not pv.has_won:
        return 0.0
    progress = max(0.0, min(1.0, (56 - wall_left) / 48.0))
    base = 0.30 * (progress ** 1.35) + 0.24 * min(3, m)
    if pv.has_won:
        base += 0.35  # 血流已胡玩家确定听牌！
    mult = opp_hu_pressure
    if infer_flush_suit(pv) != 0:
        mult *= 1.45  # 清一色 +2 番（4倍打点！）
    if m >= 2:
        mult *= 1.25  # 对对和/多杠
    return min(1.0, base * mult)


def tile_danger_for_opponent(
    tile: int,
    pv: SichuanPlayerPublicView,
    visible_34: Sequence[int],
) -> float:
    tid = _norm(tile)
    suit = tid // 10
    # 川麻防守铁律：对手缺门的花色对该对手是 100% 绝对安全牌！
    miss = infer_missing_suit(pv)
    if miss != 0 and suit == miss:
        return 0.0
    if tid in pv.discards:
        return 0.12
    num = tid % 10
    if num in (4, 5, 6):
        danger = 1.8
    elif num in (3, 7):
        danger = 1.45
    elif num in (2, 8):
        danger = 1.1
    else:
        danger = 0.75
    if num in (1, 2) and visible_34[tile_to_34(suit * 10 + 3)] >= 4:
        danger *= 0.45
    elif num in (8, 9) and visible_34[tile_to_34(suit * 10 + 7)] >= 4:
        danger *= 0.45
    dset = set(pv.discards)
    if (num >= 4 and (suit * 10 + num - 3) in dset) or (num <= 6 and (suit * 10 + num + 3) in dset):
        danger *= 0.72
    flush_s = infer_flush_suit(pv)
    if flush_s != 0:
        danger *= 2.6 if suit == flush_s else 0.25
    return danger


def total_tile_danger(tile: int, ctx: SichuanContext) -> float:
    opps = _opponent_views(ctx)
    if not opps:
        return 0.0
    # SBR 2v2：如果已有 1 名对手胡牌，另一名对手再胡就直接 team_first_two_end 输盘，威胁度 ×1.6！
    opp_pressure = 1.6 if (ctx.team_first_two_end and _opponent_already_hu_count(ctx) >= 1) else 1.0
    total = 0.0
    for opp in opps:
        t = opponent_threat(opp, ctx.wall_left, opp_pressure)
        if t > 0.0:
            total += t * tile_danger_for_opponent(tile, opp, ctx.visible_34)
    return total


def max_opponent_threat(ctx: SichuanContext) -> float:
    opps = _opponent_views(ctx)
    if not opps:
        return 0.0
    opp_pressure = 1.6 if (ctx.team_first_two_end and _opponent_already_hu_count(ctx) >= 1) else 1.0
    return max((opponent_threat(opp, ctx.wall_left, opp_pressure) for opp in opps), default=0.0)


def partner_assist_bonus(tile: int, ctx: SichuanContext) -> float:
    """2v2 队友配合喂牌加分（严格仅读公开信息，不偷看队友手牌）。

    当队友尚未胡牌且已显露缺门/主门倾向时，打出队友需要的花色（非缺门、尤其是队友清一色主门）
    可帮队友加速碰/胡（队友之间点炮 0 扣分）。
    """
    pv = _partner_view(ctx)
    if pv is None or pv.is_hu:
        return 0.0
    suit = _norm(tile) // 10
    p_miss = infer_missing_suit(pv)
    p_flush = infer_flush_suit(pv)
    if p_miss != 0 and suit == p_miss:
        return 0.0  # 队友缺这一门，喂了没用
    if _norm(tile) in pv.discards:
        return 0.0  # 队友自己刚打过
    bonus = 0.0
    if p_flush != 0 and suit == p_flush:
        bonus += 1.4
    elif p_miss != 0 and suit != p_miss:
        num = _norm(tile) % 10
        bonus += 0.55 if 3 <= num <= 7 else 0.35
    # 若队友已有副露（说明快听/在做大），喂牌价值放大
    if len(pv.melds) >= 2:
        bonus *= 1.4
    return bonus


# ── 川麻番数与指数打点评估 ────────────────────────────────────────────────────

def _live_copies(tile: int, hand_counts: Dict[int, int], visible_34: Sequence[int]) -> int:
    idx = tile_to_34(tile)
    if idx < 0 or idx >= 34:
        return 0
    return max(0, 4 - hand_counts.get(tile, 0) - visible_34[idx])


def _candidate_tiles(hand_counts: Dict[int, int], melds: Sequence[str], dingque: int) -> List[int]:
    exposed_suits = {_meld_suit(m) for m in melds if _meld_suit(m) in _ALL_SUITS}
    cands = set()
    for tid, c in hand_counts.items():
        if c <= 0:
            continue
        suit = tid // 10
        if suit not in _ALL_SUITS or suit == dingque:
            continue
        if len(exposed_suits) >= 2 and suit not in exposed_suits:
            continue
        if c < 4:
            cands.add(tid)
        num = tid % 10
        for d in (-2, -1, 1, 2):
            n2 = num + d
            if 1 <= n2 <= 9:
                nid = suit * 10 + n2
                if hand_counts.get(nid, 0) < 4:
                    cands.add(nid)
    return sorted(cands)


def _hypothetical_fan(
    ctx: SichuanContext,
    hand_base: Sequence[int],
    melds: Sequence[str],
    win_tile: int,
    dingque: int,
) -> int:
    key = (tuple(sorted(hand_base)), tuple(melds), win_tile, dingque, ctx.meld_count, ctx.is_xueliu)
    cached = ctx.fan_memo.get(key)
    if cached is not None:
        return cached
    full = list(hand_base) + [win_tile]
    try:
        if ctx.is_xueliu and ctx.meld_count == 3:
            fan, _ = _XUELIU_CHECKER.hepai_check(full, list(melds), [], win_tile, meld_count=3)
        else:
            fan, _ = _SICHUAN_CHECKER.hepai_check(full, list(melds), [], win_tile, dingque_suit=dingque)
    except Exception:
        fan = 0
    val = max(0, int(fan))
    ctx.fan_memo[key] = val
    return val


def _sichuan_pts(fan: int, fan_cap: int) -> float:
    capped = min(max(0, fan), max(3, fan_cap))
    return float(1 << capped)


def evaluate_tenpai_ev(
    ctx: SichuanContext,
    hand_base: Sequence[int],
    melds: Sequence[str],
    dingque: int,
) -> Tuple[int, float, float]:
    """精确评估 0 向听川麻手牌的 (live_waits, ev_score, avg_pts)。"""
    counts = Counter(hand_base)
    cands = _candidate_tiles(counts, melds, dingque)
    live_total = 0
    pts_sum = 0.0
    # 队友已胡（SBR 双胡锁盘）：极速小胡优先，番数权重压平！
    rush_lock = ctx.team_first_two_end and _partner_already_hu(ctx)
    for w in cands:
        rem = _live_copies(w, counts, ctx.visible_34)
        if rem <= 0:
            continue
        if _sichuan_shanten(list(hand_base) + [w], melds, ctx.meld_count, dingque) != -1:
            continue
        fan = _hypothetical_fan(ctx, hand_base, melds, w, dingque)
        pts = 1.0 if rush_lock else _sichuan_pts(fan, ctx.fan_cap)
        live_total += rem
        pts_sum += rem * pts
    if live_total <= 0:
        return 0, 0.0, 0.0
    avg_pts = pts_sum / live_total
    # 平衡听口厚度与指数打点：极速锁盘时纯按 live_total，否则 live^0.85 * avg_pts
    ev = float(live_total) if rush_lock else (live_total ** 0.85) * avg_pts
    return live_total, ev, avg_pts


def sichuan_yaku_potential(
    hand_base: Sequence[int],
    melds: Sequence[str],
    dingque: int,
    meld_count: int,
) -> float:
    """O(1) 川麻大番势能：清一色(+2番=4x)、七对/龙七对(+2~3番)、对对和(+1番)、带根(+1番)。"""
    all_tiles = list(hand_base)
    for m in melds:
        t = _meld_tile(m)
        if t:
            all_tiles.extend([t] * (4 if m[0] in ("g", "G") else 3))
    counts = Counter(all_tiles)
    hand_counts = Counter(hand_base)
    suit_cnt = {s: sum(c for t, c in counts.items() if t // 10 == s) for s in _ALL_SUITS}

    pot = 0.0
    # 1. 清一色（+2 番 = 4 倍收益！）
    valid_suits = [s for s in _ALL_SUITS if s != dingque]
    if valid_suits:
        dom_s = max(valid_suits, key=lambda s: suit_cnt[s])
        target_len = 3 * meld_count + 1
        if suit_cnt[dom_s] >= target_len - 2:
            pot += 2.6 + (suit_cnt[dom_s] - (target_len - 2)) * 1.1
        elif suit_cnt[dom_s] == target_len - 3:
            pot += 1.0

    # 2. 对对和 / 金钩钓（+1~2 番）
    pairs = sum(1 for c in hand_counts.values() if c == 2)
    trips = sum(1 for c in hand_counts.values() if c >= 3)
    open_k = len(melds)
    if pairs + trips + open_k >= meld_count + 1:
        pot += 1.0 + (trips + open_k) * 0.45

    # 3. 七对子 / 龙七对（仅 4 面子无副露）
    if meld_count == 4 and not melds and pairs + trips >= 5:
        pot += 1.6 + (pairs + trips - 5) * 0.8

    # 4. 带根（每 4 张同牌翻倍！）
    gens = sum(1 for c in counts.values() if c >= 4)
    pot += gens * 1.1
    return pot


def _flower_pig_risk_penalty(
    hand_base: Sequence[int],
    melds: Sequence[str],
    wall_left: int,
    is_imp_duplicate: bool,
) -> float:
    """未显式定缺模式（如 SBR 双打/复式）下的花猪风险惩罚。"""
    suits = {t // 10 for t in hand_base if t // 10 in _ALL_SUITS}
    for m in melds:
        s = _meld_suit(m)
        if s in _ALL_SUITS:
            suits.add(s)
    if len(suits) < 3:
        return 0.0
    # 手里留 3 门牌的最小那门张数越多、牌墙越少，花猪风险越致命！
    suit_counts = Counter(t // 10 for t in hand_base if t // 10 in _ALL_SUITS)
    meld_suits = {_meld_suit(m) for m in melds if _meld_suit(m) in _ALL_SUITS}
    drop_candidates = [suit_counts[s] for s in _ALL_SUITS if s not in meld_suits]
    min_third = min(drop_candidates) if drop_candidates else 10
    urgency = 2.8 if is_imp_duplicate else 1.8
    if wall_left <= 16:
        return 120.0 * min_third * urgency
    if wall_left <= 28:
        return 14.0 * min_third * urgency
    if wall_left <= 40:
        return 2.5 * min_third * urgency
    return 0.5 * min_third


# ── 开局选牌（定缺 & 换三张，含 2v2 队友利他换牌） ────────────────────────────

def choose_dingque_suit(hand: Sequence[int], meld_count: int = 4) -> int:
    """高水平定缺：不只选张数最少，综合向听、进张与清一色/对对潜力。"""
    hand = [_norm(t) for t in hand]
    best_suit = 1
    best_key = None
    for s in _ALL_SUITS:
        rem = [t for t in hand if t // 10 != s]
        cnt_s = sum(1 for t in hand if t // 10 == s)
        d = _sichuan_shanten(hand, (), meld_count, s)
        pot = sichuan_yaku_potential(rem, (), s, meld_count)
        key = (-d, -cnt_s, round(pot, 2), -s)
        if best_key is None or key > best_key:
            best_key = key
            best_suit = s
    return best_suit


def choose_opening_tiles(
    hand: Sequence[int],
    *,
    exchange: bool = False,
    is_team_exchange: bool = False,
    teammate_suit: int = 0,
) -> List[int]:
    """开局弃三张 / 换三张。

    - 2v2 对家互换（is_team_exchange=True）：
      1) 后手已知 teammate_suit 时坚决避开把队友刚换出的花色再换回去；
      2) 送给队友时在同向听下优先送出中张/连张质量更高的 3 张（利他喂牌）。
    - 非 2v2 换三张：优先送出散牌/边张，减少给对手送搭子。
    """
    hand = [_norm(t) for t in hand]
    suits_to_try = [
        s for s in _ALL_SUITS
        if (not (is_team_exchange and teammate_suit in _ALL_SUITS and s == teammate_suit))
        and sum(1 for t in hand if t // 10 == s) >= 3
    ]
    if not suits_to_try:
        suits_to_try = [s for s in _ALL_SUITS if sum(1 for t in hand if t // 10 == s) >= 3]
    if not suits_to_try:
        return hand[:3]

    choices = set()
    for s in suits_to_try:
        choices.update(combinations(sorted(t for t in hand if t // 10 == s), 3))

    def _tile_gift_quality(chosen: Tuple[int, ...]) -> float:
        # 送出牌本身的质量：中张 + 连张/对子
        nums = sorted(t % 10 for t in chosen)
        q = sum(1.0 if 3 <= n <= 7 else (0.5 if n in (2, 8) else 0.1) for n in nums)
        if nums[0] == nums[1] or nums[1] == nums[2]:
            q += 1.2
        if nums[1] - nums[0] <= 2:
            q += 0.7
        if nums[2] - nums[1] <= 2:
            q += 0.7
        return q

    def _score(chosen: Tuple[int, ...]) -> Tuple:
        after = list(hand)
        for t in chosen:
            after.remove(t)
        counts = Counter(after)
        d = _sichuan_shanten(after, (), 4 if exchange else 3, 0)
        pot = sichuan_yaku_potential(after, (), 0, 4 if exchange else 3)
        conn = sum(min(2, c) for c in counts.values() if c >= 2)
        conn += sum(min(c, counts.get(t + 1, 0)) for t, c in counts.items() if t % 10 < 9)
        gift = _tile_gift_quality(chosen)
        gift_term = gift if is_team_exchange else (-0.4 * gift if exchange else 0.0)
        return (-d, round(conn + 1.2 * pot + gift_term, 3), chosen)

    return list(max(sorted(choices), key=_score))


# ── 主弃牌与手牌决策 ──────────────────────────────────────────────────────────

def _structural_acceptance(
    hand_base: Sequence[int],
    melds: Sequence[str],
    visible_34: Sequence[int],
    meld_count: int,
    dingque: int,
    dist: int,
) -> int:
    counts = Counter(hand_base)
    acc = 0
    for tile in _candidate_tiles(counts, melds, dingque):
        rem = _live_copies(tile, counts, visible_34)
        if rem > 0 and _sichuan_shanten(list(hand_base) + [tile], melds, meld_count, dingque) < dist:
            acc += rem
    return acc


def choose_best_cut(ctx: SichuanContext) -> Tuple[int, int, Tuple[int, float]]:
    """选择川麻最优切牌，返回 (tile, cut_index, (-shanten, score))。"""
    hand = list(ctx.hand)
    melds = list(ctx.melds)
    forced_dingque = ctx.dingque_suit in _ALL_SUITS and any(t // 10 == ctx.dingque_suit for t in hand)
    rush_lock = ctx.team_first_two_end and _partner_already_hu(ctx)
    threat = max_opponent_threat(ctx)
    # 终局查叫/退税保听意识：若自己已有杠钱或临近流局，优先进听保叫
    chajiao_urgent = ctx.wall_left <= (18 if ctx.is_imp_duplicate else 14)

    seen = set()
    raw_cands: List[Tuple[int, int, List[int], int, float, float, float]] = []
    min_d = 99
    for i, tile in enumerate(hand):
        if tile in seen:
            continue
        if forced_dingque and tile // 10 != ctx.dingque_suit:
            continue
        seen.add(tile)
        rem = hand[:i] + hand[i + 1:]
        d = _sichuan_shanten(rem, melds, ctx.meld_count, ctx.dingque_suit)
        if d < min_d:
            min_d = d
        danger = total_tile_danger(tile, ctx)
        assist = partner_assist_bonus(tile, ctx) if ctx.is_team_game else 0.0
        fp_pen = 0.0 if ctx.dingque_suit in _ALL_SUITS else _flower_pig_risk_penalty(
            rem, melds, ctx.wall_left, ctx.is_imp_duplicate
        )
        raw_cands.append((tile, i, rem, d, danger, assist, fp_pen))

    # 1. 0 向听：精确 2^fan 期望排序（叠加防花猪与安全切微调）
    best_tenpai = None
    best_tenpai_key = None
    for tile, idx, rem, d, danger, assist, fp_pen in raw_cands:
        if d != 0:
            continue
        live_w, ev, avg_pts = evaluate_tenpai_ev(ctx, rem, melds, ctx.dingque_suit)
        if live_w <= 0:
            continue
        # 自身已有杠钱或查叫临近时，保听为第一优先；对手极高危且自身仅 1 分屁胡时轻微避险
        adj_ev = ev - fp_pen
        if threat >= 0.72 and avg_pts <= 2.0 and not ctx.has_kong_income and not rush_lock:
            adj_ev -= 0.45 * danger
        key = (round(adj_ev, 3), live_w, -round(danger, 2), round(assist, 2))
        if best_tenpai_key is None or key > best_tenpai_key:
            best_tenpai_key = key
            best_tenpai = (tile, idx, (0, adj_ev))

    if best_tenpai is not None:
        return best_tenpai

    # 2. 残局绝望防铳：无杠钱且 wall <= 10 且高危对手 >= 0.80 且仍 >= 2 向听，切最安全牌（优先对手缺门/现物）
    if ctx.wall_left <= 10 and threat >= 0.80 and min_d >= 2 and not ctx.has_kong_income and not rush_lock:
        min_fp = min(x[6] for x in raw_cands)
        safe_pool = [x for x in raw_cands if x[6] <= min_fp + 1.0]
        safest = min(safe_pool, key=lambda x: (x[4], x[3], -x[5]))
        return safest[0], safest[1], (-safest[3], 0.0)

    # 3. 1~3 向听：严守最短向听，同向听下以进张为主、温和叠加清一色/对对/带根势能与外科式防守
    early_factor = max(0.0, min(1.0, (ctx.wall_left - 10) / 42.0))
    pot_weight = 0.0 if (rush_lock or chajiao_urgent) else (0.09 * early_factor + 0.02)

    best_pick = None
    best_key = None
    for tile, idx, rem, d, danger, assist, fp_pen in raw_cands:
        if d > min_d:
            continue
        acc = _structural_acceptance(rem, melds, ctx.visible_34, ctx.meld_count, ctx.dingque_suit, d)
        pot = 0.0 if rush_lock else sichuan_yaku_potential(rem, melds, ctx.dingque_suit, ctx.meld_count)
        score = (acc + 1.0) * (1.0 + pot_weight * pot) - fp_pen
        # 外科式防守：仅在面对高危对手（>=0.75 如清一色副露/血流已胡）时轻微折减极危生张，绝不在中盘拖慢速度
        if threat >= 0.75:
            def_w = 0.55 if ctx.is_imp_duplicate else 0.35
            score -= def_w * danger
        # 2v2 队友配合：自己距离较远或同效率近手时优先喂队友需要的花色（前提是不点炮对手）
        if ctx.is_team_game and assist > 0.0 and danger < 0.5:
            assist_w = 0.9 if d >= 2 else 0.35
            score += assist_w * assist
        key = (round(score, 3), -d, acc, -round(danger, 2))
        if best_key is None or key > best_key:
            best_key = key
            best_pick = (tile, idx, (-d, score))

    if best_pick is not None:
        return best_pick
    return raw_cands[0][0], raw_cands[0][1], (-raw_cands[0][3], 0.0)


def choose_hand_action(ctx: SichuanContext, actions: Sequence[str]) -> Tuple[str, Optional[int], int]:
    action_set = set(actions)
    hand = list(ctx.hand)
    melds = list(ctx.melds)
    if not hand:
        return "pass", None, -1

    exposed_suits = {_meld_suit(m) for m in melds if _meld_suit(m) in _ALL_SUITS}
    base_d = _sichuan_shanten(hand, melds, ctx.meld_count, ctx.dingque_suit)
    threat = max_opponent_threat(ctx)

    # 暗杠 / 加杠：川麻杠牌 = 即时刮风下雨现金流 + 1 根（和牌翻倍！）
    candidates_for_kong = [hand[-1]] if ctx.post_hu_lock else sorted(set(hand))
    for tile in candidates_for_kong:
        suit = tile // 10
        if suit == ctx.dingque_suit:
            continue
        if "angang" in action_set and hand.count(tile) == 4:
            if len(exposed_suits | {suit}) == 3:
                continue  # 绝不暗杠出第三门变花猪！
            after = [t for t in hand if t != tile]
            d_after = _sichuan_shanten(after, melds + [f"G{tile}"], ctx.meld_count, ctx.dingque_suit)
            # 只要不退向听（或残局已听牌不破听），积极暗杠赚下雨+根
            if d_after <= base_d:
                if d_after == 0:
                    lw, _, _ = evaluate_tenpai_ev(ctx, after, melds + [f"G{tile}"], ctx.dingque_suit)
                    if lw > 0:
                        return "angang", tile, 0
                else:
                    return "angang", tile, 0
        if "jiagang" in action_set and f"k{tile}" in melds:
            # 防抢杠：对手极高危且该牌危险时谨慎巴杠
            if threat >= 0.78 and total_tile_danger(tile, ctx) >= 1.5 and not ctx.post_hu_lock:
                continue
            after = hand.copy()
            after.remove(tile)
            upgraded = [f"g{tile}" if m == f"k{tile}" else m for m in melds]
            d_after = _sichuan_shanten(after, upgraded, ctx.meld_count, ctx.dingque_suit)
            if d_after <= base_d:
                if d_after == 0:
                    lw, _, _ = evaluate_tenpai_ev(ctx, after, upgraded, ctx.dingque_suit)
                    if lw > 0:
                        return "jiagang", tile, 0
                else:
                    return "jiagang", tile, 0

    if ctx.post_hu_lock:
        return "cut", hand[-1], len(hand) - 1

    tile, idx, _ = choose_best_cut(ctx)
    return "cut", tile, idx


def choose_claim_action(ctx: SichuanContext, actions: Sequence[str], cut_tile: int) -> str:
    action_set = set(actions)
    hand = list(ctx.hand)
    melds = list(ctx.melds)
    cut_tile = _norm(cut_tile)
    suit = cut_tile // 10
    if suit == ctx.dingque_suit:
        return "pass"

    d_before = _sichuan_shanten(hand, melds, ctx.meld_count, ctx.dingque_suit)
    acc_before = _structural_acceptance(hand, melds, ctx.visible_34, ctx.meld_count, ctx.dingque_suit, d_before)
    pot_before = sichuan_yaku_potential(hand, melds, ctx.dingque_suit, ctx.meld_count)
    rush_lock = ctx.team_first_two_end and _partner_already_hu(ctx)
    chajiao_urgent = ctx.wall_left <= (18 if ctx.is_imp_duplicate else 14)

    # 七对子保护：若七对明显快于一般型且尚未副露，不轻率碰牌（除非残局锁盘）
    if ctx.meld_count == 4 and not melds and not rush_lock:
        useful = Counter(t for t in hand if t // 10 != ctx.dingque_suit)
        pairs = sum(c // 2 for c in useful.values())
        singles = sum(c % 2 for c in useful.values())
        d_qd = 13 - 2 * pairs - min(7 - pairs, singles)
        d_yb = xiangting_yiban(useful, 0)
        if d_qd < d_yb and d_qd <= 1:
            return "pass"

    best_action = "pass"
    best_rank: Optional[Tuple] = None

    for action, need_cnt, sign in (("gang", 3, "g"), ("peng", 2, "k")):
        if action not in action_set or hand.count(cut_tile) < need_cnt:
            continue
        next_melds = melds + [f"{sign}{cut_tile}"]
        if len({_meld_suit(m) for m in next_melds if _meld_suit(m) in _ALL_SUITS}) == 3:
            continue  # 绝不副露出第三门变花猪！
        after = hand.copy()
        for _ in range(need_cnt):
            after.remove(cut_tile)

        sub_ctx = SichuanContext(
            hand=after,
            melds=next_melds,
            visible_34=ctx.visible_34,
            meld_count=ctx.meld_count,
            dingque_suit=ctx.dingque_suit,
            wall_left=ctx.wall_left,
            fan_cap=ctx.fan_cap,
            is_xueliu=ctx.is_xueliu,
            is_team_game=ctx.is_team_game,
            is_duplicate=ctx.is_duplicate,
            is_imp_duplicate=ctx.is_imp_duplicate,
            team_first_two_end=ctx.team_first_two_end,
            my_seat=ctx.my_seat,
            my_team_id=ctx.my_team_id,
            has_kong_income=ctx.has_kong_income or action == "gang",
            players=ctx.players,
            fan_memo=ctx.fan_memo,
        )

        if action == "peng":
            _, cut_idx, _ = choose_best_cut(sub_ctx)
            after.pop(cut_idx)

        d_after = _sichuan_shanten(after, next_melds, ctx.meld_count, ctx.dingque_suit)

        # 明杠有即时刮风钱 + 1 根（翻倍），只要不退向听且不破听就非常强！
        if action == "gang":
            if d_after > d_before:
                continue
            if d_after == 0:
                lw, ev, _ = evaluate_tenpai_ev(sub_ctx, after, next_melds, ctx.dingque_suit)
                if lw <= 0:
                    continue
                rank = (3, round(ev * 2.0, 2), lw)
            else:
                acc_a = _structural_acceptance(after, next_melds, ctx.visible_34, ctx.meld_count, ctx.dingque_suit, d_after)
                rank = (2, float(acc_a + 12), acc_a)
            if best_rank is None or rank > best_rank:
                best_rank = rank
                best_action = action
            continue

        # 碰牌：推进向听，或同向听但显著提升清一色/对对和潜力，或极速锁盘/查叫进听
        if d_after > d_before:
            continue
        if d_after == 0:
            lw, ev, _ = evaluate_tenpai_ev(sub_ctx, after, next_melds, ctx.dingque_suit)
            if lw <= 0:
                continue
            if d_before == 0:
                lw0, ev0, _ = evaluate_tenpai_ev(ctx, hand, melds, ctx.dingque_suit)
                if ev <= ev0 * 1.15:
                    continue
            rank = (2, round(ev, 2), lw)
        elif d_after < d_before:
            acc_a = _structural_acceptance(after, next_melds, ctx.visible_34, ctx.meld_count, ctx.dingque_suit, d_after)
            pot_a = sichuan_yaku_potential(after, next_melds, ctx.dingque_suit, ctx.meld_count)
            # 2v2 不抢碰低价值牌；但只要推进向听且不损大牌（或极速锁盘/残局查叫）就碰
            if not rush_lock and not chajiao_urgent and d_after >= 2 and pot_a < pot_before - 0.8:
                continue
            rank = (1, round(acc_a * (1.0 + 0.18 * pot_a), 2), acc_a)
        else:
            # 同向听仅在明确转向对对和/清一色时碰
            pot_a = sichuan_yaku_potential(after, next_melds, ctx.dingque_suit, ctx.meld_count)
            acc_a = _structural_acceptance(after, next_melds, ctx.visible_34, ctx.meld_count, ctx.dingque_suit, d_after)
            if pot_a >= pot_before + 0.9 and acc_a >= acc_before - 4:
                rank = (0, round(pot_a * 10 + acc_a, 2), acc_a)
            else:
                continue

        if best_rank is None or rank > best_rank:
            best_rank = rank
            best_action = action

    return best_action


def should_hu_teammate_discard(ctx: SichuanContext) -> bool:
    """2v2 队友点炮时的和牌取舍（队友之间 0 扣分）：
    - 血流双打：胡队友 0 分且会暴露听口，坚决选过（留给对手点炮或自摸）！
    - SBR 双打/复式（team_first_two_end=True）：
      1) 若队友已胡，胡队友？(已胡队友不会再出牌)；
      2) 若残局（wall <= 14）胡队友可直接安全退场、免查叫且把剩余独立牌墙留给队友继承！
    """
    if ctx.is_xueliu:
        return False
    if ctx.team_first_two_end and ctx.wall_left <= 16:
        return True
    return False


# ── 异步游戏循环入口 ──────────────────────────────────────────────────────────

@paced_bot(lambda: _BOT_DELAY)
async def sichuan_heuristic_bot_action(state, index: int, actions: list, status: str):
    tick = state.server_action_tick
    if not await _wait_until_actionable(state, index) or not bot_action_is_current(state, index, tick):
        return
    player = state.player_list[index]
    teammate_claim = (
        getattr(state, "is_team_game", False)
        and status in ("waiting_action_after_cut", "waiting_action_qianggang")
        and player.team_id == state.player_list[state.current_player_index].team_id
    )

    ctx = context_from_game(state, index)
    hu_action = next((a for a in actions if a.startswith("hu")), None)
    choice = None
    if hu_action is not None:
        if not teammate_claim or should_hu_teammate_discard(ctx):
            choice = hu_action

    tile, cut_index = None, None
    if choice is None:
        if status in ("waiting_hand_action", "onlycut_after_action"):
            choice, tile, cut_index = await run_room_bot_cpu(state, choose_hand_action, ctx, tuple(actions))
        elif status == "waiting_action_after_cut":
            cut_tile = state.player_list[state.current_player_index].discard_tiles[-1]
            choice = await run_room_bot_cpu(state, choose_claim_action, ctx, tuple(actions), cut_tile)
        else:
            choice = "pass"

    if bot_action_is_current(state, index, tick):
        await submit_bot_action(
            get_ai_action,
            state,
            index,
            choice,
            infer_bot_cut_class(player.hand_tiles, tile, cut_index, draw_slot=has_draw_slot(player)) if choice == "cut" else None,
            tile if choice == "cut" else None,
            cut_index,
            tile if choice != "cut" else None,
        )
