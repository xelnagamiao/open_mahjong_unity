"""Physical-copy, information-boundary and complete-loop tests for Washizu."""
import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_protocol import connected
from .test_rule_branches import game
from .init_tiles import init_riichi_tiles
from .washizu import public_hand, call_candidates, draw_indicator, prepare_ura
from .boardcast import broadcast_game_start, broadcast_do_action
from .action_check import check_hepai, _normalize
from .wait_action import wait_action, _is_valid_cut_action
from ..public.ai.get_action import get_action, get_ai_action
from ..public.ai.smart_bot_logic import find_best_cut, count_visible_tiles
from ..public.hand_slot_utils import remove_cut_tile, hand_contains_tile, resolve_cut_class
from ..public.game_record_manager import build_round_header_data
from ...game_calculation.riichi.washizu_tiles import FACES, make_tiles, normalize


def washizu_game():
    g = game('majsoul')
    g.sub_rule = 'riichi/washizu'
    g.red_dora = False
    g.hepai_way = 'head_bump'
    g.open_xiru = False
    for p in g.player_list:
        p.hand_tiles = []
        p.has_draw_slot = False
    init_riichi_tiles(g)
    return g


@pytest.mark.parametrize('seed', [0, 42, 2147483647])
def test_three_glass_one_opaque_and_seventy_draws(seed):
    g = washizu_game()
    g.master_seed = seed
    for p in g.player_list: p.hand_tiles = []
    init_riichi_tiles(g)
    all_tiles = g.tiles_list + g.dora_indicators + sum((p.hand_tiles for p in g.player_list), [])
    assert Counter(all_tiles) == Counter(make_tiles())
    assert all(Counter(all_tiles)[t] == 1 and Counter(all_tiles)[t+2000] == 3 for t in FACES)
    assert len(g.tiles_list) - g.dead_wall_count == 70
    for i in range(70):
        if i in (3, 20, 30, 40):
            g.kan_dora_indicators.append(draw_indicator(g))
        g.player_list[i % 4].get_tile(g.tiles_list)
        assert len(g.tiles_list) - g.dead_wall_count == 69-i
    assert len(g.tiles_list) > 0  # A draw limit, no physical dead wall.


def test_private_sort_order_is_never_exposed():
    a = [11, 2012, 47, 2011, 2012, 23]
    assert public_hand(a) == public_hand(list(reversed(a))) == [2011,2012,2012,0,0,0]


def test_cut_cannot_substitute_an_opaque_copy_for_glass():
    hand = [11,2011]
    assert resolve_cut_class(hand, 11, True) == (False, True)
    assert resolve_cut_class(hand, 2011, False) == (True, True)
    assert not hand_contains_tile([11],2011)
    assert not hand_contains_tile([2011],11)
    assert remove_cut_tile(hand, 11, False) == 11
    assert hand == [2011]


def test_choose_glass_or_opaque_pon_and_reject_stale_or_forged_choice():
    async def run():
        g,sockets=connected();g.sub_rule='riichi/washizu'
        p=g.player_list[1];p.hand_tiles=[11,2011,2011,22]
        p.chi_candidates={'peng':call_candidates(p.hand_tiles,2011)}
        assert p.chi_candidates['peng']==[[2011,2011],[11,2011]]
        g.game_status='waiting_action_after_cut';g.waiting_players_list=[1];g.action_dict={1:['peng','pass']}
        for bad in (-1,2,True,'0'):
            await get_action(g,'101','peng',False,None,None,None,chi_combo_index=bad)
            assert g.action_queues[1].empty()
        await get_action(g,'101','peng',False,None,None,None,chi_combo_index=1)
        choice=await g.action_queues[1].get()
        assert choice['chi_combo_index']==1 and _is_valid_cut_action(g,1,choice)
        p.hand_tiles.remove(11)
        assert not _is_valid_cut_action(g,1,choice)
    asyncio.run(run())


def test_public_information_survives_actions_reconnect_and_spectating():
    async def run():
        g,sockets=connected();g.sub_rule='riichi/washizu'
        for p in g.player_list: p.hand_tiles=[2011+p.player_index,41+p.player_index]
        await broadcast_game_start(g)
        await broadcast_do_action(g,['deal'],0,deal_tile=41)
        for uid,conn in sockets.items():
            start,action=[c.args[0] for c in conn.websocket.send_json.call_args_list[-2:]]
            for message in [start['game_info'],action['do_action_info']]:
                assert message['washizu_hands']=={i:[2011+i,0] for i in range(4)}
            assert action['do_action_info'].get('deal_tile')==(41 if uid==100 else None)
        g.game_status='waiting_hand_action';g.current_player_index=1
        g.action_dict={i:[] for i in range(4)}
        await g.player_reconnect(101)
        state=next(c.args[0]['game_info'] for c in reversed(sockets[101].websocket.send_json.call_args_list) if 'game_info' in c.args[0])
        assert state['washizu_hands'][0]==[2011,0]
        spectator=SimpleNamespace(websocket=AsyncMock())
        g.game_server.user_id_to_connection[500]=spectator
        await g.send_realtime_spectator_snapshot(500,2)
        state=spectator.websocket.send_json.call_args_list[0].args[0]['game_info']
        assert state['washizu_hands'][3]==[2014,0]
        assert sum('hand_tiles' in p for p in state['players_info'])==1
    asyncio.run(run())


