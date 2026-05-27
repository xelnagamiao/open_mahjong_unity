# AutoCut机器人（摸切罗伯特）
# 最简单的机器人AI：摸牌后切最后一张（摸切），其他操作全部pass
import asyncio
import logging
from .get_action import get_ai_action

logger = logging.getLogger(__name__)

_PASS_WAIT_STATUSES = ("waiting_action_after_cut", "waiting_action_qianggang")


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
        # 通过传入的玩家索引获得玩家对象的数据
        current_player = game_state.player_list[player_index]

        if game_status in _PASS_WAIT_STATUSES:
            await _submit_pass_when_ready(game_state, player_index, action_list, current_player)
            return

        delay = 0.5
        await asyncio.sleep(delay)
        
        if game_status == "waiting_hand_action":
            # 有花牌必须先补花
            if "buhua" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 buhua（手牌补花）")
                await get_ai_action(game_state, player_index, "buhua", None, None, None, None)
                return
            # 手牌操作：选择cut（切牌）
            if "cut" in action_list:
                # 选择最后一张手牌（摸切）
                if current_player.hand_tiles:
                    tile_id = current_player.hand_tiles[-1]
                    cut_index = len(current_player.hand_tiles) - 1
                    logger.info(f"机器人 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}")
                    await get_ai_action(game_state, player_index, "cut", True, tile_id, cut_index, None)
                    return
            if "pass" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass（手牌阶段无cut）")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return

        elif game_status == "onlycut_after_action":
            # 转移行为后切牌：选择cut（切牌）
            if "cut" in action_list:
                # 选择最后一张手牌（摸切）
                if current_player.hand_tiles:
                    tile_id = current_player.hand_tiles[-1]
                    cut_index = len(current_player.hand_tiles) - 1
                    logger.info(f"机器人 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}")
                    await get_ai_action(game_state, player_index, "cut", True, tile_id, cut_index, None)
                    return

        elif game_status == "waiting_buhua_round":
            # 询问补花操作：选择pass（补花轮机器人不做特殊处理）
            if "pass" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return
        else:
            logger.warning(f"机器人 {player_index} 遇到未知游戏状态: {game_status}")
            
    except Exception as e:
        logger.error(f"机器人 {player_index} 自动操作失败: {e}", exc_info=True)
