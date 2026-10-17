import asyncio
import warnings
from types import SimpleNamespace
from unittest.mock import AsyncMock,Mock,patch

import pytest

from .test_state import make_state,drawn,ticks
from .JiujiangGameState import JiujiangGameState
from . import bot
from tests.local_hint_oracles.jiujiang import compute_hand
from .result import broadcast_result
from ...game_calculation.jiujiang.test_scoring import PLAIN
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan.boardcast import broadcast_game_start

MODULE='server.gamestate.game_jiujiang.JiujiangGameState'


@pytest.mark.parametrize('action,tile,used',[('chi_right',11,[12,13]),('chi_mid',12,[11,13]),('chi_left',13,[11,12]),('peng',45,[45,45]),('gang',45,[45,45,45])])
def test_execute_each_claim_and_first_discard_ready(action,tile,used):
    async def run():
        state=make_state();p=state.player_list[1]
        p.discard_count=0;p.first_discard_done=False
        p.hand_tiles=used+[25,25];p.combination_tiles=['s22','s32','k19'];p.combination_mask=[[0,21,0,22,0,23],[0,31,0,32,0,33],[0,19]*3]
        state.player_list[0].discard_tiles=[tile]
        await state.execute_claim(1,action)
        assert len(p.combination_tiles)==4 and not state.player_list[0].discard_tiles
        if action=='gang':
            await state._deal_supplement()
            assert state.last_draw_after_kong and p.has_draw_slot
        else:
            assert not p.has_draw_slot and 'hu_self' not in state.after_claim_actions()[1]
            assert not state._kong_candidates(1)
            assert 25 in state.ready_candidate_cuts(1)
            await state.execute_cut(1,{'TileId':25,'TileIndex':0,'CutClass':False},declare_ready=True)
            assert p.declared_ready
    asyncio.run(run())


@pytest.mark.parametrize('ready',[False,True])
def test_concealed_kong_then_tail_draw_then_kong_discard_bonus(ready):
    async def run():
        state=make_state();p=state.player_list[0]
        drawn(p,[21,22,23,31,32,33,17,18,19,25,11,11,11,11]);p.ready_locked=p.declared_ready=ready
        await state.execute_angang(0,11)
        assert p.combination_tiles==['G11'] and not p.has_draw_slot
        await state._deal_supplement()
        assert state.last_draw_after_kong and p.last_drawn_tile==25
        result=state.score_candidate(0,'self_draw')
        assert '杠上开花（1分）' in result['fan_names']
        await state.execute_cut(0,{'TileId':25,'TileIndex':len(p.hand_tiles)-1,'CutClass':True})
        assert state.jj_kong_discard
        q=state.player_list[1];q.hand_tiles=PLAIN[:-1]
        assert '杠上炮（1分）' in state.score_candidate(1,'discard',25)['fan_names']
    asyncio.run(run())


@pytest.mark.parametrize('rob',[False,True])
def test_added_kong_pass_or_rob_rolls_back_exactly_one_tile(rob):
    async def run():
        state=make_state();p=state.player_list[0]
        p.combination_tiles=['k25'];p.combination_mask=[[1,25,0,25,0,25]]
        drawn(p,[11,12,13,21,22,23,31,32,33,19,25])
        if rob: state.player_list[1].hand_tiles=PLAIN[:-1]
        await state.execute_jiagang(0,25)
        if rob:
            assert 'hu_first' in state.action_dict[1]
            await state.resolve_rob_kong_responses({1:{'action_type':'hu_first'}},state.action_dict)
            assert p.combination_tiles==['k25'] and 25 not in p.hand_tiles
            assert state.pending_winners[0]['source']=='robbing_kong'
            assert '抢杠胡（1分）' in state.pending_winners[0]['detail']['fan_names']
            assert not p.discard_tiles
        else:
            assert p.combination_tiles==['g25']
            await state._deal_supplement()
            assert state.last_draw_after_kong
    asyncio.run(run())


def test_illegal_actions_and_hint_switches():
    async def run():
        state=make_state();p=state.player_list[0]
        drawn(p,PLAIN);before=p.hand_tiles.copy()
        await state.execute_angang(0,11);await state.execute_jiagang(0,11)
        await state.execute_claim(1,'peng')
        await state.execute_cut(0,{'TileId':49,'TileIndex':0,'CutClass':False})
        await state.execute_cut(0,{'TileId':11,'TileIndex':0,'CutClass':False},declare_ready=True)
        assert p.hand_tiles==before
        p.tag_list=['peida'];assert state.score_candidate(0,'self_draw') is None
        assert state.check_hand_actions(0)[0]==['cut']
        state.tips=state.count_tips=False
        await state.prepare_private_fields([0]);assert 'jiujiang_hints' not in state.build_private_game_info_fields(0)
        state.count_tips=True;assert 'jiujiang_hints' in state.build_private_game_info_fields(0)
        p.has_draw_slot=False;p.tag_list=[];assert state.score_candidate(0,'self_draw') is None
        state._mark_eight_flowers_if_ready(0);assert state._liability_payer_for_win()==None
        with pytest.raises(ValueError): JiujiangGameState(state.game_server,{'sub_rule':'bad'},None,None,'invalid')
    asyncio.run(run())


