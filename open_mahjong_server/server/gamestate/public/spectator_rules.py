AI_RESERVED_MAX_USER_ID = 10

def ai_player_count(player_list) -> int:
    return sum(1 for p in player_list if p.user_id <= AI_RESERVED_MAX_USER_ID)

def too_many_ai_for_spectator(player_list) -> bool:
    return ai_player_count(player_list) >= 3


def find_player_index_by_user_id(game_state, user_id: int):
    """按 user_id 解析玩家当前 player_index（局间会轮转，不可缓存）。"""
    if not hasattr(game_state, "player_list"):
        return None
    for p in game_state.player_list:
        if getattr(p, "user_id", None) == user_id:
            return getattr(p, "player_index", None)
    return None


def realtime_spectator_watches_broadcast_seat(spectator, game_state, broadcast_player_index: int) -> bool:
    """观战者是否应接收「发给 broadcast_player_index 座位」的个性化广播。"""
    host_idx = find_player_index_by_user_id(game_state, spectator.host_user_id)
    return host_idx is not None and host_idx == broadcast_player_index


async def deliver_realtime_spectator_message(game_state, broadcast_player_index: int, response) -> None:
    """向挂在被观战玩家当前座位视角上的实时观战者推送消息。"""
    spectators = getattr(game_state, "realtime_spectators", None)
    if not spectators:
        return
    base_payload = response.dict(exclude_none=True) if hasattr(response, "dict") else response
    if not isinstance(base_payload, dict):
        base_payload = dict(base_payload)
    for sp in list(spectators):
        if not realtime_spectator_watches_broadcast_seat(sp, game_state, broadcast_player_index):
            continue
        conn = game_state.game_server.user_id_to_connection.get(sp.user_id)
        if conn is None:
            continue
        payload = base_payload
        host_idx = find_player_index_by_user_id(game_state, sp.host_user_id)
        game_info = payload.get("game_info")
        if host_idx is not None and isinstance(game_info, dict):
            payload = {**payload, "game_info": {**game_info, "view_player_index": host_idx}}
        try:
            await conn.websocket.send_json(payload)
        except Exception:
            pass
