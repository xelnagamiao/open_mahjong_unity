"""虹雀网络状态广播。

文件名沿用现有国标/川麻服务端约定。这里仅负责按观察者裁剪状态和发送消息，
不参与动作仲裁或修改牌面。
"""
from __future__ import annotations

import asyncio

from .wait_action import actions_for_viewer


def visible_event(source: dict, viewer_index: int) -> dict:
    event = dict(source)
    if event.get("type") in {"draw", "supplement"} and event.get("player") != viewer_index:
        event["tile"] = None
    return event


def build_state(game_state, viewer_index: int, *, sync_mode: str = "events",
                events_override=None) -> dict:
    viewer = game_state.players[viewer_index]
    actions, candidates = game_state._legal_turn_actions(viewer)
    if game_state.phase == "claim":
        actions, candidates = actions_for_viewer(game_state, viewer_index)
    win_hint = game_state._viewer_win_hint(viewer, actions)
    remaining_time, step_remaining = game_state._remaining_clock(viewer)
    state = {
        "sync_mode": sync_mode,
        "phase": game_state.phase,
        "game_status": game_state.game_status,
        "state_version": game_state.state_machine.version,
        "round": game_state.current_round,
        "dealer": game_state.dealer_index,
        "current_player": game_state.current_player_index,
        "wall_count": len(game_state.wall),
        "you": viewer_index,
        "action_tick": game_state.action_tick,
        "remaining_time": remaining_time,
        "step_remaining": step_remaining,
        "tips": game_state.tips,
        "message": game_state.message,
        "round_result": game_state.round_result,
        "events": [
            visible_event(event, viewer_index)
            for event in (game_state.events if events_override is None else events_override)
        ],
        "legal_actions": actions,
        "candidates": candidates,
        "win_hint": win_hint,
        "waiting_tiles": [],
        "waiting_hints": [],
    }
    claim_window = getattr(game_state, "claim_window", None)
    if claim_window is not None:
        state["claim_stage"] = claim_window.stage
        state["claim_pending_players"] = sorted(claim_window.pending)

    # 虹雀更新包保持完整权威快照；客户端仍用 sync_mode/events 决定是否重建桌面。
    # 这与其它规则的广播结构一致，也让断线边缘状态无需依赖上一包缓存。
    state.update({
        "room_id": int(game_state.room_id),
        "max_round": game_state.max_round,
        "round_time": game_state.round_time,
        "step_time": game_state.step_time,
        "hand": list(viewer.hand),
        "players": [{
            "index": player.index,
            "user_id": player.user_id,
            "username": player.username,
            "hand_count": len(player.hand),
            "discards": list(player.discards),
            "melds": list(player.melds),
            "score": player.score,
            "supplements": player.supplements,
            "online": player.online,
            "title_used": player.title_used,
            "profile_used": player.profile_used,
            "character_used": player.character_used,
            "voice_used": player.voice_used,
            "score_history": list(player.score_history),
            "round_number_history": list(player.round_number_history),
        } for player in game_state.players],
    })
    return state


async def send_state_to(game_state, player_index: int, **kwargs) -> None:
    player = game_state.players[player_index]
    if player.is_bot or not player.online or game_state.game_server is None:
        return
    connection = getattr(game_state.game_server, "user_id_to_connection", {}).get(
        player.user_id
    )
    if connection is None or getattr(connection, "websocket", None) is None:
        return
    sync_mode = kwargs.get("sync_mode", "events")
    if sync_mode not in {"events", "round_start", "reconnect"}:
        raise ValueError(f"invalid Hongque sync mode: {sync_mode}")
    message_type = {
        "round_start": "gamestate/hongque/game_start",
        "reconnect": "gamestate/hongque/reconnect",
    }.get(sync_mode, "gamestate/hongque/update")
    await connection.websocket.send_json({
        "type": message_type,
        "success": True,
        "message": game_state.message,
        "gamestate_id": game_state.gamestate_id,
        "hongque_state": build_state(game_state, player_index, **kwargs),
    })


async def broadcast_state(game_state, **kwargs) -> None:
    await asyncio.gather(*(
        send_state_to(game_state, player.index, **kwargs)
        for player in game_state.players
    ))
