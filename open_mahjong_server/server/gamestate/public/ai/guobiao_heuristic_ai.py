"""
高性能罗伯特（国标启发式 AI）异步外壳。

user_id=3，排在牌效罗伯特（user_id=2）之后。
仅国标标准规则（guobiao/standard）陪打；变种规则暂未支持。
状态机对齐牌效罗伯特 smart_bot_ai。

出身与自测画像见同目录 GUOBIAO_HEURISTIC_BOT.md。
"""
from __future__ import annotations

import asyncio
import logging
import time

from ..hand_slot_utils import has_draw_slot, infer_bot_cut_class
from .get_action import get_ai_action
from .guobiao_heuristic_logic import (
    choose_best_discard,
    choose_claim,
    choose_closed_kan,
    context_from_game,
)
from .guobiao_shanten import normalize_tile
from .smart_bot_logic import should_accept_hu

logger = logging.getLogger(__name__)

# 最低思考墙钟 500ms：先算后补（elapsed < 0.5 才 sleep 补齐）；超过则不补。
# 鸣牌后的 claim_meld_post_gap 是房间 gap，与 think pad 独立叠加，不计入本地板。
# smoke 等测试可临时压低 _BOT_DELAY，以便 wait_action 入队后再提交。
_BOT_DELAY = 0.5
_RON_HU_ACTIONS = ("hu", "hu_first", "hu_second", "hu_third")


async def _think_pad(t0: float) -> None:
    """决策算完后补 sleep，使墙钟至少 _BOT_DELAY；已超过则不补。"""
    pad = _BOT_DELAY - (time.perf_counter() - t0)
    if pad > 0:
        await asyncio.sleep(pad)


async def _wait_until_actionable(game_state, player_index: int, attempts: int = 200, interval: float = 0.01) -> bool:
    expected_tick = getattr(game_state, "server_action_tick", None)
    for _ in range(attempts):
        waiting_tick = getattr(game_state, "_waiting_action_tick", None)
        correct_round = waiting_tick is None or waiting_tick == expected_tick
        if correct_round and player_index in getattr(game_state, "waiting_players_list", []):
            return True
        await asyncio.sleep(interval)
    return False


