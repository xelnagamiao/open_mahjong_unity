"""Transparent MCR: physical copies, normal scoring, visibility and full rounds."""
import asyncio
import json
import os
from collections import Counter
from pathlib import Path
from unittest.mock import AsyncMock, Mock, patch

import pytest

from . import transparent as glass
from .test_sanma import sanma, HAND
from .init_tiles import init_guobiao_tiles
from .action_check import (check_action_after_cut, check_action_hand_action,
                           check_action_buhua, check_action_jiagang, check_hepai,
                           refresh_waiting_tiles)
from .buhua_broadcast import perform_buhua_and_broadcast
from .boardcast import _build_game_start_payload_for_viewer, _build_do_action_payload
from .combination_mask_view import sanitize_angang_mask
from .wait_action import wait_action
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_logic import count_visible_tiles, tile_to_34
from ..public.hand_slot_utils import normalize_tile, mark_draw_slot
from ...game_calculation.riichi.washizu_tiles import FACES, is_transparent


def state(**settings):
    return sanma(sub_rule=glass.SUB_RULE, **settings)


def physical(hand):
    """Encode up to three copies per face as glass and keep the fourth opaque."""
    used = Counter()
    result = []
    for tile in hand:
        used[tile] += 1
        result.append(tile + 2000 if used[tile] <= 3 and tile in FACES else tile)
    return result


@pytest.mark.parametrize('flowers', [False, True])
@pytest.mark.parametrize('seed', [1, 42, 20261011])
def test_full_wall_uses_three_glass_one_opaque_and_normal_deal(flowers, seed):
    g = state(flowers=flowers); g.master_seed = seed
    for p in g.player_list: p.hand_tiles = []
    init_guobiao_tiles(g)
    count = Counter(g.tiles_list + sum((p.hand_tiles for p in g.player_list), []))
    assert all(count[t] == 1 and count[t+2000] == 3 for t in FACES)
    assert all(count[t] == int(flowers) for t in range(51, 59))
    assert sum(count.values()) == (144 if flowers else 136)
    assert len(g.tiles_list) == (91 if flowers else 83)
    assert [len(p.hand_tiles) for p in g.player_list] == [14, 13, 13, 13]
    assert not hasattr(g, 'dead_wall_count') and not hasattr(g, 'dora_indicators')


@pytest.mark.parametrize('way', ['dianhe', 'qianggang', 'handgot'])
@pytest.mark.parametrize('winner', range(4))
@pytest.mark.parametrize('flower_count', [0, 1, 8])
def test_identical_mcr_fans_for_physical_and_plain_hands(way, winner, flower_count):
    results = []
    for transparent in [False, True]:
        g = state(); g.current_player_index = winner if way == 'handgot' else (winner+3)%4
        g.player_list[winner].hand_tiles = HAND + ([45] if way == 'handgot' else [])
        if transparent: g.player_list[winner].hand_tiles = physical(g.player_list[winner].hand_tiles)
        g.player_list[winner].huapai_list = list(range(51, 51+flower_count))
        g.player_list[winner].waiting_tiles = {45}
        actions = {i: [] for i in range(4)}
        check_hepai(g, actions, 2045 if transparent else 45, winner, way)
        results.append(g.result_dict)
        assert any(a.startswith('hu_') for a in actions[winner])
    assert results[0] == results[1]


@pytest.mark.parametrize('score,flowers,allowed', [(7,0,False),(8,0,True),(8,1,False),(9,1,True),(15,8,False),(16,8,True)])
def test_eight_fan_gate_does_not_include_flowers(score, flowers, allowed):
    g = state(); g.calculation_service.GB_hepai_check = Mock(return_value=(score, ['测试番']))
    g.player_list[1].huapai_list = list(range(51,51+flowers))
    actions = {i: [] for i in range(4)}
    check_hepai(g, actions, 2045, 1, 'dianhe')
    assert ('hu_first' in actions[1]) is allowed


def test_calculator_waits_and_decompositions_do_not_mutate_physical_ids():
    g = state(); service = g.calculation_service
    hand = physical(HAND); before = hand[:]
    assert service.GB_tingpai_check(hand, []) == service.GB_tingpai_check(HAND, [])
    assert service.GB_hepai_decompose(hand+[2045], [], ['自摸'], 2045) == service.GB_hepai_decompose(HAND+[45], [], ['自摸'], 45)
    assert hand == before
    g.player_list[0].hand_tiles = hand
    refresh_waiting_tiles(g, 0)
    g.player_list[0].hand_tiles += [2045]
    assert 'hu_self' in check_action_hand_action(g, 0)[0]
    assert 'buhua' not in check_action_hand_action(g, 0)[0]
    assert check_action_buhua(g,0)[0] == []


