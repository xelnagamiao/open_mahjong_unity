# 房间路由处理器
import logging
from ..response import Response

logger = logging.getLogger(__name__)

async def handle_room_message(game_server, Connect_id: str, message: dict, websocket):
    """
    处理房间相关的消息（根据 type 字段的完整路径分发）
    
    Args:
        game_server: 游戏服务器实例
        Connect_id: 连接ID
        message: 消息字典（type 字段应为 "room/xxx" 格式）
        websocket: WebSocket连接
    """
    message_type = message.get("type", "").strip("/")
    
    # 根据完整路径分发
    if message_type == "room/create_GB_room":
        await handle_create_GB_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/get_room_list":
        await handle_get_room_list(game_server, Connect_id, message, websocket)
    elif message_type == "room/join_room":
        await handle_join_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/leave_room":
        await handle_leave_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/start_game":
        await handle_start_game(game_server, Connect_id, message, websocket)
    elif message_type == "room/add_bot":
        await handle_add_bot_to_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/kick_player":
        await handle_kick_player_from_room(game_server, Connect_id, message, websocket)
    else:
        logger.warning(f"未知的房间消息路径: {message_type}")

async def handle_create_GB_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建国标房间请求"""
    logging.info(f"创建房间请求 - 用户名: {Connect_id}")
    # 检查玩家是否已经在房间中
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        if player.current_room_id:
            response = Response(
                type="tips",
                success=False,
                message="已经处于一个房间中，请先退出房间再创建新房间"
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return
    
    response = await game_server.create_GB_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message["random_seed"],
        message["open_cuohe"]
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_room_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取房间列表请求"""
    response = game_server.get_room_list()
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_join_room(game_server, Connect_id: str, message: dict, websocket):
    """处理加入房间请求"""
    await game_server.join_room(Connect_id, message["room_id"], message["password"])

async def handle_leave_room(game_server, Connect_id: str, message: dict, websocket):
    """处理离开房间请求"""
    await game_server.leave_room(Connect_id, message["room_id"])

async def handle_start_game(game_server, Connect_id: str, message: dict, websocket):
    """处理开始游戏请求"""
    await game_server.start_game(Connect_id, message["room_id"])

async def handle_add_bot_to_room(game_server, Connect_id: str, message: dict, websocket):
    """处理添加机器人到房间请求"""
    await game_server.add_bot_to_room(Connect_id, message["room_id"])

async def handle_kick_player_from_room(game_server, Connect_id: str, message: dict, websocket):
    """处理房主移除玩家请求"""
    await game_server.kick_player_from_room(Connect_id, message["room_id"], message["target_user_id"])

