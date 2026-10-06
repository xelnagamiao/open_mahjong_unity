"""Connection identity and action tick are authoritative, not client seat IDs."""

from ..public.tactical_claim import tactical_force_pass_is_live, tactical_mark_player_force_passed


async def handle_action(server, connection_id, message, websocket):
    state = server.gamestate_manager.get_game_state_by_gamestate_id(message.get("gamestate_id"))
    connection = server.players.get(connection_id)
    if state is None or state.room_rule != "hangzhou" or connection is None:
        return
    index = next((i for i, p in enumerate(state.player_list) if p.user_id == connection.user_id), None)
    if index is None:
        return
    action = "cut" if message.get("type", "").endswith("/cut_tile") else message.get("action")
    tick = message.get("action_tick")
    live_force_pass = (type(tick) is int and action == "force_pass"
                       and tactical_force_pass_is_live(state, index, tick))
    if type(tick) is not int or (tick != state.server_action_tick and not live_force_pass):
        await state.prepare_private_hints(index)
        payload = state.build_ready_status_payload(index) if state.game_status == "waiting_ready" else state.build_pending_action_payload(index)
        if payload:
            await state._send_timed_payload(index, payload, websocket)
        return
    try:
        # 放弃针对本张弃牌：申请广播期间主询问已收尾，仍须记住退出竞争。
        # 普通取消/鸣牌继续要求当前询问帧和行动权限。
        if live_force_pass and (index not in state.waiting_players_list
                                or action not in state.action_dict.get(index, [])):
            if state._decision_paused():
                raise ValueError("牌局暂停期间不能操作")
            tactical_mark_player_force_passed(state, index)
            return
        await state.submit_action(index, action, TileId=message.get("TileId"), cutIndex=message.get("cutIndex", -1),
                                  cutClass=message.get("cutClass"), target_tile=message.get("targetTile"))
    except (TypeError, ValueError):
        await websocket.send_json(dict(type="tips", success=False, message="该动作已失效或不符合杭州规则"))
