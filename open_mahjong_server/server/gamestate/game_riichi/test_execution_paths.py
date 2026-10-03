"""Execute settlement and timeout branches with their real state mutations."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_rule_branches import game
from .wait_action import wait_action


def result(points,tsumo=False,dealer=False):
    return dict(han=5,fu=30,yaku=['满贯'],score=points,
                cost={'main':4000 if tsumo else points,'additional':4000 if dealer else 2000})


@pytest.mark.parametrize('winner',[0,1,2,3])
@pytest.mark.parametrize('tsumo',[False,True])
@pytest.mark.parametrize('langyong',[False,True])
def test_actual_hu_payment_keeps_honba_and_deposits_outside_langyong_multiplier(winner,tsumo,langyong):
    g=game();g.current_player_index=winner if tsumo else (winner+1)%4
    g.hu_class='hu_self' if tsumo else 'hu_third';g.ron_player_index=winner
    if langyong:g.sub_rule='riichi/langyong'
    # The actual rule derives multipliers from all four players' calls.
    g.player_list[winner].combination_tiles=['s13']
    g.player_list[(winner+1)%4].combination_tiles=['k45','k46']
    g.honba=2;g.riichi_sticks=3
    points=12000 if winner==0 else 8000
    g.result_dict={g.hu_class:result(points,tsumo,winner==0)}
    before=[p.score for p in g.player_list]
    expected=[0]*4
    for payer in range(4):
        if payer==winner or (not tsumo and payer!=g.current_player_index):continue
        base=(4000 if winner==0 or payer==0 else 2000) if tsumo else points
        multiplier=g._langyong_multiplier(payer,winner) if langyong else 1
        payment=base*multiplier+(200 if tsumo else 600)
        expected[payer]-=payment;expected[winner]+=payment
    expected[winner]+=3000
    with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_result',new=AsyncMock()) as broadcast:
        asyncio.run(g._settle_hu(is_last_settle=True))
    assert [p.score-before[i] for i,p in enumerate(g.player_list)]==expected
    assert g.riichi_sticks==0
    tick=g.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[0]=='hu_riichi' and tick[6]==expected and tick[11]==3
    assert broadcast.await_args.kwargs['score_changes']==dict(enumerate(expected))


@pytest.mark.parametrize('dealer_included',[False,True])
@pytest.mark.parametrize('count',[2,3])
def test_multiple_ron_real_settlement_awards_honba_and_deposits_once(dealer_included,count):
    g=game('majsoul');g.honba=2;g.riichi_sticks=3
    g.current_player_index=3 if dealer_included else 0
    winners=[(g.current_player_index+i)%4 for i in range(1,count+1)]
    actions=['hu_first','hu_second','hu_third'][:count]
    queue=list(zip(winners,actions))
    g.result_dict={action:result(12000 if winner==0 else 8000) for winner,action in queue}
    expected=[0]*4
    for index,winner in enumerate(winners):
        paid=(12000 if winner==0 else 8000)+(600 if index==0 else 0)
        expected[g.current_player_index]-=paid;expected[winner]+=paid+(3000 if index==0 else 0)
    before=[p.score for p in g.player_list]
    with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_result',new=AsyncMock()) as broadcast, \
         patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase',new=AsyncMock()) as ready, \
         patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep',new=AsyncMock()):
        asyncio.run(g._settle_multi_ron_sequence(queue))
    assert [p.score-before[i] for i,p in enumerate(g.player_list)]==expected
    assert g._compute_last_renchan() is dealer_included
    ticks=g.game_record['game_round']['round_index_1']['action_ticks']
    hu=[t for t in ticks if t[0]=='hu_riichi']
    assert [t[11] for t in hu]==[3]+[0]*(count-1)
    assert sum(sum(t[6]) for t in hu)==3000 and g.riichi_sticks==0
    assert broadcast.await_count==count
    assert ready.await_count==(0 if min(p.score for p in g.player_list)<0 else 1)
    assert broadcast.await_args_list[0].kwargs['simultaneous_hu_hands'].keys()==set(winners)
    assert all(call.kwargs['skip_hand_reveal'] for call in broadcast.await_args_list[1:])


@pytest.mark.parametrize('status',['waiting_hand_action','onlycut_after_action'])
def test_timed_out_player_discards_safely_and_never_cuts_kuikae_tile(status):
    g=game();g.game_status=status;g.step_time=0
    for p in g.player_list:p.remaining_time=0
    p=g.player_list[0];p.hand_tiles=[11,12,13];p.has_draw_slot=status=='waiting_hand_action'
    p.kuikae_forbidden_tiles=[] if p.has_draw_slot else [13]
    g.action_dict={0:['cut'],1:[],2:[],3:[]}
    with patch('server.gamestate.game_riichi.wait_action.broadcast_do_action',new=AsyncMock()) as broadcast:
        asyncio.run(wait_action(g))
    assert len(p.hand_tiles)==2 and len(p.discard_tiles)==1
    assert p.discard_tiles[0]==13 if status=='waiting_hand_action' else p.discard_tiles[0]!=13
    assert not p.has_draw_slot
    assert broadcast.await_args.kwargs['is_timeout_action']


@pytest.mark.parametrize('permanent',[False,True])
@pytest.mark.parametrize('four_kan',[False,True])
def test_timeout_pass_still_commits_riichi_and_sets_correct_furiten(permanent,four_kan):
    g=game();g.game_status='waiting_action_after_cut';g.step_time=0
    for p in g.player_list:p.remaining_time=0
    p=g.player_list[0];p.discard_tiles=[33];p.pending_riichi=True
    other=g.player_list[1];other.tag_list=['riichi'] if permanent else []
    g.action_dict={0:[],1:['hu_first','pass'],2:[],3:[]}
    g._pending_four_kan_abort=four_kan
    asyncio.run(wait_action(g))
    assert p.score==24000 and g.riichi_sticks==1 and not p.pending_riichi
    assert other.riichi_furiten if permanent else other.temp_furiten
    assert g.game_status==('END' if four_kan else 'deal_card')


def test_human_riichi_auto_cut_timeout_uses_drawn_tile():
    g=game();g.game_status='waiting_hand_action';g.current_player_index=0
    p=g.player_list[0];p.tag_list=['riichi'];p.hand_tiles=[11,12,13];p.has_draw_slot=True
    g.action_dict={0:['cut'],1:[],2:[],3:[]}
    async def expire(awaitable,timeout):
        awaitable.close()
        raise asyncio.TimeoutError
    with patch('server.gamestate.game_riichi.wait_action.asyncio.wait_for',expire):
        asyncio.run(wait_action(g))
    assert p.discard_tiles==[13] and p.hand_tiles==[11,12]
    assert not p.has_draw_slot


def test_ready_timeout_clears_all_waiters_and_broadcasts_once():
    g=game();g.game_status='waiting_ready';g.step_time=0
    for p in g.player_list:p.remaining_time=0
    g.action_dict={i:['ready'] for i in range(4)}
    with patch('server.gamestate.game_riichi.wait_action.broadcast_ready_status',new=AsyncMock()) as broadcast:
        assert asyncio.run(wait_action(g)) is False
    assert not any(g.action_dict.values())
    broadcast.assert_awaited_once()
