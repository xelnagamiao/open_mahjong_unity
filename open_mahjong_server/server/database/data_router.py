# 数据路由处理器
import logging
from ..response import Response, Rule_stats_response, Player_stats_info, Record_info, Record_detail, Player_record_info, Player_info_response, UserSettings, LeaderboardEntry

logger = logging.getLogger(__name__)

async def handle_data_message(game_server, Connect_id: str, message: dict, websocket):
    """
    处理数据相关的消息（根据 type 字段的完整路径分发）
    
    Args:
        game_server: 游戏服务器实例
        Connect_id: 连接ID
        message: 消息字典（type 字段应为 "data/xxx" 格式）
        websocket: WebSocket连接
    """
    message_type = message.get("type", "").strip("/")
    
    # 根据完整路径分发
    if message_type == "data/get_record_list":
        await handle_get_record_list(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_record_by_id":
        await handle_get_record_by_id(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_guobiao_stats":
        await handle_get_guobiao_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_riichi_stats":
        await handle_get_riichi_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_qingque_stats":
        await handle_get_qingque_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_classical_stats":
        await handle_get_classical_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_leaderboard":
        await handle_get_leaderboard(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_rank_record_list":
        await handle_get_rank_record_list(game_server, Connect_id, message, websocket)
    else:
        logger.warning(f"未知的数据消息路径: {message_type}")

async def handle_get_record_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取游戏记录列表请求（仅返回元数据，不含完整牌谱）"""
    player = game_server.players.get(Connect_id)
    if player and player.user_id:
        limit = message.get("limit", 20)
        offset = message.get("offset", 0)
        try:
            limit = max(1, min(50, int(limit)))
        except (TypeError, ValueError):
            limit = 20
        try:
            offset = max(0, int(offset))
        except (TypeError, ValueError):
            offset = 0

        records = game_server.db_manager.get_record_list(player.user_id, limit=limit, offset=offset)
        record_list = []
        for game_record in records:
            players_info = []
            for player_data in game_record['players']:
                players_info.append(Player_record_info(
                    user_id=player_data['user_id'],
                    username=player_data['username'],
                    score=player_data['score'],
                    rank=player_data['rank'],
                    original_player_index=player_data.get('original_player_index'),
                    title_used=player_data.get('title_used'),
                    character_used=player_data.get('character_used'),
                    profile_used=player_data.get('profile_used'),
                    voice_used=player_data.get('voice_used')
                ))
            
            record_info = Record_info(
                game_id=game_record['game_id'],
                rule=game_record['rule'],
                sub_rule=game_record.get('sub_rule'),
                match_type=game_record.get('match_type'),
                created_at=game_record['created_at'],
                players=players_info
            )
            record_list.append(record_info)
        
        response = Response(
            type="data/get_record_list",
            success=True,
            message=f"获取到 {len(record_list)} 局游戏记录",
            record_list=record_list
        )
    else:
        response = Response(
            type="data/get_record_list",
            success=False,
            message="用户未登录"
        )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_rank_record_list(game_server, Connect_id: str, message: dict, websocket):
    """获取全服最近的天梯（排位）对局元数据；无记录时返回空列表，不中断连接。"""
    response = Response(
        type="data/get_rank_record_list",
        success=False,
        message="用户未登录",
        record_list=[],
    )
    try:
        player = game_server.players.get(Connect_id)
        if not (player and player.user_id):
            await websocket.send_json(response.dict(exclude_none=True))
            return

        limit = message.get("limit", 20)
        try:
            limit = max(1, min(50, int(limit)))
        except (TypeError, ValueError):
            limit = 20

        getter = getattr(game_server.db_manager, "get_rank_record_list", None)
        if getter is None:
            logger.error("db_manager 未挂载 get_rank_record_list，请重启游戏服")
            response = Response(
                type="data/get_rank_record_list",
                success=True,
                message="获取到 0 局天梯对局",
                record_list=[],
            )
            await websocket.send_json(response.dict(exclude_none=True))
            return

        records = getter(limit=limit) or []
        record_list = []
        for game_record in records:
            players_info = []
            for p in game_record.get("players") or []:
                players_info.append(
                    Player_record_info(
                        user_id=p["user_id"],
                        username=p.get("username") or "",
                        score=p.get("score") if p.get("score") is not None else 0,
                        rank=p.get("rank") if p.get("rank") is not None else 0,
                        original_player_index=p.get("original_player_index"),
                    )
                )
            record_list.append(
                Record_info(
                    game_id=game_record["game_id"],
                    rule=game_record.get("rule") or "",
                    sub_rule=game_record.get("sub_rule"),
                    match_type=game_record.get("match_type"),
                    match_queue_type=game_record.get("match_queue_type"),
                    created_at=game_record.get("created_at") or "",
                    players=players_info,
                )
            )
        response = Response(
            type="data/get_rank_record_list",
            success=True,
            message=f"获取到 {len(record_list)} 局天梯对局",
            record_list=record_list,
        )
    except Exception as e:
        logger.error(f"处理天梯对局列表失败: {e}", exc_info=True)
        response = Response(
            type="data/get_rank_record_list",
            success=True,
            message="获取到 0 局天梯对局",
            record_list=[],
        )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_leaderboard(game_server, Connect_id: str, message: dict, websocket):
    """处理获取国标段位排行榜请求"""
    player = game_server.players.get(Connect_id)
    if player and player.user_id:
        rows = game_server.db_manager.get_guobiao_leaderboard()
        leaderboard_list = [
            LeaderboardEntry(
                rank_position=row["rank_position"],
                user_id=row["user_id"],
                username=row["username"],
                profile_image_id=row["profile_image_id"],
                guobiao_rank=row["guobiao_rank"],
                guobiao_score=row["guobiao_score"],
            )
            for row in rows
        ]
        response = Response(
            type="data/get_leaderboard",
            success=True,
            message=f"获取到 {len(leaderboard_list)} 名排行榜玩家",
            leaderboard_list=leaderboard_list,
        )
    else:
        response = Response(
            type="data/get_leaderboard",
            success=False,
            message="用户未登录",
        )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_record_by_id(game_server, Connect_id: str, message: dict, websocket):
    """处理按ID获取完整牌谱记录请求"""
    player = game_server.players.get(Connect_id)
    if not (player and player.user_id):
        response = Response(
            type="data/get_record_by_id",
            success=False,
            message="用户未登录"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return
    
    game_id = message.get("game_id", "").strip()
    if not game_id:
        response = Response(
            type="data/get_record_by_id",
            success=False,
            message="缺少牌谱ID"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return
    
    result = game_server.db_manager.get_record_by_id(game_id)
    if result is None:
        response = Response(
            type="data/get_record_by_id",
            success=False,
            message=f"未找到牌谱 {game_id}"
        )
    else:
        players_info = []
        for p in result['players']:
            players_info.append(Player_record_info(
                user_id=p['user_id'],
                username=p['username'],
                score=p['score'],
                rank=p['rank'],
                original_player_index=p.get('original_player_index'),
                title_used=p.get('title_used'),
                character_used=p.get('character_used'),
                profile_used=p.get('profile_used'),
                voice_used=p.get('voice_used')
            ))
        
        detail = Record_detail(
            game_id=result['game_id'],
            rule=result['rule'],
            sub_rule=result.get('sub_rule'),
            record=result['record'],
            created_at=result['created_at'],
            players=players_info
        )
        response = Response(
            type="data/get_record_by_id",
            success=True,
            message="获取牌谱成功",
            record_detail=detail
        )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_guobiao_stats(game_server, Connect_id: str, message: dict, websocket):
    """处理获取国标统计数据请求"""
    from .guobiao.get_guobiao_stats import get_guobiao_history_stats, get_guobiao_fan_stats_total
    try:
        target_user_id = int(message.get("userid"))
    except (ValueError, TypeError):
        response = Response(
            type="data/get_guobiao_stats",
            success=False,
            message="无效的用户ID"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return
    
    # 检查是否需要玩家信息
    need_player_info = message.get("need_player_info", False)
    player_info = None
    
    if need_player_info:
        # 获取玩家信息
        user_settings_data = game_server.db_manager.get_user_settings(target_user_id)
        if user_settings_data:
            rank_data = game_server.db_manager.get_rank_data(target_user_id)
            player_info = Player_info_response(
                user_id=target_user_id,
                user_settings=UserSettings(
                    user_id=user_settings_data.get('user_id'),
                    username=user_settings_data.get('username'),
                    title_id=user_settings_data.get('title_id'),
                    profile_image_id=user_settings_data.get('profile_image_id'),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                guobiao_rank=rank_data.get('guobiao_rank', '10级') if rank_data else '10级',
                guobiao_score=rank_data.get('guobiao_score', 0.0) if rank_data else 0.0
            )
    
    # 获取国标历史统计数据
    history_stats_rows = get_guobiao_history_stats(game_server.db_manager, target_user_id)
    
    # 转换为 Player_stats_info 列表
    history_stats_list = []
    for stats_row in history_stats_rows:
        history_stats_list.append(Player_stats_info(
            rule=stats_row.get('rule', 'guobiao'),
            mode=stats_row.get('mode'),
            total_games=stats_row.get('total_games'),
            total_rounds=stats_row.get('total_rounds'),
            win_count=stats_row.get('win_count'),
            self_draw_count=stats_row.get('self_draw_count'),
            deal_in_count=stats_row.get('deal_in_count'),
            total_fan_score=stats_row.get('total_fan_score'),
            total_win_turn=stats_row.get('total_win_turn'),
            total_fangchong_score=stats_row.get('total_fangchong_score'),
            first_place_count=stats_row.get('first_place_count'),
            second_place_count=stats_row.get('second_place_count'),
            third_place_count=stats_row.get('third_place_count'),
            fourth_place_count=stats_row.get('fourth_place_count'),
            fulu_round_count=stats_row.get('fulu_round_count'),
            cuohe_count=stats_row.get('cuohe_count'),
            fan_stats=None  # 历史统计不包含番种数据
        ))
    
    # 获取汇总番种统计数据（始终返回，没有数据时返回全0字典）
    total_fan_stats = get_guobiao_fan_stats_total(game_server.db_manager, target_user_id)
    
    rule_stats_response = Rule_stats_response(
        rule="guobiao",
        history_stats=history_stats_list,
        total_fan_stats=total_fan_stats
    )
    
    response = Response(
        type="data/get_guobiao_stats",
        success=True,
        message="获取国标统计数据成功",
        rule_stats=rule_stats_response,
        player_info=player_info
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_riichi_stats(game_server, Connect_id: str, message: dict, websocket):
    """处理获取立直统计数据请求（暂时返回空数据，待实现）"""
    try:
        target_user_id = int(message.get("userid"))
    except (ValueError, TypeError):
        response = Response(
            type="data/get_riichi_stats",
            success=False,
            message="无效的用户ID"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return
    
    # 检查是否需要玩家信息
    need_player_info = message.get("need_player_info", False)
    player_info = None
    
    if need_player_info:
        user_settings_data = game_server.db_manager.get_user_settings(target_user_id)
        if user_settings_data:
            rank_data = game_server.db_manager.get_rank_data(target_user_id)
            player_info = Player_info_response(
                user_id=target_user_id,
                user_settings=UserSettings(
                    user_id=user_settings_data.get('user_id'),
                    username=user_settings_data.get('username'),
                    title_id=user_settings_data.get('title_id'),
                    profile_image_id=user_settings_data.get('profile_image_id'),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                guobiao_rank=rank_data.get('guobiao_rank', '10级') if rank_data else '10级',
                guobiao_score=rank_data.get('guobiao_score', 0.0) if rank_data else 0.0
            )
    
    from .riichi.get_riichi_stats import get_riichi_history_stats, get_riichi_fan_stats_total

    history_stats_rows = get_riichi_history_stats(game_server.db_manager, target_user_id)
    history_stats_list = []
    for stats_row in history_stats_rows:
        history_stats_list.append(Player_stats_info(
            rule=stats_row.get('rule', 'riichi'),
            mode=stats_row.get('mode'),
            total_games=stats_row.get('total_games'),
            total_rounds=stats_row.get('total_rounds'),
            win_count=stats_row.get('win_count'),
            self_draw_count=stats_row.get('self_draw_count'),
            deal_in_count=stats_row.get('deal_in_count'),
            total_fan_score=stats_row.get('total_fan_score'),
            total_win_turn=stats_row.get('total_win_turn'),
            total_fangchong_score=stats_row.get('total_fangchong_score'),
            first_place_count=stats_row.get('first_place_count'),
            second_place_count=stats_row.get('second_place_count'),
            third_place_count=stats_row.get('third_place_count'),
            fourth_place_count=stats_row.get('fourth_place_count'),
            fulu_round_count=stats_row.get('fulu_round_count'),
            fan_stats=None,
        ))

    total_fan_stats = get_riichi_fan_stats_total(game_server.db_manager, target_user_id)

    rule_stats_response = Rule_stats_response(
        rule="riichi",
        history_stats=history_stats_list,
        total_fan_stats=total_fan_stats
    )
    
    response = Response(
        type="data/get_riichi_stats",
        success=True,
        message="获取立直统计数据成功",
        rule_stats=rule_stats_response,
        player_info=player_info
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_qingque_stats(game_server, Connect_id: str, message: dict, websocket):
    """处理获取青雀统计数据请求"""
    from .qingque.get_qingque_stats import get_qingque_history_stats, get_qingque_fan_stats_total
    try:
        target_user_id = int(message.get("userid"))
    except (ValueError, TypeError):
        response = Response(
            type="data/get_qingque_stats",
            success=False,
            message="无效的用户ID"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return
    
    # 检查是否需要玩家信息
    need_player_info = message.get("need_player_info", False)
    player_info = None
    
    if need_player_info:
        user_settings_data = game_server.db_manager.get_user_settings(target_user_id)
        if user_settings_data:
            rank_data = game_server.db_manager.get_rank_data(target_user_id)
            player_info = Player_info_response(
                user_id=target_user_id,
                user_settings=UserSettings(
                    user_id=user_settings_data.get('user_id'),
                    username=user_settings_data.get('username'),
                    title_id=user_settings_data.get('title_id'),
                    profile_image_id=user_settings_data.get('profile_image_id'),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                guobiao_rank=rank_data.get('guobiao_rank', '10级') if rank_data else '10级',
                guobiao_score=rank_data.get('guobiao_score', 0.0) if rank_data else 0.0
            )
    
    # 获取青雀历史统计数据
    history_stats_rows = get_qingque_history_stats(game_server.db_manager, target_user_id)
    
    # 转换为 Player_stats_info 列表
    history_stats_list = []
    for stats_row in history_stats_rows:
        history_stats_list.append(Player_stats_info(
            rule=stats_row.get('rule', 'qingque'),
            mode=stats_row.get('mode'),
            total_games=stats_row.get('total_games'),
            total_rounds=stats_row.get('total_rounds'),
            win_count=stats_row.get('win_count'),
            self_draw_count=stats_row.get('self_draw_count'),
            deal_in_count=stats_row.get('deal_in_count'),
            total_fan_score=stats_row.get('total_fan_score'),
            total_win_turn=stats_row.get('total_win_turn'),
            total_fangchong_score=stats_row.get('total_fangchong_score'),
            first_place_count=stats_row.get('first_place_count'),
            second_place_count=stats_row.get('second_place_count'),
            third_place_count=stats_row.get('third_place_count'),
            fourth_place_count=stats_row.get('fourth_place_count'),
            fulu_round_count=stats_row.get('fulu_round_count'),
            fan_stats=None
        ))
    
    # 获取汇总番种统计数据
    total_fan_stats = get_qingque_fan_stats_total(game_server.db_manager, target_user_id)
    
    rule_stats_response = Rule_stats_response(
        rule="qingque",
        history_stats=history_stats_list,
        total_fan_stats=total_fan_stats
    )
    
    response = Response(
        type="data/get_qingque_stats",
        success=True,
        message="获取青雀统计数据成功",
        rule_stats=rule_stats_response,
        player_info=player_info
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_classical_stats(game_server, Connect_id: str, message: dict, websocket):
    """处理获取古典麻将统计数据请求"""
    from .classical.get_classical_stats import get_classical_history_stats, get_classical_fan_stats_total
    try:
        target_user_id = int(message.get("userid"))
    except (ValueError, TypeError):
        response = Response(
            type="data/get_classical_stats",
            success=False,
            message="无效的用户ID"
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return

    need_player_info = message.get("need_player_info", False)
    player_info = None

    if need_player_info:
        user_settings_data = game_server.db_manager.get_user_settings(target_user_id)
        if user_settings_data:
            rank_data = game_server.db_manager.get_rank_data(target_user_id)
            player_info = Player_info_response(
                user_id=target_user_id,
                user_settings=UserSettings(
                    user_id=user_settings_data.get('user_id'),
                    username=user_settings_data.get('username'),
                    title_id=user_settings_data.get('title_id'),
                    profile_image_id=user_settings_data.get('profile_image_id'),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                guobiao_rank=rank_data.get('guobiao_rank', '10级') if rank_data else '10级',
                guobiao_score=rank_data.get('guobiao_score', 0.0) if rank_data else 0.0
            )

    history_stats_rows = get_classical_history_stats(game_server.db_manager, target_user_id)

    history_stats_list = []
    for stats_row in history_stats_rows:
        history_stats_list.append(Player_stats_info(
            rule=stats_row.get('rule', 'classical'),
            mode=stats_row.get('mode'),
            total_games=stats_row.get('total_games'),
            total_rounds=stats_row.get('total_rounds'),
            win_count=stats_row.get('win_count'),
            self_draw_count=stats_row.get('self_draw_count'),
            deal_in_count=stats_row.get('deal_in_count'),
            total_fan_score=stats_row.get('total_fan_score'),
            total_win_turn=stats_row.get('total_win_turn'),
            total_fangchong_score=stats_row.get('total_fangchong_score'),
            first_place_count=stats_row.get('first_place_count'),
            second_place_count=stats_row.get('second_place_count'),
            third_place_count=stats_row.get('third_place_count'),
            fourth_place_count=stats_row.get('fourth_place_count'),
            fulu_round_count=stats_row.get('fulu_round_count'),
            fan_stats=None
        ))

    total_fan_stats = get_classical_fan_stats_total(game_server.db_manager, target_user_id)

    rule_stats_response = Rule_stats_response(
        rule="classical",
        history_stats=history_stats_list,
        total_fan_stats=total_fan_stats
    )

    response = Response(
        type="data/get_classical_stats",
        success=True,
        message="获取古典麻将统计数据成功",
        rule_stats=rule_stats_response,
        player_info=player_info
    )
    await websocket.send_json(response.dict(exclude_none=True))

