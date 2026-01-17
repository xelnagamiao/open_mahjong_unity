# 玩家操作处理
import logging
from .game_state import ChineseGameState

logger = logging.getLogger(__name__)

# 获取玩家行动
async def get_action(game_state: ChineseGameState, player_id: str, action_type: str, cutClass: bool, TileId: int, cutIndex: int, target_tile: int):
    try:
        # 检测行动合法性
        # 从游戏服务器的PlayerConnection中获取user_id
        player_conn = game_state.game_server.players.get(player_id)
        if not player_conn:
            logger.warning(f"玩家连接不存在: {player_id}")
            return
        if not player_conn.user_id:
            logger.warning(f"玩家未登录，无法执行操作: {player_id}")
            return
        user_id = player_conn.user_id
        
        # 查找对应的玩家和索引
        current_player = None
        player_index = -1
        # 通过比对user_id获取玩家索引
        for index, player in enumerate(game_state.player_list):
            if player.user_id == user_id:
                current_player = player
                player_index = index
                break
        
        # 验证玩家是否在当前房间的玩家列表中
        if current_player is None:
            logger.warning(f"当前玩家不存在当前房间玩家列表中, user_id={user_id}, 可能是玩家操作发送到了错误的房间")
            return
        
        # 验证玩家索引是否有效
        if player_index not in [0, 1, 2, 3]:
            logger.error(f"无效的玩家索引: {player_index}")
            return
        
        # 验证玩家是否在等待列表中（只有等待中的玩家才能执行操作）
        if player_index not in game_state.waiting_players_list:
            logger.warning(f"不是当前玩家的回合, player_index={player_index}, waiting_players_list={game_state.waiting_players_list}")
            return

        # 在 waiting_hand_action 状态下，只允许当前玩家操作
        if game_state.game_status == "waiting_hand_action" and player_index != game_state.current_player_index:
            logger.warning(f"waiting_hand_action 状态下只允许当前玩家操作, current_player_index={game_state.current_player_index}, player_index={player_index}")
            return
        
        # 验证操作是否合法（检查操作是否在允许的操作列表中）
        if action_type not in game_state.action_dict.get(player_index, []):
            logger.warning(f"不是该玩家的合法行动, player_index={player_index}, action_type={action_type}, allowed_actions={game_state.action_dict.get(player_index, [])}")
            return

        # 操作合法，将操作数据放入队列
        if action_type == "cut": # 切牌操作
            # 验证切牌的TileId是否在玩家手牌中
            if TileId not in current_player.hand_tiles:
                logger.warning(f"错误：切牌操作的TileId不在玩家手牌中，player_index={player_index}, user_id={user_id}, TileId={TileId}, hand_tiles={current_player.hand_tiles}")
                return  # 丢弃命令
            
            action_data_to_queue = {
                "action_type": action_type,
                "cutClass": cutClass,
                "TileId": TileId,
                "cutIndex": cutIndex
            }
            logger.info(f"放入队列: player_index={player_index}, action_data={action_data_to_queue}")
            await game_state.action_queues[player_index].put(action_data_to_queue)
            # 设置事件
            game_state.action_events[player_index].set()
        else: # 其他指令操作（buhua, angang, jiagang, hu_self, chi_left, chi_mid, chi_right, peng, gang, hu_first, hu_second, hu_third, pass）
            # 验证特殊操作的条件
            if action_type == "jiagang":
                # 加杠验证：要求组合牌中有碰牌形成的刻子
                if f"k{target_tile}" not in current_player.combination_tiles:
                    logger.warning(f"加杠失败：玩家没有碰牌形成的刻子, player_index={player_index}, user_id={user_id}, target_tile={target_tile}, combination_tiles={current_player.combination_tiles}")
                    return  # 丢弃命令
            elif action_type == "angang":
                # 暗杠验证：要求目标牌在自己手上有4张
                tile_count = current_player.hand_tiles.count(target_tile)
                if tile_count < 4:
                    logger.warning(f"暗杠失败：手牌中没有足够的牌进行暗杠, player_index={player_index}, user_id={user_id}, target_tile={target_tile}, count={tile_count}, hand_tiles={current_player.hand_tiles}")
                    return  # 丢弃命令
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "target_tile": target_tile
            })
            # 设置事件
            game_state.action_events[player_index].set()
        
    except Exception as e:
        logger.error(f"处理操作时发生错误: {e}", exc_info=True)
        raise Exception(f"处理操作时发生错误: {e}") # 出现问题时中断游戏

