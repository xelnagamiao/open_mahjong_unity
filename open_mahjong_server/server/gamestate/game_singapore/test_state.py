import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock
import pytest

from .SingaporeGameState import SingaporeGameState
from ...game_calculation.singapore import rules as r
from ...game_calculation.singapore.test_rules import PLAIN
from ..public.game_record_manager import init_game_record, init_game_round
from ..game_taiwan.boardcast import broadcast_do_action, send_reconnect_game_state


def make_state():
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock()), room_manager=SimpleNamespace())
    db = SimpleNamespace(store_singapore_game_record=Mock(return_value='singapore1'))
    state = SingaporeGameState(server, {'room_id':'123456','player_list':[101,102,103,104],
        'sub_rule':r.SUB_RULE,'round_timer':0,'step_timer':0,'game_round':1,'tips':True,
        'room_type':'custom','allow_spectator':False},None,db,'singapore-test')
    for i,p in enumerate(state.player_list):
        p.player_index=p.original_player_index=i
        p.discard_count=1;p.first_discard_done=True
    state.tiles_list=[21,22,23,24,25,26]+[29]*15
    init_game_record(state); init_game_round(state)
    return state


def drawn(p, hand):
    p.hand_tiles=list(hand);p.has_draw_slot=True;p.last_drawn_tile=hand[-1]


def ticks(state): return state.game_record['game_round'][f'round_index_{state.round_index}']['action_ticks']


@pytest.mark.parametrize('seed',range(12))
def test_dealing_is_deterministic_four_seats_and_148_tiles(seed):
    state=make_state();state.master_seed=seed;state.init_tiles()
    assert Counter(state.tiles_list+[t for p in state.player_list for t in p.hand_tiles])==Counter(r.WALL)
    assert [len(p.hand_tiles) for p in state.player_list]==[14,13,13,13]
    assert len(state.tiles_list)==95 and state.playable_wall_count()==80
    assert [p.has_draw_slot for p in state.player_list]==[True,False,False,False]
    first=[p.hand_tiles[:] for p in state.player_list];state.init_tiles()
    assert first==[p.hand_tiles for p in state.player_list]


@pytest.mark.parametrize('count',[0,14,15,16,17])
def test_dead_wall_boundary_and_no_illegal_kong(count):
    state=make_state();state.tiles_list=[21]*count
    drawn(state.player_list[0],[11]*4+list(PLAIN[4:]))
    assert state.can_take_normal_tile()==(count>15)
    assert (11 in state._kong_candidates(0))==(count>15)
    assert state.can_take_supplement_tile()==(count>15)


def test_opening_chain_bonus_identity_and_first_hand_marriage():
    async def run():
        state=make_state();p=state.player_list[0]
        state.opening_dealer_action=True
        drawn(p,PLAIN[:-4]+(51,55,61,62));state.tiles_list=list(range(11,20))*4+[63,52]
        await state._opening_flower_replacement()
        assert set(p.huapai_list)=={51,52,55,61,62,63}
        assert len(p.hand_tiles)==14 and not set(p.hand_tiles).intersection(r.BONUS_TILES)
        assert [x['amount'] for x in state.sg_payments]==[2,2]
        assert [x['reason'] for x in state.sg_payments]==['花配1','猫鼠']
        assert [p.score for p in state.player_list]==[412,396,396,396]
        assert ticks(state)[-1]==['reset',0]
        assert sum(t[0]=='bh' for t in ticks(state))==6
        assert sum(t[:2]==['singapore','payment'] for t in ticks(state))==2
        assert p.has_draw_slot and all(not p.has_draw_slot for p in state.player_list[1:])
    asyncio.run(run())


def test_bonus_replacement_is_mandatory_and_original_pair_only_doubles():
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN[:-1]+(61,))
        state.tiles_list=list(PLAIN)*2+[62]
        assert state.check_hand_actions(0)[0]==['buhua']
        await state.execute_cut(0,{'TileId':11,'CutClass':False,'TileIndex':0})
        assert p.hand_tiles[0]==11 and not p.discard_tiles
        await state._opening_flower_replacement()
        assert state.sg_payments[0]['amount']==1
        assert sum(p.score for p in state.player_list)==1600
    asyncio.run(run())


