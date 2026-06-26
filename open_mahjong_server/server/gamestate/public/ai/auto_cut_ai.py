# AutoCut机器人（摸切罗伯特）
# 最简单的机器人AI：摸牌后切最后一张（摸切），其他操作全部pass
import asyncio
import logging
from ..hand_slot_utils import has_draw_slot, infer_bot_cut_class
from .get_action import get_ai_action
from .smart_bot_logic import first_dingque_tile

logger = logging.getLogger(__name__)

_PASS_WAIT_STATUSES = ("waiting_action_after_cut", "waiting_action_qianggang")
_BOT_DELAY = 0.5


def _pick_auto_cut_tile(player):
    """摸切优先；手牌仍含定缺花色时先切第一张定缺牌（与服务端 _enforce_dingque_first 一致）。"""
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
    """鸣牌/抢杠询问：等 wait_action 建立 waiting_players_list 后立即 pass（0 秒，与摸切机器人一致）。"""
    if "pass" not in action_list:
        return False
    for _ in range(200):
        if player_index in getattr(game_state, "waiting_players_list", []):
            logger.info(f"自动过牌 {player_index} ({current_player.username}) 选择 pass")
            await get_ai_action(game_state, player_index, "pass", None, None, None, None)
            return True
        await asyncio.sleep(0.01)
    logger.warning(
        f"自动过牌失败：玩家 {player_index} ({current_player.username}) 未进入 waiting_players_list"
    )
    return False


# 自动切牌机器人，支持补花询问，手牌询问，其他玩家询问，抢杠询问
async def auto_cut_action(game_state, player_index: int, action_list: list, game_status: str):
    """
    机器人自动操作
    规则：如果有pass选pass，没pass选cut（对于手牌操作），否则不操作
    
    Args:
        game_state: 游戏状态对象
        player_index: 玩家索引
        action_list: 可用操作列表
        game_status: 游戏状态
    """
    try:
        current_player = game_state.player_list[player_index]

        if game_status in _PASS_WAIT_STATUSES:
            await _submit_pass_when_ready(game_state, player_index, action_list, current_player)
            return

        if game_status == "waiting_hand_action":
            await asyncio.sleep(_BOT_DELAY)
            if "buhua" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 buhua（手牌补花）")
                await get_ai_action(game_state, player_index, "buhua", None, None, None, None)
                return
            if "cut" in action_list and current_player.hand_tiles:
                tile_id, cut_index, is_moqie = _pick_auto_cut_tile(current_player)
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}, moqie={is_moqie}")
                await get_ai_action(game_state, player_index, "cut", is_moqie, tile_id, cut_index, None)
                return
            if "pass" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass（手牌阶段无cut）")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return

        elif game_status == "onlycut_after_action":
            cp = bool(getattr(game_state, "claim_protection", False))
            await asyncio.sleep(_BOT_DELAY * (2 if cp else 1))
            if "cut" in action_list and current_player.hand_tiles:
                tile_id, cut_index, is_moqie = _pick_auto_cut_tile(current_player)
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}, moqie={is_moqie}")
                await get_ai_action(game_state, player_index, "cut", is_moqie, tile_id, cut_index, None)
                return

        elif game_status == "waiting_buhua_round":
            await asyncio.sleep(_BOT_DELAY)
            if "pass" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return
        else:
            logger.warning(f"机器人 {player_index} 遇到未知游戏状态: {game_status}")
            
    except Exception as e:
        logger.error(f"机器人 {player_index} 自动操作失败: {e}", exc_info=True)
