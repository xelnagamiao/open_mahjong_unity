# 数据路由处理器
import logging
import asyncio
from functools import partial
from ..response import Response, Rule_stats_response, Player_stats_info, Record_info, Record_detail, Player_record_info, Player_info_response, UserSettings, LeaderboardEntry

logger = logging.getLogger(__name__)

# Cancelled requests retain their DB slot until the worker actually finishes.
_record_read_slots = asyncio.Semaphore(2)


async def _run_record_read(func, *args, **kwargs):
    await _record_read_slots.acquire()
    try:
        future = asyncio.get_running_loop().run_in_executor(
            None, partial(func, *args, **kwargs)
        )
    except BaseException:
        _record_read_slots.release()
        raise

    def finished(done):
        _record_read_slots.release()
        if not done.cancelled():
            done.exception()

    future.add_done_callback(finished)
    return await asyncio.shield(future)

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
    elif message_type == "data/update_record_favorite":
        await handle_update_record_favorite(game_server, Connect_id, message, websocket)
    elif message_type == "data/update_record_note":
        await handle_update_record_note(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_guobiao_stats":
        await handle_get_guobiao_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_player_recent_records":
        await handle_get_player_recent_records(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_riichi_stats":
        await handle_get_riichi_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_qingque_stats":
        await handle_get_qingque_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_classical_stats":
        await handle_get_classical_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_jiandan_stats":
        await handle_get_jiandan_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_rule_stats":
        await handle_get_rule_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_leaderboard":
        await handle_get_leaderboard(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_ranked_stats":
        await handle_get_ranked_stats(game_server, Connect_id, message, websocket)
    elif message_type == "data/get_rank_record_list":
        await handle_get_rank_record_list(game_server, Connect_id, message, websocket)
    else:
        logger.warning(f"未知的数据消息路径: {message_type}")

async def handle_get_player_recent_records(game_server, Connect_id: str, message: dict, websocket):
    from ..response import Player_recent_records_response
    from .player_recent_records import get_player_recent_records
    request_id = str(message.get("request_id") or "")[:128]
    target_user_id = 0
    success = False
    rules = {}
    try:
        target_user_id = int(message.get("userid"))
        if target_user_id <= 10:
            raise ValueError("无效的用户ID")
    except (ValueError, TypeError):
        result_message = "无效的用户ID"
    else:
        try:
            rules = await asyncio.to_thread(get_player_recent_records, game_server.db_manager, target_user_id)
            success = True
            result_message = "获取玩家近期记录成功"
        except Exception:
            logger.exception("获取玩家近期记录失败 user_id=%s", target_user_id)
            result_message = "获取玩家近期记录失败"
    response = Response(
        type="data/get_player_recent_records", success=success, message=result_message,
        player_recent_records=Player_recent_records_response(
            user_id=target_user_id, request_id=request_id, rules=rules,
        ),
    )
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_record_list(game_server, Connect_id: str, message: dict, websocket):
    """处理获取游戏记录列表请求（仅返回元数据，不含完整牌谱）"""
    player = game_server.players.get(Connect_id)
    if player and player.user_id:
        limit = message.get("limit", 20)
        offset = message.get("offset", 0)
        favorites_only = bool(message.get("favorites_only", False))
        try:
            limit = max(1, min(50, int(limit)))
        except (TypeError, ValueError):
            limit = 20
        try:
            offset = max(0, int(offset))
        except (TypeError, ValueError):
            offset = 0

        records = await _run_record_read(
            game_server.db_manager.get_record_list,
            player.user_id,
            limit=limit,
            offset=offset,
            favorites_only=favorites_only,
        )
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
                    avatar_frame_used=player_data.get('avatar_frame_used',0),
                    voice_used=player_data.get('voice_used')
                ))
            
            record_info = Record_info(
                game_id=game_record['game_id'],
                rule=game_record['rule'],
                sub_rule=game_record.get('sub_rule'),
                match_type=game_record.get('match_type'),
                created_at=game_record['created_at'],
                players=players_info,
                is_favorite=bool(game_record.get('is_favorite')),
                note=game_record.get('note') or '',
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

async def handle_update_record_favorite(game_server, Connect_id: str, message: dict, websocket):
    """处理更新牌谱收藏请求"""
    player = game_server.players.get(Connect_id)
    if not (player and player.user_id):
        response = Response(
            type="data/update_record_favorite",
            success=False,
            message="用户未登录",
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return

    game_id = (message.get("game_id") or "").strip()
    is_favorite = bool(message.get("is_favorite", False))
    result = game_server.db_manager.set_record_favorite(player.user_id, game_id, is_favorite)
    response = Response(
        type="data/update_record_favorite",
        success=result.get("success", False),
        message=result.get("message", ""),
        game_id=game_id,
        is_favorite=result.get("is_favorite", False),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_update_record_note(game_server, Connect_id: str, message: dict, websocket):
    """处理更新牌谱备注请求"""
    player = game_server.players.get(Connect_id)
    if not (player and player.user_id):
        response = Response(
            type="data/update_record_note",
            success=False,
            message="用户未登录",
        )
        await websocket.send_json(response.dict(exclude_none=True))
        return

    game_id = (message.get("game_id") or "").strip()
    note = message.get("note")
    if note is None:
        note = ""
    elif not isinstance(note, str):
        note = str(note)
    result = game_server.db_manager.set_record_note(player.user_id, game_id, note)
    response = Response(
        type="data/update_record_note",
        success=result.get("success", False),
        message=result.get("message", ""),
        game_id=game_id,
        note=result.get("note", ""),
    )
    await websocket.send_json(response.dict(exclude_none=True))

async def handle_get_rule_stats(game_server, connect_id, message, websocket):
    from .get_rule_stats import get_custom_rule_history, validate_selection
    response = Response(type="data/get_rule_stats", success=False, message="用户未登录",
                        data_request_id=str(message.get("data_request_id") or "")[:128])
    try:
        player = game_server.players.get(connect_id)
        if player and player.user_id:
            rule = validate_selection(message)
            try:
                uid = int(message.get("userid", player.user_id))
            except (TypeError, ValueError):
                raise ValueError("无效的用户ID") from None
            if uid <= 10:
                raise ValueError("无效的用户ID")
            rows = await _run_record_read(get_custom_rule_history, game_server.db_manager, uid, rule)
            fans = {}
            for row in rows:
                for key, value in (row.get("fan_stats") or {}).items():
                    fans[key] = fans.get(key, 0) + value
            response.rule_stats = Rule_stats_response(rule=rule, history_stats=[Player_stats_info(**row) for row in rows],
                                                     total_fan_stats=fans, ranked_fan_stats={})
            response.success = True
            response.message = "自定义对局统计已更新"
    except ValueError as error:
        response.message = str(error)
    except Exception:
        logger.exception("获取自定义玩法统计失败")
        response.message = "获取统计失败，请重试"
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_ranked_stats(game_server, connect_id, message, websocket):
    from .rule_ratings import get_ranked_history
    from .riichi.get_riichi_stats import get_riichi_fan_stats_total
    from .qingque.get_qingque_stats import get_qingque_fan_stats_total
    from ..match.rating_rules import RULES
    rule = message.get('rule')
    response = Response(type='data/get_ranked_stats', success=False, message='用户未登录',
                        rating_rule=rule if isinstance(rule,str) else None, data_request_id=message.get('data_request_id'))
    try:
        player = game_server.players.get(connect_id)
        if player and player.user_id:
            if not isinstance(rule,str) or rule not in RULES:
                response.message = '不支持的匹配规则'
            else:
                uid = int(message.get('userid',player.user_id))
                rows = await _run_record_read(get_ranked_history, game_server.db_manager, uid, rule)
                fans = {}
                if rule in ('riichi', 'riichi_sanma'):
                    fans = await _run_record_read(get_riichi_fan_stats_total, game_server.db_manager, uid, ranked=True, sanma=rule == 'riichi_sanma')
                elif rule == 'qingque':
                    fans = await _run_record_read(get_qingque_fan_stats_total, game_server.db_manager, uid, ranked=True)
                response.rule_stats = Rule_stats_response(rule=rule, history_stats=[Player_stats_info(**r) for r in rows], total_fan_stats={}, ranked_fan_stats=fans)
                response.success = True
                response.message = '匹配统计已更新'
    except Exception:
        logger.exception('获取匹配统计失败')
        response.message = '获取匹配统计失败，请重试'
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_rank_record_list(game_server, Connect_id: str, message: dict, websocket):
    from ..match.rating_rules import RULES
    rule = message.get("rule", "guobiao")
    response = Response(type="data/get_rank_record_list", success=False, message="用户未登录",
                        rating_rule=rule if isinstance(rule,str) else None,
                        data_request_id=message.get("data_request_id"), record_list=[])
    try:
        player = game_server.players.get(Connect_id)
        if not (player and player.user_id):
            pass
        elif not isinstance(rule,str) or rule not in RULES:
            response.message = "不支持的匹配规则"
        else:
            try:
                limit = max(1,min(50,int(message.get("limit",20))))
            except (ValueError,TypeError):
                limit = 20
            records = await _run_record_read(game_server.db_manager.get_rank_record_list, limit=limit, rule=rule)
            response.record_list = [Record_info(
                game_id=r['game_id'], rule=r.get('rule') or '', sub_rule=r.get('sub_rule'),
                match_type=r.get('match_type'), match_queue_type=r.get('match_queue_type'),
                created_at=r.get('created_at') or '', players=[Player_record_info(**p) for p in r.get('players',[])]) for r in records]
            response.success = True
            response.message = f"获取到 {len(records)} 局对局"
    except Exception:
        logger.exception("读取规则对局失败")
        response.message = "读取最近对局失败，请重试"
    await websocket.send_json(response.dict(exclude_none=True))


async def handle_get_leaderboard(game_server, Connect_id: str, message: dict, websocket):
    from ..match.rating_rules import RULES
    rule = message.get("rule", "guobiao")
    response = Response(type="data/get_leaderboard", success=False, message="用户未登录",
                        rating_rule=rule if isinstance(rule,str) else None,
                        data_request_id=message.get("data_request_id"), leaderboard_list=[])
    try:
        player = game_server.players.get(Connect_id)
        if not (player and player.user_id):
            pass
        elif not isinstance(rule,str) or rule not in RULES:
            response.message = "不支持的匹配规则"
        else:
            rows = await _run_record_read(game_server.db_manager.get_rule_leaderboard, rule=rule)
            response.leaderboard_list = [LeaderboardEntry(**row) for row in rows]
            response.success = True
            response.message = f"获取到 {len(rows)} 名排行榜玩家"
    except Exception:
        logger.exception("读取规则排行榜失败")
        response.message = "读取排行榜失败，请重试"
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
    
    result = await _run_record_read(game_server.db_manager.get_record_by_id, game_id)
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
                avatar_frame_used=p.get('avatar_frame_used',0),
                voice_used=p.get('voice_used')
            ))
        
        detail = Record_detail(
            game_id=result['game_id'],
            cloud_saved=True,
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
    from .guobiao.get_guobiao_stats import get_guobiao_history_stats
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
                    avatar_frame_id=user_settings_data.get('avatar_frame_id', 0),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                ratings=rank_data.get('ratings', {}) if rank_data else {},
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
            total_round_score=stats_row.get('total_round_score'),
            fan_stats=None  # 历史统计不包含番种数据
        ))
    
    # 获取汇总番种统计数据（普通 / 天梯分开，仅国标区分）
    from .guobiao.get_guobiao_stats import get_guobiao_fan_stats_split
    total_fan_stats, ranked_fan_stats = get_guobiao_fan_stats_split(game_server.db_manager, target_user_id)
    
    rule_stats_response = Rule_stats_response(
        rule="guobiao",
        history_stats=history_stats_list,
        total_fan_stats=total_fan_stats,
        ranked_fan_stats=ranked_fan_stats
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
    """读取日麻逐局摘要，返回局制、行为、流局、点数及役种统计。"""
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
                    avatar_frame_id=user_settings_data.get('avatar_frame_id', 0),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                ratings=rank_data.get('ratings', {}) if rank_data else {},
                guobiao_rank=rank_data.get('guobiao_rank', '10级') if rank_data else '10级',
                guobiao_score=rank_data.get('guobiao_score', 0.0) if rank_data else 0.0
            )
    
    from .riichi.get_riichi_stats import get_riichi_history_stats, get_riichi_fan_stats_total

    history_stats_rows = get_riichi_history_stats(game_server.db_manager, target_user_id)
    history_stats_list = [Player_stats_info(**row) for row in history_stats_rows]

    total_fan_stats = get_riichi_fan_stats_total(game_server.db_manager, target_user_id, ranked=False)

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
                    avatar_frame_id=user_settings_data.get('avatar_frame_id', 0),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                ratings=rank_data.get('ratings', {}) if rank_data else {},
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
    total_fan_stats = get_qingque_fan_stats_total(game_server.db_manager, target_user_id, ranked=False)
    
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
                    avatar_frame_id=user_settings_data.get('avatar_frame_id', 0),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                ),
                gb_stats=[],
                jp_stats=[],
                ratings=rank_data.get('ratings', {}) if rank_data else {},
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