@pytest.mark.parametrize('steal', [False,True])
@pytest.mark.parametrize('opening', [False,True])
def test_eight_flowers_auto_win_and_transfer_without_complete_hand(steal,opening):
    async def run():
        state=make_state();winner=state.player_list[1];winner.huapai_list=list(range(51,58))
        owner=state.player_list[0 if steal else 1];drawn(owner,[11,12,58])
        state.current_player_index=owner.player_index
        assert not await state._replace_one_flower(owner.player_index,2,is_drawn=True,opening=opening)
        assert state.game_status=='END' and state.pending_winners[0]['index']==1
        assert state.pending_winners[0]['payer']==(0 if steal else None)
        assert set(winner.huapai_list)==set(r.FLOWERS)
        assert all(58 not in p.hand_tiles for p in state.player_list)
        assert len(state.tiles_list)==21
        bh=next(t for t in ticks(state) if t[0]=='bh')
        assert bh[1:3]==[58,owner.player_index]
        if steal: assert bh[4:]==[1,0,58]
    asyncio.run(run())


def test_seventh_flower_can_steal_already_exposed_eighth():
    async def run():
        state=make_state();p=state.player_list[0];p.huapai_list=list(range(51,57));drawn(p,[11,57])
        state.player_list[2].huapai_list=[58]
        await state.execute_buhua(0)
        assert state.pending_winners[0]['index']==0 and state.pending_winners[0]['payer']==2
        assert not state.player_list[2].huapai_list and len(p.huapai_list)==8
    asyncio.run(run())


@pytest.mark.parametrize('tile', [25,29,61])
def test_last_draw_can_win_but_never_discard(tile):
    async def run():
        state=make_state();p=state.player_list[1];p.hand_tiles=list(PLAIN[:-1])
        state.tiles_list=[tile]+[29]*15
        await state._deal_normal()
        assert len(state.tiles_list)==15
        if tile==25:
            assert 'hu_self' in state.action_dict[1]
            await state.execute_timeout_cut(1)
            assert state.draw_reason=='last_tile_passed'
        else: assert state.game_status=='END'
        assert not p.discard_tiles
        if tile==61: assert p.huapai_list==[61] and 61 not in p.hand_tiles
    asyncio.run(run())


def test_replacement_last_flower_is_published_without_drawing_dead_wall():
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,PLAIN[:-1]+(61,))
        state.tiles_list=[29]*15+[62]
        await state.execute_buhua(0)
        assert p.huapai_list==[61,62] and len(state.tiles_list)==15 and state.game_status=='END'
    asyncio.run(run())


def test_natural_pung_chow_kong_and_last_wall_claim_gates():
    state=make_state()
    for p in state.player_list: p.hand_tiles=[12,13,11,11,11]
    offered=state.check_discard_actions(11)
    assert {'peng','gang','chi_right','pass'}<=set(offered[1])
    assert not any(a.startswith('chi_') for i in (2,3) for a in offered[i])
    state.tiles_list=[29]*15
    assert not any(offered for offered in state.check_discard_actions(11).values())


def test_thirteen_orphans_overrides_nearest_winner():
    state=make_state();state.current_player_index=0
    state.result_dict={'hu_first':r.detail([('普通',5)]),'hu_third':r.detail([('十三幺',5)],orphans=True)}
    assert state._selected_winners([(1,'hu_first'),(3,'hu_third')])==[(3,'hu_third')]


@pytest.mark.parametrize('action,tile,melds,hand', [('peng',47,['k45','k46'],[47,47,11,12,13,26,28]),
    ('gang',44,['k41','k42','k43'],[44,44,44,11])])
def test_automatic_meld_win_after_claim_and_pao(action,tile,melds,hand):
    async def run():
        state=make_state();p=state.player_list[1];p.combination_tiles=melds;p.combination_mask=[[0,int(c[1:])]*3 for c in melds]
        p.hand_tiles=hand;state.player_list[0].discard_tiles=[tile]
        await state.execute_claim(1,action)
        assert state.game_status=='END' and state.pending_winners[0]['detail']['automatic']
        assert state.pending_winners[0]['liable_payer']==0 and state.pending_winners[0]['payer']==0
        assert not state.player_list[0].discard_tiles
    asyncio.run(run())


