"""终局明手与断线恢复；局中不发送其他玩家的手牌。"""
import logging

from ...response import Response, Show_result_info

logger = logging.getLogger(__name__)


async def broadcast_result(state, **fields):
    if getattr(state, "_tactical_silent_action", False):
        fields["silent"] = True
        state._tactical_silent_action = False
    state.server_action_tick += 1
    hands = {p.player_index: list(p.hand_tiles) for p in state.player_list}
    winner = fields.get("hepai_player_index")
    if winner is not None:
        hands[winner] = list(fields["hepai_player_hand"])
    response = Response(type=f"gamestate/{state.room_rule}/show_result", success=True, message="显示结算结果",
        show_result_info=Show_result_info(**fields, action_tick=state.server_action_tick, revealed_hands=hands))
    # 保存不可变的序列化快照，重连只发给该连接，不改变动作窗口或再次记账。
    state._terminal_result = response.model_dump(exclude_none=True)
    for player in state.player_list:
        # 观战连接独立于被观看玩家；玩家掉线、机器人座位或发送失败不能吞掉终局。
        try:
            await state.send_to_realtime_spectators(player.player_index, response)
        except Exception:
            logger.exception("推倒和终局观战广播失败 player_index=%s", player.player_index)
        if player.user_id < 10 or "offline" in player.tag_list:
            continue
        connection = state.game_server.user_id_to_connection.get(player.user_id)
        if connection is None:
            continue
        try:
            await connection.websocket.send_json(state._terminal_result)
        except Exception:
            logger.exception("推倒和终局广播失败 user_id=%s", player.user_id)
