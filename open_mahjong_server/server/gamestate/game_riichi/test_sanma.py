"""Sanma rule branches and complete authoritative matches, including north extraction."""
import asyncio
import json
import os
from pathlib import Path
from collections import Counter
from itertools import combinations
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .RiichiGameState import RiichiGameState
from .test_double_riichi import make_game
from .action_check import check_action_hand_action, check_action_after_cut, check_hepai, _normalize
from .init_tiles import init_riichi_tiles
from .nuki_actions import can_nuki, begin_nuki, check_nuki_ron
from .kan_actions import commit_kan, rob_pending_kan
from .rule_logic import draw_payments, finalize_scores, is_first_draw, apply_pao
from .wait_action import wait_action, _is_valid_cut_action
from .boardcast import broadcast_game_start, reconnected_send_pending_ask
from ..public.ai.get_action import get_ai_action
from ...game_calculation.riichi.rule_config import preset_room_config, normalize_riichi_config
from ...game_calculation.riichi.sanma import tsumo_payments


def sanma(preset='sanma_majsoul'):
    old, _ = make_game(0)
    config = preset_room_config(preset)
    g = RiichiGameState(old.game_server, dict(config, player_list=[100, 101, 102], room_id=825005,
        room_rule='riichi', room_type='custom', round_timer=20, step_timer=5, tips=False,
        allow_spectator=False), old.calculation_service, None, 'sanma-test')
    for i, p in enumerate(g.player_list):
        p.player_index = p.original_player_index = i
        p.score = 35000
        p.hand_tiles = [21,22,23,24,25,26,31,32,33,36,37,38,45]
    g.master_seed = 20261005
    g.tiles_list = [21] * 40
    g.game_status = 'waiting_hand_action'
    g.game_record = {'game_title': {}, 'game_round': {'round_index_1': {'action_ticks': []}}}
    return g


@pytest.mark.parametrize('preset', ['sanma_majsoul', 'sanma_tenhou'])
@pytest.mark.parametrize('red', [False, True])
def test_wall_has_108_tiles_and_only_two_red_fives(preset, red):
    g = sanma(preset); g.red_dora = red
    for p in g.player_list: p.hand_tiles = []
    init_riichi_tiles(g)
    physical = g.tiles_list + sum([p.hand_tiles for p in g.player_list], [])
    assert len(physical) == 108 and len(g.tiles_list) - g.dead_wall_count == 55
    assert set(Counter(map(_normalize, physical)).values()) == {4}
    assert not any(t in range(12,19) or t == 105 for t in physical)
    assert sum(t in (205,305) for t in physical) == (2 if red else 0)
    assert g.dora_indicators == [g.tiles_list[-6]]


@pytest.mark.parametrize('seat', range(3))
def test_seats_rotate_and_never_create_a_fourth_player(seat):
    g = sanma(); g.current_player_index = seat
    from ..public.logic_common import next_current_index, get_index_relative_position
    next_current_index(g)
    assert g.current_player_index == (seat + 1) % 3
    g._rotate_seats()
    assert [p.original_player_index for p in g.player_list] == [1,2,0]
    assert get_index_relative_position(seat, (seat+2)%3, 3) == 'left'


@pytest.mark.parametrize('riichi,drawn,allowed', [(False,False,True),(False,True,True),(True,False,False),(True,True,True)])
def test_north_extraction_respects_draw_slot_and_riichi(riichi, drawn, allowed):
    g = sanma(); p = g.player_list[0]
    p.hand_tiles = ([44] + p.hand_tiles if not drawn else p.hand_tiles + [44])
    p.has_draw_slot = True; p.tag_list = ['riichi'] if riichi else []
    assert can_nuki(g,0) is allowed
    assert _is_valid_cut_action(g,0,dict(action_type='nuki')) is allowed
    assert ('nuki' in check_action_hand_action(g,0)[0]) is allowed
    p.has_draw_slot = False
    assert not can_nuki(g,0)
    p.has_draw_slot = True; g.game_status = 'onlycut_after_action'
    assert not can_nuki(g,0)


