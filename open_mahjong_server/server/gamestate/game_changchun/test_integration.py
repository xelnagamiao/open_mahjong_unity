"""Whole production executor/record runs plus independent physical conservation."""
import asyncio
import json
import random
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock,patch

import pytest

from .test_state import make_state,hand,ticks
from ...game_calculation.changchun import rules as book
from ...game_calculation.changchun.test_rules import READY
from ...room.changchun_room import ChangchunRoomValidator,normalize_changchun_config,create_changchun_room,handle_create_changchun_room


def assert_supply(state):
    physical=list(state.tiles_list)
    for player in state.player_list:
        physical+=player.hand_tiles+player.discard_tiles
        for code in player.combination_tiles: physical+=list(book.parse_meld(code).physical)
    if state.cc_bao_tile is not None: physical.append(state.cc_bao_tile)
    if state.cc_pending_special: physical.append(state.cc_pending_special['tile'])
    physical += [t for _,t in state.cc_tail_tiles]
    if state.game_status in ('END','waiting_ready') and state.pending_winners and state.pending_winners[0]['source']=='robbing_kong':
        physical.append(state.pending_winners[0]['tile'])
    assert len(physical)==136
    assert Counter(physical)==Counter({t:4 for t in book.TILES})


@pytest.mark.parametrize('seed',[2024,17,901,442])
def test_complete_four_hand_match_production_loop_conserves_physical_and_records(seed):
    state=make_state(random_seed=seed)
    state.db_manager=SimpleNamespace(store_changchun_game_record=lambda *args:'cc-unit-record')
    state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
    state.run_hu_result_ready_phase=AsyncMock()
    rng=random.Random(seed)
    windows=[]
    async def collect(current):
        assert_supply(current)
        allowed={i:list(a) for i,a in current.action_dict.items() if a}
        responses={}
        for index,offered in allowed.items():
            player=current.player_list[index]
            win=next((a for a in offered if a.startswith('hu_')),None)
            if win: data={'action_type':win}
            elif 'cc_draw' in offered: data={'action_type':'cc_draw'}
            elif 'cc_pass' in offered: data={'action_type':'cc_pass'}
            elif 'cc_special' in offered: data={'action_type':'cc_special','target_tile':0}
            elif 'cc_added' in offered: data={'action_type':'cc_added','target_tile':0}
            elif 'angang' in offered:
                data={'action_type':'angang','target_tile':next(t for t in player.hand_tiles if current.kong_allowed(index,t,'concealed'))}
            elif 'jiagang' in offered:
                data={'action_type':'jiagang','target_tile':next(t for t in player.hand_tiles if current.kong_allowed(index,t,'added'))}
            elif 'riichi_cut' in offered:
                tile=sorted(player.riichi_candidate_cuts)[0]
                data={'action_type':'riichi_cut','TileId':tile,'cutIndex':player.hand_tiles.index(tile),'cutClass':False}
            elif 'cut' in offered:
                tile=player.last_drawn_tile if player.ready_locked else rng.choice(player.hand_tiles)
                data={'action_type':'cut','TileId':tile,'cutIndex':player.hand_tiles.index(tile),'cutClass':tile==player.last_drawn_tile}
            elif 'gang' in offered: data={'action_type':'gang'}
            elif 'peng' in offered and rng.random()<.6: data={'action_type':'peng'}
            else: data={'action_type':'pass'}
            responses[index]=data
        windows.append((current.game_status,current.cc_window))
        assert len(windows)<2000,'non-progressing action cycle'
        return responses,allowed
    with patch('server.gamestate.game_changchun.ChangchunGameState._collect_responses',side_effect=collect), \
         patch('server.gamestate.game_taiwan.wait_action._collect_responses',side_effect=collect), \
         patch('server.gamestate.game_changchun.lifecycle.asyncio.sleep',new=AsyncMock()):
        asyncio.run(state.game_loop_chinese())
    assert_supply(state)
    assert len(state.game_record['game_round'])==4
    assert sum(p.score for p in state.player_list)==0
    assert all(len(p.score_history)==4 for p in state.player_list)
    assert any(window=='final_four' for _,window in windows)
    for record in state.game_record['game_round'].values():
        assert record['rule_version']==book.EDITION
        assert record['action_ticks'][-1][0]=='end'
    state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()


@pytest.mark.parametrize('raw',[[],True,{'fan_cap':5},{'ordinary_jokers':1},{'unexpected':False},{'edition':'old'}])
def test_fixed_rule_config_rejects_unsupported_overrides(raw):
    with pytest.raises(ValueError): normalize_changchun_config(raw)


@pytest.mark.parametrize('field,value',[('sub_rule','changchun/folk'),('use_flowers',True),('hepai_limit',2),('claim_protection',True),('open_cuohe',True)])
def test_room_validator_branches(field,value):
    with pytest.raises(ValueError): ChangchunRoomValidator(room_name='长春',round_timer=20,step_timer=5,**{field:value})


def room_manager():
    connection=SimpleNamespace(user_id=101,current_room_id=None,username='cc-test')
    server=SimpleNamespace(players={'c':connection},db_manager=SimpleNamespace(get_user_settings=lambda uid:{'username':'cc-test'}))
    return SimpleNamespace(game_server=server,rooms={},room_passwords={},_generate_room_id=lambda:'123456',
        _reject_room_entry_conflicts=lambda *args:None,_normalize_event_id=lambda value:value,
        _validate_event_for_room=lambda *args:None,_apply_event_fields=lambda *args:None,_broadcast_room_info=AsyncMock())


def test_create_room_and_router_roundtrip():
    manager=room_manager()
    response=asyncio.run(create_changchun_room(manager,'c',room_name='长春测试',gameround=1,password='123',detailed_config={}))
    assert response.success and manager.rooms['123456']['room_rule']=='changchun'
    assert manager.rooms['123456']['detailed_config']==normalize_changchun_config()
    assert manager.room_passwords=={'123456':'123'}
    manager=room_manager();socket=SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_create_changchun_room(SimpleNamespace(room_manager=manager),'c',{'roomname':'长春'},socket))
    assert socket.send_json.call_args.args[0]['success']


@pytest.mark.parametrize('reason',['no_connection','no_login','already_room','conflict','event','bad_config','no_settings'])
def test_room_creation_failures_make_no_room(reason):
    manager=room_manager();kwargs={}
    from ...response import Response
    failed=Response(type='tips',success=False,message='已占用')
    if reason=='no_connection': manager.game_server.players.clear()
    elif reason=='no_login': manager.game_server.players['c'].user_id=None
    elif reason=='already_room': manager.game_server.players['c'].current_room_id='other'
    elif reason=='conflict': manager._reject_room_entry_conflicts=lambda *args:failed
    elif reason=='event': manager._validate_event_for_room=lambda *args:failed
    elif reason=='bad_config': kwargs={'detailed_config':{'fan_cap':99}}
    elif reason=='no_settings': manager.game_server.db_manager.get_user_settings=lambda uid:None
    response=asyncio.run(create_changchun_room(manager,'c',room_name='长春',**kwargs))
    assert not response.success and not manager.rooms


def test_game_registry_loads_callable_class():
    from . import ChangchunGameState
    assert callable(ChangchunGameState)
