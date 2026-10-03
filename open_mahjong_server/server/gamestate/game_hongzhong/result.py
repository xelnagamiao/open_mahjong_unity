"""终局公开所有手牌；不可变快照用于断线恢复，避免再次支付。"""
import logging

from ...response import Response, Show_result_info

logger = logging.getLogger(__name__)


async def broadcast_result(state, **fields):
    state.server_action_tick += 1
    response = Response(type="gamestate/hongzhong/show_result", success=True, message="显示结算结果",
        show_result_info=Show_result_info(**fields, action_tick=state.server_action_tick,
            revealed_hands={p.player_index: list(p.hand_tiles) for p in state.player_list}))
    state._terminal_result = response.model_dump(exclude_none=True)
    for player in state.player_list:
        try:
            await state.send_to_realtime_spectators(player.player_index, response)
        except Exception:
            logger.exception("红中终局观战广播失败 player_index=%s", player.player_index)
        if player.user_id < 10 or "offline" in player.tag_list:
            continue
        connection = state.game_server.user_id_to_connection.get(player.user_id)
        if connection is None:
            continue
        try:
            await connection.websocket.send_json(state._terminal_result)
        except Exception:
            logger.exception("红中终局广播失败 user_id=%s", player.user_id)
