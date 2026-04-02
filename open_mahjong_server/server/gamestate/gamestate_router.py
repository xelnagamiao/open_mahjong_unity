# 游戏状态路由处理器
import logging
from .public.ai.get_action import get_action
from ..response import Response, SpectatorInfo

logger = logging.getLogger(__name__)

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
    elif message_type == "gamestate/GB/add_spectator":
        await handle_add_spectator(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/GB/remove_spectator":
        await handle_remove_spectator(game_server, Connect_id, message, websocket)
    elif message_type == "gamestate/get_spectator_list":
        await handle_get_spectator_list(game_server, Connect_id, message, websocket)
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
            target_tile=message.get("targetTile")
        )
    except Exception as e:
        logger.error(f"处理发送操作请求失败: {e}", exc_info=True)

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

