# 玩家操作处理
import logging

from ..hand_slot_utils import hand_contains_tile, has_draw_slot, infer_bot_cut_class

logger = logging.getLogger(__name__)

def _normalize_tile(tile: int) -> int:
    if tile == 105:
        return 15
    if tile == 205:
        return 25
    if tile == 305:
        return 35
    return tile

def _count_normalized(hand_tiles: list, normal_tile: int) -> int:
    return sum(1 for t in hand_tiles if _normalize_tile(t) == normal_tile)

def _is_kuikae_forbidden_cut(player, tile_id: int) -> bool:
    return _normalize_tile(tile_id) in {
        _normalize_tile(t) for t in (getattr(player, "kuikae_forbidden_tiles", None) or [])
    }

def _infer_jiagang_target(player):
    """从玩家已有碰牌与手牌推断可加杠目标，兼容赤 5 与客户端 targetTile 缺失。"""
    for combo in getattr(player, "combination_tiles", []) or []:
        if not combo or combo[0] != "k":
            continue
        try:
            normal_tile = int(combo[1:])
        except ValueError:
            continue
        if _count_normalized(player.hand_tiles, normal_tile) > 0:
            return normal_tile
    return None

def _resolve_jiagang_target(player, target_tile):
    normal_target = _normalize_tile(target_tile) if target_tile is not None else None
    if normal_target is not None and f"k{normal_target}" in player.combination_tiles and _count_normalized(player.hand_tiles, normal_target) > 0:
        return normal_target
    return _infer_jiagang_target(player)

# 获取机器人AI行动（直接使用玩家索引，只检测逻辑合法性）
async def get_ai_action(game_state, player_index: int, action_type: str, cutClass: bool, TileId: int, cutIndex: int, target_tile: int, chi_combo_index: int = 0):
    """
    机器人AI操作处理
    直接使用玩家索引，只检测逻辑相关的合法性
    
    Args:
        game_state: 游戏状态对象
        player_index: 玩家索引（0-3）
        action_type: 操作类型
        cutClass: 切牌类型（仅用于cut操作）
        TileId: 切牌ID（仅用于cut操作）
        cutIndex: 切牌索引（仅用于cut操作）
        target_tile: 目标牌（用于angang、jiagang等操作）
        chi_combo_index: 立直麻将涉赤 5 时吃牌候选索引（默认 0 = 优先非赤 5）
    """
    try:
        # 验证玩家索引是否有效
        if player_index not in [0, 1, 2, 3]:
            logger.error(f"无效的玩家索引: {player_index}")
            return
        
        # 获取玩家对象
        current_player = game_state.player_list[player_index]
        
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
        if action_type in ("cut", "riichi_cut"): # 切牌/立直切
            # 验证切牌的TileId是否在玩家手牌中
            if not hand_contains_tile(current_player.hand_tiles, TileId):
                logger.warning(f"错误：切牌操作的TileId不在玩家手牌中，player_index={player_index}, TileId={TileId}, hand_tiles={current_player.hand_tiles}")
                return  # 丢弃命令
            if _is_kuikae_forbidden_cut(current_player, TileId):
                logger.warning(f"食替禁切：丢弃机器人非法切牌, player_index={player_index}, TileId={TileId}, forbidden={current_player.kuikae_forbidden_tiles}")
                return

            cut_class = infer_bot_cut_class(
                current_player.hand_tiles,
                TileId,
                cutIndex,
                draw_slot=has_draw_slot(current_player),
            )
            action_data_to_queue = {
                "action_type": action_type,
                "cutClass": cut_class,
                "TileId": TileId,
                "cutIndex": cutIndex
            }
            logger.info(f"机器人放入队列: player_index={player_index}, action_data={action_data_to_queue}")
            await game_state.action_queues[player_index].put(action_data_to_queue)
            # 设置事件
            game_state.action_events[player_index].set()
        else: # 其他指令操作（buhua, angang, jiagang, hu_self, chi_left, chi_mid, chi_right, peng, gang, hu_first, hu_second, hu_third, pass）
            # 验证特殊操作的条件
            if action_type == "jiagang":
                # 加杠验证：要求组合牌中有碰牌形成的刻子
                target_tile = _resolve_jiagang_target(current_player, target_tile)
                if target_tile is None:
                    logger.warning(f"加杠失败：玩家没有可加杠的刻子, player_index={player_index}, target_tile={target_tile}, combination_tiles={current_player.combination_tiles}, hand_tiles={current_player.hand_tiles}")
                    return  # 丢弃命令
            elif action_type == "angang":
                # 暗杠验证：要求目标牌在自己手上有4张
                target_tile = _normalize_tile(target_tile)
                tile_count = _count_normalized(current_player.hand_tiles, target_tile)
                if tile_count < 4:
                    logger.warning(f"暗杠失败：手牌中没有足够的牌进行暗杠, player_index={player_index}, target_tile={target_tile}, count={tile_count}, hand_tiles={current_player.hand_tiles}")
                    return  # 丢弃命令
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "target_tile": target_tile,
                "chi_combo_index": chi_combo_index,
            })
            # 设置事件
            game_state.action_events[player_index].set()
        
    except Exception as e:
        logger.error(f"处理机器人操作时发生错误: {e}", exc_info=True)
        raise Exception(f"处理机器人操作时发生错误: {e}") # 出现问题时中断游戏

