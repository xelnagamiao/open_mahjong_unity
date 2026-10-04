"""Flower timing, interruption, exhaustion, and payment-on-pass regressions."""
from copy import deepcopy

import pytest

from .test_flow import make_state, open_round, assert_conservation
from .test_branches import opening_flow, table, act, replace_all
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.models import PROFILES


def test_old_seven_flower_offer_can_be_declined_on_first_natural_turn():
    hand=list(range(51,58))+[11,12,13,21,22,23]
    state, window=opening_flow(PROFILES[0],{1:hand},tail=[45,44,43,42,41,39,38])
    window=replace_all(state,window)
    p=state.player_list[0]
    window=act(state,window,**{'0':dict(action_type='cut',TileId=p.hand_tiles[-1])})
    window=act(state,window)
    assert state.machine.phase==P.FLOWER and window['player']==1
    window=act(state,window,**{'1':'pass'})
    assert state.current_player_index==1 and state.machine.phase==P.TURN
    assert 'seven' in state.player_list[1].flower_choice_declined
    assert state.player_list[1].draw_count==1
    assert not any(p.score for p in state.player_list)
    assert_conservation(state)


def test_old_seven_flower_qualification_is_lost_when_opening_is_interrupted():
    hand=list(range(51,58))+[11,12,13,21,22,23]
    state,_=opening_flow(PROFILES[0],{2:hand},tail=[45,44,43,42,41,39,38])
    assert state.player_list[2].initial_seven_pending
    assert state._offer_flowers(('natural',1)) is None
    state.interrupt_opening()
    assert not state.player_list[2].initial_seven_pending
    assert 'seven' in state.player_list[2].flower_choice_declined
    # Even a restored snapshot without a remembered decline may not reinstate it.
    state.player_list[2].flower_choice_declined.clear()
    assert state._offer_flowers(('natural',2),initial_seven=2) is None


def test_eighth_flower_during_initial_replacement_opens_a_new_win_window():
    hand=list(range(51,58))+[11,12,13,21,22,23]
    state,window=opening_flow(PROFILES[0],{1:hand},tail=[58])
    window=replace_all(state,window)
    assert state.machine.phase==P.FLOWER
    assert state.flower_pending['kind']=='eight'
    assert state.flower_pending['continuation']==('initial',)
    assert len(state.player_list[1].huapai_list)==1
    assert 58 in state.player_list[1].hand_tiles
    window=act(state,window,**{'1':'pass'})
    window=replace_all(state,window)
    assert state.machine.phase==P.TURN
    assert len(state.player_list[1].hand_tiles)==13
    # The older seven-flower flag may still be present, but declining eight
    # must not produce another offer when this nondealer first draws.
    tile=state.player_list[0].hand_tiles[-1]
    window=act(state,window,**{'0':dict(action_type='cut',TileId=tile)})
    act(state,window)
    assert state.machine.phase==P.TURN and state.current_player_index==1
    assert not state.player_list[1].initial_seven_pending
    assert_conservation(state)


def test_sixteen_water_blocks_flower_hu_but_not_one_time_midhand_award():
    state,window=table(PROFILES[2],flowers={1:list(range(51,58)),2:[58]})
    p=state.player_list[1]
    p.water=True
    assert state._offer_flowers(('replacement',1)) is None
    assert [p.score for p in state.player_list]==[0,30,-30,0]
    before=deepcopy(state.immediate_changes)
    state._flower_midway_award(1,'seven_steal',2)
    assert state.immediate_changes==before and not state.ledger.debts
    assert_conservation(state)


def test_sixteen_seven_flowers_waits_for_another_player_to_reveal_eighth():
    state,_=table(PROFILES[2],flowers={1:list(range(51,58))})
    assert state._offer_flowers(('replacement',1)) is None
    assert not state.flower_pending and not any(p.score for p in state.player_list)


def test_eighth_flower_on_a_natural_draw_can_be_declined_and_replaced():
    state,_=table(PROFILES[2],flowers={1:list(range(51,58))},actor=0)
    # Move the remaining physical flower to the live draw edge.
    state.tiles_list.remove(58)
    state.tiles_list.insert(0,58)
    window=state.open_action_window(state.draw_for(1,normal=True))
    assert state.machine.phase==P.FLOWER
    window=act(state,window,**{'1':'pass'})
    window=replace_all(state,window)
    assert state.machine.phase==P.TURN and state.current_player_index==1
    assert state.player_list[1].replacement_kind=='flower'
    assert [p.score for p in state.player_list]==[-40,120,-40,-40]
    assert_conservation(state)


def test_nonwinning_flower_replacement_chain_draws_from_tail():
    state,_=table(PROFILES[0],flowers={1:[51]},actor=0,tail=46)
    state.tiles_list.remove(52)
    state.tiles_list.insert(0,52)
    window=state.open_action_window(state.draw_for(1,normal=True))
    replace_all(state,window)
    assert state.player_list[1].hand_tiles[-1]==46
    assert state.player_list[1].huapai_list==[51,52]
    assert state.player_list[1].replacement_kind=='flower'
    assert_conservation(state)


@pytest.mark.parametrize('opening',[False,True])
def test_exhausted_replacement_wall_finishes_without_index_error(opening):
    # Fault-injection boundary: the normal physical opening cannot exhaust the
    # full wall, but a corrupt/interrupted restore must not index an empty list.
    state=make_state(PROFILES[0],flowers=True)
    state.initialize_round()
    state.tiles_list.clear()
    if opening:
        state._initial_queue=[(0,51)]
        state._initial_cursor=0
        state._initial_next=[]
        result=state._continue_opening()
    else:
        result=state.draw_for(0,kind='flower')
    assert result['status']==P.END.value and state.ended_by=='wall'


def test_scripted_wall_validation_rejects_wrong_copies_in_both_flower_modes():
    for flowers in (False,True):
        state=make_state(PROFILES[0],flowers=flowers)
        with pytest.raises(ValueError,match='exact physical'):
            state.initialize_round(wall=[11]*144)


def test_initial_ready_qualification_and_timeout_guard_boundaries():
    state,window=table(PROFILES[2],opening=True)
    assert state._ready_kind(0,True)=='heaven'
    assert state._ready_kind(0,False)=='earth'
    state.opening_flow_interrupted=True
    state.meld_actors=[1]
    assert state._ready_kind(1,False)=='human'
    state.player_list[1].discard_count=1
    assert state._ready_kind(1,False)=='ordinary'
    state.player_list[0].is_hu=True
    assert not state.can_win(0,'self_draw',11)
    state.player_list[0].forbidden_discards=set(state.player_list[0].hand_tiles)
    with pytest.raises(RuntimeError,match='no legal discard'):
        state._build_timeout_action(0)
    state.player_list[0].forbidden_discards.clear()
    state.waiting_players_list=[]
    assert state._build_timeout_action(0)['action_type']=='cut'


def test_thirteen_heaven_earth_ready_turn_bounds_and_repeated_declaration_tag():
    state,_=table(PROFILES[1],opening=True)
    assert state._ready_kind(0,False)=='heaven'
    assert state._ready_kind(1,True)=='earth'
    state.discard_log=[(0,11)]*8
    assert state._ready_kind(1,True)=='ordinary'
    state.declare_ready(1,'ordinary')
    state.declare_ready(1,'ordinary')
    assert state.player_list[1].tag_list.count('declared_ready')==1
