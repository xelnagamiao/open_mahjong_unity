"""Drive complete real rule loops with deterministic in-memory players (no service/database)."""
import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_rule_branches import game
from .wait_action import wait_action, _is_valid_cut_action
from ..public.ai.get_action import get_ai_action
from .action_check import _normalize
from .init_tiles import init_riichi_tiles
from ..public.ai.smart_bot_logic import find_best_cut, count_visible_tiles
from ...game_calculation.riichi.rule_config import preset_room_config


@pytest.mark.parametrize('preset,langyong', [('majsoul',False),('tenhou',False),('jpml_a',False),('mleague',False),('majsoul',True)])
def test_complete_matches_keep_physical_tiles_scores_and_record_config(preset,langyong,caplog):
    async def run():
        g=game(preset)
        for key,value in preset_room_config(preset).items(): setattr(g,key,value)
        if langyong: g.sub_rule='riichi/langyong';g.starting_score=50000
        g.max_round=1;g.open_xiru=False;g.room_random_seed=20260924
        g.game_server.gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock())
        g.game_server.room_manager=SimpleNamespace(finish_custom_game_room=AsyncMock())
        for i,p in enumerate(g.player_list): p.user_id=i+1;p.hand_tiles=[];p.has_draw_slot=False
        g.game_server.user_id_to_connection={}
        original_sleep=asyncio.sleep
        actions=Counter()
        async def instant_sleep(_): await original_sleep(0)
        async def automated_wait():
            task=asyncio.create_task(wait_action(g))
            await original_sleep(0)
            try:
                for seat in list(g.waiting_players_list):
                    p=g.player_list[seat]; choices=g.action_dict.get(seat,[])
                    if not choices: continue
                    action=next((a for a in choices if a.startswith('hu_') and not g.result_dict.get(a,{}).get('no_yaku')),None)
                    tile=None;target=None
                    if action is None and 'riichi_cut' in choices:
                        action='riichi_cut';tile=next(iter(p.riichi_candidate_cuts))
                    if action is None:
                        for candidate in ['angang','jiagang']:
                            if candidate not in choices: continue
                            for t in p.hand_tiles:
                                if _is_valid_cut_action(g,seat,dict(action_type=candidate,target_tile=_normalize(t))):
                                    action=candidate;target=_normalize(t);break
                            if action: break
                    if action is None and 'cut' in choices:
                        action='cut'
                        if 'riichi' in p.tag_list: tile=p.hand_tiles[-1]
                        else: tile,_=find_best_cut(p.hand_tiles,len(p.combination_tiles),count_visible_tiles(g),set(map(_normalize,p.kuikae_forbidden_tiles)))
                    if action is None:
                        action=next((a for a in ['gang','peng','chi_left','chi_mid','chi_right','pass','ready'] if a in choices),choices[0])
                    actions[action]+=1
                    await get_ai_action(g,seat,action,bool(p.has_draw_slot and tile==p.hand_tiles[-1]),tile,None,target)
                await task
            finally:
                if not task.done(): task.cancel()
            # Every physical tile remains in exactly one wall/hand/meld/river, except a ron clone at END.
            if g.game_status not in ('END','waiting_ready'):
                all_tiles=list(g.tiles_list)
                for p in g.player_list:
                    all_tiles+=p.hand_tiles+p.discard_tiles
                    for mask in p.combination_mask: all_tiles+=mask[1::2]
                assert len(all_tiles)==136, (g.game_status,actions)
                assert set(Counter(map(_normalize,all_tiles)).values())=={4}
            assert sum(p.score for p in g.player_list)+g.riichi_sticks*1000==g._starting_score()*4
            assert g.round_index<30, 'match failed to terminate'
        g.wait_action=automated_wait
        with patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep',instant_sleep), \
             patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase',new=AsyncMock()), \
             patch('server.gamestate.game_riichi.RiichiGameState.vote_checkpoint',new=AsyncMock()):
            await asyncio.wait_for(g.game_loop_riichi(),timeout=45)
        assert g.game_record['game_title']['detailed_config']==g.detailed_config
        assert all(r['action_ticks'][-1]==['end'] for r in g.game_record['game_round'].values())
        assert actions['cut']>30
        assert g._riichi_finalized
        assert all(hasattr(p,'riichi_points') for p in g.player_list)
        assert not any('失败' in r.message or 'ERROR'==r.levelname for r in caplog.records)
    asyncio.run(run())


def test_tile_shuffle_is_reproducible_and_does_not_reseed_other_rooms():
    import random
    g=game();g.master_seed=20260924
    for p in g.player_list:p.hand_tiles=[]
    before=random.getstate();init_riichi_tiles(g)
    assert random.getstate()==before
    first=[list(p.hand_tiles) for p in g.player_list]+[list(g.tiles_list)]
    for p in g.player_list:p.hand_tiles=[]
    init_riichi_tiles(g)
    assert first==[list(p.hand_tiles) for p in g.player_list]+[list(g.tiles_list)]
