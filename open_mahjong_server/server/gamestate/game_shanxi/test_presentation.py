"""Actual start/result JSON and reconnect presentation contracts."""
import asyncio
import copy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch
import pytest

from .test_state import make_state, BASE
from .ShanxiGameState import ShanxiGameState


def sockets(state):
    for player in state.player_list:
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(
            websocket=SimpleNamespace(send_json=AsyncMock()))
    state.send_to_realtime_spectators = AsyncMock()


@pytest.mark.parametrize('winner', [None, 0, 1])
def test_terminal_result_reveals_all_hands_only_after_hand_ends(winner):
    from .ShanxiGameState import broadcast_result
    state = make_state()
    sockets(state)
    for i, player in enumerate(state.player_list): player.hand_tiles = BASE+[41+i]
    hands = {p.player_index:list(p.hand_tiles) for p in state.player_list}
    fields = dict(hu_class='liuju' if winner is None else 'hu_first')
    if winner is not None:
        fields.update(hepai_player_index=winner, hepai_player_hand=hands[winner]+[41+winner])
        hands[winner] = fields['hepai_player_hand']
    asyncio.run(broadcast_result(state, **fields))
    for connection in state.game_server.user_id_to_connection.values():
        payload = connection.websocket.send_json.await_args.args[0]
        assert payload['show_result_info']['revealed_hands'] == hands
    assert state.send_to_realtime_spectators.await_count == 4
    # Cached result is an immutable terminal snapshot, not aliased hand lists.
    state.player_list[0].hand_tiles.clear()
    assert state._terminal_result['show_result_info']['revealed_hands'] == hands


def test_terminal_spectators_receive_result_when_watched_seat_is_offline():
    from .ShanxiGameState import broadcast_result
    state=make_state(); sockets(state)
    state.player_list[0].tag_list.append('offline')
    asyncio.run(broadcast_result(state, hu_class='liuju'))
    assert state.send_to_realtime_spectators.await_count==4
    assert state.game_server.user_id_to_connection[state.player_list[0].user_id].websocket.send_json.await_count==0


def test_terminal_delivery_failure_does_not_block_other_players_or_spectators():
    from .result import broadcast_result
    state=make_state(); sockets(state)
    first=state.game_server.user_id_to_connection[state.player_list[0].user_id]
    first.websocket.send_json.side_effect=ConnectionError('disconnected during result')
    state.send_to_realtime_spectators.side_effect=[ConnectionError('observer disconnected'),None,None,None]
    asyncio.run(broadcast_result(state,hu_class='liuju'))
    assert state.send_to_realtime_spectators.await_count==4
    for player in state.player_list[1:]:
        message=state.game_server.user_id_to_connection[player.user_id].websocket.send_json.await_args.args[0]
        assert set(message['show_result_info']['revealed_hands'])=={0,1,2,3}
    assert state._terminal_result['show_result_info']['hu_class']=='liuju'


@pytest.mark.parametrize('connected', [False,True])
def test_no_terminal_cache_or_missing_connection_does_not_invent_a_result(connected):
    from .result import restore_terminal_result
    state=make_state(); sockets(state)
    uid=state.player_list[0].user_id
    connection=state.game_server.user_id_to_connection[uid]
    if not connected:
        state._terminal_result={'show_result_info':{'hu_class':'liuju'}}
        del state.game_server.user_id_to_connection[uid]
    tick=state.server_action_tick
    asyncio.run(restore_terminal_result(state,uid))
    connection.websocket.send_json.assert_not_awaited()
    assert state.server_action_tick==tick


def test_reconnect_restores_terminal_result_without_recounting_or_replaying_voice():
    from .ShanxiGameState import broadcast_result
    state=make_state(); sockets(state)
    asyncio.run(broadcast_result(state, hu_class='liuju', score_changes={0:0,1:0,2:0,3:0}))
    before=copy.deepcopy(state._terminal_result)
    tick=state.server_action_tick
    with patch('server.gamestate.game_taiwan.TaiwanGameState.TaiwanGameState.player_reconnect',new_callable=AsyncMock):
        asyncio.run(state.player_reconnect(state.player_list[1].user_id))
    payload=state.game_server.user_id_to_connection[state.player_list[1].user_id].websocket.send_json.await_args.args[0]
    assert payload['show_result_info']['silent'] is True
    assert payload['show_result_info']['revealed_hands']==before['show_result_info']['revealed_hands']
    assert state._terminal_result==before and state.server_action_tick==tick
    state._reset_hand_runtime()
    assert state._terminal_result is None


def test_opening_and_midhand_payloads_keep_other_players_hands_private():
    from ..game_taiwan.boardcast import broadcast_game_start, send_reconnect_game_state
    state=make_state(); sockets(state)
    state.init_tiles()
    asyncio.run(broadcast_game_start(state))
    for viewer in state.player_list:
        connection=state.game_server.user_id_to_connection[viewer.user_id]
        info=connection.websocket.send_json.await_args.args[0]['game_info']
        for player in info['players_info']:
            if player['player_index']==viewer.player_index:
                assert player['hand_tiles']==viewer.hand_tiles
            else: assert 'hand_tiles' not in player
    with patch('server.gamestate.game_taiwan.boardcast.reconnected_send_pending_ask',new_callable=AsyncMock):
        asyncio.run(send_reconnect_game_state(state,state.player_list[0]))
    info=state.game_server.user_id_to_connection[state.player_list[0].user_id].websocket.send_json.await_args.args[0]['game_info']
    assert 'revealed_hands' not in info
