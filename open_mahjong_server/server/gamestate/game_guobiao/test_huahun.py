import json, os
from pathlib import Path
import asyncio
from collections import Counter
from unittest.mock import patch, AsyncMock
import pytest

from .test_sanma import sanma
from .action_check import check_action_after_cut, check_action_hand_action, check_action_buhua, refresh_waiting_tiles, check_hepai
from .init_tiles import init_guobiao_tiles
from .four_jokers_bot import choose_action, choose_cut
from .four_jokers_runtime import evaluate_actions, prepare_hints
from .boardcast import _build_game_start_payload_for_viewer
from ...game_calculation import guobiao_huahun as rule
from ...game_calculation.test_guobiao_four_jokers import BASE, SEVEN_FAN_CASES
from ..public.ai.get_action import get_ai_action
from ..public.hand_slot_utils import mark_draw_slot


def state(**kw):
    return sanma(sub_rule=rule.SUB_RULE, **kw)


@pytest.mark.parametrize('flowers',[False,True])
@pytest.mark.parametrize('seed',range(6))
def test_wall_and_defaults(flowers,seed):
    s=state(flowers=flowers);s.master_seed=seed
    for p in s.player_list:p.hand_tiles=[]
    init_guobiao_tiles(s)
    physical=Counter(s.tiles_list+sum((p.hand_tiles for p in s.player_list),[]))
    assert sum(physical.values())==144 and physical[59]==0
    assert all(physical[t]==1 for t in range(51,59))
    assert s.no_joker_bonus=='add8' and s.black_flower_progressive and not s.joker_multi_wait
    assert s.use_flowers


@pytest.mark.parametrize('joker',rule.JOKERS)
def test_red_not_flower_nor_claimable(joker):
    s=state();p=s.player_list[0];p.hand_tiles=BASE[:13]+[joker]
    assert check_action_buhua(s,0)[0]==[]
    assert all(not a for a in check_action_after_cut(s,joker).values())
    p.hand_tiles[-1]=55
    assert check_action_buhua(s,0)[0]==['buhua','pass']


@pytest.mark.parametrize('bonus',['add8','double','none'])
@pytest.mark.parametrize('progressive',[False,True])
def test_worker_settings_and_gate(bonus,progressive):
    s=state(no_joker_bonus=bonus,black_flower_progressive=progressive)
    s.current_player_index=0;s.player_list[0].discard_tiles=[24]
    p=s.player_list[1];p.hand_tiles=[24];p.combination_tiles=['k32','k16','k36','k28'];p.huapai_list=[55,56,57]
    async def worker(_,fn,*args,**kw):return fn(*args,**kw)
    with patch('server.gamestate.game_guobiao.four_jokers_runtime.run_room_bot_cpu',worker):
        actions=asyncio.run(evaluate_actions(s,check_action_after_cut,24))
    assert 'hu_first' in actions[1]
    expected=32+(16 if progressive else 3)
    expected=expected+8 if bonus=='add8' else expected*2 if bonus=='double' else expected
    assert s.result_dict['hu_first'][0]==expected
    payload=_build_game_start_payload_for_viewer(s,1)
    assert payload['no_joker_bonus']==bonus and payload['black_flower_progressive']==progressive


@pytest.mark.parametrize('visible', [2,3])
@pytest.mark.parametrize('draw', [51,52,53,54,23])
def test_last_copy_requires_actual_natural_draw(visible, draw):
    s=state(no_joker_bonus='none');s.current_player_index=0
    p=s.player_list[0]
    p.hand_tiles=[51 if draw==23 else 21,22,45,45,draw]
    p.combination_tiles=['s12','s25','s32']
    p.waiting_tiles=rule.waiting_tiles(p.hand_tiles[:-1],p.combination_tiles)
    for other in s.player_list[1:]:
        other.hand_tiles=[];other.discard_tiles=[];other.combination_tiles=[]
    s.player_list[1].discard_tiles=[23]*visible
    actions={i:[] for i in range(4)}
    check_hepai(s,actions,draw,0,'handgot')
    assert 'hu_self' in actions[0]
    score,fans=s.result_dict['hu_self']
    assert ('和绝张' in fans)==(draw==23 and visible==3)
    assert score==(15 if draw==23 and visible==3 else 11)


@pytest.mark.parametrize('tile,slot,cut_class,accepted',[(51,False,False,False),(51,False,True,False),
 (51,True,True,False),(54,True,False,False),(54,True,True,True),(11,False,False,True)])
def test_queue_rejects_retained_jokers(tile,slot,cut_class,accepted):
    s=state();p=s.player_list[0];p.hand_tiles=[11,51,52,53,54]
    if slot:mark_draw_slot(p,54)
    s.waiting_players_list=[0];s.game_status='waiting_hand_action';s.current_player_index=0;s.action_dict={0:['cut']}
    asyncio.run(get_ai_action(s,0,'cut',cut_class,tile,4,None))
    assert (not s.action_queues[0].empty())==accepted