def test_kong_payment_and_robbed_added_kong_has_no_payment():
    async def run():
        state=make_state();p=state.player_list[0];drawn(p,[11]*4+list(PLAIN[4:]))
        await state.execute_angang(0,11)
        assert state.game_status=='deal_card_after_gang' and state.sg_payments[-1]['amount']==2
        assert state.sg_kong_chain==1 and p.combination_tiles==['G11']
        state=make_state();p=state.player_list[0];p.combination_tiles=['k25'];p.combination_mask=[[1,25,0,25,0,25]]
        drawn(p,[11,12,13,14,15,16,21,22,23,28,25])
        state.player_list[1].hand_tiles=list(PLAIN[:-1]);state.player_list[1].huapai_list=[61]
        await state.execute_jiagang(0,25)
        assert state.game_status=='waiting_action_qianggang' and not state.sg_payments
        allowed=state.action_dict.copy()
        await state.resolve_rob_kong_responses({1:{'action_type':'hu_first'}},allowed)
        assert state.pending_winners[0]['source']=='robbing_kong'
        assert p.combination_tiles==['k25'] and not state.sg_payments
        assert p.hand_tiles.count(25)==0
    asyncio.run(run())


def test_added_kong_only_pays_after_pass_and_cannot_pay_twice():
    async def run():
        state=make_state();p=state.player_list[0];p.combination_tiles=['k25'];p.combination_mask=[[1,25,0,25,0,25]]
        drawn(p,[11,12,13,14,15,16,21,22,23,28,25])
        await state.execute_jiagang(0,25)
        assert len(state.sg_payments)==1 and state.sg_payments[0]['amount']==1
        await state.finalize_jiagang()
        assert len(state.sg_payments)==1
    asyncio.run(run())


@pytest.mark.parametrize('remaining,seen,expected',[(8,False,False),(7,False,True),(5,False,True),(7,True,False)])
def test_last_seven_fresh_kong_liability(remaining,seen,expected):
    async def run():
        state=make_state();state.sg_last_draw_count=remaining;state.sg_fresh_discard=not seen
        state.player_list[0].discard_tiles=[11];state.player_list[1].hand_tiles=[11,11,11,22]
        await state.execute_claim(1,'gang')
        changes=state.sg_payments[0]['changes']
        assert changes[0]==(-3 if expected else -1)
    asyncio.run(run())


@pytest.mark.parametrize('count,expected',[(6,None),(5,0),(1,0)])
def test_last_five_ron_and_existing_bao_scope(count,expected):
    state=make_state();state.sg_last_draw_count=count;state.sg_fresh_discard=True
    assert state._liability_payer_for_win(1,'discard',0,25,{})==expected
    state.sg_fresh_discard=False;state.sg_liability[1]=2
    assert state._liability_payer_for_win(1,'self_draw',None,25,{})==2
    assert state._liability_payer_for_win(1,'discard',2,25,{})==2
    assert state._liability_payer_for_win(1,'discard',3,25,{}) is None


def test_dealer_rotation_rules():
    state=make_state();assert state.next_dealer()==0
    state.player_list[2].combination_tiles=['g11'];assert state.next_dealer()==1
    state.pending_winners=[{'index':0}];assert state.next_dealer()==0
    state.pending_winners=[{'index':3}];assert state.next_dealer()==1


def test_settlement_and_persistence_are_once_only_with_side_payments():
    async def run():
        state=make_state();state.current_round=4
        state._pay_instant(0,2,'暗杠')
        state.pending_winners=[state._build_pending_winner(1,'discard','hu_first',r.detail([('动物',1)]),25,0)]
        before={i:400 for i in range(4)}
        assert await state._settle_hand(before)
        scores=[p.score for p in state.player_list]
        assert scores==[404,402,397,397]
        assert await state._settle_hand(before) and scores==[p.score for p in state.player_list]
        assert sum(p.record_counter.dianhe_times for p in state.player_list)==1
        assert sum(p.record_counter.round_score_total for p in state.player_list)==0
        assert state.player_list[0].score_history==['+4']
        assert state._terminal_result['show_result_info']['score_changes']=={0:4,1:2,2:-3,3:-3}
        await state._persist_match();await state._persist_match()
        state.db_manager.store_singapore_game_record.assert_called_once()
        assert all(p.record_counter.recorded_fans==[] for p in state.player_list)
    asyncio.run(run())


