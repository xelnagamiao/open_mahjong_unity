# 数据路由处理器
import logging
from ..response import Response, Rule_stats_response, Player_stats_info, Record_info, Player_record_info

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
    elif message_type == "data/get_guobiao_stats":
        await handle_get_guobiao_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_riichi_stats":
        await handle_get_riichi_stats(game_server, Connect_id, message, websocket)
    else:
        logger.warning(f"未知的数据消息路径: {message_type}")

async def handle_get_record_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取游戏记录列表请求"""
    player = game_server.players.get(Connect_id)
    if player and player.user_id:
        records = game_server.db_manager.get_record_list(player.user_id, limit=20)
        # 转换为 Record_info 列表
        record_list = []
        for game_record in records:
            # 将玩家信息转换为 Player_record_info 列表
            players_info = []
            for player_data in game_record['players']:
                players_info.append(Player_record_info(
                    user_id=player_data['user_id'],
                    username=player_data['username'],
                    score=player_data['score'],
                    rank=player_data['rank'],
                    title_used=player_data.get('title_used'),
                    character_used=player_data.get('character_used'),
                    profile_used=player_data.get('profile_used'),
                    voice_used=player_data.get('voice_used')
                ))
            
            record_info = Record_info(
                game_id=game_record['game_id'],
                rule=game_record['rule'],
                record=game_record['record'],
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

async def handle_get_guobiao_stats(game_server, Connect_id: str, message: dict, websocket):
    """处理获取国标统计数据请求"""
    from ..database.guobiao.get_guobiao_stats import get_guobiao_history_stats, get_guobiao_fan_stats_total
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
        rule_stats=rule_stats_response
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
    
    # TODO: 实现立直统计数据获取
    rule_stats_response = Rule_stats_response(
        rule="riichi",
        history_stats=[],
        total_fan_stats=None
    )
    
    response = Response(
        type="data/get_riichi_stats",
        success=True,
        message="获取立直统计数据成功",
        rule_stats=rule_stats_response
    )
    await websocket.send_json(response.dict(exclude_none=True))

