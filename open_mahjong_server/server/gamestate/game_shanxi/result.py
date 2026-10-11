"""MIL V.9: expose all four hands at the terminal result, never midhand."""
import copy
import logging

from ...response import Response, Show_result_info

logger = logging.getLogger(__name__)


async def broadcast_result(state, **fields):
    state.server_action_tick += 1
    hands = {p.player_index: list(p.hand_tiles) for p in state.player_list}
    winner = fields.get('hepai_player_index')
    if winner is not None:
        hands[winner] = list(fields['hepai_player_hand'])
    response = Response(type='gamestate/shanxi/show_result', success=True, message='显示结算结果',
        show_result_info=Show_result_info(**fields, action_tick=state.server_action_tick, revealed_hands=hands))
    state._terminal_result = response.model_dump(exclude_none=True)
    for player in state.player_list:
        # Watching a bot/offline seat must still receive the terminal reveal.
        try:
            await state.send_to_realtime_spectators(player.player_index, response)
        except Exception:
            logger.exception('山西终局观战广播失败 player_index=%s', player.player_index)
        connection = state.game_server.user_id_to_connection.get(player.user_id)
        if player.user_id < 10 or 'offline' in player.tag_list or connection is None:
            continue
        try:
            await connection.websocket.send_json(state._terminal_result)
        except Exception:
            logger.exception('山西终局广播失败 user_id=%s', player.user_id)


async def restore_terminal_result(state, user_id):
    connection = state.game_server.user_id_to_connection.get(user_id)
    if connection is None or state._terminal_result is None:
        return
    payload = copy.deepcopy(state._terminal_result)
    payload['show_result_info']['silent'] = True
    await connection.websocket.send_json(payload)