# 获取玩家行动
async def get_action(game_state, player_id: str, action_type: str, cutClass: bool, TileId: int, cutIndex: int, target_tile: int, chi_combo_index: int = 0):
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
        if action_type in ("cut", "riichi_cut"): # 切牌/立直切
            # 验证切牌的TileId是否在玩家手牌中
            if not hand_contains_tile(current_player.hand_tiles, TileId):
                logger.warning(f"错误：切牌操作的TileId不在玩家手牌中，player_index={player_index}, user_id={user_id}, TileId={TileId}, hand_tiles={current_player.hand_tiles}")
                return  # 丢弃命令
            if _is_kuikae_forbidden_cut(current_player, TileId):
                logger.warning(f"食替禁切：丢弃玩家非法切牌, player_index={player_index}, user_id={user_id}, TileId={TileId}, forbidden={current_player.kuikae_forbidden_tiles}")
                return
            
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
        elif action_type == "ready":  # 准备操作
            # 检查是否在等待准备状态
            if game_state.game_status != "waiting_ready":
                logger.warning(f"当前不在等待准备状态，无法准备，game_status={game_state.game_status}")
                return
            
            action_data_to_queue = {
                "action_type": "ready"
            }
            logger.info(f"放入队列: player_index={player_index}, action_data={action_data_to_queue}")
            await game_state.action_queues[player_index].put(action_data_to_queue)
            # 设置事件
            game_state.action_events[player_index].set()
        else: # 其他指令操作（buhua, angang, jiagang, hu_self, chi_left, chi_mid, chi_right, peng, gang, hu_first, hu_second, hu_third, pass）
            # 验证特殊操作的条件
            if action_type == "jiagang":
                # 加杠验证：要求组合牌中有碰牌形成的刻子
                target_tile = _resolve_jiagang_target(current_player, target_tile)
                if target_tile is None:
                    logger.warning(f"加杠失败：玩家没有可加杠的刻子, player_index={player_index}, user_id={user_id}, target_tile={target_tile}, combination_tiles={current_player.combination_tiles}, hand_tiles={current_player.hand_tiles}")
                    return  # 丢弃命令
            elif action_type == "angang":
                # 暗杠验证：要求目标牌在自己手上有4张
                target_tile = _normalize_tile(target_tile)
                tile_count = _count_normalized(current_player.hand_tiles, target_tile)
                if tile_count < 4:
                    logger.warning(f"暗杠失败：手牌中没有足够的牌进行暗杠, player_index={player_index}, user_id={user_id}, target_tile={target_tile}, count={tile_count}, hand_tiles={current_player.hand_tiles}")
                    return  # 丢弃命令
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "target_tile": target_tile,
                "chi_combo_index": chi_combo_index,
            })
            # 设置事件
            game_state.action_events[player_index].set()
        
    except Exception as e:
        logger.error(f"处理操作时发生错误: {e}", exc_info=True)
        raise Exception(f"处理操作时发生错误: {e}") # 出现问题时中断游戏
