"""保存单次结算消息，重连只重发快照，不重新收付。"""
import logging
from ...response import Response,Show_result_info

logger = logging.getLogger(__name__)


async def broadcast_result(state,**fields):
    state.server_action_tick += 1
    response = Response(type="gamestate/jiujiang/show_result",success=True,message="显示结算结果",
        show_result_info=Show_result_info(**fields,action_tick=state.server_action_tick))
    state._terminal_result = response.model_dump(exclude_none=True)
    for player in state.player_list:
        try:
            await state.send_to_realtime_spectators(player.player_index,response)
            connection = state.game_server.user_id_to_connection.get(player.user_id)
            if player.user_id>=10 and "offline" not in player.tag_list and connection is not None:
                await connection.websocket.send_json(state._terminal_result)
        except Exception:
            logger.exception("九江结算发送失败 user_id=%s",player.user_id)
