"""立直麻将高性能罗伯特（user_id=3）：宝牌打点 EV + 立直/默听决策 + 天凤级 Betaori 攻防。

兼容四麻与三麻立直规则，严格遵守食替（kuikae）禁切、振听不可荣和、红5保留、
三麻拔北（nuki）与无役禁鸣约束，仅读取合法公开信息。
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from typing import Dict, FrozenSet, List, Optional, Sequence, Set, Tuple

from ...game_riichi.action_check import _is_furiten, hand_kong_candidates
from ..hand_slot_utils import has_draw_slot, infer_bot_cut_class
from .bot_executor import bot_action_is_current, run_room_bot_cpu
from .get_action import get_ai_action
from .pacing import paced_bot, submit_bot_action
from .riichi_smart_bot_ai import (
    _count_normalized,
    _has_credible_open_yaku,
    _is_riichi_ron_furiten,
    _kuikae_forbidden_after_meld,
    _kuikae_forbidden_for_chi,
    _kuikae_forbidden_for_peng,
    _remove_one_normalized,
    _riichi_should_accept_hu,
    _riichi_should_accept_ron,
    _riichi_visible_tiles,
    _riichi_winds,
)
from .smart_bot_ai import _BOT_DELAY, _handle_buhua_round, _wait_until_actionable
from .smart_bot_logic import (
    count_acceptance,
    count_melds,
    evaluate_hand,
    normalize_tile,
    tile_to_34,
)

_AKA_5 = frozenset((105, 205, 305))
_WIND_IDS = (41, 42, 43, 44)
_DRAGON_IDS = (45, 46, 47)
_YAOJIU_IDS = frozenset((11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47))


def indicator_to_dora(indicator: int, is_sanma: bool = False) -> int:
    """由宝牌指示牌计算实际宝牌（兼容四麻与三麻 1m->9m 循环）。"""
    t = normalize_tile(indicator)
    suit, num = t // 10, t % 10
    if suit in (1, 2, 3):
        if is_sanma and suit == 1:
            return 19 if num == 1 else 11
        return suit * 10 + (1 if num == 9 else num + 1)
    if suit == 4:
        if 1 <= num <= 4:
            return 41 + (num % 4)
        if 5 <= num <= 7:
            return 45 + ((num - 4) % 3)
    return 0


def dora_tiles_from_game(game_state) -> Tuple[int, ...]:
    is_sanma = len(getattr(game_state, "player_list", ())) == 3 or "sanma" in str(getattr(game_state, "sub_rule", ""))
    inds = list(getattr(game_state, "dora_indicators", ()) or ()) + list(
        getattr(game_state, "kan_dora_indicators", ()) or ()
    )
    return tuple(d for d in (indicator_to_dora(x, is_sanma=is_sanma) for x in inds) if d > 0)


@dataclass(frozen=True)
class RiichiOpponentView:
    """对手公开信息快照（用于立直 Betaori 弃和与染手/副露威胁识别）。"""

    seat: int
    discards: Tuple[int, ...] = ()
    melds: Tuple[str, ...] = ()
    is_riichi: bool = False
    is_dealer: bool = False
    post_riichi_safe: Tuple[int, ...] = ()  # 立直后全桌切过且未荣和的同巡/立直后现物


@dataclass(frozen=True)
class RiichiContext:
    hand: Tuple[int, ...]
    combs: Tuple[str, ...]
    meld_count: int
    visible_34: Tuple[int, ...]
    dora_tiles: Tuple[int, ...]
    round_wind: int
    seat_wind: int
    open_tanyao: bool
    is_riichi: bool
    is_dealer: bool
    is_furiten: bool
    wall_left: int
    opponents: Tuple[RiichiOpponentView, ...] = ()


def _build_opponents(game_state, player_index: int) -> Tuple[RiichiOpponentView, ...]:
    dealer = int(getattr(game_state, "dealer_index", 0) or 0)
    # 简化且 100% 合法的立直后现物：每个玩家自己的弃牌均为自己的现物；
    # 若某对手立直，统计其立直宣言之后的他家弃牌亦可作为立直后现物（这里保守用其自身弃牌即可保证零误判）。
    views = []
    for p in game_state.player_list:
        if p.player_index == player_index:
            continue
        tags = getattr(p, "tag_list", ()) or ()
        is_r = ("riichi" in tags) or ("daburu_riichi" in tags)
        views.append(
            RiichiOpponentView(
                seat=p.player_index,
                discards=tuple(normalize_tile(t) for t in getattr(p, "discard_tiles", ())),
                melds=tuple(getattr(p, "combination_tiles", ())),
                is_riichi=is_r,
                is_dealer=(p.player_index == dealer),
            )
        )
    return tuple(views)


def context_from_game(game_state, player_index: int) -> RiichiContext:
    player = game_state.player_list[player_index]
    tags = getattr(player, "tag_list", ()) or ()
    is_riichi = ("riichi" in tags) or ("daburu_riichi" in tags)
    round_wind, seat_wind = _riichi_winds(game_state, player_index)
    open_tanyao = bool((getattr(game_state, "detailed_config", None) or {}).get("open_tanyao", True))
    combs = tuple(getattr(player, "combination_tiles", ()))
    dealer = int(getattr(game_state, "dealer_index", 0) or 0)
    return RiichiContext(
        hand=tuple(player.hand_tiles),
        combs=combs,
        meld_count=count_melds(list(combs)),
        visible_34=tuple(_riichi_visible_tiles(game_state, player_index)),
        dora_tiles=dora_tiles_from_game(game_state),
        round_wind=round_wind,
        seat_wind=seat_wind,
        open_tanyao=open_tanyao,
        is_riichi=is_riichi,
        is_dealer=(player_index == dealer),
        is_furiten=_is_furiten(player),
        wall_left=len(getattr(game_state, "tiles_list", ())),
        opponents=_build_opponents(game_state, player_index),
    )


# ── 宝牌与打点快速估值 ────────────────────────────────────────────────────────

def count_hand_dora(hand: Sequence[int], combs: Sequence[str], dora_tiles: Sequence[int]) -> int:
    dora_cnt = Counter(dora_tiles)
    total = 0
    for t in hand:
        if t in _AKA_5:
            total += 1
        nt = normalize_tile(t)
        if nt in dora_cnt:
            total += dora_cnt[nt]
    for code in combs:
        if not code or len(code) < 2:
            continue
        try:
            base = normalize_tile(int(code[1:]))
        except ValueError:
            continue
        sign = code[0]
        if sign in ("s", "S"):
            for x in (base - 1, base, base + 1):
                total += dora_cnt.get(x, 0)
        elif sign in ("k", "K"):
            total += 3 * dora_cnt.get(base, 0)
        elif sign in ("g", "G"):
            total += 4 * dora_cnt.get(base, 0)
    return total


def estimate_hand_han(
    hand: Sequence[int],
    combs: Sequence[str],
    dora_tiles: Sequence[int],
    round_wind: int,
    seat_wind: int,
    open_tanyao: bool,
    *,
    assume_riichi: bool = False,
) -> float:
    """快速估算手牌成型后的预期番数（含宝牌、赤5、役牌、断幺、平和、混清一色、对对、七对）。"""
    han = float(count_hand_dora(hand, combs, dora_tiles))
    is_closed = len(combs) == 0
    if is_closed and assume_riichi:
        han += 1.6  # 立直(1) + 一发/里宝/门清自摸期望(+0.6)

    norm_hand = [normalize_tile(t) for t in hand]
    all_tiles = list(norm_hand)
    has_seq_meld = False
    for code in combs:
        if not code or len(code) < 2:
            continue
        try:
            base = normalize_tile(int(code[1:]))
        except ValueError:
            continue
        if code[0] in ("s", "S"):
            has_seq_meld = True
            all_tiles.extend((base - 1, base, base + 1))
        elif code[0] in ("k", "K"):
            all_tiles.extend((base, base, base))
        elif code[0] in ("g", "G"):
            all_tiles.extend((base, base, base, base))

    counts = Counter(all_tiles)
    # 役牌（三元 / 场风 / 自风）
    for d in _DRAGON_IDS:
        if counts.get(d, 0) >= 3:
            han += 1.0
    if counts.get(round_wind, 0) >= 3:
        han += 1.0
    if counts.get(seat_wind, 0) >= 3:
        han += 1.0

    # 断幺九
    if (is_closed or open_tanyao) and all(t not in _YAOJIU_IDS for t in all_tiles):
        han += 1.0

    # 混一色 / 清一色
    suits = {t // 10 for t in all_tiles if t // 10 in (1, 2, 3)}
    has_honor = any(t // 10 == 4 for t in all_tiles)
    if len(suits) == 1:
        if not has_honor:
            han += 6.0 if is_closed else 5.0
        else:
            han += 3.0 if is_closed else 2.0

    # 对对和
    if not has_seq_meld:
        hand_c = Counter(norm_hand)
        trips = sum(1 for c in hand_c.values() if c >= 3) + len(combs)
        pairs = sum(1 for c in hand_c.values() if c == 2)
        if trips + pairs >= 5 and trips >= 2:
            han += 2.0

    # 门清平和倾向（全数牌或无役牌刻、顺子多）
    if is_closed and not has_honor and sum(1 for c in Counter(norm_hand).values() if c >= 3) == 0:
        han += 0.8

    return han


def _han_to_pts(han: float, is_dealer: bool) -> float:
    mult = 1.5 if is_dealer else 1.0
    if han < 1.0:
        return 0.0
    if han < 2.0:
        return 1300.0 * mult
    if han < 3.0:
        return 2600.0 * mult
    if han < 4.0:
        return 5200.0 * mult
    if han < 6.0:
        return 8000.0 * mult
    if han < 8.0:
        return 12000.0 * mult
    return 16000.0 * mult


# ── 日麻核心：天凤级安全度评估与 Betaori 弃和 ─────────────────────────────────

def _opponent_flush_suit(opp: RiichiOpponentView) -> int:
    if len(opp.melds) < 2:
        return 0
    suits = set()
    for code in opp.melds:
        if len(code) < 2:
            continue
        try:
            s = normalize_tile(int(code[1:])) // 10
        except ValueError:
            continue
        if s in (1, 2, 3):
            suits.add(s)
    if len(suits) != 1:
        return 0
    s = next(iter(suits))
    same_disc = sum(1 for t in opp.discards if t // 10 == s)
    return s if same_disc <= 2 else 0


def opponent_threat(opp: RiichiOpponentView, wall_left: int) -> float:
    if opp.is_riichi:
        return 1.15 if opp.is_dealer else 1.0
    m = len(opp.melds)
    if m == 0:
        return 0.0 if wall_left > 24 else 0.25
    base = 0.28 * min(3, m) + (0.15 if wall_left < 24 else 0.0)
    if _opponent_flush_suit(opp) != 0:
        base *= 1.35
    if opp.is_dealer:
        base *= 1.15
    return min(0.92, base)


def tile_danger_for_opponent(
    tile: int,
    opp: RiichiOpponentView,
    visible_34: Sequence[int],
    dora_tiles: Sequence[int],
) -> float:
    """单张牌对指定对手的日麻安全度评分（0.0 = 绝对安全现物）。"""
    nt = normalize_tile(tile)
    if nt in opp.discards or nt in opp.post_riichi_safe:
        return 0.0  # 现物 100% 安全！

    suit, num = nt // 10, nt % 10
    seen = visible_34[tile_to_34(nt)] if 0 <= tile_to_34(nt) < 34 else 0
    is_dora = (tile in _AKA_5) or (nt in dora_tiles)
    dora_mult = 1.38 if is_dora else 1.0

    # 字牌：越枯越安全（3 张可见仅剩地狱单骑，4 张可见不可能铳）
    if suit == 4:
        if seen >= 4:
            return 0.05
        if seen == 3:
            return 0.22 * dora_mult
        if seen == 2:
            return 0.55 * dora_mult
        if seen == 1:
            return 1.05 * dora_mult
        return 1.65 * dora_mult

    # 数牌基础危险度（456 > 37 > 28 > 19）
    if num in (4, 5, 6):
        danger = 2.2
    elif num in (3, 7):
        danger = 1.75
    elif num in (2, 8):
        danger = 1.25
    else:
        danger = 0.85

    # No-Chance（4 张壁封死两面顺子）
    if num in (1, 2) and visible_34[tile_to_34(suit * 10 + 3)] >= 4:
        danger *= 0.35
    elif num in (8, 9) and visible_34[tile_to_34(suit * 10 + 7)] >= 4:
        danger *= 0.35
    elif num == 1 and visible_34[tile_to_34(suit * 10 + 2)] >= 4:
        danger *= 0.30
    elif num == 9 and visible_34[tile_to_34(suit * 10 + 8)] >= 4:
        danger *= 0.30

    # 筋（Suji）判断：立直家弃牌河中若有同门 ±3，显著降低两面放铳概率
    dset = set(opp.discards)
    if num in (1, 2, 3) and (suit * 10 + num + 3) in dset:
        danger *= 0.52
    elif num in (7, 8, 9) and (suit * 10 + num - 3) in dset:
        danger *= 0.52
    elif num in (4, 5, 6):
        has_lo = (suit * 10 + num - 3) in dset
        has_hi = (suit * 10 + num + 3) in dset
        if has_lo and has_hi:
            danger *= 0.48  # 双筋
        elif has_lo or has_hi:
            danger *= 0.82  # 半筋

    # 对手染手副露
    fs = _opponent_flush_suit(opp)
    if fs != 0:
        danger *= 2.4 if suit == fs else 0.25

    return danger * dora_mult


def total_tile_danger(tile: int, ctx: RiichiContext) -> float:
    total = 0.0
    for opp in ctx.opponents:
        t = opponent_threat(opp, ctx.wall_left)
        if t > 0.0:
            total += t * tile_danger_for_opponent(tile, opp, ctx.visible_34, ctx.dora_tiles)
    return total


def has_riichi_opponent(ctx: RiichiContext) -> bool:
    return any(opp.is_riichi for opp in ctx.opponents)


def max_opponent_threat(ctx: RiichiContext) -> float:
    return max((opponent_threat(opp, ctx.wall_left) for opp in ctx.opponents), default=0.0)


# ── 核心切牌与立直/默听/弃和决策 ──────────────────────────────────────────────

def _dora_keep_bonus(hand_after: Sequence[int], dora_tiles: Sequence[int]) -> float:
    """保留宝牌与赤 5 的加分（避免等向听时随手切掉宝牌）。"""
    dset = Counter(dora_tiles)
    score = 0.0
    for t in hand_after:
        if t in _AKA_5:
            score += 2.2
        nt = normalize_tile(t)
        if nt in dset:
            score += 2.4 * dset[nt]
    return score


def _riichi_shape_bonus(hand_after: Sequence[int], combs: Sequence[str], round_wind: int, seat_wind: int, open_tanyao: bool) -> float:
    """手牌良形与高番方向轻量加分（断幺/役牌/混清一色/中张良形）。"""
    norm = [normalize_tile(t) for t in hand_after]
    c = Counter(norm)
    bonus = 0.0
    # 役牌对/刻保护
    for h in (_DRAGON_IDS + (round_wind, seat_wind)):
        if c.get(h, 0) >= 3:
            bonus += 1.6
        elif c.get(h, 0) == 2:
            bonus += 0.9
    # 断幺方向（若绝大部分牌是中张 2~8）
    if (len(combs) == 0 or open_tanyao) and sum(1 for t in norm if t in _YAOJIU_IDS) <= 1:
        bonus += 0.8
    # 混/清一色方向
    suit_cnt = {1: 0, 2: 0, 3: 0, 4: 0}
    for t in norm:
        s = t // 10
        if s in suit_cnt:
            suit_cnt[s] += 1
    dom_s = max((1, 2, 3), key=lambda s: suit_cnt[s])
    if suit_cnt[dom_s] >= 9:
        bonus += 2.4
    elif suit_cnt[dom_s] + suit_cnt[4] >= 10:
        bonus += 1.5
    return bonus


def choose_hand_plan(
    ctx: RiichiContext,
    actions: Sequence[str],
    riichi_candidates: Dict[int, List[int]],
    forbidden_tiles: FrozenSet[int],
    kong_candidates: Sequence[Tuple[str, int]],
) -> Tuple[str, Optional[int], int]:
    """立直麻将手牌阶段总决策：立直/默听/暗杠加杠/攻防切牌/Betaori 弃和。"""
    action_set = set(actions)
    hand = list(ctx.hand)
    combs = list(ctx.combs)
    if not hand:
        return "pass", None, -1

    # 已立直：只能摸切或安全暗杠
    if ctx.is_riichi:
        if "angang" in action_set and not has_riichi_opponent(ctx):
            for kind, tid in kong_candidates:
                if kind == "angang":
                    return "angang", tid, -1
        return "cut", hand[-1], len(hand) - 1

    opp_riichi = has_riichi_opponent(ctx)
    threat = max_opponent_threat(ctx)

    # 1. 可立直：评估立直 vs 默听（Dama） vs 面对立直弃和
    if "riichi_cut" in action_set and riichi_candidates:
        best_r = None
        best_r_key = None
        for tile_id, waits in riichi_candidates.items():
            try:
                cut_idx = hand.index(tile_id)
            except ValueError:
                continue
            rem = hand[:cut_idx] + hand[cut_idx + 1:]
            acc = count_acceptance(rem, ctx.meld_count, ctx.visible_34)
            han_r = estimate_hand_han(rem, combs, ctx.dora_tiles, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao, assume_riichi=True)
            han_dama = estimate_hand_han(rem, combs, ctx.dora_tiles, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao, assume_riichi=False)
            danger = total_tile_danger(tile_id, ctx)
            pts_r = _han_to_pts(han_r, ctx.is_dealer)
            # 听牌质量 = 活张^0.75 * 预期点数 - 对手立直时的宣言牌放铳风险
            ev = (max(1, acc) ** 0.75) * pts_r - (2600.0 * danger if opp_riichi else 0.0)
            key = (round(ev, 1), acc, len(waits or ()), 0 if tile_id in _AKA_5 else 1)
            if best_r_key is None or key > best_r_key:
                best_r_key = key
                best_r = (tile_id, cut_idx, acc, han_r, han_dama, danger, rem)

        if best_r is not None:
            tile_id, cut_idx, acc, han_r, han_dama, danger, rem = best_r
            # 面对他家立直：若自身是极差愚形小牌（acc <= 3 且 han_r < 3）且宣言牌极危（danger >= 2.0），弃和！
            if opp_riichi and acc <= 3 and han_r < 3.0 and danger >= 2.0 and "cut" in action_set:
                pass  # 落入下方 Betaori 弃和逻辑
            else:
                # 默听（Dama）判断：已有满贯以上明确大牌（han_dama >= 4.0）且非振听、且确定有役，或残局 wall <= 5 不白送立直棒
                has_yaku_dama = _has_credible_open_yaku(rem, combs, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao, 0) or (
                    all(normalize_tile(t) not in _YAOJIU_IDS for t in rem)
                )
                should_dama = (
                    "cut" in action_set
                    and has_yaku_dama
                    and not ctx.is_furiten
                    and ((han_dama >= 4.5 and acc >= 4) or (ctx.wall_left <= 5 and han_dama >= 2.0))
                )
                return ("cut" if should_dama else "riichi_cut"), tile_id, cut_idx

    # 2. 暗杠 / 加杠：有人立直时绝不轻率开杠；否则不退向听可杠
    if ("angang" in action_set or "jiagang" in action_set) and kong_candidates and not opp_riichi and threat < 0.75:
        neg_base_s, base_acc = evaluate_hand(hand, ctx.meld_count, ctx.visible_34)
        base_shanten = -neg_base_s
        for kind, tid in kong_candidates:
            if kind not in action_set:
                continue
            if kind == "angang":
                rem = [t for t in hand if normalize_tile(t) != tid]
                neg_s_a, acc_a = evaluate_hand(rem, ctx.meld_count + 1, ctx.visible_34)
                if -neg_s_a <= base_shanten and acc_a >= base_acc - 2:
                    return "angang", tid, -1
            elif kind == "jiagang" and base_shanten <= 1:
                rem = list(hand)
                if _remove_one_normalized(rem, tid):
                    neg_s_a, acc_a = evaluate_hand(rem, ctx.meld_count, ctx.visible_34)
                    if -neg_s_a <= base_shanten:
                        return "jiagang", tid, -1

    # 3. 切牌候选评估
    legal_indices = [
        i for i, t in enumerate(hand)
        if normalize_tile(t) not in forbidden_tiles
    ]
    if not legal_indices:
        legal_indices = list(range(len(hand)))

    # 去重：保留非红 5 优先切出
    seen_exact = set()
    cands: List[Tuple[int, int, int, int, float, float, float]] = []
    min_shanten = 99
    for i in legal_indices:
        t = hand[i]
        if t in seen_exact:
            continue
        seen_exact.add(t)
        rem = hand[:i] + hand[i + 1:]
        neg_s, acc = evaluate_hand(rem, ctx.meld_count, ctx.visible_34)
        s = -neg_s
        if s < min_shanten:
            min_shanten = s
        danger = total_tile_danger(t, ctx)
        dora_b = _dora_keep_bonus(rem, ctx.dora_tiles)
        shape_b = _riichi_shape_bonus(rem, combs, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao)
        cands.append((t, i, s, acc, danger, dora_b, shape_b))

    # 4. 天凤级 Push/Fold 与 Betaori 绝对弃和！
    if opp_riichi:
        est_han = estimate_hand_han(hand, combs, ctx.dora_tiles, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao, assume_riichi=(len(combs) == 0))
        # >= 2 向听面对立直：坚决完全弃和（Betaori）！无视向听，只挑最安全牌！
        if min_shanten >= 2:
            safest = min(cands, key=lambda x: (round(x[4], 3), x[2], -x[3], 1 if x[0] in _AKA_5 else 0))
            return "cut", safest[0], safest[1]
        # 1 向听面对立直：仅当满贯级好形（est_han >= 4.0 且 max_acc >= 14）才对攻，否则 Betaori！
        max_1s_acc = max((x[3] for x in cands if x[2] == 1), default=0)
        if min_shanten == 1 and not (est_han >= 4.0 and max_1s_acc >= 14):
            # 优先切绝对安全现物（danger == 0）；若同为安全牌再保 1 向听
            safest = min(cands, key=lambda x: (round(x[4], 3), x[2], -x[3], 1 if x[0] in _AKA_5 else 0))
            return "cut", safest[0], safest[1]
        # 0 向听面对立直（如副露听牌）：若仅 1~2 番小听且最优听牌切极危无筋（danger >= 2.0），转弃和！
        if min_shanten == 0 and est_han < 3.0:
            tenpai_cands = [x for x in cands if x[2] == 0]
            safest_tenpai = min(tenpai_cands, key=lambda x: (x[4], -x[3]))
            if safest_tenpai[4] >= 2.0:
                safest = min(cands, key=lambda x: (round(x[4], 3), x[2], -x[3]))
                return "cut", safest[0], safest[1]

    # 5. 非立直高危对手（如 2+ 副露染手）残局防护
    if not opp_riichi and threat >= 0.72 and min_shanten >= 2 and ctx.wall_left <= 18:
        safest = min(cands, key=lambda x: (round(x[4], 3), x[2], -x[3]))
        return "cut", safest[0], safest[1]

    # 6. 进攻切牌：向听优先，同向听下综合「进张 + 宝牌保留 + 役形潜力 - 高危对手危险度」
    best_pick = None
    best_key = None
    for t, idx, s, acc, danger, dora_b, shape_b in cands:
        if s > min_shanten:
            continue
        # 进张效率 + 宝牌价值（保留一张宝牌/赤5 ≈ 值 3~4 张进张）+ 役形潜力
        offense = float(acc) + 1.35 * dora_b + 0.9 * shape_b
        if opp_riichi:
            offense -= 8.0 * danger  # 对攻中同向听仍强烈避开危险牌
        elif threat >= 0.55:
            offense -= 2.2 * threat * danger
        red_keep = 0 if t in _AKA_5 else 1
        key = (-s, round(offense, 2), acc, -round(danger, 2), red_keep)
        if best_key is None or key > best_key:
            best_key = key
            best_pick = (t, idx)

    if best_pick is not None:
        return "cut", best_pick[0], best_pick[1]
    return "cut", cands[0][0], cands[0][1]


# ── 鸣牌决策（吃/碰/明杠） ────────────────────────────────────────────────────

def choose_claim_plan(
    ctx: RiichiContext,
    actions: Sequence[str],
    cut_tile: int,
) -> str:
    """日麻鸣牌：严守有役门闩 + 高打点/宝牌/速攻条件，面对他家立直严控副露。"""
    action_set = set(actions)
    claim_actions = [a for a in ("peng", "chi_left", "chi_mid", "chi_right", "gang") if a in action_set]
    if not claim_actions:
        return "pass"

    hand = list(ctx.hand)
    combs = list(ctx.combs)
    cut_norm = normalize_tile(cut_tile)
    neg_base_s, base_acc = evaluate_hand(hand, ctx.meld_count, ctx.visible_34)
    base_s = -neg_base_s
    opp_riichi = has_riichi_opponent(ctx)

    # 面对立直：除非副露能直接听牌（0向听）且有役有打点，否则绝不副露减少安全牌！
    if opp_riichi and base_s >= 2:
        return "pass"

    best_action = "pass"
    best_rank = None

    for action in claim_actions:
        if action == "gang":
            if opp_riichi:
                continue  # 立直时绝不明杠送新宝牌
            if _count_normalized(hand, cut_norm) < 3:
                continue
            rem = list(hand)
            for _ in range(3):
                _remove_one_normalized(rem, cut_norm)
            new_combs = combs + [f"g{cut_norm}"]
            neg_s_a, acc_a = evaluate_hand(rem, ctx.meld_count + 1, ctx.visible_34)
            s_a = -neg_s_a
            if not _has_credible_open_yaku(rem, new_combs, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao, s_a):
                continue
            if s_a <= base_s and acc_a >= base_acc:
                han_a = estimate_hand_han(rem, new_combs, ctx.dora_tiles, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao)
                if han_a >= 3.0 or s_a == 0:
                    rank = (2 if s_a == 0 else 1, round(han_a * 10 + acc_a, 1))
                    if best_rank is None or rank > best_rank:
                        best_rank = rank
                        best_action = action
            continue

        # 吃 / 碰模拟
        rem = list(hand)
        if action == "peng":
            if _count_normalized(rem, cut_norm) < 2:
                continue
            _remove_one_normalized(rem, cut_norm)
            _remove_one_normalized(rem, cut_norm)
            new_combs = combs + [f"k{cut_norm}"]
            kuikae = _kuikae_forbidden_for_peng(cut_tile)
        else:
            if cut_norm // 10 not in (1, 2, 3):
                continue
            if action == "chi_left":
                need = (cut_norm - 2, cut_norm - 1)
                mid = cut_norm - 1
            elif action == "chi_mid":
                need = (cut_norm - 1, cut_norm + 1)
                mid = cut_norm
            else:
                need = (cut_norm + 1, cut_norm + 2)
                mid = cut_norm + 1
            if any(x // 10 != cut_norm // 10 or not (1 <= x % 10 <= 9) for x in need):
                continue
            if not all(_count_normalized(rem, x) >= 1 for x in need):
                continue
            for x in need:
                _remove_one_normalized(rem, x)
            new_combs = combs + [f"s{mid}"]
            kuikae = _kuikae_forbidden_for_chi(action, cut_tile)

        # 找副露后合法最优选切
        best_cut_s = 99
        best_cut_acc = -1
        best_rem_after = None
        for i, t in enumerate(rem):
            if normalize_tile(t) in kuikae:
                continue
            after = rem[:i] + rem[i + 1:]
            neg_s_a, acc_a = evaluate_hand(after, ctx.meld_count + 1, ctx.visible_34)
            s_a = -neg_s_a
            if (s_a, -acc_a) < (best_cut_s, -best_cut_acc):
                best_cut_s, best_cut_acc, best_rem_after = s_a, acc_a, after
        if best_rem_after is None:
            continue

        # 必须有可靠副露役！
        if not _has_credible_open_yaku(best_rem_after, new_combs, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao, best_cut_s):
            continue
        # 必须严格推进向听！
        if best_cut_s >= base_s:
            continue
        if opp_riichi and best_cut_s > 0:
            continue

        han_a = estimate_hand_han(
            best_rem_after, new_combs, ctx.dora_tiles, ctx.round_wind, ctx.seat_wind, ctx.open_tanyao
        )
        # 门清首次鸣牌门槛：日麻门清立直价值极高，首次副露需有高打点（>=2番或宝牌>=2）或残局速听
        if len(combs) == 0:
            if best_cut_s == 0:
                if han_a < 2.0 and ctx.wall_left > 24 and best_cut_acc < 5:
                    continue
            elif best_cut_s == 1:
                if han_a < 2.8 and ctx.wall_left > 24:
                    continue
            else:
                # 2 向听及以上首次副露：仅在混/清一色/对对和/满贯级高打点（han >= 3.5）时才开鸣
                if han_a < 3.5:
                    continue

        rank = (-best_cut_s, round(han_a * 6.0 + best_cut_acc, 1), best_cut_acc)
        if best_rank is None or rank > best_rank:
            best_rank = rank
            best_action = action

    return best_action


# ── 异步游戏循环入口 ──────────────────────────────────────────────────────────

@paced_bot(lambda: _BOT_DELAY)
async def riichi_heuristic_bot_action(game_state, player_index: int, action_list: list, game_status: str):
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
        if "hu_self" in action_list and _riichi_should_accept_hu(game_state, "hu_self"):
            await submit_bot_action(get_ai_action, game_state, player_index, "hu_self", None, None, None, None)
            return
        if "nuki" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "nuki", None, None, None, None)
            return

        kuikae = _kuikae_forbidden_after_meld(player) if game_status == "onlycut_after_action" else set()
        forbidden = set(kuikae)
        forbidden.update(normalize_tile(t) for t in (getattr(player, "kuikae_forbidden_tiles", None) or ()))
        candidates = dict(getattr(player, "riichi_candidate_cuts", None) or {})
        kongs = tuple(hand_kong_candidates(game_state, player_index))
        ctx = context_from_game(game_state, player_index)

        action, tile_id, cut_index = await run_room_bot_cpu(
            game_state,
            choose_hand_plan,
            ctx,
            tuple(action_list),
            candidates,
            frozenset(forbidden),
            kongs,
        )
        if not bot_action_is_current(game_state, player_index, expected_tick):
            return
        if action in ("cut", "riichi_cut") and tile_id is not None:
            hand = list(player.hand_tiles)
            is_moqie = infer_bot_cut_class(hand, tile_id, cut_index, draw_slot=has_draw_slot(player))
            await submit_bot_action(get_ai_action, game_state, player_index, action, is_moqie, tile_id, cut_index, None)
        elif action in ("angang", "jiagang"):
            await submit_bot_action(get_ai_action, game_state, player_index, action, None, None, None, tile_id)
        return

    if game_status == "waiting_action_qianggang":
        for hu in ("hu_first", "hu_second", "hu_third"):
            if hu in action_list and _riichi_should_accept_ron(game_state, player, hu):
                await submit_bot_action(get_ai_action, game_state, player_index, hu, None, None, None, None)
                return
        if "pass" in action_list:
            await submit_bot_action(get_ai_action, game_state, player_index, "pass", None, None, None, None)
        return

    if game_status == "waiting_action_after_cut":
        for hu in ("hu_first", "hu_second", "hu_third"):
            if hu in action_list and _riichi_should_accept_ron(game_state, player, hu):
                await submit_bot_action(get_ai_action, game_state, player_index, hu, None, None, None, None)
                return
            if hu in action_list and _is_riichi_ron_furiten(player):
                break
        discards = game_state.player_list[game_state.current_player_index].discard_tiles
        cut_tile = discards[-1] if discards else None
        if cut_tile is None:
            if "pass" in action_list:
                await submit_bot_action(get_ai_action, game_state, player_index, "pass", None, None, None, None)
            return
        ctx = context_from_game(game_state, player_index)
        choice = await run_room_bot_cpu(
            game_state,
            choose_claim_plan,
            ctx,
            tuple(action_list),
            cut_tile,
        )
        if bot_action_is_current(game_state, player_index, expected_tick):
            await submit_bot_action(get_ai_action, game_state, player_index, choice, None, None, None, None)
