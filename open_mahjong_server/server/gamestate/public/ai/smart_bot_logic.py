"""
牌效 AI 计算逻辑
牌面编码转换、向听数计算、进张数计算、手牌评估函数。
"""
from collections import Counter
import logging
from typing import List, Optional, Set, Tuple

from ....game_calculation.riichi.washizu_tiles import normalize as _washizu_normalize
from .guobiao_shanten import (
    SHISANYAO_IDS,
    _yiban_candidate_tiles,
    shanten_shisanyao,
    xiangting_yiban,
)

logger = logging.getLogger(__name__)
# 兼容旧测试对 smart_bot_logic.Shanten 的 monkeypatch（现统一走 guobiao_shanten 缓存引擎）
Shanten = None

# ─── 牌面编码转换 ───────────────────────────────────────────

# 项目牌ID -> mahjong 库 34-index
# 项目: 11-19(万) 21-29(筒) 31-39(索) 41(东) 42(南) 43(西) 44(北) 45(中) 46(发) 47(白)
# 34-index: 0-8(万) 9-17(筒) 18-26(索) 27(东) 28(南) 29(西) 30(北) 31(白) 32(发) 33(中)
_HONOR_MAP = {41: 27, 42: 28, 43: 29, 44: 30, 45: 33, 46: 32, 47: 31}

def tile_suit(tile_id: int) -> int:
    return tile_id // 10


def cut_candidate_hand(hand_tiles: List[int], dingque_suit: int) -> List[int]:
    """手牌仍含定缺花色时，只能在定缺牌中切牌（与四川 action_check / 服务端纠正一致）。"""
    if dingque_suit not in (1, 2, 3):
        return hand_tiles
    if not any(tile_suit(t) == dingque_suit for t in hand_tiles):
        return hand_tiles
    return [t for t in hand_tiles if tile_suit(t) == dingque_suit]


def first_dingque_tile(hand_tiles: List[int], dingque_suit: int) -> Optional[int]:
    """返回手牌中第一张定缺花色牌（与 _enforce_dingque_first 顺序一致）。"""
    if dingque_suit not in (1, 2, 3):
        return None
    for tile in hand_tiles:
        if tile_suit(tile) == dingque_suit:
            return tile
    return None


def normalize_tile(tile_id: int) -> int:
    """红5/透明牌归一化：105->15, 205->25, 305->35 等。其他牌原值返回。"""
    return _washizu_normalize(tile_id)

def tile_to_34(tile_id: int) -> int:
    tile_id = normalize_tile(tile_id)
    if tile_id in _HONOR_MAP:
        return _HONOR_MAP[tile_id]
    suit = tile_id // 10  # 1=万 2=筒 3=索
    num = tile_id % 10    # 1-9
    return (suit - 1) * 9 + (num - 1)

def hand_to_34array(hand_tiles: List[int]) -> List[int]:
    arr = [0] * 34
    for t in hand_tiles:
        idx = tile_to_34(t)
        if 0 <= idx < 34:
            arr[idx] += 1
    return arr

def combination_to_34array(combination_tiles: List[str]) -> List[int]:
    """将副露编码转为 34 数组（仅统计副露占用的牌数）"""
    arr = [0] * 34
    for c in combination_tiles:
        if len(c) < 2:
            continue
        sign = c[0]
        try:
            tile_id = int(c[1:])
        except ValueError:
            continue
        idx = tile_to_34(tile_id)
        if idx < 0 or idx >= 34:
            continue
        if sign in ('s', 'S'):
            if idx > 0:
                arr[idx - 1] += 1
            arr[idx] += 1
            if idx < 33:
                arr[idx + 1] += 1
        elif sign in ('k', 'K'):
            arr[idx] += 3
        elif sign in ('g', 'G'):
            arr[idx] += 4
        elif sign == 'q':
            arr[idx] += 2
    return arr

def count_melds(combination_tiles: List[str]) -> int:
    """副露面子数"""
    count = 0
    for c in combination_tiles:
        if len(c) < 2:
            continue
        if c[0] in ('s', 'S', 'k', 'K', 'g', 'G'):
            count += 1
    return count

# ─── 向听数计算 ────────────────────────────────────────────

def _shanten_riichi_qidui(counts: Counter) -> int:
    """日麻/标准七对向听（4同张仅计1对，需7种不同对子）。"""
    pairs = sum(1 for c in counts.values() if c >= 2)
    kinds = sum(1 for c in counts.values() if c >= 1)
    return 6 - pairs + max(0, 7 - kinds)


