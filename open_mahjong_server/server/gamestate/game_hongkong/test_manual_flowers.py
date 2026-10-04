"""Flower replacement is a player decision, also used by the auto-flower UI."""
import asyncio
from copy import deepcopy

import pytest

from .test_branches import opening_flow, table
from .test_flow import assert_conservation
from .bot import choose_action
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.models import PROFILES


def initial_table():
    return opening_flow(PROFILES[2], {
        0: [51,52,11,12,13,14,15,16,17,21,22,23,24,25,26,27],
        1: [53,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36],
    }, tail=[46,45,54])


def replace_one(state, window):
    actor = window['player']
    assert window['actions'] == {i: ['buhua'] if i == actor else [] for i in range(4)}
    result = state.apply_action_results(window, {actor: {'action_type':'buhua'}})
    assert_conservation(state)
    return result


def test_opening_waits_for_each_flower_and_defers_new_flowers_to_next_round():
    state, window = initial_table()
    assert window['status'] == 'waiting_buhua_round'
    assert all(not p.huapai_list for p in state.player_list)
    assert not state.domain_events
    before = len(state.tiles_list)
    window = replace_one(state, window)
    assert len(state.tiles_list) == before - 1
    assert state.player_list[0].huapai_list == [52]
    assert 54 in state.player_list[0].hand_tiles
    assert window['player'] == 0
    window = replace_one(state, window)
    assert state.player_list[0].huapai_list == [52,51]
    assert window['player'] == 1
    window = replace_one(state, window)
    assert state.player_list[1].huapai_list == [53]
    assert window['player'] == 0
    assert state.player_list[0].hand_tiles[-1] != 54
    window = replace_one(state, window)
    assert state.player_list[0].huapai_list == [52,51,54]
    events = [e for e in state.domain_events if e['action'] in ('buhua','deal_buhua_tile')]
    assert [e['action'] for e in events[:6]] == ['buhua','deal_buhua_tile']*3


@pytest.mark.parametrize('profile', [PROFILES[0], PROFILES[2]])
def test_drawn_flower_stays_in_hand_until_manual_replacement(profile):
    state, _ = table(profile, flowers={1:[51]}, actor=0, tail=46)
    state.tiles_list.remove(52)
    state.tiles_list.insert(0,52)
    window = state.open_action_window(state.draw_for(1,normal=True))
    assert window['status'] == 'waiting_buhua_round'
    assert state.player_list[1].hand_tiles[-1] == 52
    assert state.player_list[1].huapai_list == [51]
    assert choose_action(state,1,window['actions'][1])['action_type'] == 'buhua'
    window = replace_one(state,window)
    assert state.machine.phase == P.TURN
    assert state.player_list[1].hand_tiles[-1] == 46
    assert state.player_list[1].huapai_list == [51,52]
    messages = state.outbound_payloads
    reveal = next(i for i,p in enumerate(messages) if (p.get('do_action_info') or {}).get('action_list') == ['buhua'])
    assert messages[reveal+4] == {'_pause':0.3}
    assert messages[reveal+5]['do_action_info']['action_list'] == ['deal_buhua_tile']


def test_flower_window_restore_timeout_and_rejected_actions_preserve_state():
    state, window = initial_table()
    assert window['status'] == 'waiting_buhua_round'
    before = deepcopy((state.domain_events,state.tiles_list,[p.hand_tiles for p in state.player_list]))
    for actions in ({0:{'action_type':'cut','TileId':11}}, {0:{'action_type':'pass'}}, {1:{'action_type':'buhua'}}):
        with pytest.raises(ValueError):
            state.apply_action_results(window,actions)
        assert (state.domain_events,state.tiles_list,[p.hand_tiles for p in state.player_list]) == before
    restored = state.restore_payloads(0)
    assert restored[-1]['ask_hand_action_info']['action_list'] == ['buhua']
    assert restored[0]['game_info']['self_hand_tiles'] == state.player_list[0].hand_tiles
    assert all(p['hand_tiles'] is None for p in restored[0]['game_info']['players_info'][1:])
    state.step_time = 0
    state.player_list[0].remaining_time = 0
    results = asyncio.run(state.wait_action())
    assert results[0]['action_type'] == 'buhua'
    state.apply_action_results(window,results)
    assert len(state.player_list[0].huapai_list) == 1
    with pytest.raises(ValueError,match='Expired'):
        state.apply_action_results(window,results)
    assert_conservation(state)


def test_next_pass_replacement_keeps_the_drawn_flower_slot_identity():
    state,window=opening_flow(PROFILES[2],{
        0:[51,11,12,13,14,15,16,21,22,23,24,25,26,31,32,33],
    },tail=[46,52])
    window=replace_one(state,window)
    assert state.player_list[0].hand_tiles[-1]==52
    assert window['player']==0
    replace_one(state,window)
    flower_events=[e for e in state.domain_events if e['action']=='buhua']
    assert [e['is_mo_buhua'] for e in flower_events]==[False,True]


def test_ready_player_still_gets_manual_flower_replacement():
    state,_=table(PROFILES[2],flowers={1:[51]},actor=0,tail=46)
    state.player_list[1].declared_ready=True
    state.tiles_list.remove(52)
    state.tiles_list.insert(0,52)
    window=state.open_action_window(state.draw_for(1,normal=True))
    assert window['actions'][1]==['buhua']
    window=replace_one(state,window)
    assert state.player_list[1].declared_ready
    assert 'cut' in window['actions'][1]
    assert state.legal_discard_tiles(1)=={46}


def test_disabling_flowers_profiles_do_not_add_replacement_windows():
    from .test_flow import make_state,open_round
    for profile in PROFILES[:2]:
        state=make_state(profile,flowers=False)
        window=open_round(state)
        assert window['status']==P.TURN.value
        assert all('buhua' not in actions for actions in window['actions'].values())
