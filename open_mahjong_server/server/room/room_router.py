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
    from ..database.duplicate_walls import duplicate_room_context, load_duplicate_wall
    token = None
    try:
        if message.get("type", "").strip("/").startswith("room/create_") and message.get("duplicate_key"):
            if message.get("type", "").strip("/") != "room/create_GB_room":
                raise ValueError("复式牌墙仅支持国标麻将（含蓝十改）")
            if message.get("sub_rule") == "guobiao/blood_battle":
                raise ValueError("国标血战暂不支持复式牌墙")
            wall = load_duplicate_wall(game_server.db_manager, message["duplicate_key"])
            token = duplicate_room_context.set(wall)
        await _dispatch_room_message(game_server, Connect_id, message, websocket)
    except ValueError as error:
        await websocket.send_json(Response(type="tips", success=False, message=str(error)).model_dump(exclude_none=True))
    finally:
        if token is not None:
            duplicate_room_context.reset(token)


async def _dispatch_room_message(game_server, Connect_id: str, message: dict, websocket):
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
    elif message_type == "room/create_Changsha_room":
        await handle_create_Changsha_room(game_server, Connect_id, message, websocket)
    elif message_type in {"room/create_Jiandan_room", "room/create_Zhongyong_room"}:
        await handle_create_Jiandan_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Hongque_room":
        await handle_create_Hongque_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Free_room":
        await handle_create_Free_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Classical_room":
        await handle_create_Classical_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Shanghai_room":
        await handle_create_Shanghai_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Sichuan_room":
        await handle_create_Sichuan_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Shanxi_room":
        from .shanxi_room import handle_create_shanxi_room
        await handle_create_shanxi_room(game_server, Connect_id, message, websocket)
    elif message_type in {"room/create_Changchun_room", "room/create_changchun_room"}:
        from .changchun_room import handle_create_changchun_room
        await handle_create_changchun_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Tuidao_room":
        from .tuidao_room import handle_create_tuidao_room
        await handle_create_tuidao_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Guangdong_room":
        from .guangdong_room import handle_create_guangdong_room
        await handle_create_guangdong_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Guizhou_room":
        from .guizhou_room import handle_create_guizhou_room
        await handle_create_guizhou_room(game_server, Connect_id, message, websocket)
    elif message_type in {"room/create_Hongzhong_room", "room/create_hongzhong_room"}:
        from .hongzhong_room import handle_create_hongzhong_room
        await handle_create_hongzhong_room(game_server, Connect_id, message, websocket)
    elif message_type in {"room/create_Hangzhou_room", "room/create_hangzhou_room"}:
        from .hangzhou_room import handle_create_hangzhou_room
        await handle_create_hangzhou_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Wenzhou_room":
        from .wenzhou_room import handle_create_wenzhou_room
        await handle_create_wenzhou_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Yixing_room":
        from .yixing_room import handle_create_yixing_room
        await handle_create_yixing_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_HongKong_room":
        from .hongkong_room import handle_create_hongkong_room
        await handle_create_hongkong_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/create_Taiwan_room":
        await handle_create_Taiwan_room(game_server, Connect_id, message, websocket)
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
    elif message_type == "room/add_guobiao_heuristic_bot":
        await handle_add_guobiao_heuristic_bot_to_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/kick_player":
        await handle_kick_player_from_room(game_server, Connect_id, message, websocket)
    elif message_type == "room/set_ready":
        await handle_set_ready(game_server, Connect_id, message, websocket)
    elif message_type == "room/set_claim_protection":
        response = await game_server.room_manager.set_claim_protection(
            Connect_id, message.get("room_id"), message.get("claim_protection"))
        if response is not None:
            await websocket.send_json(response.dict(exclude_none=True))
    elif message_type == "room/set_bot_speed":
        response = await game_server.room_manager.set_bot_speed(
            Connect_id, str(message.get("room_id", "")), message.get("bot_speed"))
        if response is not None:
            await websocket.send_json(response.model_dump(exclude_none=True))
    elif message_type == "room/sync_my_room":
        await handle_sync_my_room(game_server, Connect_id, websocket)
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
        message.get("claim_protection", True),
        message.get("cuohe_type", 0),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
        use_flowers=message.get("use_flowers", True),
        tian_di_ren_he=message.get("tian_di_ren_he", False),
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
        message.get("claim_protection", True),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Changsha_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建长沙麻将房间请求"""
    logging.info(f"创建长沙麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Changsha_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "changsha/classic_double_bird"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        message.get("tactical_call", False),
        message.get("claim_protection", True),
        message.get("open_kong_replacement_count", 2),
        message.get("initial_hu_si_xi", True),
        message.get("initial_hu_ban_ban_hu", True),
        message.get("initial_hu_que_yi_se", True),
        message.get("initial_hu_liu_liu_shun", True),
        message.get("initial_hu_san_tong", True),
        message.get("bird_count", 2),
        message.get("dealer_bird", True),
        message.get("base_score_no_dealer", False),
        message.get("small_hu_score", 2),
        message.get("big_hu_score", 8),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_create_Jiandan_room(game_server, Connect_id: str, message: dict, websocket):
    """Handle Zhongyong family rooms and the legacy Nanque request."""
    logging.info(f"创建简单麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Jiandan_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "zhongyong/standard" if message.get("type") == "room/create_Zhongyong_room" else "jiandan/standard"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        False,
        message.get("claim_protection", True),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_create_Hongque_room(game_server, Connect_id: str, message: dict, websocket):
    """Create a no-stats/no-record Hongque prototype room."""
    if Connect_id in game_server.players:
        blocked = _reject_room_entry(game_server, game_server.players[Connect_id])
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return
    response = await game_server.create_Hongque_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message.get("roundTimerValue", 20),
        message.get("stepTimerValue", 5),
        message.get("tips", True),
        message.get("random_seed", 0),
        message.get("sub_rule", "hongque/v1.6"),
        message.get("tourist_limit", False),
        False,
        message.get("hepai_way", "multi_ron"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_create_Free_room(game_server, Connect_id: str, message: dict, websocket):
    """创建自由模式房间。"""
    if Connect_id in game_server.players:
        blocked = _reject_room_entry(game_server, game_server.players[Connect_id])
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return
    response = await game_server.create_Free_room(
        Connect_id,
        message["roomname"],
        message.get("password", ""),
        message.get("random_seed", 0),
        message.get("sub_rule", "free/standard"),
        message.get("tourist_limit", False),
        message.get("wall_wan", True),
        message.get("wall_tong", True),
        message.get("wall_suo", True),
        message.get("wall_winds", True),
        message.get("wall_dragons", True),
        message.get("wall_flowers", True),
        pointer_tips=message.get("pointer_tips", True),
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
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Shanghai_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建上海敲麻麻将房间请求"""
    logging.info(f"创建上海敲麻麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Shanghai_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message["tips"],
        message.get("random_seed", 0),
        message.get("sub_rule", "shanghai/qiaoma"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
        hepai_limit=message.get("hepai_limit", 0),
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
        message.get("claim_protection", True),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
        hepai_limit=message.get("hepai_limit", 0),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_create_Taiwan_room(game_server, Connect_id: str, message: dict, websocket):
    """处理创建台湾麻将房间请求。"""
    logging.info(f"创建台湾麻将房间请求 - 用户名: {Connect_id}")
    if Connect_id in game_server.players:
        player = game_server.players[Connect_id]
        blocked = _reject_room_entry(game_server, player)
        if blocked:
            await websocket.send_json(blocked.dict(exclude_none=True))
            return

    response = await game_server.create_Taiwan_room(
        Connect_id,
        message["roomname"],
        message["gameround"],
        message["password"],
        message["roundTimerValue"],
        message["stepTimerValue"],
        message.get("tips", False),
        message.get("random_seed", 0),
        message.get("sub_rule", "taiwan/standard"),
        message.get("tourist_limit", False),
        message.get("allow_spectator", True),
        message.get("open_cuohe", False),
        message.get("cuohe_type", 0),
        message.get("detailed_config"),
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
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
        message.get("event_id"),
        count_tips=message.get("count_tips", False),
        pointer_tips=message.get("pointer_tips", True),
        starting_score=message.get("starting_score"),
        detailed_config=message.get("detailed_config"),
        claim_protection=message.get("claim_protection", False),
    )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_room_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取房间列表请求。show_tip：True=手动刷新显示tips，False/null=静默刷新"""
    show_tip = message.get("show_tip", False)
    event_id = message.get("event_id")
    response = game_server.get_room_list(show_tip=show_tip, event_id=event_id)
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
    await game_server.add_bot_to_room(Connect_id, message["room_id"], message.get("seat_index"))

async def handle_add_smart_bot_to_room(game_server, Connect_id: str, message: dict, websocket):
    """处理添加牌效机器人到房间请求"""
    await game_server.add_smart_bot_to_room(Connect_id, message["room_id"], message.get("seat_index"))

async def handle_add_guobiao_heuristic_bot_to_room(game_server, Connect_id: str, message: dict, websocket):
    """处理添加国标启发式机器人（高性能罗伯特）到房间请求"""
    await game_server.add_guobiao_heuristic_bot_to_room(Connect_id, message["room_id"], message.get("seat_index"))

async def handle_kick_player_from_room(game_server, Connect_id: str, message: dict, websocket):
    """处理房主移除玩家请求"""
    await game_server.kick_player_from_room(Connect_id, message["room_id"], message["target_user_id"], message.get("seat_index"))

async def handle_set_ready(game_server, Connect_id: str, message: dict, websocket):
    """处理玩家准备状态变更请求"""
    await game_server.set_player_ready(Connect_id, message["room_id"], message.get("ready", True))

async def handle_sync_my_room(game_server, Connect_id: str, websocket):
    """处理同步当前玩家房间状态请求"""
    response = await game_server.sync_my_room(Connect_id)
    await websocket.send_json(response.dict(exclude_none=True))
