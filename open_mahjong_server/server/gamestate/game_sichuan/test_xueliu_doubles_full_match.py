"""Drive complete four-human matches through serialized asks and the real action router."""
import asyncio
import json
from unittest.mock import patch

import pytest

from .test_xueliu_doubles import make_state
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
                if 'xueliu_exchange_three' in actions:
                    await submit(state, index, 'xueliu_exchange_three', tick=info['action_tick'], selected_tiles=state._default_xueliu_throw_tiles(index))
                    return
                if 'dingque' in actions:
                    suit = min((1,2,3), key=lambda s: sum(t//10==s for t in player.hand_tiles))
                    await submit(state, index, 'dingque', tick=info['action_tick'], target_tile=suit)
                    return
                if choice is None:
                    if 'cut' in actions:
                        choice, tile, cut_index = choose_hand(list(player.hand_tiles), list(player.combination_tiles), count_visible_tiles(state), actions, 4, player.dingque_suit, player.post_hu_lock)
                    elif 'peng' in actions or 'gang' in actions:
                        tile = state.player_list[state.current_player_index].discard_tiles[-1]
                        choice = choose_claim(list(player.hand_tiles), list(player.combination_tiles), count_visible_tiles(state), actions, tile, 4, player.dingque_suit)
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
def test_complete_xueliu_team_match(seed):
    state, messages = asyncio.run(play_match(seed))
    rounds = state.game_record['game_round']
    assert len(rounds) == len(state.team_round_history) >= 4
    assert state.team_result['winner_team_id'] in (0,1)
    assert sum(state.team_result['scores'].values()) == 0
    assert all(not p.is_hu for p in state.player_list)
    assert state.team_result == state.game_record['game_title']['doubles_result']
    for round_index, record in enumerate(rounds.values()):
        start = round_index%4
        assert record['xueliu_exchange_groups'] == [[start,(start+1)%4],[(start+2)%4,(start+3)%4]]
        assert record['xueliu_exchange_direction'] == 2
        assert sorted(len(record[f'p{i}_tiles']) for i in range(4)) == [13,13,13,14]
        assert len(record['dingque_suits']) == 4
    endings = [m for m in messages if m['type']=='gamestate/sichuan/game_end']
    assert len(endings)==4
    assert any(m['type'].endswith('dingque_done') for m in messages)
    for entry in messages:
        payload=entry['message']
        info=payload.get('game_info',{})
        for p in info.get('players_info',[]):
            if p['user_id']!=entry['user_id']:
                assert 'hand_tiles' not in p and not p['xueliu_throw_tiles']
        ask=payload.get('ask_hand_action_info',{})
        phase=ask.get('xueliu_exchange')
        if phase:
            assert phase['stage'] in (1,2)
            assert len(phase['active_players'])==2
            assert set(phase)=={'stage','active_players','selection_done','teammate_suit','outgoing_suit','animating','animation_seconds'}
            if phase['animating']:
                assert not ask['action_list'] and ask['remaining_time']==0
            elif phase['stage']==1:
                assert phase['teammate_suit']==0
            if ask['player_index'] not in phase['active_players']:
                assert not ask['action_list']
    for team in (0,1):
        assert sum(r['score_changes'][team] for r in state.team_round_history)==state.team_result['scores'][team]
    recorded=endings[0]['message']['game_end_info']['record_detail']['record']
    assert recorded['doubles_result']==json.loads(json.dumps(state.team_result))
