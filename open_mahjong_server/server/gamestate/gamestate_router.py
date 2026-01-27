# 游戏状态路由处理器
import logging
from .game_guobiao.get_action import get_action

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