async def handle_get_jiandan_stats(game_server, Connect_id: str, message: dict, websocket):
    """Handle the Jiandan player statistics request."""
    from .jiandan.get_jiandan_stats import (
        get_jiandan_fan_stats_total,
        get_jiandan_history_stats,
    )

    try:
        target_user_id = int(message.get("userid"))
    except (ValueError, TypeError):
        response = Response(
            type="data/get_jiandan_stats",
            success=False,
            message="无效的用户ID",
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
                    user_id=user_settings_data.get("user_id"),
                    username=user_settings_data.get("username"),
                    title_id=user_settings_data.get("title_id"),
                    profile_image_id=user_settings_data.get("profile_image_id"),
                    avatar_frame_id=user_settings_data.get("avatar_frame_id", 0),
                    character_id=user_settings_data.get("character_id"),
                    voice_id=user_settings_data.get("voice_id"),
                ),
                gb_stats=[],
                jp_stats=[],
                ratings=rank_data.get('ratings', {}) if rank_data else {},
                guobiao_rank=rank_data.get("guobiao_rank", "10级") if rank_data else "10级",
                guobiao_score=rank_data.get("guobiao_score", 0.0) if rank_data else 0.0,
            )

    history_stats_list = [
        Player_stats_info(
            rule=stats_row.get("rule", "jiandan"),
            mode=stats_row.get("mode"),
            total_games=stats_row.get("total_games"),
            total_rounds=stats_row.get("total_rounds"),
            win_count=stats_row.get("win_count"),
            self_draw_count=stats_row.get("self_draw_count"),
            deal_in_count=stats_row.get("deal_in_count"),
            total_fan_score=stats_row.get("total_fan_score"),
            total_win_turn=stats_row.get("total_win_turn"),
            total_fangchong_score=stats_row.get("total_fangchong_score"),
            first_place_count=stats_row.get("first_place_count"),
            second_place_count=stats_row.get("second_place_count"),
            third_place_count=stats_row.get("third_place_count"),
            fourth_place_count=stats_row.get("fourth_place_count"),
            fulu_round_count=stats_row.get("fulu_round_count"),
            fan_stats=None,
        )
        for stats_row in get_jiandan_history_stats(game_server.db_manager, target_user_id)
    ]
    rule_stats_response = Rule_stats_response(
        rule="jiandan",
        history_stats=history_stats_list,
        total_fan_stats=get_jiandan_fan_stats_total(game_server.db_manager, target_user_id),
    )
    response = Response(
        type="data/get_jiandan_stats",
        success=True,
        message="获取南雀统计数据成功",
        rule_stats=rule_stats_response,
        player_info=player_info,
    )
    await websocket.send_json(response.dict(exclude_none=True))