def test_score_is_independent_of_glass_and_ura_query_does_not_change_draws():
    g=washizu_game();p=g.player_list[0]
    normal=[12,13,14,22,23,24,33,34,35,36,37,38,28,28]
    results=[]
    for glass in (False,True):
        p.hand_tiles=[t+2000 if glass else t for t in normal]
        p.waiting_tiles={28};p.tag_list=['riichi']
        before=list(g.tiles_list)
        check_hepai(g,{i:[] for i in range(4)},p.hand_tiles[-1],0,'tsumo')
        results.append(g.result_dict['hu_self'])
        assert g.tiles_list==before
    assert results[0]['score']==results[1]['score']
    assert results[0]['yaku']==results[1]['yaku']
    header=build_round_header_data(g)
    assert header['washizu']['dora_indicators']==g.dora_indicators
    assert header['washizu']['draw_limit']==70


@pytest.mark.parametrize('seed',[20261010, 12345])
def test_complete_washizu_match_preserves_copies_and_terminates(seed,caplog):
    async def run():
        g=washizu_game();g.max_round=1;g.room_random_seed=seed
        for p in g.player_list: p.hand_tiles=[];p.has_draw_slot=False
        g.game_server.gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock())
        g.game_server.room_manager=SimpleNamespace(finish_custom_game_room=AsyncMock())
        g.game_server.user_id_to_connection={}
        original_sleep=asyncio.sleep
        actions=Counter()
        async def instant_sleep(_): await original_sleep(0)
        async def automated_wait():
            task=asyncio.create_task(wait_action(g));await original_sleep(0)
            try:
                for seat in list(g.waiting_players_list):
                    p=g.player_list[seat];choices=g.action_dict.get(seat,[])
                    if not choices: continue
                    action=next((a for a in choices if a.startswith('hu_') and not g.result_dict.get(a,{}).get('no_yaku')),None)
                    tile=None;target=None
                    if action is None and 'riichi_cut' in choices:
                        action='riichi_cut';tile=next(iter(p.riichi_candidate_cuts))
                    if action is None:
                        for candidate in ['angang','jiagang']:
                            if candidate not in choices: continue
                            for t in p.hand_tiles:
                                if _is_valid_cut_action(g,seat,dict(action_type=candidate,target_tile=normalize(t))):
                                    action=candidate;target=normalize(t);break
                            if action: break
                    if action is None and 'cut' in choices:
                        action='cut'
                        if 'riichi' in p.tag_list: tile=p.hand_tiles[-1]
                        else: tile,_=find_best_cut(p.hand_tiles,len(p.combination_tiles),count_visible_tiles(g),set(map(normalize,p.kuikae_forbidden_tiles)))
                    if action is None:
                        action=next((a for a in ['gang','peng','chi_left','chi_mid','chi_right','pass','ready'] if a in choices),choices[0])
                    actions[action]+=1
                    await get_ai_action(g,seat,action,bool(p.has_draw_slot and tile==p.hand_tiles[-1]),tile,None,target)
                await task
            finally:
                if not task.done():task.cancel()
            if g.game_status not in ('END','waiting_ready'):
                physical=list(g.tiles_list)+g.dora_indicators+g.kan_dora_indicators
                for p in g.player_list:
                    physical+=p.hand_tiles+p.discard_tiles
                    for mask in p.combination_mask:physical+=mask[1::2]
                assert Counter(physical)==Counter(make_tiles()), (g.game_status,actions,Counter(physical)-Counter(make_tiles()))
            assert sum(p.score for p in g.player_list)+g.riichi_sticks*1000==g._starting_score()*4
            assert g.round_index<=4
        g.wait_action=automated_wait
        with patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep',instant_sleep), patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase',new=AsyncMock()), patch('server.gamestate.game_riichi.RiichiGameState.vote_checkpoint',new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_riichi(),timeout=60)
        assert actions['cut']>30 and g._riichi_finalized
        assert all('washizu' in r and r['action_ticks'][-1]==['end'] for r in g.game_record['game_round'].values())
        assert not any(r.levelname=='ERROR' for r in caplog.records)
    asyncio.run(run())


