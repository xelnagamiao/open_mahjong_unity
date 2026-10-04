"""Ron selection, invalid declarations and no-yaku furiten."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_rule_branches import game
from .ron_resolution import resolve_collected_rons, should_interrupt_wait_for_action
from .wait_action import _execute_cut


def claims(mode, hans):
    g = game(); g.hepai_way = mode; g.current_player_index = 0
    g.player_list[0].discard_tiles = [33]
    g.player_list[0].discard_riichi_flags = [False]
    g._pending_ron_claims = {i + 1: ['hu_first','hu_second','hu_third'][i] for i in range(len(hans))}
    g.result_dict = {a: dict(han=hans[i-1], no_yaku=hans[i-1]==0)
                     for i,a in g._pending_ron_claims.items()}
    return g


@pytest.mark.parametrize('mode,count,abort', [('multi_ron',1,False),('multi_ron',2,False),('multi_ron',3,False),
                                           ('three_ron_abort',2,False),('three_ron_abort',3,True)])
def test_valid_multi_ron_modes(mode,count,abort):
    g = claims(mode,[1]*count)
    with patch('server.gamestate.game_riichi.ron_resolution.THREE_RON_ABORT_HOLD_SEC',0):
        assert asyncio.run(resolve_collected_rons(g,33,list(range(1,count+1))))
    assert g.game_status == 'END'
    assert (g.hu_class == 'three_ron_abort') is abort
    if not abort:
        assert all(g.player_list[i].hand_tiles[-1] == 33 for i in range(1,count+1))


@pytest.mark.parametrize('mode',['multi_ron','three_ron_abort'])
def test_valid_win_precedes_wrong_win_and_cannot_trigger_triple_abort(mode):
    g = claims(mode,[0,2,0])
    assert asyncio.run(resolve_collected_rons(g,33,[1,2,3]))
    assert g.hu_class == 'hu_second' and g.ron_player_index == 2
    assert not getattr(g,'_pending_cuohe_queue',None)


def test_head_bump_waits_for_valid_further_win_before_wrong_win():
    g = claims('head_bump',[0,1])
    assert not should_interrupt_wait_for_action(g,'hu_first',[2],{2:['hu_second','pass']})
    assert should_interrupt_wait_for_action(g,'hu_second',[1],{1:['hu_first','pass']})


def test_two_wrong_declarations_are_both_settled():
    g = claims('multi_ron',[0,0]); g.open_cuohe=True
    assert asyncio.run(resolve_collected_rons(g,33,[1,2]))
    seen=[]
    async def settle(): seen.append((g.ron_player_index,g.hu_class))
    g._settle_cuohe = settle
    with patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep', new=AsyncMock()) as wait:
        asyncio.run(g._settle_round())
        wait.assert_awaited_once()
    assert seen==[(1,'hu_first'),(2,'hu_second')]


def test_no_yaku_wait_still_creates_same_turn_furiten_without_ron_button():
    g = game(); g.current_player_index=0
    g.player_list[0].hand_tiles=[19];g.player_list[0].has_draw_slot=True
    p=g.player_list[2]
    p.hand_tiles=[12,13,14,23,24,25,31,32,33,41,41,17,18]
    p.waiting_tiles={16,19}
    assert asyncio.run(_execute_cut(g,0,19,True,None,False))
    assert not any(a.startswith('hu_') for a in g.action_dict[2])
    assert p.temp_furiten


@pytest.mark.parametrize('penalty,expected', [('fixed_9000',[3000,4000,3000,-9000]),
                                           ('reverse_mangan',[4000,3000,2000,-8000]),
                                           ('penalty_20000',[0,1000,0,0])])
def test_chombo_refunds_only_current_hand_riichi_once(penalty,expected):
    g=game();g.hu_class='hu_first';g.ron_player_index=3;g.open_cuohe=True
    g.detailed_config['chombo_penalty']=penalty
    g.result_dict={'hu_first':dict(han=0,fu=30,no_yaku=True,yaku=[])}
    scores=[25000,24000,37000,13000]
    for p,s in zip(g.player_list,scores): p.score=s
    g.player_list[1].riichi_paid_this_round=True;g.riichi_sticks=2
    with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_result',new=AsyncMock()):
        asyncio.run(g._settle_cuohe())
    assert [p.score-s for p,s in zip(g.player_list,scores)]==expected
    assert g.riichi_sticks==1 and not g.player_list[1].riichi_paid_this_round
    assert g._cuohe_triggered