def calc_shanten(hand_tiles: List[int], meld_count: int = 0) -> int:
    # 统一复用 guobiao_shanten 的缓存一般型 DP（共享 yiban_general 48 MiB 预算），
    # 16 张 5 面子规则走 structural_shanten。
    total_melds = len(hand_tiles) // 3 + meld_count
    if len(hand_tiles) > 17 or total_melds > 5:
        return 99  # Invalid snapshot; do not expand an unbounded structural search.
    norm = [normalize_tile(t) for t in hand_tiles]
    if total_melds > 4:
        from .structural_shanten import distance
        return distance(norm, meld_count, total_melds)
    counts = Counter(t for t in norm if 11 <= t <= 47 and t // 10 != 5)
    best = xiangting_yiban(counts, meld_count)
    if meld_count == 0 and len(norm) >= 13:
        best = min(best, _shanten_riichi_qidui(counts), shanten_shisanyao(counts))
    return best

# ─── 场上已见牌统计 ─────────────────────────────────────────

def count_visible_tiles(game_state, viewer_index=None) -> List[int]:
    """统计所有玩家的弃牌 + 副露中的已见牌（34 数组）"""
    visible = [0] * 34
    for player in game_state.player_list:
        for t in getattr(player, 'discard_tiles', []):
            idx = tile_to_34(t)
            if 0 <= idx < 34:
                visible[idx] += 1
        comb_arr = combination_to_34array(getattr(player, 'combination_tiles', []))
        for i in range(34):
            visible[i] += comb_arr[i]
    if getattr(game_state, 'sub_rule', None) in ('riichi/washizu', 'riichi/washizu_realtime', 'riichi/washizu_doubles', 'guobiao/transparent', 'guobiao/transparent_realtime') and viewer_index is not None:
        from ....game_calculation.riichi.washizu_tiles import is_transparent
        for player in game_state.player_list:
            if player.player_index == viewer_index:
                continue  # Own hand is subtracted separately by count_acceptance.
            from ...game_riichi.washizu import can_see_teammate_hand
            reveal_teammate = can_see_teammate_hand(game_state, player, viewer_index)
            for tile in player.hand_tiles:
                if is_transparent(tile) or reveal_teammate:
                    visible[tile_to_34(tile)] += 1
        for tile in getattr(game_state, "dora_indicators", []) + getattr(game_state, "kan_dora_indicators", []):
            visible[tile_to_34(tile)] += 1
    return visible

# ─── 进张数计算 ─────────────────────────────────────────────

ALL_TILE_IDS = (
    list(range(11, 20)) +  # 万
    list(range(21, 30)) +  # 筒
    list(range(31, 40)) +  # 索
    [41, 42, 43, 44, 45, 46, 47]  # 字
)

def count_acceptance(hand_tiles: List[int], meld_count: int, visible_34: List[int]) -> int:
    """计算进张数：有多少张未见牌摸入后能降低向听数"""
    base_shanten = calc_shanten(hand_tiles, meld_count)
    if base_shanten <= -1:
        return 999  # 已和牌
    norm = [normalize_tile(t) for t in hand_tiles]
    counts = Counter(t for t in norm if 11 <= t <= 47 and t // 10 != 5)
    # 候选牌剪枝：一般型仅已有牌及邻张 ±1/±2 可降向听；
    # 门清接近国士或种类不足 7 种（七对需新牌种）时补全对应候选。
    if meld_count == 0 and len(counts) < 7:
        candidates = ALL_TILE_IDS
    else:
        cand_set = set(_yiban_candidate_tiles(counts))
        if meld_count == 0 and len(norm) >= 13 and shanten_shisanyao(counts) <= base_shanten + 1:
            cand_set.update(SHISANYAO_IDS)
        candidates = cand_set
    hand_34 = hand_to_34array(norm)
    total = 0
    total_melds = len(norm) // 3 + meld_count
    use_unified = total_melds <= 4
    n_len = len(norm)
    for tile_id in candidates:
        idx = tile_to_34(tile_id)
        remaining = 4 - visible_34[idx] - hand_34[idx]
        if remaining <= 0:
            continue
        if use_unified:
            counts[tile_id] += 1
            new_shanten = xiangting_yiban(counts, meld_count)
            if meld_count == 0 and n_len + 1 >= 13:
                new_shanten = min(new_shanten, _shanten_riichi_qidui(counts), shanten_shisanyao(counts))
            counts[tile_id] -= 1
            if counts[tile_id] == 0:
                del counts[tile_id]
        else:
            new_shanten = calc_shanten(norm + [tile_id], meld_count)
        if new_shanten < base_shanten:
            total += remaining
    return total

# ─── 评估函数 ──────────────────────────────────────────────

def evaluate_hand(hand_tiles: List[int], meld_count: int, visible_34: List[int]) -> Tuple[int, int]:
    """返回 (-shanten, acceptance) 作为评分元组，越大越好"""
    s = calc_shanten(hand_tiles, meld_count)
    a = count_acceptance(hand_tiles, meld_count, visible_34)
    return (-s, a)

def find_best_cut(hand_tiles: List[int], meld_count: int, visible_34: List[int],
                   forbidden_normalized: Optional[Set[int]] = None) -> Tuple[int, int]:
    """枚举每张手牌切出后的最优评分，返回 (best_tile_id, best_cut_index)
    forbidden_normalized: 食替等规则禁切牌的归一化牌ID集合（与 hand_tiles 比较时手牌也会归一化）
    """
    forbidden = forbidden_normalized or set()
    best_score = (-999, -1)
    best_tile = None
    best_index = -1
    seen = set()
    for i, tile in enumerate(hand_tiles):
        if tile in seen:
            continue
        seen.add(tile)
        if normalize_tile(tile) in forbidden:
            continue
        remaining = hand_tiles[:i] + hand_tiles[i+1:]
        score = evaluate_hand(remaining, meld_count, visible_34)
        if score > best_score:
            best_score = score
            best_tile = tile
            best_index = i
    if best_tile is None:
        # 全部被禁切（理论上不应发生）时退回首张未被禁切的牌
        for i, tile in enumerate(hand_tiles):
            if normalize_tile(tile) not in forbidden:
                return tile, i
        return hand_tiles[-1], len(hand_tiles) - 1
    return best_tile, best_index

def find_best_cut_score(hand_tiles: List[int], meld_count: int, visible_34: List[int],
                         forbidden_normalized: Optional[Set[int]] = None) -> Tuple[int, int]:
    """枚举每张手牌切出后的最优评分，返回最优评分元组
    forbidden_normalized: 禁切牌的归一化牌ID集合（如日麻食替）；为空集时不过滤
    """
    forbidden = forbidden_normalized or set()
    best_score = (-999, -1)
    seen = set()
    for i, tile in enumerate(hand_tiles):
        if tile in seen:
            continue
        seen.add(tile)
        if normalize_tile(tile) in forbidden:
            continue
        remaining = hand_tiles[:i] + hand_tiles[i+1:]
        score = evaluate_hand(remaining, meld_count, visible_34)
        if score > best_score:
            best_score = score
    return best_score

# ─── 起和番检查 ────────────────────────────────────────────

def should_accept_hu(game_state, player_index: int, hu_action: str) -> bool:
    """检查 AI 是否应该接受和牌（避免错和）
    在国标麻将中，result[0] 减去花牌数需 >= hepai_limit；台湾麻将使用 result.tai 判断；立直麻将使用 result.han 判断。
    无起和番限制的规则直接返回 True。
    """
    hepai_limit = getattr(game_state, 'hepai_limit', 0)
    result = getattr(game_state, 'result_dict', {}).get(hu_action)
    from ....game_calculation import guobiao_jinluo as jinluo
    if jinluo.enabled(game_state):
        if not result:
            return False
        player = game_state.player_list[player_index]
        hand = list(player.hand_tiles)
        if hu_action != "hu_self":
            tile = getattr(game_state, "jiagang_tile", None)
            if tile is None:
                discards = game_state.player_list[game_state.current_player_index].discard_tiles
                if not discards:
                    return False
                tile = discards[-1]
            hand.append(tile)
        return jinluo.allows_state_win(game_state, result, hand, player.combination_tiles, player_index)
    if isinstance(result, dict):
        if not result:
            return True
        score_key = "tai" if "tai" in result else "han" if "han" in result else None
        score = result.get(score_key, 0) if score_key is not None else 0
        return int(score) >= hepai_limit
    if hepai_limit <= 1:
        return True
    if not result:
        return True
    player = game_state.player_list[player_index]
    huapai_count = len(getattr(player, 'huapai_list', []))
    return result[0] - huapai_count >= hepai_limit