def test_ankan_keeps_transparency_in_records_and_replacement_pool():
    from .kan_actions import begin_kan
    async def run():
        g=washizu_game();g.current_player_index=0
        p=g.player_list[0];p.hand_tiles=[11,2011,2011,22,23,24,31,32,33,45,45,45,47,2011]
        for other in g.player_list[1:]:other.waiting_tiles=set()
        before=len(g.tiles_list)-g.dead_wall_count
        await begin_kan(g,'angang',11)
        assert Counter(p.combination_mask[0][1::2])==Counter([11,2011,2011,2011])
        assert all(normalize(t)!=11 for t in p.hand_tiles)
        expected=g.tiles_list[0]
        p.get_gang_tile(g.tiles_list,g)
        assert p.hand_tiles[-1]==expected
        assert len(g.tiles_list)-g.dead_wall_count==before-1
        await g._reveal_kan_dora()
        assert len(g.tiles_list)-g.dead_wall_count==before-1
        tick=g.game_record['game_round']['round_index_1']['action_ticks'][0]
        assert tick[0]=='ag' and Counter(tick[3:])==Counter([11,2011,2011,2011])
    asyncio.run(run())


def test_bot_only_counts_other_players_public_copies():
    from ..public.ai.smart_bot_logic import tile_to_34
    g=washizu_game()
    for p in g.player_list:p.hand_tiles=[];p.discard_tiles=[];p.combination_tiles=[]
    g.dora_indicators=[2047];g.kan_dora_indicators=[]
    g.player_list[0].hand_tiles=[2011]
    g.player_list[1].hand_tiles=[2011,12]
    visible=count_visible_tiles(g,0)
    assert visible[tile_to_34(11)]==1
    assert visible[tile_to_34(12)]==0
    assert visible[tile_to_34(47)]==1


def test_room_api_canonicalizes_fixed_washizu_rules():
    from ...room.room_validators import RiichiRoomValidator
    room=RiichiRoomValidator(sub_rule='riichi/washizu',room_name='test',game_round=1,round_timer=20,step_timer=5,
                            red_dora=True,open_xiru=True,hepai_way='multi_ron')
    assert room.red_dora is False and room.open_xiru is False and room.hepai_way=='head_bump'


def test_added_kan_preserves_the_actual_fourth_copy():
    from .kan_actions import begin_kan
    async def run():
        for fourth in (11,2011):
            g=washizu_game();g.current_player_index=0;p=g.player_list[0]
            p.hand_tiles=[22,23,24,31,32,33,45,45,47,47,fourth];p.has_draw_slot=True
            p.combination_tiles=['k11']
            three=[2011,2011,2011] if fourth==11 else [11,2011,2011]
            p.combination_mask=[[1,three[0],0,three[1],0,three[2]]]
            for other in g.player_list[1:]:other.waiting_tiles=set()
            await begin_kan(g,'jiagang',11)
            assert p.combination_tiles==['g11']
            assert Counter(p.combination_mask[0][1::2])==Counter([11,2011,2011,2011])
            tick=g.game_record['game_round']['round_index_1']['action_ticks'][0]
            assert tick[0]=='jg' and tick[3]==fourth
            assert all(normalize(t)!=11 for t in p.hand_tiles)
    asyncio.run(run())


def test_final_result_reaches_human_after_rank_sort_with_bots():
    from .boardcast import broadcast_game_end
    async def run():
        g,sockets=connected();g.sub_rule='riichi/washizu'
        human=g.player_list[0]
        for p,score in zip(g.player_list,[10000,40000,30000,20000]):
            p.score=score
            if p is not human:p.user_id=2
        g.game_server.user_id_to_connection={human.user_id:sockets[100]}
        g._assign_final_ranks()
        assert g.player_list[0] is not human
        await broadcast_game_end(g)
        messages=[c.args[0] for c in sockets[100].websocket.send_json.call_args_list]
        ends=[m for m in messages if m['type']=='gamestate/riichi/game_end']
        assert len(ends)==1
        assert len(ends[0]['game_end_info']['player_final_data'])==4
    asyncio.run(run())


def test_replay_verifier_uses_the_shared_pool_and_glass_faces():
    from ..verifier.record_sim.goto_action import RecordSim,stringify_tick
    from ..verifier.record_sim.meld_codec import normalize_tile
    g=washizu_game();header=build_round_header_data(g)
    record={'game_title':{'rule':'riichi','sub_rule':'riichi/washizu'},'game_round':{'round_index_1':header}}
    sim=RecordSim(record);sim.load_round('round_index_1')
    assert sim.snapshot()['remain_tiles']==70
    for action in ('d','gd'):
        tile=sim.current_tiles_list[0];sim.apply_tick(stringify_tick([action,tile]))
    assert sim.snapshot()['remain_tiles']==68
    indicator=sim.current_tiles_list[-1]
    sim.apply_tick(stringify_tick(['dora',indicator]))
    assert sim.snapshot()['remain_tiles']==68 and indicator not in sim.current_tiles_list[-1:]
    assert normalize_tile(2011)==11 and normalize_tile(105)==15
