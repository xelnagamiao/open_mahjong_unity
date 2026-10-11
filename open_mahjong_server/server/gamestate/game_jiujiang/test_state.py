import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .JiujiangGameState import JiujiangGameState
from ...game_calculation.jiujiang import patterns as rule
from ...game_calculation.jiujiang.test_scoring import PLAIN
from ..public.game_record_manager import init_game_record,init_game_round
from ..game_taiwan.boardcast import broadcast_do_action,send_reconnect_game_state


def make_state():
    server=SimpleNamespace(user_id_to_connection={},gamestate_manager=SimpleNamespace(),room_manager=SimpleNamespace())
    state=JiujiangGameState(server,{'room_id':'123456','player_list':[101,102,103,104],
        'sub_rule':rule.SUB_RULE,'round_timer':0,'step_timer':0,'game_round':1,'tips':True,
        'room_type':'custom','allow_spectator':False},None,None,'jiujiang-test')
    for i,p in enumerate(state.player_list):
        p.player_index=p.original_player_index=i
        p.discard_count=1;p.first_discard_done=True
    state.tiles_list=[21,22,23,24,25,26]
    init_game_record(state);init_game_round(state)
    return state


def drawn(player,hand):
    player.hand_tiles=list(hand);player.has_draw_slot=True;player.last_drawn_tile=hand[-1]


def ticks(state): return state.game_record['game_round'][f'round_index_{state.round_index}']['action_ticks']


@pytest.mark.parametrize('seed',range(12))
def test_deal_wall_and_hidden_kong(seed):
    state=make_state();state.master_seed=seed;state.init_tiles()
    all_tiles=state.tiles_list+[t for p in state.player_list for t in p.hand_tiles]
    assert Counter(all_tiles)==Counter({t:4 for t in rule.WALL_TILES})
    assert [len(p.hand_tiles) for p in state.player_list]==[14,13,13,13]
    assert len(state.tiles_list)==67 and state.dead_wall_count==0
    assert state.jj_break_side==(sum(state.jj_dice)-1)%4
    assert state.build_concealed_kong_mask([45]*4)==[2,45]*4
    assert not state.player_list[1].has_draw_slot


@pytest.mark.parametrize('count',[0,1,4,20])
def test_last_tile_claim_and_replacement_gates(count):
    state=make_state();state.tiles_list=[21]*count;p=state.player_list[1]
    drawn(p,[11]*4+[47]+[22,22,23,24,25,26,27,28,29])
    assert ('buhua' in state.check_hand_actions(1)[1])==bool(count)
    assert ('angang' in state.check_hand_actions(1)[1])==bool(count)
    p.hand_tiles.remove(11)
    assert ('peng' in state.check_discard_actions(11)[1])==bool(count)
    assert ('gang' in state.check_discard_actions(11)[1])==bool(count)
    assert all(not a for a in state.check_discard_actions(47).values())


@pytest.mark.parametrize('length',[4,7,10,13])
def test_chi_only_four_hand_tiles_and_only_upper_player(length):
    state=make_state()
    for p in state.player_list: p.hand_tiles=[12,13,25,25]+[21]*(length-4)
    actions=state.check_discard_actions(11)
    assert ('chi_right' in actions[1])==(length==4)
    assert all(not any(a.startswith('chi_') for a in actions[i]) for i in (0,2,3))


def test_fa_is_optional_and_discarded_fa_has_no_flower_credit():
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN[:-1]+[47])
        await state._opening_flower_replacement()
        assert p.hand_tiles[-1]==47 and not p.huapai_list
        assert {'buhua','cut'}<=set(state.check_hand_actions(0)[0])
        await state.execute_cut(0,{'TileId':47,'TileIndex':13,'CutClass':True})
        assert p.discard_tiles==[47] and not p.huapai_list and state.game_status=='deal_card'
    asyncio.run(run())


@pytest.mark.parametrize('tile',[47,25])
def test_last_fa_draw_can_discard_any_held_tile(tile):
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN[:-1]+[47]);state.tiles_list=[]
        before=p.hand_tiles.copy();await state.execute_buhua(0)
        assert p.hand_tiles==before and 'buhua' not in state.check_hand_actions(0)[0]
        i=p.hand_tiles.index(tile)
        await state.execute_cut(0,{'TileId':tile,'TileIndex':i,'CutClass':i==13})
        assert p.discard_tiles==[tile] and not p.huapai_list
    asyncio.run(run())