@pytest.mark.parametrize('instant', [False, True])
@pytest.mark.parametrize('drawn', [False, True])
def test_flower_replacement_only_removes_flower_and_does_not_leak_next_draw(instant, drawn):
    async def run():
        g = state(); p = g.player_list[0]; p.hand_tiles = physical(HAND)+[58]
        if drawn: mark_draw_slot(p, 58)
        g.tiles_list = [2012, 2013, 2014]
        g.game_record = {'game_round': {f'round_index_{g.round_index}': {'action_ticks': []}}}
        original = Counter(p.hand_tiles)
        with patch('server.gamestate.game_guobiao.buhua_broadcast.HAND_SETTLE_GAP_SEC', 0):
            tile = await perform_buhua_and_broadcast(g, 0, instant=instant)
        assert p.huapai_list == [58] and Counter(p.hand_tiles) == original-Counter([58])+Counter([tile])
        messages = [c.args[0]['do_action_info'] for c in g.game_server.user_id_to_connection[102].websocket.send_json.call_args_list]
        assert len(messages) == (1 if instant else 2)
        assert all(m.get('deal_tile') is None for m in messages)
        assert messages[0]['buhua_tile'] == 58 and messages[0]['is_mo_buhua'] == drawn
        if not instant:
            first = messages[0]['washizu_hands'][0]
            assert len(first) == 13 and tile not in first
        assert messages[-1]['washizu_hands'][0].count(tile) == p.hand_tiles.count(tile)
    asyncio.run(run())


@pytest.mark.parametrize('action,called,hand', [
    ('chi_left',2013,[11,2011,12,2012,44]),
    ('chi_mid',2012,[11,2011,13,2013,44]),
    ('chi_right',11,[12,2012,13,2013,44]),
    ('peng',2015,[15,2015,2015,44]),
    ('gang',15,[2015,2015,2015,44]),
])
@pytest.mark.parametrize('source', range(4))
def test_every_physical_call_choice_keeps_exact_tiles(action, called, hand, source):
    g = state(); g.current_player_index = source; seat = (source+1)%4
    g.calculation_service.GB_tingpai_check = Mock(return_value=set())
    for p in g.player_list: p.hand_tiles = []
    p = g.player_list[seat]; p.hand_tiles = hand[:]
    actions = check_action_after_cut(g, called)
    assert action in actions[seat]
    options = p.chi_candidates.get(action,[None])
    assert len(options) == (4 if action.startswith('chi') else 2 if action=='peng' else 1)
    for index, option in enumerate(options):
        p.hand_tiles = hand[:]; p.combination_tiles = []
        result = glass.consume_meld(g, seat, action, called, index)
        assert result is not None
        target, mask = result
        assert Counter(p.hand_tiles + mask[1::2]) == Counter(hand+[called])
        assert mask[1] == called and mask[0] == 1
        if option: assert Counter(hand)-Counter(p.hand_tiles) == Counter(option)
        assert int(target[1:]) < 50


@pytest.mark.parametrize('choice', [-1, 2, True, None, '0', 0.5])
def test_invalid_call_choices_rejected_at_network_queue(choice):
    async def run():
        g = state(); p = g.player_list[1]; p.hand_tiles = [15,2015,2015]
        g.action_dict = check_action_after_cut(g, 2015)
        g.waiting_players_list=[1]; g.game_status='waiting_action_after_cut'
        await get_ai_action(g, 1, 'peng', False, None, None, None, chi_combo_index=choice)
        assert g.action_queues[1].empty()
    asyncio.run(run())


@pytest.mark.parametrize('viewer', range(4))
def test_public_projection_reconnect_masks_and_claims(viewer):
    g=state(); p=g.player_list[0]; p.hand_tiles=[15,2044,2012,41,2011]
    p.combination_tiles=['G22'];p.combination_mask=[[2,22,2,2022,2,2022,2,2022]]
    info=_build_game_start_payload_for_viewer(g,viewer)
    assert info['washizu_hands'][0]==[2011,2012,2044,0,0]
    for index, player in enumerate(info['players_info']):
        assert player['hand_tiles']==g.player_list[index].hand_tiles if index==viewer else player['hand_tiles'] is None
    mask=info['players_info'][0]['combination_mask'][0]
    assert mask==([2,22,2,2022,2,2022,2,2022] if viewer==0 else [2,0,2,2022,2,2022,2,2022])
    assert sanitize_angang_mask([2,22]*4,0,1)==[2,0]*4
    claim=_build_do_action_payload(g,['peng'],1,viewer,is_claim=True)
    assert claim['washizu_hands'] is None
    frame=_build_do_action_payload(g,['deal'],0,viewer,deal_tile=15)
    assert frame['deal_tile']==(15 if viewer==0 else None)
    p.hand_tiles.append(2019)
    assert 2019 not in frame['washizu_hands'][0]


