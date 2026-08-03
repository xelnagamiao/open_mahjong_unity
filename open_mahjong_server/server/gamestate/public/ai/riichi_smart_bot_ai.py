"""
日麻牌效AI机器人（牌效罗伯特·立直版）
在公共牌效AI基础上叠加日麻专属规则：
- 食替（喰い替え）禁切牌检查
- 红5（105/205/305）归一化，正确识别红5的吃/碰
"""
import asyncio
import logging
from typing import List, Set

from ..hand_slot_utils import has_draw_slot, infer_bot_cut_class
from .bot_executor import bot_action_is_current, run_room_bot_cpu
from .get_action import get_ai_action
from .smart_bot_logic import (
    count_melds, count_visible_tiles, evaluate_hand,
    find_best_cut, find_best_cut_score,
    normalize_tile, count_acceptance,
)
from ...game_riichi.action_check import _is_furiten
from .smart_bot_ai import _handle_buhua_round, _wait_until_actionable

logger = logging.getLogger(__name__)


def _is_riichi_ron_furiten(player) -> bool:
    """荣和/抢杠振听：永久振听、同巡振听、立直振听任一成立则不可荣和/抢杠（自摸不受影响）。"""
    return (
        _is_furiten(player)
        or getattr(player, "temp_furiten", False)
        or getattr(player, "riichi_furiten", False)
    )


def _riichi_should_accept_hu(game_state, hu_action: str) -> bool:
    """日麻 AI 是否接受和牌：避免主动错和。
    - 无役（no_yaku）：不接受（开启错和时人类可主动错和，但 AI 不应送人头）。
    - 番数低于起和番数（hepai_limit）：不接受。
    日麻 result_dict 存的是字典（与国标列表不同），故单独判定，避免误用 should_accept_hu 的 result[0]。"""
    result = getattr(game_state, "result_dict", {}).get(hu_action)
    if not isinstance(result, dict):
        return True
    if result.get("no_yaku"):
        return False
    han = int(result.get("han", 0))
    limit = int(getattr(game_state, "hepai_limit", 1) or 1)
    return han >= limit


def _riichi_should_accept_ron(game_state, player, hu_action: str) -> bool:
    """日麻 AI 是否接受荣和/抢杠和：振听状态下不可荣和/抢杠。"""
    if _is_riichi_ron_furiten(player):
        return False
    return _riichi_should_accept_hu(game_state, hu_action)


# ─── 食替（喰い替え）禁切牌 ─────────────────────────────────

def _is_same_number_suit(t1: int, t2: int) -> bool:
    """判断两张归一化牌是否同花色数牌（万/筒/索内 1-9）"""
    if not (11 <= t1 <= 39 and 11 <= t2 <= 39):
        return False
    if t1 % 10 == 0 or t2 % 10 == 0:
        return False
    return t1 // 10 == t2 // 10


def _kuikae_forbidden_for_chi(chi_type: str, called_tile: int) -> Set[int]:
    """指定吃法对应的食替禁切牌（归一化集合）。
    - chi_left: 吃来的牌为顺子最右（手牌出 T-1, T-2），禁切 T 与 T-3
    - chi_mid : 吃来的牌为顺子中间（手牌出 T-1, T+1），禁切 T
    - chi_right: 吃来的牌为顺子最左（手牌出 T+1, T+2），禁切 T 与 T+3
    """
    called = normalize_tile(called_tile)
    forbidden: Set[int] = {called}
    swap = None
    if chi_type == "chi_left":
        swap = called - 3
    elif chi_type == "chi_right":
        swap = called + 3
    if swap is not None and _is_same_number_suit(swap, called):
        forbidden.add(swap)
    return forbidden


def _kuikae_forbidden_for_peng(called_tile: int) -> Set[int]:
    """碰后的食替禁切牌：仅碰来的牌本身。"""
    return {normalize_tile(called_tile)}


