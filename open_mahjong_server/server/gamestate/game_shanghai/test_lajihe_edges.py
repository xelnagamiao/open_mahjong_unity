"""动作裁决、真实八花选择队列、私有提示和牌谱回退边界。"""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_lajihe_state import make_state, ticks, PLAIN, HONORS
from ..public.ai.get_action import get_ai_action
from ..game_taiwan.boardcast import send_reconnect_game_state
from ..verifier.record_sim import RecordSim, stringify_tick
from ..verifier.protocol import filter_client_actions
from .LajiheGameState import lajihe_bot


@pytest.mark.parametrize('choice',['lajihe_flower_end','lajihe_flower_continue'])
def test_eight_flowers_choice_through_actual_message_queue(choice):
    async def run():
        state=make_state();state.step_time=2
        assert filter_client_actions(['lajihe_flower_end','lajihe_flower_continue'],kind='hand')==['lajihe_flower_end','lajihe_flower_continue']
        p=state.player_list[0];p.remaining_time=10
        p.huapai_list=list(range(51,58));p.hand_tiles=PLAIN[:-1]+[58]
        state.tiles_list=[22,23]
        messages=[];tasks=[]
        async def respond():
            while 0 not in state.waiting_players_list: await asyncio.sleep(0)
            # 非获花者及未列出的动作不能代替获花者做选择。
            await get_ai_action(state,1,choice,None,None,None,None)
            await get_ai_action(state,0,'hu_self',None,None,None,None)
            assert state.action_queues[0].empty()
            await get_ai_action(state,0,choice,None,None,None,None)
        class Socket:
            async def send_json(self,payload):
                messages.append(payload)
                if payload.get('ask_hand_action_info',{}).get('action_list')==['lajihe_flower_end','lajihe_flower_continue']:
                    tasks.append(asyncio.create_task(respond()))
        state.game_server.user_id_to_connection[101]=SimpleNamespace(websocket=Socket())
        await asyncio.wait_for(state.execute_buhua(0),5)
        await asyncio.gather(*tasks)
        assert [p.score for p in state.player_list]==[90,-30,-30,-30]
        assert (state.game_status=='END')==(choice=='lajihe_flower_end')
        result=next(m['show_result_info'] for m in messages if 'show_result_info' in m)
        assert result['hu_class']=='lajihe_eight_flowers' and not result.get('hepai_player_hand')
        assert result['next_status']=='round_continue'
        assert any(t[:3]==['state','lajihe_flower_choice',0] for t in ticks(state))
    asyncio.run(run())


@pytest.mark.parametrize('count',[0,1,2,3])
def test_rob_added_kong_responses_rollback_or_complete(count):
    async def run():
        state=make_state();p=state.player_list[0]
        p.hand_tiles=[11,12,13,21,22,23,31,32,33,41,47]
        p.combination_tiles=['k47'];p.combination_mask=[[0,47,0,47,1,47]]
        p.has_draw_slot=True;p.last_drawn_tile=47
        # 其他家等同一张47；副本是独立裁决样例，不用于整副牌守恒测试。
        for other in state.player_list[1:]: other.hand_tiles=HONORS[:-1]
        await state.execute_jiagang(0,47)
        assert state.game_status=='waiting_action_qianggang'
        allowed=deepcopy(state.action_dict)
        responses={i:{'action_type':next(a for a in allowed[i] if a.startswith('hu_')) if i<=count else 'pass'} for i in range(1,4)}
        await state.resolve_rob_kong_responses(responses,allowed)
        assert p.combination_tiles==(['k47'] if count else ['g47'])
        assert len(p.combination_mask[0])==(6 if count else 8)
        assert 47 not in p.hand_tiles
        assert len(state.pending_winners)==count
        assert (state.game_status=='deal_card_after_gang')==(count==0)
        assert all(p.lajihe_passed_win for p in state.player_list[count+1:])
    asyncio.run(run())


