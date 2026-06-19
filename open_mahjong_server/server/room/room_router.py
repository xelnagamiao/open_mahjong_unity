# 房间路由处理器
import logging
from typing import Optional
from ..response import Response

logger = logging.getLogger(__name__)

def _reject_room_entry(game_server, player) -> Optional[Response]:
    """创建/加入房间前的统一拦截：已在房间，或仍在进行中的对局内。"""
    if player.current_room_id:
        return Response(
            type="tips",
            success=False,
            message="已经处于一个房间中，请先退出房间再创建新房间",
        )
    if player.user_id and game_server.gamestate_manager.is_user_in_active_game(player.user_id):
        return Response(
            type="tips",
            success=False,
            message="您正在对局中，无法进入或创建房间",
        )
    # 已匹配成功但对局尚未结束（含开局前 5 秒空窗）的玩家，同样禁止创建/加入房间
    if (
        player.user_id
        and getattr(game_server, "match_manager", None)
        and game_server.match_manager.is_user_committed(player.user_id)
    ):
        return Response(
            type="tips",
            success=False,
            message="您已匹配到对局，请完成当前对局后再进入或创建房间",
        )
    # 正在匹配等待队列中的玩家，禁止创建/加入房间
    if (
        player.user_id
        and getattr(game_server, "match_manager", None)
        and game_server.match_manager.is_user_in_queue(player.user_id)
    ):
        return Response(
            type="tips",
            success=False,
            message="您正在匹配队列中，请先取消匹配再进入或创建房间",
        )
    return None

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
    elif message_type == "room/create_Qingque_room":
        await handle_create_Qingque_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Classical_room":
        await handle_create_Classical_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Sichuan_room":
        await handle_create_Sichuan_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Riichi_room":
        await handle_create_Riichi_room(game_server, Connect_id, message, websocket)
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
    elif message_type == "room/add_smart_bot":
        await handle_add_smart_bot_to_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/kick_player":
        await handle_kick_player_from_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/set_ready":
        await handle_set_ready(game_server, Connect_id, message, websocket)
    else:
        logger.warning(f"未知的房间消息路径: {message_type}")

async def handle_create_GB_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建国标房间请求"""
    logging.info(f"创建房间请求 - 用户名: {Connect_id}")
    # 检查玩家是否已经在房间中
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_GB_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message["open_cuohe"],
        message.get("sub_rule", "guobiao/standard"),
        message.get("hepai_limit", 8),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        message.get("tactical_call", False),
        message.get("cuohe_type", 0),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Qingque_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建青雀房间请求"""
    logging.info(f"创建青雀房间请求 - 用户名: {Connect_id}")
    # 与国标相同：先检查是否已经在房间中
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Qingque_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "qingque/standard"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        message.get("tactical_call", False),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Classical_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建古典麻将房间请求"""
    logging.info(f"创建古典麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Classical_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "classical/standard"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Sichuan_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建四川麻将（血战到底）房间请求"""
    logging.info(f"创建四川麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Sichuan_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "sichuan/standard"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        message.get("tactical_call", False),
        message.get("blood_battle", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Riichi_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建立直麻将房间请求"""
    logging.info(f"创建立直麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Riichi_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "riichi/standard"),
        message.get("open_cuohe", False),
        message.get("hepai_limit", 1),
        message.get("red_dora", True),
        message.get("allow_kuikae", False),
        message.get("open_xiru", True),
        message.get("open_tobi", True),
        message.get("hepai_way", "multi_ron"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_room_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取房间列表请求。show_tip：True=手动刷新显示tips，False/null=静默刷新"""
    show_tip = message.get("show_tip", False)
    response = game_server.get_room_list(show_tip=show_tip)
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

async def handle_add_smart_bot_to_room(game_server, Connect_id: str, message: dict, websocket):
    """处理添加牌效机器人到房间请求"""
    await game_server.add_smart_bot_to_room(Connect_id, message["room_id"])

async def handle_kick_player_from_room(game_server, Connect_id: str, message: dict, websocket):
    """处理房主移除玩家请求"""
    await game_server.kick_player_from_room(Connect_id, message["room_id"], message["target_user_id"])

async def handle_set_ready(game_server, Connect_id: str, message: dict, websocket):
    """处理玩家准备状态变更请求"""
    await game_server.set_player_ready(Connect_id, message["room_id"], message.get("ready", True))

