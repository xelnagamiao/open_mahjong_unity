# 国标麻将机器人AI
import asyncio
import logging
from .get_action import get_ai_action

logger = logging.getLogger(__name__)

# 游戏服务器，玩家索引，可选行动列表，游戏状态
async def bot_auto_action(game_state, player_index: int, action_list: list, game_status: str):
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
        # 等待0.1秒
        await asyncio.sleep(0.1)
        # 通过传入的玩家索引获得玩家对象的数据
        current_player = game_state.player_list[player_index]
        
        if game_status == "waiting_hand_action":
            # 手牌操作：选择cut（切牌）
            if "cut" in action_list:
                # 选择最后一张手牌（摸切）
                if current_player.hand_tiles:
                    tile_id = current_player.hand_tiles[-1]
                    cut_index = len(current_player.hand_tiles) - 1
                    logger.info(f"机器人 {player_index} ({current_player.username}) 选择 cut, tile_id={tile_id}")
                    await get_ai_action(game_state, player_index, "cut", True, tile_id, cut_index, None)
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

        elif game_status == "waiting_action_after_cut":
            # 询问切牌后操作：选择pass
            if "pass" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return
            
        elif game_status == "waiting_action_qianggang":
            # 询问抢杠操作：选择pass
            if "pass" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return

        elif game_status == "waiting_buhua_round":
            # 询问补花操作：选择补花
            if "buhua" in action_list:
                logger.info(f"机器人 {player_index} ({current_player.username}) 选择 pass")
                await get_ai_action(game_state, player_index, "pass", None, None, None, None)
                return
        else:
            logger.warning(f"机器人 {player_index} 遇到未知游戏状态: {game_status}")
            
    except Exception as e:
        logger.error(f"机器人 {player_index} 自动操作失败: {e}", exc_info=True)