@pytest.mark.parametrize('winner',[False,True])
def test_win_beats_pung_and_pung_beats_chow(winner):
    async def run():
        state=make_state();state.game_status='waiting_action_after_cut'
        state.player_list[0].discard_tiles=[11]
        state.player_list[1].hand_tiles=[12,13,15,17,19,21,23,25,27,29,31,33,35]
        state.player_list[2].hand_tiles=[11,11,15,17,19,21,23,25,27,29,31,33,35]
        if winner:
            state.player_list[3].hand_tiles=[12,13,21,22,23,31,32,33,17,18,19,41,41]
            state.player_list[3].huapai_list=[51,52]
        allowed=state.check_discard_actions(11)
        responses={1:{'action_type':'chi_right'},2:{'action_type':'peng'}}
        if winner: responses[3]={'action_type':'hu_third'}
        await state.resolve_discard_responses(responses,allowed)
        assert not state.player_list[1].combination_tiles
        if winner:
            assert [w['index'] for w in state.pending_winners]==[3]
            assert not state.player_list[2].combination_tiles
        else:
            assert state.player_list[2].combination_tiles==['k11']
            assert state.current_player_index==2
    asyncio.run(run())


def test_empty_wall_allows_last_discard_win_but_no_claim_or_kong():
    state=make_state();state.tiles_list=[]
    state.player_list[1].hand_tiles=HONORS[:-1]
    actions=state.check_discard_actions(47)[1]
    assert actions==['hu_first','pass']
    state.player_list[0].hand_tiles=[11]*4+PLAIN[4:]
    state.player_list[0].has_draw_slot=True
    assert not state._kong_allowed(0,11,'G')


def test_private_missed_win_is_sent_on_same_cut_and_reconnect():
    async def run():
        state=make_state();state.master_seed=1;p=state.player_list[0];p.hand_tiles=HONORS.copy()
        for other in state.player_list:
            state.game_server.user_id_to_connection[other.user_id]=SimpleNamespace(websocket=AsyncMock())
        await state.execute_cut(0,{'TileId':47,'cutIndex':13,'cutClass':False})
        for other in state.player_list:
            packet=state.game_server.user_id_to_connection[other.user_id].websocket.send_json.await_args.args[0]
            assert packet['do_action_info']['lajihe_passed_win']==(other.player_index==0)
        await send_reconnect_game_state(state,p)
        packets=[c.args[0] for c in state.game_server.user_id_to_connection[101].websocket.send_json.await_args_list]
        assert next(x['game_info'] for x in packets if 'game_info' in x)['lajihe_passed_win']
    asyncio.run(run())


@pytest.mark.parametrize('target',[51,0,None,99,21])
def test_invalid_kong_leaves_hand_and_record_unchanged(target):
    async def run():
        state=make_state();p=state.player_list[0];p.hand_tiles=PLAIN.copy();p.has_draw_slot=True
        before=deepcopy(ticks(state))
        await state.execute_angang(0,target)
        await state.execute_jiagang(0,target)
        assert p.hand_tiles==PLAIN and not p.combination_tiles and ticks(state)==before
    asyncio.run(run())


def test_timeout_flower_replacement_and_valid_cut():
    async def run():
        state=make_state();p=state.player_list[0]
        p.hand_tiles=PLAIN[:-1]+[51];state.tiles_list=[21,52,41]
        state.game_status='waiting_hand_action';state.action_dict=state.check_hand_actions(0)
        await state.wait_action()
        assert p.huapai_list==[51] and p.hand_tiles[-1]==41
        state.action_dict=state.check_hand_actions(0)
        await state.wait_action()
        assert len(p.discard_tiles)==1 and len(p.hand_tiles)==13
    asyncio.run(run())


def test_replay_award_missed_win_and_public_concealed_kong():
    state=make_state();state.player_list[0].hand_tiles=PLAIN.copy()
    record=deepcopy(state.game_record);round_key='round_index_1'
    record['game_round'][round_key]['p0_tiles']=[45]*4+PLAIN[4:]
    sim=RecordSim(record);sim.load_round(round_key)
    sim.apply_tick(stringify_tick(['lajihe_eight_flowers',2,30,['八花齐'],[-30,-30,90,-30]]))
    assert [p.score for p in sim.players.values()]==[-30,-30,90,-30]
    assert not any(p.is_hu for p in sim.players.values())
    sim.apply_tick(stringify_tick(['state','lajihe_passed_win',2,'T']))
    assert sim.players[2].tag_list==['lajihe_passed_win']
    sim.apply_tick(stringify_tick(['state','lajihe_passed_win',2,'F']))
    assert not sim.players[2].tag_list
    sim.apply_tick(stringify_tick(['ag',45,'F']))
    assert sim.players[0].combination_masks==[[0,45]*4]


