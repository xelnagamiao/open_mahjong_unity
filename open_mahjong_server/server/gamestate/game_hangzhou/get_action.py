"""Connection identity and action tick are authoritative, not client seat IDs."""


async def handle_action(server, connection_id, message, websocket):
    state = server.gamestate_manager.get_game_state_by_gamestate_id(message.get("gamestate_id"))
    connection = server.players.get(connection_id)
    if state is None or state.room_rule != "hangzhou" or connection is None:
        return
    index = next((i for i, p in enumerate(state.player_list) if p.user_id == connection.user_id), None)
    if index is None:
        return
    if type(message.get("action_tick")) is not int or message["action_tick"] != state.server_action_tick:
        await state.prepare_private_hints(index)
        payload = state.build_ready_status_payload(index) if state.game_status == "waiting_ready" else state.build_pending_action_payload(index)
        if payload:
            await websocket.send_json(payload)
        return
    action = "cut" if message.get("type", "").endswith("/cut_tile") else message.get("action")
    try:
        await state.submit_action(index, action, TileId=message.get("TileId"), cutIndex=message.get("cutIndex", -1),
                                  cutClass=message.get("cutClass"), target_tile=message.get("targetTile"))
    except (TypeError, ValueError):
        await websocket.send_json(dict(type="tips", success=False, message="该动作已失效或不符合杭州规则"))