async def guobiao_heuristic_action(game_state, player_index: int, action_list: list, game_status: str):
    """国标启发式自动操作入口。"""
    try:
        current_player = game_state.player_list[player_index]

        if game_status in ("waiting_initial_hu", "waiting_sea_bottom"):
            if "pass" in action_list and await _wait_until_actionable(game_state, player_index):
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
            return

        if game_status == "waiting_hand_action":
            if not await _wait_until_actionable(game_state, player_index):
                logger.warning(
                    f"高性能罗伯特 {player_index} ({current_player.username}) 手牌询问未进入 waiting_players_list"
                )
                return
            await _handle_hand_action(game_state, player_index, action_list, current_player)
            return

        if game_status == "onlycut_after_action":
            if not await _wait_until_actionable(game_state, player_index):
                logger.warning(
                    f"高性能罗伯特 {player_index} ({current_player.username}) 鸣牌后未进入 waiting_players_list"
                )
                return
            cp = bool(getattr(game_state, "claim_protection", False))
            from ..claim_protection import get_meld_post_gap
            meld_gap = get_meld_post_gap(game_state) if cp else 0.0
            await _handle_hand_action(
                game_state, player_index, action_list, current_player, meld_gap=meld_gap
            )
            return

        if game_status == "waiting_action_after_cut":
            await _handle_after_cut(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_action_qianggang":
            await _handle_qianggang(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_buhua_round":
            if not await _wait_until_actionable(game_state, player_index):
                return
            await _handle_buhua_round(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_flower_choice":
            if "hu_flower" in action_list and await _wait_until_actionable(game_state, player_index):
                await get_ai_action(game_state, player_index, "hu_flower", None, None, None, None)
            return

        logger.warning(f"高性能罗伯特 {player_index} 遇到未知游戏状态: {game_status}")

    except Exception as e:
        logger.error(f"高性能罗伯特 {player_index} 自动操作失败: {e}", exc_info=True)


async def _handle_hand_action(game_state, player_index, action_list, player, *, meld_gap: float = 0.0):
    t0 = time.perf_counter()

    async def _submit(action, is_moqie=None, tile_id=None, cut_index=None, gang_tile=None):
        await _think_pad(t0)
        if meld_gap > 0:
            await asyncio.sleep(meld_gap)
        await get_ai_action(game_state, player_index, action, is_moqie, tile_id, cut_index, gang_tile)

    if "buhua" in action_list:
        logger.info(f"高性能罗伯特 {player_index} 选择 buhua")
        await _submit("buhua")
        return

    if "hu_self" in action_list and should_accept_hu(game_state, player_index, "hu_self"):
        logger.info(f"高性能罗伯特 {player_index} 选择 hu_self")
        await _submit("hu_self")
        return

    ctx = context_from_game(game_state, player_index)
    hand = [normalize_tile(t) for t in player.hand_tiles]

    # 先算最优切作基线；暗杠/加杠仅在不恶化 winningShanten、不削合法听进张时取
    best_disc = choose_best_discard(ctx) if ("cut" in action_list and hand) else None
    kan = choose_closed_kan(
        ctx,
        allow_angang="angang" in action_list,
        allow_jiagang=("jiagang" in action_list or "buzhang" in action_list),
        baseline_discard=best_disc,
    )
    if kan is not None:
        kind, tile = kan
        if kind == "angang":
            logger.info(f"高性能罗伯特 {player_index} 选择 angang tile={tile}")
            await _submit("angang", gang_tile=tile)
            return
        action = "jiagang" if "jiagang" in action_list else "buzhang"
        logger.info(f"高性能罗伯特 {player_index} 选择 {action} tile={tile}")
        await _submit(action, gang_tile=tile)
        return

    if "cut" in action_list and hand:
        tile_id = best_disc if best_disc is not None else choose_best_discard(ctx)
        if tile_id is None:
            tile_id = hand[-1]
        cut_index = player.hand_tiles.index(tile_id) if tile_id in player.hand_tiles else 0
        # 红5：手牌可能是 105 等
        if tile_id not in player.hand_tiles:
            for i, t in enumerate(player.hand_tiles):
                if normalize_tile(t) == tile_id:
                    tile_id = t
                    cut_index = i
                    break
        is_moqie = infer_bot_cut_class(
            player.hand_tiles, tile_id, cut_index, draw_slot=has_draw_slot(player)
        )
        logger.info(f"高性能罗伯特 {player_index} 选择 cut tile={tile_id} moqie={is_moqie}")
        await _submit("cut", is_moqie, tile_id, cut_index, None)


async def _handle_after_cut(game_state, player_index, action_list, player):
    if not await _wait_until_actionable(game_state, player_index):
        return

    t0 = time.perf_counter()
    for hu_action in _RON_HU_ACTIONS:
        if hu_action in action_list and should_accept_hu(game_state, player_index, hu_action):
            logger.info(f"高性能罗伯特 {player_index} 选择 {hu_action}")
            await _think_pad(t0)
            await get_ai_action(game_state, player_index, hu_action, None, None, None, None)
            return

    discard_tiles = game_state.player_list[game_state.current_player_index].discard_tiles
    cut_tile = discard_tiles[-1] if discard_tiles else None
    if cut_tile is None:
        if "pass" in action_list:
            await get_ai_action(game_state, player_index, "pass", None, None, None, None)
        return

    ctx = context_from_game(game_state, player_index)
    claim_actions = [a for a in action_list if a in ("peng", "gang", "chi_left", "chi_mid", "chi_right")]
    best = choose_claim(ctx, claim_actions + (["pass"] if "pass" in action_list else []), cut_tile)
    if best not in action_list:
        best = "pass" if "pass" in action_list else (action_list[0] if action_list else "pass")
    logger.info(f"高性能罗伯特 {player_index} 选择 {best}")
    if best != "pass":
        await _think_pad(t0)
    await get_ai_action(game_state, player_index, best, None, None, None, None)


async def _handle_qianggang(game_state, player_index, action_list, player):
    if not await _wait_until_actionable(game_state, player_index):
        return
    t0 = time.perf_counter()
    for hu_action in _RON_HU_ACTIONS:
        if hu_action in action_list and should_accept_hu(game_state, player_index, hu_action):
            await _think_pad(t0)
            await get_ai_action(game_state, player_index, hu_action, None, None, None, None)
            return
    if "pass" in action_list:
        await get_ai_action(game_state, player_index, "pass", None, None, None, None)


async def _handle_buhua_round(game_state, player_index, action_list, player):
    t0 = time.perf_counter()
    if "buhua" in action_list:
        await _think_pad(t0)
        await get_ai_action(game_state, player_index, "buhua", None, None, None, None)
        return
    if "hu_self" in action_list and should_accept_hu(game_state, player_index, "hu_self"):
        await _think_pad(t0)
        await get_ai_action(game_state, player_index, "hu_self", None, None, None, None)
        return
    if "pass" in action_list:
        await _think_pad(t0)
        await get_ai_action(game_state, player_index, "pass", None, None, None, None)