def test_bot_sees_only_public_opponent_copies():
    g=state()
    for p in g.player_list: p.hand_tiles=[]
    g.player_list[0].hand_tiles=[2011,11]
    g.player_list[1].hand_tiles=[2012,13]
    visible=count_visible_tiles(g,0)
    assert visible[tile_to_34(12)]==1 and visible[tile_to_34(13)]==0
    assert visible[tile_to_34(11)]==0


@pytest.mark.parametrize('kind', ['angang', 'jiagang'])
@pytest.mark.parametrize('actual', [22,2022])
@pytest.mark.parametrize('drawn', [False,True])
def test_kongs_execute_with_real_copies_and_record_them(kind,actual,drawn):
    async def run():
        g=state();p=g.player_list[0]
        p.hand_tiles=physical([11,12,13,31,32,33,41,41,41,45])
        if kind=='angang':p.hand_tiles += [2022,2022,2022,22]
        else:
            p.hand_tiles += [actual]
            others=[2022,2022,2022] if actual==22 else [22,2022,2022]
            p.combination_tiles=['k22'];p.combination_mask=[[0,others[0],0,others[1],1,others[2]]]
        if drawn:mark_draw_slot(p,p.hand_tiles[-1])
        g.game_status='waiting_hand_action';g.action_dict={0:[kind],1:[],2:[],3:[]}
        g.game_record={'game_round':{f'round_index_{g.round_index}':{'action_ticks':[]}}}
        g.action_queues[0].put_nowait({'action_type':kind,'target_tile':22})
        await wait_action(g)
        assert g.game_status=='deal_card_after_gang'
        assert not any(normalize_tile(t)==22 for t in p.hand_tiles)
        assert Counter(p.combination_mask[0][1::2])==Counter([22,2022,2022,2022])
        tick=g.game_record['game_round'][f'round_index_{g.round_index}']['action_ticks'][-1]
        if kind=='angang':assert Counter(tick[3:7])==Counter([22,2022,2022,2022])
        else:assert tick==['jg',22,'T' if drawn else 'F',actual]
        assert not p.has_draw_slot
    asyncio.run(run())


@pytest.mark.parametrize('winning_tile', [45,2045])
def test_rob_kong_recognizes_transparent_wait(winning_tile):
    g=state();g.player_list[1].hand_tiles=physical(HAND)
    refresh_waiting_tiles(g,1)
    assert 'hu_first' in check_action_jiagang(g,winning_tile)[1]
    assert any('抢杠和' in fan for fan in g.result_dict['hu_first'][1])


@pytest.mark.parametrize('kong', ['ag', 'jg'])
def test_best_win_backfill_preserves_physical_kong_and_win(kong):
    from ..public.guobiao_win_snapshot import restore_guobiao_best_wins
    hand=physical([11,12,13,31,32,33,41,41,41,45])
    if kong=='ag':
        initial=hand+[22,2022,2022,2022]
        ticks=[['ag',22,'F',22,2022,2022,2022]]
    else:
        initial=hand+[22,2022,2022]
        ticks=[['c',2022,'F'],['p',2022,0,22,2022],['jg',22,'F',2022]]
    ticks += [['gd',2045],['hu_self',0,16,['三色三步高'],[48,-16,-16,-16],2045]]
    record={'game_title':{f'p{i}_uid':101+i for i in range(4)},'game_round':{'round_index_1':{
        'seats':[0,1,2,3],'start_player_index':0 if kong=='ag' else 3,
        'p0_tiles':initial,'p1_tiles':[],'p2_tiles':[],'p3_tiles':[2022] if kong=='jg' else [],
        'action_ticks':ticks}}}
    restored,errors=restore_guobiao_best_wins(record)
    assert not errors
    best=restored['101']
    assert best['winning_tile']==2045 and 2045 in best['concealed_tiles']
    assert Counter(best['melds'][0])==Counter([22,2022,2022,2022])