@pytest.mark.parametrize('reason', ['four_player','other_seat','no_north','empty_wall','eight_replacements'])
def test_illegal_north_extraction_is_rejected(reason):
    g=sanma();p=g.player_list[0];p.hand_tiles.append(44);p.has_draw_slot=True
    if reason=='four_player':g.sub_rule='riichi/standard'
    elif reason=='other_seat':g.current_player_index=1
    elif reason=='no_north':p.hand_tiles.remove(44)
    elif reason=='empty_wall':g.tiles_list=[21]*g.dead_wall_count
    else:g.rinshan_count=8
    assert not can_nuki(g,0)
    with pytest.raises(ValueError):asyncio.run(begin_nuki(g))


@pytest.mark.parametrize('drawn', [False,True])
def test_committed_north_is_closed_bonus_interrupts_first_turn_and_ippatsu(drawn):
    async def run():
        g=sanma();p=g.player_list[0]
        p.hand_tiles=([44]+p.hand_tiles if not drawn else p.hand_tiles+[44]);p.has_draw_slot=True
        for other in g.player_list:other.tag_list=['ippatsu']
        await begin_nuki(g)
        assert g.game_status=='deal_card_after_nuki' and p.huapai_list==[44]
        assert len(p.hand_tiles)==13 and not p.combination_tiles
        assert g.total_kans==0 and g.kan_dora_indicators==[]
        assert not is_first_draw(g,g.player_list[1]) and not g._can_declare_kyuushu(1)
        assert all('ippatsu' not in other.tag_list for other in g.player_list)
        assert g.game_record['game_round']['round_index_1']['action_ticks']==[['nuki',0,44,'T' if drawn else 'F']]
    asyncio.run(run())


@pytest.mark.parametrize('mode,shape,allowed', [('all','ordinary',True),('kokushi','ordinary',False),('none','ordinary',False),('all','kokushi',True),('kokushi','kokushi',True)])
def test_robbing_north_requires_configured_shape_and_a_real_yaku(mode,shape,allowed):
    g=sanma();g.detailed_config['nuki_ron']=mode;p=g.player_list[1]
    if shape=='kokushi':p.hand_tiles=[11,19,21,29,31,39,41,42,43,45,46,47,47]
    else:p.hand_tiles=[21,21,22,22,24,24,26,26,31,31,35,35,44]
    actions=check_nuki_ron(g)
    assert ('hu_first' in actions[1]) is allowed
    if allowed:
        assert '枪杠' not in g.result_dict['hu_first']['yaku']
        p.temp_furiten=True
        assert 'hu_first' not in check_nuki_ron(g)[1]


def test_robbed_north_never_creates_bonus_replacement_or_kan_dora():
    async def run():
        g=sanma();p=g.player_list[0];p.hand_tiles.append(44);p.has_draw_slot=True
        g.player_list[1].hand_tiles=[21,21,22,22,24,24,26,26,31,31,35,35,44]
        await begin_nuki(g)
        assert g.game_status=='waiting_action_qianggang' and len(p.hand_tiles)==14
        assert await rob_pending_kan(g)
        assert len(p.hand_tiles)==13 and p.huapai_list==[] and g.rinshan_count==0 and g.total_kans==0
        assert g.game_record['game_round']['round_index_1']['action_ticks'][0][4]=='nuki'
    asyncio.run(run())


def test_no_chi_but_pon_and_kan_remain_legal():
    g=sanma();p=g.player_list[1];p.hand_tiles=[21,22,23,23,23]+[45]*8
    actions=check_action_after_cut(g,23)
    assert {'peng','gang'} <= set(actions[1])
    assert not any(a.startswith('chi') for choices in actions.values() for a in choices)
    assert set(actions)=={0,1,2}


@pytest.mark.parametrize('seat', range(3))
@pytest.mark.parametrize('mode', ['loss','split'])
def test_tsumo_payments_and_settlement_conserve_points(seat,mode):
    async def run():
        g=sanma();g.detailed_config['sanma_tsumo']=mode;g.current_player_index=seat;g.hu_class='hu_self';g.honba=2;g.riichi_sticks=1
        g.player_list[0].score-=1000
        cost=dict(main=4000,additional=4000 if seat==0 else 2000)
        g.result_dict={'hu_self':dict(han=5,fu=30,yaku=['满贯'],cost=cost,score=sum(tsumo_payments(cost,seat,3,mode).values()))}
        await g._settle_hu()
        pay=tsumo_payments(cost,seat,3,mode)
        tick=g.game_record['game_round']['round_index_1']['action_ticks'][-1]
        assert len(tick[6])==3 and sum(tick[6])==1000
        for payer,amount in pay.items():assert tick[6][payer]==-amount-200
        assert sum(p.score for p in g.player_list)==105000
    asyncio.run(run())