def _kuikae_forbidden_after_meld(player) -> Set[int]:
    """根据玩家最近一次副露推断食替禁切牌（用于 onlycut_after_action 阶段）。"""
    server_forbidden = {
        normalize_tile(t) for t in (getattr(player, 'kuikae_forbidden_tiles', None) or [])
    }
    if server_forbidden:
        return server_forbidden

    combos = getattr(player, 'combination_tiles', None)
    masks = getattr(player, 'combination_mask', None)
    if not combos or not masks:
        return set()
    meld_code = combos[-1]
    mask = masks[-1]
    if len(meld_code) < 2 or not mask:
        return set()
    sign = meld_code[0]
    try:
        meld_num = int(meld_code[1:])
    except ValueError:
        return set()
    called_tile = None
    for i in range(0, len(mask), 2):
        if mask[i] == 1:
            called_tile = mask[i + 1]
            break
    if called_tile is None:
        return set()
    if sign in ('k', 'K'):
        return _kuikae_forbidden_for_peng(called_tile)
    if sign in ('s', 'S'):
        called_norm = normalize_tile(called_tile)
        # meld_num 为吃顺中间位归一化牌号；据此还原吃法类型
        if called_norm == meld_num + 1:
            chi_type = "chi_left"
        elif called_norm == meld_num - 1:
            chi_type = "chi_right"
        else:
            chi_type = "chi_mid"
        return _kuikae_forbidden_for_chi(chi_type, called_tile)
    return set()


# ─── 红5归一化辅助：从手牌中按归一化牌值移除/查找 ───────────────

def _count_normalized(hand: List[int], normal_target: int) -> int:
    """统计 hand 中归一化后等于 normal_target 的牌数"""
    return sum(1 for t in hand if normalize_tile(t) == normal_target)


def _remove_one_normalized(hand: List[int], normal_target: int) -> bool:
    """从 hand 中移除一张归一化后等于 normal_target 的牌（原地修改）。
    优先移除非红5（保留红5以增加打点价值）。
    """
    for i, t in enumerate(hand):
        if normalize_tile(t) == normal_target and t not in (105, 205, 305):
            hand.pop(i)
            return True
    for i, t in enumerate(hand):
        if normalize_tile(t) == normal_target:
            hand.pop(i)
            return True
    return False


def _choose_riichi_hand_plan(
    hand, combs, meld_count, visible, action_list, is_riichi, candidates, forbidden
):
    hand = list(hand)
    combs = list(combs)
    actions = set(action_list)
    if "riichi_cut" in actions and candidates:
        tile, index = _pick_best_riichi_cut(hand, meld_count, visible, candidates)
        if tile is not None:
            return "riichi_cut", tile, index
    if "angang" in actions:
        seen = set()
        for tile in hand:
            norm = normalize_tile(tile)
            if norm in seen:
                continue
            seen.add(norm)
            if _count_normalized(hand, norm) >= 4:
                test = [t for t in hand if normalize_tile(t) != norm]
                if evaluate_hand(test, meld_count + 1, visible) >= evaluate_hand(hand, meld_count, visible):
                    return "angang", tile, -1
    if "jiagang" in actions:
        for c in combs:
            if not c.startswith("k"):
                continue
            try:
                tile = int(c[1:])
            except ValueError:
                continue
            if _count_normalized(hand, tile) >= 1:
                test = hand[:]
                _remove_one_normalized(test, tile)
                if evaluate_hand(test, meld_count, visible) >= evaluate_hand(hand, meld_count, visible):
                    return "jiagang", tile, -1
    if is_riichi and "cut" in actions and hand:
        return "cut", hand[-1], len(hand) - 1
    if "cut" in actions and hand:
        tile, index = find_best_cut(hand, meld_count, visible, set(forbidden))
        return "cut", tile, index
    return "pass", None, -1


def _choose_riichi_claim_plan(hand, meld_count, visible, action_list, cut_tile):
    hand = list(hand)
    actions = set(action_list)
    normal_cut = normalize_tile(cut_tile)
    best_action = "pass"
    best_score = evaluate_hand(hand, meld_count, visible)
    if "peng" in actions and _count_normalized(hand, normal_cut) >= 2:
        test = hand[:]
        _remove_one_normalized(test, normal_cut)
        _remove_one_normalized(test, normal_cut)
        score = find_best_cut_score(
            test, meld_count + 1, visible, _kuikae_forbidden_for_peng(cut_tile)
        )
        if score > best_score:
            best_action, best_score = "peng", score
    for action in ("chi_left", "chi_mid", "chi_right"):
        if action not in actions:
            continue
        if action == "chi_left":
            need = (normal_cut - 2, normal_cut - 1)
        elif action == "chi_mid":
            need = (normal_cut - 1, normal_cut + 1)
        else:
            need = (normal_cut + 1, normal_cut + 2)
        test = hand[:]
        if not all(_remove_one_normalized(test, n) for n in need):
            continue
        score = find_best_cut_score(
            test, meld_count + 1, visible, _kuikae_forbidden_for_chi(action, cut_tile)
        )
        if score > best_score:
            best_action, best_score = action, score
    if "gang" in actions and _count_normalized(hand, normal_cut) >= 3:
        test = hand[:]
        for _ in range(3):
            _remove_one_normalized(test, normal_cut)
        score = evaluate_hand(test, meld_count + 1, visible)
        if score > best_score:
            best_action = "gang"
    return best_action