@pytest.mark.parametrize('missing',[None,51,52,53,54])
def test_flower_kong_queue_checks_four_distinct_tiles(missing):
    s=state();p=s.player_list[0];p.hand_tiles=BASE[:10]+list(rule.JOKERS)
    if missing:p.hand_tiles.remove(missing)
    s.waiting_players_list=[0];s.game_status='waiting_hand_action';s.current_player_index=0;s.action_dict={0:['angang','cut']}
    asyncio.run(get_ai_action(s,0,'angang',None,None,None,51))
    assert (not s.action_queues[0].empty())==(missing is None)


@pytest.mark.parametrize('slot',[False,True])
def test_flower_kong_execution_and_record(slot):
    from .wait_action import wait_action
    from .combination_mask_view import get_combination_fields_for_viewer
    s=state();p=s.player_list[0];p.hand_tiles=BASE[:10]+list(rule.JOKERS)
    if slot:mark_draw_slot(p,54)
    s.current_player_index=0;s.game_status='waiting_hand_action';s.action_dict={0:['angang','cut'],1:[],2:[],3:[]}
    s.game_record={'game_round':{f'round_index_{s.round_index}':{'action_ticks':[]}}}
    async def run():
        task=asyncio.create_task(wait_action(s));await asyncio.sleep(0)
        await get_ai_action(s,0,'angang',None,None,None,51)
        await asyncio.wait_for(task,3)
    asyncio.run(run())
    assert p.hand_tiles==BASE[:10] and p.combination_tiles==['G51']
    assert set(p.combination_mask[0][1::2])==set(rule.JOKERS)
    assert p.combination_mask[0][::2]==[0]*4
    assert p.combination_mask[0][1]==(54 if slot else 51)
    assert s.game_status=='deal_card_after_gang'
    assert get_combination_fields_for_viewer(p,1)[1]==p.combination_mask
    ticks=s.game_record['game_round'][f'round_index_{s.round_index}']['action_ticks']
    assert set(next(t for t in ticks if t[0]=='ag')[3:7])==set(rule.JOKERS)
    from ..verifier.record_sim.meld_codec import build_angang_mask_from_removed
    assert build_angang_mask_from_removed(p.combination_mask[0][1::2],rule.SUB_RULE)==p.combination_mask[0]


@pytest.mark.parametrize('kind',['auto','offline'])
def test_takeover_never_discards_initial_or_retained_joker(kind, monkeypatch):
    import importlib
    module=importlib.import_module('server.gamestate.public.'+('ai.auto_cut_ai' if kind=='auto' else 'offline.offline_auto_action'))
    s=state();p=s.player_list[0];p.hand_tiles=[11,12,13,51];p.tag_list=[]
    s.waiting_players_list=[0];s.game_status='onlycut_after_action';s.action_dict={0:['cut']}
    received=[]
    async def submit(*args):received.append(args)
    async def instant(*args):pass
    monkeypatch.setattr(module,'get_ai_action',submit)
    monkeypatch.setattr(module.asyncio,'sleep',instant)
    asyncio.run((module.auto_cut_action if kind=='auto' else module.offline_auto_action)(s,0,['cut'],'onlycut_after_action'))
    assert len(received)==1
    assert received[0][2:6]==('cut',False,13,2)


@pytest.mark.parametrize('jokers',range(1,5))
def test_bot_uses_structure_only(jokers):
    from ...game_calculation.guobiao_hepai_check import Chinese_Hepai_Check
    hand=BASE[:14-jokers]+list(rule.JOKERS[:jokers])
    with patch.object(Chinese_Hepai_Check,'hepai_check',side_effect=AssertionError('speculative scoring')):
        assert choose_cut(hand,sub_rule=rule.SUB_RULE) not in rule.JOKERS
        if jokers==4:assert choose_action(hand,[],['angang','cut'],{},sub_rule=rule.SUB_RULE)==('angang',51)


@pytest.mark.parametrize('flowers,circles,seed',[(True,1,811),(True,1,42),(True,4,20261013)])
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
                action,tile=('ready',None) if 'ready' in choices else choose_action(p.hand_tiles,p.combination_tiles,choices,visible,source[-1] if source else None, sub_rule=rule.SUB_RULE)
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
        assert Counter(physical)[59]==0 and len(physical)==144
        assert all(Counter(physical)[t]==1 for t in range(51,59))
        sim.load_round(key)
        for p in sim.players.values():p.score=totals[p.original_player_index]
        frames[key]=[sim.snapshot()];changes=None
        for raw in data['action_ticks']:
            tick=stringify_tick(raw);sim.apply_tick(tick)
            changes=accumulate_score_changes_from_tick(changes,tick,data['seats'])
            frames[key].append(sim.snapshot())
        for i in range(4):totals[i]+=changes[i] if changes else 0
    assert totals==[p.score for p in sorted(g.player_list,key=lambda p:p.original_player_index)]
    directory=os.environ.get('GUOBIAO_HUAHUN_FIXTURES_DIR')
    if directory:
        out=Path(directory);out.mkdir(parents=True,exist_ok=True);name=f'huahun-{int(flowers)}-{circles}-{seed}'
        for suffix,data in [('',g.game_record),('-frames',frames),('-actions',dict(counts))]:
            (out/(name+suffix+'.json')).write_text(json.dumps(data,ensure_ascii=False,default=str),encoding='utf8')
