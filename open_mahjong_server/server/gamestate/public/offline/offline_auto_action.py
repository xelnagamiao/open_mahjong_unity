# 掉线玩家托管：与 AI 机器人逻辑分离，只摸切、不自动补花/和/杠
import asyncio
import logging

from ..ai.get_action import get_ai_action
from ..ai.smart_bot_logic import first_dingque_tile
from ..hand_slot_utils import bot_ask_hand_game_status, has_draw_slot, infer_bot_cut_class

logger = logging.getLogger(__name__)

_PASS_WAIT_STATUSES = (
    "waiting_action_after_cut",
    "waiting_action_qianggang",
    "waiting_initial_hu",
    "waiting_sea_bottom",
)
_OFFLINE_DELAY = 0.5


def _pick_offline_cut_tile(player):
    """掉线托管：摸切优先（末张含花牌照常打出）；定缺花色仍优先（与服务端 _enforce_dingque_first 一致）。"""
    hand = player.hand_tiles
    dingque = getattr(player, "dingque_suit", 0)
    dingque_tile = first_dingque_tile(hand, dingque)
    if dingque_tile is not None:
        tile_id = dingque_tile
        cut_index = next(i for i, t in enumerate(hand) if t == tile_id)
    else:
        tile_id = hand[-1]
        cut_index = len(hand) - 1
    is_moqie = infer_bot_cut_class(hand, tile_id, cut_index, draw_slot=has_draw_slot(player))
    return tile_id, cut_index, is_moqie


async def _submit_pass_when_ready(game_state, player_index: int, action_list: list, current_player) -> bool:
    """鸣牌/抢杠询问：等 wait_action 建立 waiting_players_list 后立即 pass。"""
    if "pass" not in action_list:
        return False
    for _ in range(200):
        if player_index in getattr(game_state, "waiting_players_list", []):
            logger.info(f"掉线托管 {player_index} ({current_player.username}) 选择 pass")
            await get_ai_action(game_state, player_index, "pass", None, None, None, None)
            return True
        await asyncio.sleep(0.01)
    logger.warning(
        f"掉线托管失败：玩家 {player_index} ({current_player.username}) 未进入 waiting_players_list"
    )
    return False


async def _auto_dingque_on_disconnect(game_state, player_index: int) -> None:
    """四川定缺阶段掉线：自动选数量最少的花色。"""
    try:
        player = game_state.player_list[player_index]
        counts = {1: 0, 2: 0, 3: 0}
        for t in player.hand_tiles:
            counts[t // 10] = counts.get(t // 10, 0) + 1
        suit = min(counts, key=lambda s: counts[s])
        await game_state.action_queues[player_index].put({"action_type": "dingque", "target_tile": suit})
        game_state.action_events[player_index].set()
        logger.info(f"掉线托管 {player_index} ({player.username}) 自动定缺 suit={suit}")
    except Exception as e:
        logger.error(f"掉线托管 {player_index} 自动定缺失败: {e}", exc_info=True)


def schedule_offline_auto_on_disconnect(game_state, user_id: int) -> None:
    """
    玩家刚掉线时：若当前巡仍有待操作，立即派发托管，不必等下次 broadcast / 超时。
    全员掉线即将销毁对局时不派发。
    """
    player = next((p for p in game_state.player_list if p.user_id == user_id), None)
    if player is None:
        return

    non_ai = [p for p in game_state.player_list if p.user_id >= 10]
    if non_ai and all("offline" in p.tag_list for p in non_ai):
        return

    player_index = player.player_index
    action_list = list((getattr(game_state, "action_dict", None) or {}).get(player_index) or [])
    if not action_list:
        return

    if getattr(game_state, "game_status", None) == "waiting_dingque" and "dingque" in action_list:
        asyncio.create_task(_auto_dingque_on_disconnect(game_state, player_index))
        return

    status = bot_ask_hand_game_status(game_state, player_index)
    logger.info(
        f"掉线即时托管：user_id={user_id}, player_index={player_index}, "
        f"status={status}, actions={action_list}"
    )

    # 长沙沿用摸切机器人逻辑（含起手胡/海底等状态）
    if getattr(game_state, "room_rule", None) == "changsha":
        from ..ai.auto_cut_ai import auto_cut_action

        asyncio.create_task(auto_cut_action(game_state, player_index, action_list, status))
        return

    asyncio.create_task(offline_auto_action(game_state, player_index, action_list, status))


async def offline_auto_action(game_state, player_index: int, action_list: list, game_status: str):
    """
    掉线玩家托管：鸣牌/抢杠/补花轮一律 pass，行牌只摸切，不自动补花/和/杠。
    """
    try:
        current_player = game_state.player_list[player_index]

        if game_status in _PASS_WAIT_STATUSES:
            await _submit_pass_when_ready(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_buhua_round":
            await asyncio.sleep(_OFFLINE_DELAY)
            if "pass" in action_list:
                logger.info(f"掉线托管 {player_index} ({current_player.username}) 选择 pass（补花轮）")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
            return

        if game_status == "waiting_hand_action":
            await asyncio.sleep(_OFFLINE_DELAY)
            if "cut" in action_list and current_player.hand_tiles:
                tile_id, cut_index, is_moqie = _pick_offline_cut_tile(current_player)
                logger.info(
                    f"掉线托管 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}, moqie={is_moqie}"
                )
                await get_ai_action(game_state, player_index, "cut", is_moqie, tile_id, cut_index, None)
            return

        if game_status == "onlycut_after_action":
            cp = bool(getattr(game_state, "claim_protection", False))
            from ..claim_protection import get_meld_post_gap
            await asyncio.sleep(_OFFLINE_DELAY + (get_meld_post_gap(game_state) if cp else 0.0))
            if "cut" in action_list and current_player.hand_tiles:
                tile_id, cut_index, is_moqie = _pick_offline_cut_tile(current_player)
                logger.info(
                    f"掉线托管 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}, moqie={is_moqie}"
                )
                await get_ai_action(game_state, player_index, "cut", is_moqie, tile_id, cut_index, None)
            return

        logger.warning(f"掉线托管 {player_index} 遇到未知游戏状态: {game_status}")

    except Exception as e:
        logger.error(f"掉线托管 {player_index} 自动操作失败: {e}", exc_info=True)