# ─── 入口与各 game_status 处理 ─────────────────────────────

async def riichi_smart_bot_action(game_state, player_index: int, action_list: list, game_status: str):
    """
    日麻牌效AI自动操作：在公共牌效逻辑上叠加食替禁切与红5归一化处理。
    """
    try:
        current_player = game_state.player_list[player_index]

        if game_status == "waiting_hand_action":
            await asyncio.sleep(0.5)
            # 摸牌后手牌操作：和牌 > 暗杠/加杠 > 切牌
            await _handle_hand_action(game_state, player_index, action_list, current_player, set())
            return

        if game_status == "onlycut_after_action":
            await asyncio.sleep(0.5)
            # 吃碰后手牌操作：和牌 > 切牌（不可暗杠/加杠），并需遵守食替禁切
            kuikae_forbidden = _kuikae_forbidden_after_meld(current_player)
            await _handle_hand_action(game_state, player_index, action_list, current_player, kuikae_forbidden)
            return

        if game_status == "waiting_action_after_cut":
            # 他家切牌后：和牌 > 碰/吃/明杠评估 > pass
            await _handle_after_cut(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_action_qianggang":
            await _handle_riichi_qianggang(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_buhua_round":
            await asyncio.sleep(0.5)
            await _handle_buhua_round(game_state, player_index, action_list, current_player)
            return

        logger.warning(f"日麻牌效AI {player_index} 遇到未知游戏状态: {game_status}")

    except Exception as e:
        logger.error(f"日麻牌效AI {player_index} 自动操作失败: {e}", exc_info=True)


async def _handle_hand_action(game_state, player_index, action_list, player, kuikae_forbidden: Set[int]):
    """摸牌后/吃碰后的手牌阶段：补花 > 和牌 > 暗杠/加杠 > 牌效切牌
    kuikae_forbidden: 食替禁切牌（归一化集合），仅在吃碰后切牌阶段非空
    """
    if "buhua" in action_list:
        logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 buhua（手牌补花）")
        await get_ai_action(game_state, player_index, "buhua", None, None, None, None)
        return

    if "hu_self" in action_list and _riichi_should_accept_hu(game_state, "hu_self"):
        logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 hu_self")
        await get_ai_action(game_state, player_index, "hu_self", None, None, None, None)
        return

    is_riichi = "riichi" in player.tag_list or "daburu_riichi" in player.tag_list
    hand = player.hand_tiles[:]
    combs = list(getattr(player, 'combination_tiles', []))
    meld_count = count_melds(combs)
    visible = count_visible_tiles(game_state)
    forbidden = set(kuikae_forbidden or set())
    forbidden.update(normalize_tile(t) for t in (getattr(player, 'kuikae_forbidden_tiles', None) or []))
    candidates = dict(getattr(player, "riichi_candidate_cuts", None) or {})
    expected_tick = getattr(game_state, "server_action_tick", None)
    action, tile_id, cut_index = await run_room_bot_cpu(
        game_state,
        _choose_riichi_hand_plan,
        tuple(hand),
        tuple(combs),
        meld_count,
        tuple(visible),
        tuple(action_list),
        is_riichi,
        candidates,
        frozenset(forbidden),
    )
    if not bot_action_is_current(game_state, player_index, expected_tick):
        return
    if action in ("cut", "riichi_cut") and tile_id is not None:
        is_moqie = infer_bot_cut_class(hand, tile_id, cut_index, draw_slot=has_draw_slot(player))
        logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 {action}, tile_id={tile_id}, moqie={is_moqie}")
        await get_ai_action(game_state, player_index, action, is_moqie, tile_id, cut_index, None)
        return
    if action in ("angang", "jiagang"):
        logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 {action}, tile={tile_id}")
        await get_ai_action(game_state, player_index, action, None, None, None, tile_id)
        return


def _pick_best_riichi_cut(hand_tiles, meld_count, visible_34, candidates):
    """从 riichi_candidate_cuts 中挑出听牌枚数最大的切牌；按 (受入枚数, 听牌种类数) 排序，
    红 5（105/205/305）优先保留：评分相同优先切出非红 5。
    返回 (tile_id, cut_index)；候选为空时返回 (None, -1)。
    """
    best_score = (-1, -1, 1)  # (受入枚数, 种类数, 红5优先级倒序)
    best_tile = None
    best_index = -1
    for tile_id, wait_list in candidates.items():
        # 切出后手牌 13 张已是听牌
        try:
            cut_idx = hand_tiles.index(tile_id)
        except ValueError:
            continue
        new_hand = hand_tiles[:cut_idx] + hand_tiles[cut_idx + 1:]
        accept_count = count_acceptance(new_hand, meld_count, visible_34)
        wait_types = len(wait_list) if wait_list else 0
        red_dora_penalty = 0 if tile_id in (105, 205, 305) else 1
        score = (accept_count, wait_types, red_dora_penalty)
        if score > best_score:
            best_score = score
            best_tile = tile_id
            best_index = cut_idx
    return best_tile, best_index


async def _handle_after_cut(game_state, player_index, action_list, player):
    """他家切牌后的响应阶段：和牌 > 碰/吃/明杠评估 > pass
    红5归一化与食替禁切牌均在此函数内处理。
    """
    if not await _wait_until_actionable(game_state, player_index):
        logger.warning(f"日麻牌效AI {player_index} ({player.username}) 切牌后询问未进入 waiting_players_list，放弃操作")
        return
    for hu_action in ("hu_first", "hu_second", "hu_third"):
        if hu_action in action_list and _riichi_should_accept_ron(game_state, player, hu_action):
            logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 {hu_action}")
            await asyncio.sleep(0.5)
            await get_ai_action(game_state, player_index, hu_action, None, None, None, None)
            return
        if hu_action in action_list and _is_riichi_ron_furiten(player):
            logger.info(
                f"日麻牌效AI {player_index} ({player.username}) 振听跳过 {hu_action}, "
                f"temp={getattr(player, 'temp_furiten', False)}, "
                f"riichi_furiten={getattr(player, 'riichi_furiten', False)}"
            )
            break

    discard_tiles = game_state.player_list[game_state.current_player_index].discard_tiles
    cut_tile = discard_tiles[-1] if discard_tiles else None
    if cut_tile is None:
        if "pass" in action_list:
            await get_ai_action(game_state, player_index, "pass", None, None, None, None)
        return

    hand = player.hand_tiles[:]
    combs = getattr(player, 'combination_tiles', [])
    meld_count = count_melds(combs)
    visible = count_visible_tiles(game_state)
    expected_tick = getattr(game_state, "server_action_tick", None)
    best_action = await run_room_bot_cpu(
        game_state,
        _choose_riichi_claim_plan,
        tuple(hand),
        meld_count,
        tuple(visible),
        tuple(action_list),
        cut_tile,
    )
    if not bot_action_is_current(game_state, player_index, expected_tick):
        return

    logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 {best_action}")
    if best_action != "pass":
        await asyncio.sleep(0.5)
    await get_ai_action(game_state, player_index, best_action, None, None, None, None)


async def _handle_riichi_qianggang(game_state, player_index, action_list, player):
    """他家加杠时的抢杠询问：振听不可抢杠和，否则按番数判定是否和牌。"""
    if not await _wait_until_actionable(game_state, player_index):
        logger.warning(f"日麻牌效AI {player_index} ({player.username}) 抢杠询问未进入 waiting_players_list，放弃操作")
        return
    for hu_action in ("hu_first", "hu_second", "hu_third"):
        if hu_action in action_list and _riichi_should_accept_ron(game_state, player, hu_action):
            logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 {hu_action}（抢杠和）")
            await asyncio.sleep(0.5)
            await get_ai_action(game_state, player_index, hu_action, None, None, None, None)
            return
        if hu_action in action_list and _is_riichi_ron_furiten(player):
            logger.info(
                f"日麻牌效AI {player_index} ({player.username}) 振听跳过抢杠 {hu_action}, "
                f"temp={getattr(player, 'temp_furiten', False)}, "
                f"riichi_furiten={getattr(player, 'riichi_furiten', False)}"
            )
            break
    if "pass" in action_list:
        logger.info(f"日麻牌效AI {player_index} ({player.username}) 选择 pass（抢杠）")
        await get_ai_action(game_state, player_index, "pass", None, None, None, None)