@pytest.mark.parametrize('mutation',['hand','melds','flowers'])
def test_local_hint_packet_is_immutable_and_refreshes_without_cpu(mutation):
    from copy import deepcopy
    async def run():
        state=make_state();state.player_list[0].hand_tiles=PLAIN[:-1]
        cpu=AsyncMock(side_effect=AssertionError('UI previews must stay on the client'))
        with patch('server.gamestate.public.ai.bot_executor.run_room_bot_cpu',cpu):
            await state.prepare_private_fields([0])
            packet=state._client_contexts[0][1];saved=deepcopy(packet)
            player=state.player_list[0]
            if mutation=='hand': player.hand_tiles.append(25)
            elif mutation=='melds': player.combination_tiles.append('G45')
            else: player.huapai_list.append(51)
            await state.prepare_private_fields([0])
        cpu.assert_not_awaited()
        assert packet==saved
        assert state._client_contexts[0][1]!=packet
        assert 'waiting_details' not in state._client_contexts[0][1]
    asyncio.run(run())


def test_failed_flower_replacement_does_not_advance():
    async def run():
        state=make_state();drawn(state.player_list[0],PLAIN[:-1]+[47])
        with patch.object(state,'_replace_one_flower',AsyncMock(return_value=False)):
            await state.execute_buhua(0)
        assert not state.player_list[0].huapai_list
    asyncio.run(run())


@pytest.mark.parametrize('terminal',[False,True])
def test_result_reconnect_never_reapplies_payment(terminal):
    async def run():
        state=make_state();socket=AsyncMock();state.game_server.user_id_to_connection[101]=SimpleNamespace(websocket=socket)
        state._terminal_result={'type':'gamestate/jiujiang/show_result'} if terminal else None
        state.player_list[0].score=100
        with patch.object(TaiwanGameState,'player_reconnect',AsyncMock()): await state.player_reconnect(101)
        assert socket.send_json.await_count==int(terminal) and state.player_list[0].score==100
    asyncio.run(run())


def test_exhaustive_draw_reveals_kongs_keeps_dealer_and_persists_four_rounds():
    async def run():
        state=make_state();state.room_random_seed=1
        state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        state.db_manager=SimpleNamespace(store_jiujiang_game_record=Mock(return_value='test-record'))
        async def draw():
            state.pending_winners=[];state.draw_reason='exhaustive'
            state.player_list[0].combination_tiles=['G11'];state.player_list[0].combination_mask=[[2,11]*4]
        with patch.object(state,'_run_hand',draw),patch('server.gamestate.game_jiujiang.lifecycle.liuju_ready_wait_seconds',return_value=0):
            await state.game_loop_chinese()
        rounds=list(state.game_record['game_round'].values())
        assert len(rounds)==4 and all(r['seats']==rounds[0]['seats'] for r in rounds)
        assert all(['jiujiang','reveal_kongs',[[[0,11]*4],[],[],[]]] in r['action_ticks'] for r in rounds)
        assert sum(p.score for p in state.player_list)==0 and state.next_dealer()==0
        state.db_manager.store_jiujiang_game_record.assert_called_once()
    asyncio.run(run())


def test_broken_or_offline_connection_does_not_skip_other_results():
    async def run():
        state=make_state();bad=AsyncMock();bad.send_json.side_effect=OSError('test connection closed');good=AsyncMock()
        state.game_server.user_id_to_connection={101:SimpleNamespace(websocket=bad),102:SimpleNamespace(websocket=good)}
        await broadcast_result(state,hu_class='liuju')
        good.send_json.assert_awaited_once()
        state.player_list[1].tag_list=['offline'];good.reset_mock()
        await broadcast_result(state,hu_class='liuju');good.send_json.assert_not_awaited()
    asyncio.run(run())


@pytest.mark.parametrize('action',['ready','hu_self','buhua','riichi_cut','angang','jiagang','cut','pass','locked'])
def test_bot_uses_legal_targets_and_public_action_entry(action):
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN)
        p.riichi_candidate_cuts={25:[25]}
        if action=='locked': p.ready_locked=True
        async def cpu(state,fn,*args): return (11,0)
        submit=AsyncMock()
        with patch.object(bot,'_wait_until_actionable',AsyncMock(return_value=True)),patch.object(bot,'bot_action_is_current',return_value=True),\
             patch.object(bot,'submit_bot_action',submit),patch.object(bot,'run_room_bot_cpu',cpu),patch.object(state,'_kong_candidates',return_value=[11]):
            await bot.jiujiang_bot_action.__wrapped__(state,0,['cut' if action=='locked' else action],'waiting_hand_action')
        assert submit.await_args.args[3]==('cut' if action=='locked' else action)
        if action=='locked': assert submit.await_args.args[5]==25
    asyncio.run(run())


@pytest.mark.parametrize('stage',['not_actionable','stale_before','stale_after'])
def test_bot_drops_outdated_actions(stage):
    async def run():
        state=make_state();submit=AsyncMock()
        checks=[False] if stage=='stale_before' else [True,False]
        with patch.object(bot,'_wait_until_actionable',AsyncMock(return_value=stage!='not_actionable')),\
             patch.object(bot,'bot_action_is_current',side_effect=checks),patch.object(bot,'submit_bot_action',submit):
            await bot.jiujiang_bot_action.__wrapped__(state,0,['pass'],'waiting_action_after_cut')
        submit.assert_not_awaited()
    asyncio.run(run())