@pytest.mark.parametrize('viewer', range(4))
def test_real_reconnect_path_restores_projection_and_selected_call(viewer):
    async def run():
        g=state();g.current_player_index=(viewer+3)%4
        p=g.player_list[viewer];p.hand_tiles=[15,2015,2015]+physical([11,12,13,21,22,23,31,32,33,41])
        g.player_list[g.current_player_index].discard_tiles=[2015]
        g.action_dict=check_action_after_cut(g,2015);g.game_status='waiting_action_after_cut'
        uid=p.user_id
        await g.player_reconnect(uid)
        messages=[c.args[0] for c in g.game_server.user_id_to_connection[uid].websocket.send_json.call_args_list]
        info=next(m['game_info'] for m in messages if m.get('game_info'))
        assert info['washizu_hands']==glass.public_hands(g)
        ask=next(m['ask_other_action_info'] for m in messages if m.get('ask_other_action_info'))
        assert ask['chi_candidates']['peng']==[[2015,2015],[15,2015]]
    asyncio.run(run())


@pytest.mark.parametrize('flowers,circles,seed,calls', [(True,1,1,True),(False,1,42,True),(True,2,20261011,True),(True,4,73,False)])
def test_complete_matches_record_replay_and_protocol(flowers,circles,seed,calls,caplog):
    async def run():
        g=state(flowers=flowers);g.max_round=circles;g.room_random_seed=seed
        for p in g.player_list:p.hand_tiles=[]
        original_sleep=asyncio.sleep;counts=Counter()
        async def instant(_):await original_sleep(0)
        async def automated_wait():
            task=asyncio.create_task(wait_action(g));await original_sleep(0)
            for seat in list(g.waiting_players_list):
                p=g.player_list[seat];choices=g.action_dict[seat];tile=target=None
                priority=['hu_self','hu_first','hu_second','hu_third','buhua','ready']
                if calls:priority+=['angang','jiagang','gang','peng','chi_left','chi_mid','chi_right']
                action=next((a for a in priority if a in choices),None)
                if action=='angang':target=next(t for t,n in Counter(map(normalize_tile,p.hand_tiles)).items() if n==4)
                if action=='jiagang':target=next(normalize_tile(t) for t in p.hand_tiles if f'k{normalize_tile(t)}' in p.combination_tiles)
                if action is None and 'cut' in choices:action='cut';tile=p.hand_tiles[-1]
                if action is None:action='pass'
                counts[action]+=1
                options=getattr(p,'chi_candidates',{}).get(action,[])
                choice=(counts[action]-1)%len(options) if options else 0
                await get_ai_action(g,seat,action,p.has_draw_slot and tile==p.hand_tiles[-1],tile,None,target,chi_combo_index=choice)
            await task
            assert g.current_player_index in range(4) and sum(p.score for p in g.player_list)==0
        g.wait_action=automated_wait
        with patch('server.gamestate.game_guobiao.GuobiaoGameState.asyncio.sleep',instant), \
             patch('server.gamestate.game_guobiao.GuobiaoGameState.run_synced_hu_ready_phase',new=AsyncMock()), \
             patch('server.gamestate.game_guobiao.GuobiaoGameState.vote_checkpoint',new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_chinese(),50)
        assert len(g.game_record['game_round'])==4*circles
        assert counts['cut']>100 and bool(counts['buhua'])==flowers
        assert not any(r.levelname=='ERROR' for r in caplog.records)
        return g,dict(counts)
    g,counts=asyncio.run(run())
    from ..verifier.record_sim.goto_action import RecordSim,stringify_tick
    from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick
    sim=RecordSim(g.game_record);totals=[0]*4;frames={}
    for key,data in g.game_record['game_round'].items():
        all_tiles=data['tiles_list']+sum((data[f'p{i}_tiles'] for i in range(4)),[])
        tile_counts=Counter(all_tiles)
        assert all(tile_counts[t]==1 and tile_counts[t+2000]==3 for t in FACES)
        sim.load_round(key)
        for p in sim.players.values():p.score=totals[p.original_player_index]
        frames[key]=[sim.snapshot()];changes=None
        for raw in data['action_ticks']:
            tick=stringify_tick(raw);sim.apply_tick(tick)
            changes=accumulate_score_changes_from_tick(changes,tick,data['seats'])
            frames[key].append(sim.snapshot())
        for i in range(4):totals[i]+=changes[i] if changes else 0
    assert totals==[p.score for p in sorted(g.player_list,key=lambda p:p.original_player_index)]
    directory=os.environ.get('GUOBIAO_TRANSPARENT_FIXTURES_DIR')
    if directory:
        out=Path(directory);out.mkdir(parents=True,exist_ok=True);name=f'gb-transparent-{int(flowers)}-{circles}-{seed}'
        def save(suffix,obj):(out/(name+suffix+'.json')).write_text(json.dumps(obj,ensure_ascii=False,default=str),encoding='utf-8')
        save('',g.game_record);save('-frames',frames);save('-actions',counts)
        for uid,conn in g.game_server.user_id_to_connection.items():save(f'-viewer-{uid}',[call.args[0] for call in conn.websocket.send_json.call_args_list])
