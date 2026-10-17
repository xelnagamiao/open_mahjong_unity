import asyncio
from collections import Counter
from unittest.mock import patch
from unittest.mock import AsyncMock
import json
import os
from pathlib import Path

import pytest

from .test_sanma import sanma
from .action_check import check_action_after_cut, check_action_hand_action, check_action_buhua, refresh_waiting_tiles
from .init_tiles import init_guobiao_tiles
from .four_jokers_bot import choose_action, choose_cut
from .four_jokers_runtime import evaluate_actions
from ...game_calculation import guobiao_four_jokers as rule
from ...game_calculation.guobiao_hepai_check import Chinese_Hepai_Check
from ...game_calculation.test_guobiao_four_jokers import BASE
from ...game_calculation.test_guobiao_four_jokers import SEVEN_FAN_CASES, HONOR_WAIT
from ...database.duplicate_walls import validate_duplicate_wall


def state(**kw):
    return sanma(sub_rule=rule.SUB_RULE, **kw)


@pytest.mark.parametrize('sub_rule',['guobiao/standard',rule.SUB_RULE])
@pytest.mark.parametrize('multi',[False,True])
def test_screenshot_all_exposed_ron(sub_rule,multi):
    s=sanma(sub_rule=sub_rule,joker_multi_wait=multi)
    s.current_player_index=0;s.player_list[0].discard_tiles=[24]
    p=s.player_list[1];p.hand_tiles=[24];p.combination_tiles=['k32','k16','k36','k28'];p.huapai_list=[51,52,53]
    assert 'hu_first' in check_action_after_cut(s,24)[1]
    assert s.result_dict['hu_first'][0]==35
    assert '全求人' in s.result_dict['hu_first'][1]


@pytest.mark.parametrize('multi',[False,True])
@pytest.mark.parametrize('case',SEVEN_FAN_CASES+[(HONOR_WAIT,['s24'],45,'无番和')])
def test_worker_uses_room_toggle_for_win_permission_and_snapshots(multi,case):
    from .boardcast import _build_game_start_payload_for_viewer
    from ..public.game_record_manager import build_game_title_data
    hand,melds,win,fan=case
    s=state(joker_multi_wait=multi);s.current_player_index=0;s.player_list[0].discard_tiles=[win]
    p=s.player_list[1];p.hand_tiles=hand[:];p.hand_tiles.remove(win);p.combination_tiles=melds
    async def worker(_state,fn,*args,**kw):return fn(*args,**kw)
    with patch('server.gamestate.game_guobiao.four_jokers_runtime.run_room_bot_cpu',worker):
        actions=asyncio.run(evaluate_actions(s,check_action_after_cut,win))
    expected=multi if fan=='无番和' else not multi
    assert ('hu_first' in actions[1])==expected
    assert _build_game_start_payload_for_viewer(s,1)['joker_multi_wait']==multi
    assert build_game_title_data(s)['joker_multi_wait']==multi


@pytest.mark.parametrize('flowers', [False,True])
@pytest.mark.parametrize('seed', range(8))
def test_wall_has_four_new_red_flowers(flowers,seed):
    s=state(flowers=flowers); s.master_seed=seed
    for p in s.player_list: p.hand_tiles=[]
    init_guobiao_tiles(s)
    count=Counter(s.tiles_list+sum((p.hand_tiles for p in s.player_list),[]))
    assert count[59]==4 and all(count[t]==4 for t in rule.TILES)
    assert all(count[t]==int(flowers) for t in range(51,59))
    assert sum(count.values())==140+8*int(flowers)
    assert list(map(lambda p:len(p.hand_tiles),s.player_list))==[14,13,13,13]


def test_joker_is_not_a_replacement_flower_or_a_kong_or_a_claim():
    s=state(); s.player_list[0].hand_tiles=BASE[:10]+[59]*4
    assert check_action_buhua(s,0)[0]==[]
    assert 'angang' not in check_action_hand_action(s,0)[0]
    for p in s.player_list: p.hand_tiles=[59,59]+BASE[:11]
    assert all(not a for a in check_action_after_cut(s,59).values())
    s.player_list[0].hand_tiles=BASE[:12]+[51,59]
    assert 'buhua' in check_action_buhua(s,0)[0]


def test_draw_joker_can_win_and_keeps_physical_tile():
    s=state(); p=s.player_list[0]; p.hand_tiles=BASE[:-1]
    refresh_waiting_tiles(s,0)
    assert p.waiting_tiles=={45,59}
    p.hand_tiles.append(59)
    assert 'hu_self' in check_action_hand_action(s,0)[0]
    assert p.hand_tiles[-1]==59 and s.result_dict['hu_self'][0]>=8


def test_worker_scores_snapshot_and_applies_only_results():
    s=state(); s.player_list[0].hand_tiles=BASE[:-1]+[59]
    refresh_waiting_tiles(s,0,True)
    async def worker(_state,fn,*args,**kw):
        return await asyncio.to_thread(fn,*args,**kw)
    with patch('server.gamestate.game_guobiao.four_jokers_runtime.run_room_bot_cpu',worker):
        actions=asyncio.run(evaluate_actions(s,check_action_hand_action,0))
    assert 'hu_self' in actions[0]
    assert s.player_list[0].hand_tiles==BASE[:-1]+[59]


@pytest.mark.parametrize('ghosts', range(1,5))
def test_bot_retains_jokers_without_hypothetical_scoring(ghosts):
    hand=BASE[:14-ghosts]+[59]*ghosts
    with patch.object(Chinese_Hepai_Check,'hepai_check',side_effect=AssertionError('bot fan search')):
        tile=choose_cut(hand)
        assert tile in hand and tile!=59
        assert choose_action(hand,[],['hu_self','cut'],{})==('hu_self',None)