def test_ordinary_fa_replacement_allows_win_without_kong_bonus():
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN[:-1]+[47]);state.tiles_list=[25,26]
        await state.execute_buhua(0)
        assert p.huapai_list==[47] and p.last_drawn_tile==25
        assert 'hu_self' in state.check_hand_actions(0)[0]
        assert not any('杠' in n for n in state.score_candidate(0,'self_draw')['fan_names'])
    asyncio.run(run())


def test_report_ready_first_cut_only_and_locked_draw_discard():
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN);p.discard_count=0;p.first_discard_done=False
        assert 25 in state.ready_candidate_cuts(0)
        await state.execute_cut(0,{'TileId':25,'TileIndex':13,'CutClass':True},declare_ready=True)
        assert p.declared_ready and p.ready_locked and 'declared_ready' in p.tag_list
        assert not state.ready_candidate_cuts(0)
        drawn(p,p.hand_tiles+[29]);before=p.hand_tiles.copy()
        await state.execute_cut(0,{'TileId':11,'TileIndex':0,'CutClass':False})
        assert p.hand_tiles==before
        await state.execute_cut(0,{'TileId':29,'TileIndex':13,'CutClass':True})
        assert p.discard_tiles==[25,29]
        assert not any(a in ('peng','gang','chi_right') for a in state.check_discard_actions(11)[0])
    asyncio.run(run())


@pytest.mark.parametrize('added,locked,draw_tile',[(a,l,t) for a in (False,True) for l in (False,True) for t in (11,29)])
def test_ready_kongs_only_with_newly_drawn_tile(added,locked,draw_tile):
    state=make_state();p=state.player_list[0]
    p.combination_tiles=['k11'] if added else []
    drawn(p,([11] if added else [11]*4)+[21,22,23,24,25,26,27,28,29])
    p.last_drawn_tile=draw_tile;p.ready_locked=locked
    assert (11 in state._kong_candidates(0,added))==(not locked or draw_tile==11)
    p.has_draw_slot=False
    assert not state._kong_candidates(0,added)


@pytest.mark.parametrize('discarder',range(4))
def test_head_bump_order_is_from_discarder(discarder):
    async def run():
        state=make_state();state.current_player_index=discarder
        state.player_list[discarder].discard_tiles=[25]
        for p in state.player_list:
            if p.player_index!=discarder: p.hand_tiles=PLAIN[:-1]
        actions=state.check_discard_actions(25)
        responses={i:{'action_type':next(a for a in opts if a.startswith('hu_'))} for i,opts in actions.items() if opts}
        await state.resolve_discard_responses(responses,actions)
        assert [i['index'] for i in state.pending_winners]==[(discarder+1)%4]
        assert state.next_dealer()==(discarder+1)%4
    asyncio.run(run())


def test_private_hints_and_hidden_kong_on_reconnect_and_action():
    async def run():
        state=make_state();p=state.player_list[0];p.hand_tiles=PLAIN[:-1]
        for q in state.player_list: state.game_server.user_id_to_connection[q.user_id]=SimpleNamespace(websocket=AsyncMock())
        await state.prepare_private_fields(range(4))
        assert state.build_private_game_info_fields(0)['jiujiang_hints']['waiting_details'][25]['points']==2
        assert not state.build_private_do_action_info(0,1)
        p.combination_mask=[[2,45]*4];p.combination_tiles=['G45']
        await broadcast_do_action(state,['angang'],0,combination_mask=[2,45]*4,combination_target='G45')
        packet=state.game_server.user_id_to_connection[102].websocket.send_json.await_args.args[0]
        assert packet['do_action_info']['combination_mask']==[2,0]*4
        assert packet['do_action_info']['combination_target']=='G0'
        await send_reconnect_game_state(state,state.player_list[1])
        packet=state.game_server.user_id_to_connection[102].websocket.send_json.await_args.args[0]
        assert packet['game_info']['jiujiang_info']['self_has_draw_slot']==False
    asyncio.run(run())