@pytest.mark.parametrize('tenpai', [list(c) for n in range(4) for c in combinations(range(3),n)])
@pytest.mark.parametrize('penalty', [0,3000])
def test_every_three_player_noten_distribution(tenpai,penalty):
    g=sanma();g.detailed_config['noten_penalty']=penalty
    changes,winners=draw_payments(g,tenpai)
    assert set(changes)=={0,1,2} and sum(changes.values())==0 and not winners
    if tenpai and len(tenpai)<3:assert sum(max(x,0) for x in changes.values())==penalty
    else:assert not any(changes.values())


@pytest.mark.parametrize('preset', ['sanma_majsoul','sanma_tenhou'])
@pytest.mark.parametrize('tie', ['initial','shared'])
@pytest.mark.parametrize('deposits', ['winner','shared','discard'])
def test_final_uma_deposits_and_three_player_points(preset,tie,deposits):
    g=sanma(preset);g.riichi_sticks=1;g.player_list[2].score-=1000
    g.detailed_config.update(tie_break=tie,end_deposits=deposits)
    finalize_scores(g);before=[p.score for p in g.player_list];finalize_scores(g)
    assert [p.score for p in g.player_list]==before
    assert len(g.game_record['game_title']['riichi_final_scores'])==3
    assert sum(p.riichi_points for p in g.player_list)==(-1 if deposits=='discard' else 0)


def test_three_hands_per_wind_and_abort_rules():
    g=sanma();g.max_round=2;g.hu_class='hu_first';g.open_xiru=False
    assert not g._riichi_match_should_end(False,6)
    assert g._riichi_match_should_end(False,7)
    for p in g.player_list:p.tag_list=['riichi'];p.discard_origin_tiles=[41]
    assert not asyncio.run(g._check_four_wind_abort())
    assert not asyncio.run(g._check_four_player_riichi_abort())
    g.open_xiru=True;g.detailed_config['extension_rounds']=3
    assert not g._riichi_match_should_end(False,9)
    assert g._riichi_match_should_end(False,10)


@pytest.mark.parametrize('preset', ['sanma_majsoul','sanma_tenhou'])
def test_reconnect_includes_three_hands_north_bonus_and_pending_robbing(preset,caplog):
    async def run():
        g=sanma(preset);sockets={p.user_id:SimpleNamespace(websocket=AsyncMock()) for p in g.player_list}
        g.game_server.user_id_to_connection=sockets;g.player_list[0].huapai_list=[44,44]
        await broadcast_game_start(g)
        info=sockets[100].websocket.send_json.call_args_list[0].args[0]['game_info']
        assert len(info['players_info'])==3 and info['players_info'][0]['huapai_list']==[44,44]
        assert sum('hand_tiles' in p for p in info['players_info'])==1
        g.game_status='waiting_action_qianggang';g.jiagang_tile=44;g._pending_kan={'kind':'nuki'}
        g.action_dict={0:[],1:['hu_first','pass'],2:[]}
        await reconnected_send_pending_ask(g,101)
        assert sockets[101].websocket.send_json.call_args_list[-2].args[0]['do_action_info']['action_list']==['nuki']
        assert not any(r.levelname=='ERROR' for r in caplog.records)
        return g
    asyncio.run(run())