def test_private_packets_reconnect_and_hidden_kong():
    async def run():
        state=make_state();p=state.player_list[0];p.hand_tiles=list(PLAIN[:-1])
        for q in state.player_list: state.game_server.user_id_to_connection[q.user_id]=SimpleNamespace(websocket=AsyncMock())
        await state.prepare_private_fields(range(4))
        preview=state.build_private_game_info_fields(0)['singapore_hints']
        assert 'waiting_details' not in preview
        assert preview['local_request']['rule']=='singapore'
        assert preview['local_request']['hand']==list(PLAIN[:-1])
        p.combination_mask=[[2,45]*4];p.combination_tiles=['G45']
        await broadcast_do_action(state,['angang'],0,combination_mask=[2,45]*4,combination_target='G45')
        packet=state.game_server.user_id_to_connection[102].websocket.send_json.await_args.args[0]
        assert packet['do_action_info']['combination_mask']==[2,0]*4
        assert packet['do_action_info']['singapore_hints']['source_hand_tiles']==[]
        await send_reconnect_game_state(state,state.player_list[1])
        packet=state.game_server.user_id_to_connection[102].websocket.send_json.await_args.args[0]
        assert packet['game_info']['singapore_info']['scores']=={0:400,1:400,2:400,3:400}
        assert packet['game_info']['players_info'][0].get('hand_tiles') is None
    asyncio.run(run())


def test_exhausted_capital_stops_before_supplement_or_automatic_win():
    async def run():
        state=make_state();p=state.player_list[0]
        p.combination_tiles=['k45','k46'];p.huapai_list=[61]
        drawn(p,[47]*4+[11,12,13,25]);state.player_list[1].score=2
        wall=list(state.tiles_list)
        await state.execute_angang(0,47)
        assert state.game_status=='END' and state.draw_reason=='capital_exhausted'
        assert not state.pending_winners and state.tiles_list==wall
        assert await state._settle_hand({i:(2 if i==1 else 400) for i in range(4)})
    asyncio.run(run())


@pytest.mark.parametrize('room',[{'player_list':[101,102,103]},{'player_list':[101,102,103,104],'sub_rule':'singapore/unknown'}])
def test_wrong_player_count_or_unversioned_variant_rejected(room):
    with pytest.raises(ValueError):SingaporeGameState(None,room,None,None,'bad-room')


def test_eight_flowers_auto_win_interrupts_opening_replacement():
    async def run():
        state=make_state();state.opening_dealer_action=True;state.tiles_list=[21]*40
        drawn(state.player_list[0],list(r.FLOWERS)+[11,12,13,14,15,16])
        await state._opening_flower_replacement()
        assert state.game_status=='END' and len(state.pending_winners)==1
        assert state.pending_winners[0]['detail']['fan_names']==['八花和（5台）']
        assert len(state.player_list[0].huapai_list)==8
    asyncio.run(run())


def test_local_hint_inputs_refresh_after_hand_changes_without_server_worker(monkeypatch):
    async def run():
        state=make_state();state.player_list[0].hand_tiles=list(PLAIN[:-1])
        worker=AsyncMock(side_effect=AssertionError('UI previews must stay on the client'))
        monkeypatch.setattr('server.gamestate.public.ai.bot_executor.run_room_bot_cpu',worker)
        await state.prepare_private_fields([0])
        before=state.build_private_game_info_fields(0)['singapore_hints']
        state.player_list[0].hand_tiles[0]=19
        await state.prepare_private_fields([0])
        after=state.build_private_game_info_fields(0)['singapore_hints']
        assert before['local_request']['hand']==list(PLAIN[:-1])
        assert after['local_request']['hand']==state.player_list[0].hand_tiles
        assert 'waiting_details' not in after
        worker.assert_not_awaited()
    asyncio.run(run())
