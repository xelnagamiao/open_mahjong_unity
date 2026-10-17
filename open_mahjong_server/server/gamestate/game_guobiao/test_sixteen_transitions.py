"""Exercise the common state machine with sixteen-tile, fifth-meld inputs."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_sixteen import state, HAND, rule
from .wait_action import wait_action
from .action_check import check_action_after_cut, check_action_hand_action, refresh_waiting_tiles
from .buhua_broadcast import perform_buhua_and_broadcast
from ..public.ai.get_action import get_ai_action
from ..public.random_seed_manager import setup_random_seed_system
from ..public.game_record_manager import init_game_record, init_game_round


def recorded_state(**settings):
    g=state(**settings)
    g.master_seed,g.salt,g.commitment,g.isPlayerSetRandomSeed=setup_random_seed_system(42)
    init_game_record(g)
    init_game_round(g)
    return g


@pytest.mark.parametrize('seat',range(4))
@pytest.mark.parametrize('action,tile,hand,expected',[
    ('chi_right',11,[12,13,45,46],'s12'),
    ('chi_mid',12,[11,13,45,46],'s12'),
    ('chi_left',13,[11,12,45,46],'s12'),
    ('peng',22,[22,22,45,46],'k22'),
    ('gang',22,[22,22,22,45],'g22'),
    ('angang',22,[22,22,22,22,45],'G22'),
    ('jiagang',22,[22,45],'g22'),
])
def test_fifth_meld_mutations_and_supplement(seat,action,tile,hand,expected):
    async def run():
        g=recorded_state()
        g.tactical_call=False;g.claim_protection=False
        p=g.player_list[seat];p.hand_tiles=hand[:]
        p.combination_tiles=['s15','s28','s35','k41']
        p.combination_mask=[[1,14,0,15,0,16],[1,27,0,28,0,29],[1,34,0,35,0,36],[1,41,0,41,0,41]]
        own=action in ('angang','jiagang')
        if action=='jiagang':
            p.combination_tiles.append('k22');p.combination_mask.append([1,22,0,22,0,22])
        g.current_player_index=seat if own else (seat-1)%4
        g.game_status='waiting_hand_action' if own else 'waiting_action_after_cut'
        if not own:g.player_list[g.current_player_index].discard_tiles=[tile]
        choices=check_action_hand_action(g,seat) if own else check_action_after_cut(g,tile)
        assert action in choices[seat]
        g.action_dict={i:[action] if i==seat else [] for i in range(4)}
        task=asyncio.create_task(wait_action(g));await asyncio.sleep(0)
        await get_ai_action(g,seat,action,False,None,None,tile)
        await asyncio.wait_for(task,3)
        assert len(p.combination_tiles)==5 and p.combination_tiles[-1]==expected
        assert len(p.combination_mask)==5
        assert g.current_player_index==seat
        if action in ('gang','angang','jiagang'):
            assert g.game_status=='deal_card_after_gang'
            assert p.hand_tiles==[45]
            refresh_waiting_tiles(g,seat)
            p.get_gang_tile([19,45,39],g)
            assert len(p.hand_tiles)+3*len(p.combination_tiles)==17
            assert 'hu_self' in check_action_hand_action(g,seat,is_get_gang_tile=True)[seat]
            assert '杠上开花' in g.result_dict['hu_self'][1]
        else:
            assert g.game_status=='onlycut_after_action'
            assert p.hand_tiles==[45,46]
        assert len(g.game_record['game_round']['round_index_1']['action_ticks'])==1
    asyncio.run(run())


@pytest.mark.parametrize('seat',range(4))
@pytest.mark.parametrize('opening',[False,True])
def test_reconnect_restores_full_hand_settings_waits_and_pending_action(seat,opening):
    async def run():
        g=recorded_state(tian_di_ren_he=opening)
        g.tips=True;g.current_player_index=seat;g.game_status='waiting_hand_action'
        p=g.player_list[seat];p.hand_tiles=HAND[:];p.tag_list=['offline']
        g.action_dict={i:['cut'] if i==seat else [] for i in range(4)}
        g.waiting_players_list=[seat]
        await g.player_reconnect(p.user_id)
        messages=[c.args[0] for c in g.game_server.user_id_to_connection[p.user_id].websocket.send_json.await_args_list]
        info=next(m['game_info'] for m in messages if m['type'].endswith('game_start'))
        assert info['sub_rule']==rule.SUB_RULE and info['tian_di_ren_he']==opening
        assert info.get('four_jokers_hints') is None
        assert next(row['hand_tiles'] for row in info['players_info'] if row['player_index']==seat)==HAND
        for row in info['players_info']:
            assert bool(row.get('hand_tiles'))==(row['player_index']==seat)
        ask=next(m['ask_hand_action_info'] for m in messages if m['type'].endswith('broadcast_hand_action'))
        assert ask['action_list']==['cut'] and ask['player_index']==seat
        assert 'offline' not in p.tag_list
    asyncio.run(run())


@pytest.mark.parametrize('source',range(4))
@pytest.mark.parametrize('robbing',[False,True])
def test_three_ron_claimants_keep_seat_priority(source,robbing):
    async def run():
        g=recorded_state();g.current_player_index=source;g.jiagang_tile=46
        g.tactical_call=False;g.player_list[source].discard_tiles=[46]
        g.game_status='waiting_action_qianggang' if robbing else 'waiting_action_after_cut'
        g.action_dict={source:[],**{(source+i)%4:[a,'pass'] for i,a in enumerate(('hu_first','hu_second','hu_third'),1)}}
        task=asyncio.create_task(wait_action(g));await asyncio.sleep(0)
        for offset,action in [(3,'hu_third'),(2,'hu_second'),(1,'hu_first')]:
            await get_ai_action(g,(source+offset)%4,action,False,None,None,None)
        await asyncio.wait_for(task,3)
        assert g.hu_class=='hu_first' and g.resolve_hepai_player_index(g.hu_class)==(source+1)%4
    asyncio.run(run())


@pytest.mark.parametrize('draw',[False,True])
def test_timeout_cuts_exactly_one_from_seventeen_tiles(draw):
    async def run():
        g=recorded_state();g.current_player_index=0;g.game_status='waiting_hand_action'
        p=g.player_list[0];p.hand_tiles=HAND[:];p.has_draw_slot=draw;p.last_drawn_tile=46 if draw else None
        p.remaining_time=0;g.step_time=0
        g.action_dict={0:['cut'],1:[],2:[],3:[]}
        await asyncio.wait_for(wait_action(g),2)
        assert len(p.hand_tiles)==16 and len(p.discard_tiles)==1 and not p.has_draw_slot
        assert g.game_status in ('deal_card','waiting_action_after_cut')
    asyncio.run(run())


@pytest.mark.parametrize('instant',[False,True])
def test_flower_replacements_remain_individually_requested(instant):
    async def run():
        g=recorded_state();p=g.player_list[0]
        p.hand_tiles=HAND[:15]+[51,52];g.tiles_list=[19,21,53,22]
        with patch('server.gamestate.game_guobiao.buhua_broadcast.broadcast_do_action',new=AsyncMock()), \
             patch('server.gamestate.game_guobiao.buhua_broadcast.asyncio.sleep',new=AsyncMock()):
            await perform_buhua_and_broadcast(g,0,instant=instant)
            assert p.huapai_list==[52] and 51 in p.hand_tiles and 53 in p.hand_tiles
            assert len(p.hand_tiles)==17 and len(g.tiles_list)==3
            await perform_buhua_and_broadcast(g,0,instant=instant)
            assert p.huapai_list==[52,53] and 51 in p.hand_tiles
            assert len(p.hand_tiles)==17 and len(g.tiles_list)==2
    asyncio.run(run())


@pytest.mark.parametrize('action,robbed',[('hu_self',False),('hu_first',False),('hu_first',True)])
def test_wrong_win_continues_without_inserting_ron_tile(action,robbed):
    async def run():
        g=recorded_state(open_cuohe=True)
        g.current_player_index=0;g.player_list[0].discard_tiles=[46]
        winner=0 if action=='hu_self' else 1
        before=list(g.player_list[winner].hand_tiles)
        g.result_dict={action:(1,['自摸'])};g.hu_class=action
        if robbed:
            g.jiagang_tile=46;g.qianggang_action_dict={1:['hu_first','pass']};g.qianggang_responses={1:'hu_first'}
        await g.apply_cuohe_resume_after_ready(winner,action,robbed)
        assert 'peida' in g.player_list[winner].tag_list
        assert g.player_list[winner].hand_tiles==before
        assert g.hu_class==''
        assert g.game_status==('deal_card_after_gang' if robbed else 'waiting_hand_action' if action=='hu_self' else 'waiting_action_after_cut')
    asyncio.run(run())