@pytest.mark.parametrize('preset', ['sanma_majsoul','sanma_tenhou'])
@pytest.mark.parametrize('seed', [1,2,3,20261005])
def test_complete_sanma_matches_with_real_actions(preset,seed,caplog):
    async def run():
        g=sanma(preset);g.max_round=1;g.open_xiru=False;g.room_random_seed=seed
        g.game_server.gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock())
        g.game_server.room_manager=SimpleNamespace(finish_custom_game_room=AsyncMock())
        g.game_server.user_id_to_connection={}
        for p in g.player_list:p.hand_tiles=[];p.user_id=1;p.has_draw_slot=False
        original_sleep=asyncio.sleep;actions=Counter()
        async def instant(_):await original_sleep(0)
        async def automated_wait():
            task=asyncio.create_task(wait_action(g));await original_sleep(0)
            for seat in list(g.waiting_players_list):
                p=g.player_list[seat];choices=g.action_dict[seat];tile=target=None
                action=next((a for a in choices if a.startswith('hu_') and not g.result_dict.get(a,{}).get('no_yaku')),None)
                if action is None and 'nuki' in choices:action='nuki'
                if action is None and 'riichi_cut' in choices:action='riichi_cut';tile=next(iter(p.riichi_candidate_cuts))
                if action is None:
                    for candidate in ['angang','jiagang']:
                        if candidate not in choices:continue
                        for t in p.hand_tiles:
                            if _is_valid_cut_action(g,seat,dict(action_type=candidate,target_tile=_normalize(t))):action=candidate;target=_normalize(t);break
                        if action:break
                if action is None and 'cut' in choices:
                    action='cut';tile=p.hand_tiles[-1] if 'riichi' in p.tag_list else next((t for t in p.hand_tiles if t not in p.kuikae_forbidden_tiles),p.hand_tiles[-1])
                if action is None:action=next((a for a in ['gang','peng','pass','ready'] if a in choices),choices[0])
                actions[action]+=1
                await get_ai_action(g,seat,action,bool(p.has_draw_slot and tile==p.hand_tiles[-1]),tile,None,target)
            await task
            if g.game_status not in ('END','waiting_ready'):
                physical=list(g.tiles_list)
                for p in g.player_list:
                    physical+=p.hand_tiles+p.discard_tiles+p.huapai_list
                    for mask in p.combination_mask:physical+=mask[1::2]
                assert len(physical)==108,(g.game_status,actions)
                assert set(Counter(map(_normalize,physical)).values())=={4}
            assert sum(p.score for p in g.player_list)+g.riichi_sticks*1000==105000
            assert g.current_player_index in range(3) and g.round_index<40
        g.wait_action=automated_wait
        with patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep',instant),patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase',new=AsyncMock()),patch('server.gamestate.game_riichi.RiichiGameState.vote_checkpoint',new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_riichi(),45)
        assert g._riichi_finalized and actions['cut']>20 and actions['nuki']>0
        assert all(r['action_ticks'][-1]==['end'] for r in g.game_record['game_round'].values())
        assert g.game_record['game_title']['player_count']==3
        assert not any(r.levelname=='ERROR' for r in caplog.records)
        return g
    g=asyncio.run(run())
    verify_sanma_record(g.game_record, f'{preset}-{seed}')


def verify_sanma_record(record, name):
    from ..verifier.record_sim.goto_action import RecordSim, stringify_tick
    from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick
    totals = list(record['game_title'].get('starting_scores', [record['game_title']['starting_score']]*3))
    all_frames = {}
    sim = RecordSim(record)
    for key, round_data in record['game_round'].items():
        sim.load_round(key)
        for p in sim.players.values(): p.score = totals[p.original_player_index]
        frames = [sim.snapshot()]
        changes = None
        for raw in round_data['action_ticks']:
            tick=stringify_tick(raw)
            if tick[0] in ('d','gd','nd'):
                from ...game_calculation.riichi.sanma import replacement_index
                expected=sim.current_tiles_list[0 if tick[0]=='d' else replacement_index(sim.replacement_draw_count, True)]
                assert int(tick[1])==expected, (name,key,tick)
            sim.apply_tick(tick)
            changes=accumulate_score_changes_from_tick(changes,tick,round_data['seats'])
            frame=sim.snapshot()
            assert set(frame['players'])=={'0','1','2'} and frame['current_player_index'] in range(3)
            frames.append(frame)
        for i in range(3): totals[i] += changes[i] if changes else 0
        all_frames[key]=frames
    assert [p.score for p in sorted(sim.players.values(), key=lambda p:p.original_player_index)]==record['game_title']['riichi_final_scores']
    directory=os.environ.get('SANMA_FIXTURES_DIR')
    if directory:
        target=Path(directory);target.mkdir(parents=True,exist_ok=True)
        (target/(name+'.json')).write_text(json.dumps(record,ensure_ascii=False,default=str),encoding='utf-8')
        (target/(name+'-frames.json')).write_text(json.dumps(all_frames,ensure_ascii=False),encoding='utf-8')
