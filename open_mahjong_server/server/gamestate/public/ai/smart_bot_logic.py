"""
牌效 AI 计算逻辑
牌面编码转换、向听数计算、进张数计算、手牌评估函数。
"""
import logging
from typing import List, Tuple

logger = logging.getLogger(__name__)

try:
    from mahjong.shanten import Shanten
except ImportError:
    Shanten = None
    logger.warning("mahjong 库未安装，牌效 AI 将退化为摸切模式。请执行 pip install mahjong")

# ─── 牌面编码转换 ───────────────────────────────────────────

# 项目牌ID -> mahjong 库 34-index
# 项目: 11-19(万) 21-29(筒) 31-39(索) 41(东) 42(南) 43(西) 44(北) 45(中) 46(发) 47(白)
# 34-index: 0-8(万) 9-17(筒) 18-26(索) 27(东) 28(南) 29(西) 30(北) 31(白) 32(发) 33(中)
_HONOR_MAP = {41: 27, 42: 28, 43: 29, 44: 30, 45: 33, 46: 32, 47: 31}

def tile_to_34(tile_id: int) -> int:
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

def calc_shanten(hand_tiles: List[int], meld_count: int = 0) -> int:
    if Shanten is None:
        return 99
    arr = hand_to_34array(hand_tiles)
    try:
        return Shanten.calculate_shanten(arr)
    except ValueError:
        return 99

# ─── 场上已见牌统计 ─────────────────────────────────────────

def count_visible_tiles(game_state) -> List[int]:
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
    hand_34 = hand_to_34array(hand_tiles)
    total = 0
    for tile_id in ALL_TILE_IDS:
        idx = tile_to_34(tile_id)
        remaining = 4 - visible_34[idx] - hand_34[idx]
        if remaining <= 0:
            continue
        test_hand = hand_tiles + [tile_id]
        new_shanten = calc_shanten(test_hand, meld_count)
        if new_shanten < base_shanten:
            total += remaining
    return total

# ─── 评估函数 ──────────────────────────────────────────────

def evaluate_hand(hand_tiles: List[int], meld_count: int, visible_34: List[int]) -> Tuple[int, int]:
    """返回 (-shanten, acceptance) 作为评分元组，越大越好"""
    s = calc_shanten(hand_tiles, meld_count)
    a = count_acceptance(hand_tiles, meld_count, visible_34)
    return (-s, a)

def find_best_cut(hand_tiles: List[int], meld_count: int, visible_34: List[int]) -> Tuple[int, int]:
    """枚举每张手牌切出后的最优评分，返回 (best_tile_id, best_cut_index)"""
    best_score = (-999, -1)
    best_tile = hand_tiles[-1]
    best_index = len(hand_tiles) - 1
    seen = set()
    for i, tile in enumerate(hand_tiles):
        if tile in seen:
            continue
        seen.add(tile)
        remaining = hand_tiles[:i] + hand_tiles[i+1:]
        score = evaluate_hand(remaining, meld_count, visible_34)
        if score > best_score:
            best_score = score
            best_tile = tile
            best_index = i
    return best_tile, best_index

def find_best_cut_score(hand_tiles: List[int], meld_count: int, visible_34: List[int]) -> Tuple[int, int]:
    """枚举每张手牌切出后的最优评分，返回最优评分元组"""
    best_score = (-999, -1)
    seen = set()
    for i, tile in enumerate(hand_tiles):
        if tile in seen:
            continue
        seen.add(tile)
        remaining = hand_tiles[:i] + hand_tiles[i+1:]
        score = evaluate_hand(remaining, meld_count, visible_34)
        if score > best_score:
            best_score = score
    return best_score

# ─── 起和番检查 ────────────────────────────────────────────

def should_accept_hu(game_state, player_index: int, hu_action: str) -> bool:
    """检查 AI 是否应该接受和牌（避免错和）
    有起和番限制的规则（如国标）中，result[0] 减去花牌数需 >= hepai_limit。
    无起和番限制的规则直接返回 True。
    """
    hepai_limit = getattr(game_state, 'hepai_limit', 0)
    if hepai_limit <= 1:
        return True
    result = getattr(game_state, 'result_dict', {}).get(hu_action)
    if not result:
        return True
    player = game_state.player_list[player_index]
    huapai_count = len(getattr(player, 'huapai_list', []))
    return result[0] - huapai_count >= hepai_limit