def test_duplicate_wall_rejected_before_loading_inventory():
    with pytest.raises(ValueError):
        validate_duplicate_wall({},dict(room_rule='guobiao',sub_rule=rule.SUB_RULE))


@pytest.mark.parametrize('api', ['shared', 'legacy'])
def test_reject_red_kong_even_when_another_kong_is_allowed(api):
    from ..public.ai.get_action import get_ai_action as shared
    from .get_action import get_ai_action as legacy
    s=state();s.player_list[0].hand_tiles=[11]*4+[59]*4+[22,23,24,31,32,33]
    s.game_status='waiting_hand_action';s.action_dict={0:['angang','cut'],1:[],2:[],3:[]}
    s.waiting_players_list=[0]
    asyncio.run((shared if api=='shared' else legacy)(s,0,'angang',None,None,None,59))
    assert s.action_queues[0].empty()


def test_snapshots_keep_own_hand_without_server_hint_scoring():
    from .boardcast import _build_game_start_payload_for_viewer
    s=state();s.tips=True;s.player_list[0].hand_tiles=BASE[:-1]+[59]
    async def run():
        return _build_game_start_payload_for_viewer(s,0)
    with patch.object(Chinese_Hepai_Check,'hepai_check',side_effect=AssertionError('eager hint scoring')):
        data=asyncio.run(run())
    assert 'four_jokers_hints' not in data
    assert data['players_info'][0]['hand_tiles']==s.player_list[0].hand_tiles
    assert all(not p.get('hand_tiles') for p in data['players_info'][1:])
    assert rule.calculate_hint(BASE[:-1]+[59],[],['点和'],['自摸'],59,8)['kind']=='zimo'


@pytest.mark.parametrize('flowers,circles,seed',[(True,1,811),(False,1,42),(True,4,20261012)])
def test_complete_matches_with_joker_bots_and_replay(flowers,circles,seed,caplog):
    from .wait_action import wait_action
    from ..public.ai.get_action import get_ai_action
    from ..public.hand_slot_utils import infer_bot_cut_class
    async def run():
        g=state(flowers=flowers);g.max_round=circles;g.room_random_seed=seed
        for p in g.player_list:p.hand_tiles=[]
        original_sleep=asyncio.sleep;counts=Counter()
        async def instant(_):await original_sleep(0)
        async def automated_wait():
            task=asyncio.create_task(wait_action(g));await original_sleep(0)
            for seat in list(g.waiting_players_list):
                p=g.player_list[seat];choices=g.action_dict[seat]
                source=g.player_list[g.current_player_index].discard_tiles
                visible=Counter(t for other in g.player_list for t in other.discard_tiles)
                action,tile=('ready',None) if 'ready' in choices else choose_action(p.hand_tiles,p.combination_tiles,choices,visible,source[-1] if source else None)
                counts[action]+=1
                position=max((i for i,t in enumerate(p.hand_tiles) if t==tile),default=-1)
                await get_ai_action(g,seat,action,
                    infer_bot_cut_class(p.hand_tiles,tile,position,draw_slot=p.has_draw_slot) if action=='cut' else None,
                    tile if action=='cut' else None,position if action=='cut' else None,tile if action!='cut' else None)
            await task
            assert sum(p.score for p in g.player_list)==0
        g.wait_action=automated_wait
        with patch('server.gamestate.game_guobiao.GuobiaoGameState.asyncio.sleep',instant), \
             patch('server.gamestate.game_guobiao.GuobiaoGameState.run_synced_hu_ready_phase',new=AsyncMock()), \
             patch('server.gamestate.game_guobiao.GuobiaoGameState.vote_checkpoint',new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_chinese(),180)
        assert len(g.game_record['game_round'])==4*circles
        assert counts['hu_self']+counts['hu_first']+counts['hu_second']+counts['hu_third']>0
        assert bool(counts['buhua'])==flowers
        assert not any(r.levelname=='ERROR' for r in caplog.records)
        return g,counts
    g,counts=asyncio.run(run())
    from ..verifier.record_sim.goto_action import RecordSim,stringify_tick
    from ..verifier.record_sim.decoder import accumulate_score_changes_from_tick
    sim=RecordSim(g.game_record);totals=[0]*4;frames={}
    for key,data in g.game_record['game_round'].items():
        physical=data['tiles_list']+sum((data[f'p{i}_tiles'] for i in range(4)),[])
        assert Counter(physical)[59]==4 and len(physical)==140+8*int(flowers)
        sim.load_round(key)
        for p in sim.players.values():p.score=totals[p.original_player_index]
        frames[key]=[sim.snapshot()];changes=None
        for raw in data['action_ticks']:
            tick=stringify_tick(raw);sim.apply_tick(tick)
            changes=accumulate_score_changes_from_tick(changes,tick,data['seats'])
            frames[key].append(sim.snapshot())
        for i in range(4):totals[i]+=changes[i] if changes else 0
    assert totals==[p.score for p in sorted(g.player_list,key=lambda p:p.original_player_index)]
    directory=os.environ.get('GUOBIAO_FOUR_JOKERS_FIXTURES_DIR')
    if directory:
        out=Path(directory);out.mkdir(parents=True,exist_ok=True);name=f'four-jokers-{int(flowers)}-{circles}-{seed}'
        for suffix,data in [('',g.game_record),('-frames',frames),('-actions',dict(counts))]:
            (out/(name+suffix+'.json')).write_text(json.dumps(data,ensure_ascii=False,default=str),encoding='utf8')