@pytest.mark.parametrize('label',['抢杠（5分）','抢杠（10分）'])
def test_replay_robbed_kong_reverts_to_pung(label):
    state=make_state();sim=RecordSim(state.game_record);sim.load_round('round_index_1')
    p=sim.players[0];p.tile_list=[47];p.combination_tiles=['k47'];p.combination_masks=[[0,47,0,47,1,47]]
    sim.apply_tick(stringify_tick(['jg',47,'T']))
    assert p.combination_tiles==['g47']
    sim.apply_tick(stringify_tick(['hu_first',1,10,['垃圾和',label],[-30,30,0,0],47]))
    assert p.combination_tiles==['k47'] and len(p.combination_masks[0])==6
    assert p.tile_list==[]


@pytest.mark.parametrize('action,status',[('lajihe_flower_continue','waiting_flower_choice'),('buhua','waiting_hand_action')])
def test_bot_submits_allowed_flower_action(action,status):
    async def run():
        state=make_state();state.bot_speed='instant';state.player_list[0].user_id=0
        state.game_status=status;state.waiting_players_list=[0];state.action_dict={0:[action]}
        await lajihe_bot(state,0,[action],status)
        assert state.action_queues[0].get_nowait()['action_type']==action
    asyncio.run(run())


def test_bot_does_not_submit_after_flower_window_closes():
    async def run():
        state=make_state();state.bot_speed='instant';state.player_list[0].user_id=0
        state.game_status='END'
        await lajihe_bot(state,0,['lajihe_flower_continue'],'waiting_flower_choice')
        assert state.action_queues[0].empty()
    asyncio.run(run())


def test_failed_actions_do_not_mutate_hand_or_missed_win_state():
    async def run():
        state=make_state();p=state.player_list[0];p.hand_tiles=PLAIN.copy()
        assert state.score_candidate(1,'discard') is None
        assert not any(state.check_discard_actions(51).values())
        await state.execute_claim(1,'peng')
        state.player_list[0].discard_tiles=[12]
        await state.execute_claim(1,'gang')
        assert not state.player_list[1].combination_tiles
        for reason in ('flower','pre_draw','ready'):
            p.hand_tiles=PLAIN[:-1]+[51] if reason=='flower' else PLAIN.copy()
            state.opening_before_draw=0 if reason=='pre_draw' else None
            original=p.hand_tiles.copy()
            await state.execute_cut(0,{'TileId':11},declare_ready=reason=='ready')
            assert p.hand_tiles==original and not p.lajihe_passed_win
        state.game_status='waiting_action_after_cut';state.enter_water(1)
        before=deepcopy(ticks(state));state.enter_water(1)
        assert ticks(state)==before
    asyncio.run(run())


def test_fourth_pung_with_only_forbidden_cut_is_not_offered():
    state=make_state();p=state.player_list[1]
    p.combination_tiles=['s22','s25','s32'];p.hand_tiles=[11]*4
    assert 'peng' not in state.check_discard_actions(11)[1]


def test_initial_direct_kong_replaces_flowers_before_kong_tile_and_empty_tail_keeps_missed_win():
    async def run():
        state=make_state();p=state.player_list[0]
        p.hand_tiles=[11]*3+[12,14,16,18,21,23,25,27,29,51]
        state.current_player_index=3;state.player_list[3].discard_tiles=[11];state.tiles_list=[22,23,24]
        await state.execute_claim(0,'gang');p.lajihe_passed_win=True
        await state._deal_supplement()
        assert p.lajihe_passed_win and state.action_dict[0]==['buhua']
        await state.execute_buhua(0)
        assert p.huapai_list==[51] and p.hand_tiles[-1]==24
        assert state.game_status=='deal_card_after_gang' and not p.lajihe_passed_win
        await state._deal_supplement()
        assert p.hand_tiles[-1]==23 and state.tiles_list==[22]
        state.tiles_list=[];p.lajihe_passed_win=True
        await state._deal_supplement()
        assert state.game_status=='END' and p.lajihe_passed_win
    asyncio.run(run())
