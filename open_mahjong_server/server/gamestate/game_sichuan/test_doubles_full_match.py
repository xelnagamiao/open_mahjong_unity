"""Drive complete four-human matches through serialized asks and the real action router."""
import asyncio
import json
from unittest.mock import patch

import pytest

from .test_doubles import make_state
from .test_action_window import submit
from .xueliu_bot import choose_hand, choose_claim
from ..public.ai.smart_bot_logic import count_visible_tiles
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.hand_slot_utils import has_draw_slot


async def play_match(seed):
    state, messages = make_state()
    state.room_random_seed = seed
    state.db_manager.store_sichuan_game_record.return_value = None
    real_sleep = asyncio.sleep

    async def fast_sleep(delay=0, result=None):
        await real_sleep(0)
        return result

    for connection in state.game_server.user_id_to_connection.values():
        original = connection.websocket.send_json
        uid = connection.user_id

        async def send(payload, original=original, uid=uid):
            await original(payload)
            player = next(p for p in state.player_list if p.user_id == uid)
            index = player.player_index
            info = payload.get('ask_hand_action_info') or payload.get('ask_other_action_info')
            if info and info.get('action_list'):
                actions = info['action_list']
                choice = next((a for a in actions if a.startswith('hu')), None)
                tile = cut_index = None
                if choice is None:
                    if 'cut' in actions:
                        choice, tile, cut_index = choose_hand(list(player.hand_tiles), list(player.combination_tiles), count_visible_tiles(state), actions, 4, 0)
                    elif 'peng' in actions or 'gang' in actions:
                        tile = state.player_list[state.current_player_index].discard_tiles[-1]
                        choice = choose_claim(list(player.hand_tiles), list(player.combination_tiles), count_visible_tiles(state), actions, tile, 4, 0)
                    else:
                        choice = 'pass'
                fields = {'targetTile': tile} if choice in ('angang', 'jiagang') else {}
                if choice == 'cut':
                    fields = {'TileId': tile, 'cutIndex': cut_index, 'cutClass': has_draw_slot(player) and cut_index == len(player.hand_tiles)-1}
                await submit(state, index, choice, tick=info['action_tick'], **fields)
            elif payload.get('ready_status_info') and state.action_dict.get(index) == ['ready']:
                await submit(state, index, 'ready')
        connection.websocket.send_json = send

    try:
        with patch('asyncio.sleep', fast_sleep):
            await asyncio.wait_for(state.game_loop_sichuan(), 120)
        return state, messages
    finally:
        await cancel_auxiliary_tasks(state)


@pytest.mark.parametrize('seed', [20261009, 20261010, 20261011])
def test_four_boards_finish_with_private_snapshots_and_consistent_team_record(seed):
    state, messages = asyncio.run(play_match(seed))
    assert len(state.team_round_history) == 4
    assert len(state.game_record['game_round']) == 4
    for board, record in enumerate(state.game_record['game_round'].values()):
        assert record['dealer_index'] == record['start_player_index'] == board % 4
    assert state.team_result == state.game_record['doubles_result']
    teams = state.game_record['game_title']['team_by_player_index']
    assert teams[0] == teams[2] != teams[1] == teams[3]
    endings = [m for m in messages if m['type'] == 'gamestate/sichuan/game_end']
    assert len(endings) == 4
    for entry in messages:
        payload = entry['message']
        assert not entry['type'].endswith('ask_dingque')
        if 'game_info' in payload:
            info = payload['game_info']
            assert info['dealer_index'] == (info['current_round'] - 1) % 4
            for player in payload['game_info']['players_info']:
                if player['user_id'] != entry['user_id']:
                    assert 'hand_tiles' not in player
        info = payload.get('show_result_info', {})
        if info.get('defer_score_settlement'):
            assert 'hu_fan' not in info and 'hu_score' not in info
    for t in (0,1):
        assert sum(r['score_changes'][t] for r in state.team_round_history) == state.team_result['scores'][t]
    assert endings[0]['message']['game_end_info']['record_detail']['record']['doubles_result'] == json.loads(json.dumps(state.team_result))
