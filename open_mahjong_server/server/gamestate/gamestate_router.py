# 游戏状态路由处理器
import logging
import re
import time
from .public.ai.get_action import get_action
from .public.sticker import broadcast_sticker
from ..response import Response, SpectatorInfo

logger = logging.getLogger(__name__)

STICKER_PATTERN = re.compile(r"^[a-zA-Z0-9_]+/[1-9]$")
STICKER_SEND_INTERVAL_SECONDS = 5.0

async def handle_gamestate_message(game_server, Connect_id: str, message: dict, websocket):
    """
    处理游戏状态相关的消息（根据 type 字段的完整路径分发）
    
    Args:
        game_server: 游戏服务器实例
        Connect_id: 连接ID
        message: 消息字典（type 字段应为 "gamestate/GB/xxx" 格式）
        websocket: WebSocket连接
    """
    message_type = message.get("type", "").strip("/")
    
    # 根据完整路径分发
    if message_type == "gamestate/GB/cut_tile":
        await handle_cut_tile(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/GB/send_action":
        await handle_send_action(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/riichi/cut_tile":
        await handle_cut_tile(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/riichi/riichi_cut":
        await handle_riichi_cut(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/riichi/send_action":
        await handle_send_action(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/riichi/set_ryuukyoku_tenpai":
        await handle_set_ryuukyoku_tenpai(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/GB/add_spectator":
        await handle_add_spectator(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/GB/remove_spectator":
        await handle_remove_spectator(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/get_spectator_list":
        await handle_get_spectator_list(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/send_sticker":
        await handle_send_sticker(game_server, Connect_id, message, websocket)
    else:
        logger.warning(f"未知的游戏状态消息路径: {message_type}")

async def handle_cut_tile(game_server, Connect_id: str, message: dict, websocket):
    """处理切牌请求"""
    try:
        gamestate_id = message.get("gamestate_id")
        if not gamestate_id:
            logger.warning(f"切牌请求缺少 gamestate_id: {message}")
            return
        
        guobiao_game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if not guobiao_game_state:
            logger.warning(f"gamestate_id {gamestate_id} 的游戏状态不存在")
            return
        await get_action(
            guobiao_game_state, 
            Connect_id, 
            "cut", 
            message.get("cutClass"), 
            message.get("TileId"), 
            cutIndex=message.get("cutIndex"),
            target_tile=None
        )
    except Exception as e:
        logger.error(f"处理切牌请求失败: {e}", exc_info=True)

async def handle_riichi_cut(game_server, Connect_id: str, message: dict, websocket):
    """处理立直切牌请求"""
    try:
        gamestate_id = message.get("gamestate_id")
        if not gamestate_id:
            return
        game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if not game_state:
            return
        await get_action(
            game_state,
            Connect_id,
            "riichi_cut",
            message.get("cutClass"),
            message.get("TileId"),
            cutIndex=message.get("cutIndex"),
            target_tile=None,
        )
    except Exception as e:
        logger.error(f"处理立直切牌请求失败: {e}", exc_info=True)


async def handle_send_action(game_server, Connect_id: str, message: dict, websocket):
    """处理发送操作请求"""
    try:
        gamestate_id = message.get("gamestate_id")
        if not gamestate_id:
            logger.warning(f"发送操作请求缺少 gamestate_id: {message}")
            return
        
        guobiao_game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if not guobiao_game_state:
            logger.warning(f"gamestate_id {gamestate_id} 的游戏状态不存在")
            return
        await get_action(
            guobiao_game_state, 
            Connect_id, 
            message.get("action"), 
            cutClass=None, 
            TileId=None, 
            cutIndex=None,
            target_tile=message.get("targetTile"),
            chi_combo_index=message.get("chiComboIndex", 0) or 0,
        )
    except Exception as e:
        logger.error(f"处理发送操作请求失败: {e}", exc_info=True)

async def handle_send_sticker(game_server, Connect_id: str, message: dict, websocket):
    """处理对局表情包发送请求（仅对局玩家可发）。"""
    try:
        gamestate_id = message.get("gamestate_id")
        sticker = (message.get("sticker") or "").strip()
        if not gamestate_id or not sticker:
            logger.warning(f"表情包请求缺少参数: {message}")
            return
        if not STICKER_PATTERN.match(sticker):
            logger.warning(f"表情包格式非法: {sticker}")
            return

        game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if not game_state:
            logger.warning(f"gamestate_id {gamestate_id} 的游戏状态不存在")
            return

        if Connect_id not in game_server.players:
            logger.warning(f"连接 {Connect_id} 不存在")
            return
        player_connection = game_server.players[Connect_id]
        user_id = player_connection.user_id
        if not user_id:
            logger.warning(f"连接 {Connect_id} 未登录")
            return

        sender_index = None
        for player in game_state.player_list:
            if player.user_id == user_id:
                sender_index = player.player_index
                break
        if sender_index is None:
            logger.warning(f"用户 {user_id} 不是对局玩家，拒绝发送表情包")
            return

        last_send_map = getattr(game_state, "sticker_last_send_mono", None)
        if last_send_map is None:
            last_send_map = {}
            game_state.sticker_last_send_mono = last_send_map
        now = time.monotonic()
        last_send = last_send_map.get(user_id, 0.0)
        if now - last_send < STICKER_SEND_INTERVAL_SECONDS:
            logger.info(f"用户 {user_id} 表情包发送过于频繁，间隔需 {STICKER_SEND_INTERVAL_SECONDS}s")
            return
        last_send_map[user_id] = now

        await broadcast_sticker(game_state, sender_index, sticker)
        logger.info(f"用户 {user_id} 发送表情包: {sticker}, player_index={sender_index}")
    except Exception as e:
        logger.error(f"处理表情包请求失败: {e}", exc_info=True)

async def handle_set_ryuukyoku_tenpai(game_server, Connect_id: str, message: dict, websocket):
    """设置立直麻将荒牌流局时的听牌申报。"""
    try:
        gamestate_id = message.get("gamestate_id")
        game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        player_conn = game_server.players.get(Connect_id)
        if not game_state or not player_conn or not player_conn.user_id:
            return
        for player in game_state.player_list:
            if player.user_id == player_conn.user_id:
                player.ryuukyoku_declared_tenpai = bool(message.get("tenpai", True))
                logger.info(
                    f"设置荒牌听牌申报: gamestate_id={gamestate_id}, "
                    f"user_id={player.user_id}, tenpai={player.ryuukyoku_declared_tenpai}"
                )
                return
    except Exception as e:
        logger.error(f"处理荒牌听牌申报失败: {e}", exc_info=True)

async def handle_add_spectator(game_server, Connect_id: str, message: dict, websocket):
    """处理添加观战玩家请求"""
    try:
        gamestate_id = message.get("gamestate_id")
        if not gamestate_id:
            logger.warning(f"添加观战请求缺少 gamestate_id: {message}")
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message="缺少 gamestate_id"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 获取游戏状态
        guobiao_game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if not guobiao_game_state:
            logger.warning(f"gamestate_id {gamestate_id} 的游戏状态不存在")
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message="游戏状态不存在"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 获取用户ID
        if Connect_id not in game_server.players:
            logger.warning(f"连接 {Connect_id} 不存在")
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message="连接不存在"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        player_connection = game_server.players[Connect_id]
        user_id = player_connection.user_id
        
        if not user_id:
            logger.warning(f"连接 {Connect_id} 未登录")
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message="请先登录"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return

        if game_server.match_manager.blocks_spectator(user_id):
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message="正在匹配队列中，请先取消匹配再进入观战"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 检查是否已经是玩家
        is_player = any(p.user_id == user_id for p in guobiao_game_state.player_list)
        if is_player:
            logger.warning(f"用户 {user_id} 已经是游戏玩家，不能观战")
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message="您已经是游戏玩家，不能观战"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 添加观战玩家
        await guobiao_game_state.add_spectator(user_id, player_connection)
        
        logger.info(f"用户 {user_id} 已添加为观战玩家，gamestate_id: {gamestate_id}")

        
    except Exception as e:
        logger.error(f"处理添加观战请求失败: {e}", exc_info=True)
        response = Response(
            type="spectator/add_spectator",
            success=False,
            message=f"添加观战失败: {str(e)}"
        )
        try:
            await websocket.send_json(response.dict(exclude_none=True))
        except:
            pass

async def handle_remove_spectator(game_server, Connect_id: str, message: dict, websocket):
    """处理移除观战玩家请求"""
    try:
        gamestate_id = message.get("gamestate_id")
        if not gamestate_id:
            logger.warning(f"移除观战请求缺少 gamestate_id: {message}")
            response = Response(
                type="spectator/remove_spectator",
                success=False,
                message="缺少 gamestate_id"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 获取游戏状态
        guobiao_game_state = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if not guobiao_game_state:
            logger.warning(f"gamestate_id {gamestate_id} 的游戏状态不存在")
            response = Response(
                type="spectator/remove_spectator",
                success=False,
                message="游戏状态不存在"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 获取用户ID
        if Connect_id not in game_server.players:
            logger.warning(f"连接 {Connect_id} 不存在")
            response = Response(
                type="spectator/remove_spectator",
                success=False,
                message="连接不存在"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        player_connection = game_server.players[Connect_id]
        user_id = player_connection.user_id
        
        if not user_id:
            logger.warning(f"连接 {Connect_id} 未登录")
            response = Response(
                type="spectator/remove_spectator",
                success=False,
                message="请先登录"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
        
        # 移除观战玩家
        await guobiao_game_state.remove_spectator(user_id)
        
        logger.info(f"用户 {user_id} 已移除观战，gamestate_id: {gamestate_id}")
        response = Response(
            type="spectator/remove_spectator",
            success=True,
            message="已成功退出观战"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        
    except Exception as e:
        logger.error(f"处理移除观战请求失败: {e}", exc_info=True)
        response = Response(
            type="spectator/remove_spectator",
            success=False,
            message=f"移除观战失败: {str(e)}"
        )
        try:
            await websocket.send_json(response.dict(exclude_none=True))
        except:
            pass

async def handle_get_spectator_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取观战列表请求"""
    try:
        spectator_list = game_server.gamestate_manager.get_spectator_list()
        
        response = Response(
            type="gamestate/get_spectator_list",
            success=True,
            message=f"获取到 {len(spectator_list)} 个可观战游戏",
            spectator_list=spectator_list
        )
        await websocket.send_json(response.dict(exclude_none=True))
    except Exception as e:
        logger.error(f"处理获取观战列表请求失败: {e}", exc_info=True)
        response = Response(
            type="gamestate/get_spectator_list",
            success=False,
            message=f"获取观战列表失败: {str(e)}"
        )
        try:
            await websocket.send_json(response.dict(exclude_none=True))
        except:
            pass

