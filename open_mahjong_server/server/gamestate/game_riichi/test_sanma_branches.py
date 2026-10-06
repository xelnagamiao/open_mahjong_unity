"""Special action, settlement and wall branches on a three-player table."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_sanma import sanma
from .action_check import check_hepai, check_action_hand_action
from .nuki_actions import begin_nuki, check_nuki_ron, can_nuki
from .rule_logic import apply_pao, draw_payments
from .wait_action import wait_action
from .ron_resolution import resolve_collected_rons
from .init_tiles import init_riichi_tiles


@pytest.mark.parametrize('mode', ['head_bump','multi_ron','three_ron_abort'])
def test_two_ron_claims_use_three_player_order_and_never_triple_abort(mode):
    g=sanma();g.hepai_way=mode;g.current_player_index=2
    g._pending_ron_claims={0:'hu_first',1:'hu_second'}
    if mode=='head_bump':g._pending_ron_claims={0:'hu_first'}
    g.result_dict={a:dict(han=2,no_yaku=False) for a in g._pending_ron_claims.values()}
    assert asyncio.run(resolve_collected_rons(g,33,[0,1]))
    assert g.hu_class!='three_ron_abort' and g.game_status=='END'
    assert g.ron_player_index==0
    assert (len(g.multi_ron_queue or [])==2) is (mode!='head_bump')


@pytest.mark.parametrize('source', range(3))
def test_double_ron_settlement_honba_deposits_once_and_dealer_continuance(source):
    g=sanma();g.current_player_index=source;g.honba=2;g.riichi_sticks=1;g.player_list[source].score-=1000
    queue=[((source+1)%3,'hu_first'),((source+2)%3,'hu_second')]
    g.result_dict={a:dict(han=5,fu=30,yaku=['满贯'],score=12000 if seat==0 else 8000,
        cost=dict(main=12000 if seat==0 else 8000)) for seat,a in queue}
    with patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase',new=AsyncMock()), \
         patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep',new=AsyncMock()):
        asyncio.run(g._settle_multi_ron_sequence(queue))
    ticks=g.game_record['game_round']['round_index_1']['action_ticks']
    assert [t[11] for t in ticks if t[0]=='hu_riichi']==[1,0]
    assert sum(p.score for p in g.player_list)==105000 and g.riichi_sticks==0
    assert g._compute_last_renchan() is (source!=0)


@pytest.mark.parametrize('winner',range(3))
@pytest.mark.parametrize('mode',['loss','split'])
@pytest.mark.parametrize('tsumo',[False,True])
@pytest.mark.parametrize('scope',['liable','all'])
def test_pao_conserves_points_with_three_payers_and_multiple_yakuman(winner,mode,tsumo,scope):
    from ...game_calculation.riichi.sanma import tsumo_payments
    g=sanma();g.detailed_config.update(sanma_tsumo=mode,pao_scope=scope);g.honba=2
    g.hu_class='hu_self' if tsumo else 'hu_first';g.current_player_index=(winner+1)%3
    liable=(winner+2)%3;g.player_list[winner].pao_liability={'大三元':liable}
    cost=dict(main=32000,additional=32000 if winner==0 else 16000)
    changes={i:0 for i in range(3)}
    if tsumo:
        for payer,base in tsumo_payments(cost,winner,3,mode).items():
            changes[payer]-=base+200;changes[winner]+=base+200
    else:
        changes[g.current_player_index]=-(96000 if winner==0 else 64000)-600
        changes[winner]=-changes[g.current_player_index]
    apply_pao(g,changes,winner,dict(yakuman_multiplier=2,yakuman_components={'大三元':1,'字一色':1}),True)
    assert sum(changes.values())==0 and changes[liable]<0
    if scope=='all' and tsumo:
        assert changes[g.current_player_index]==0
        assert changes[liable]==-(96000 if winner==0 else 64000)-400


@pytest.mark.parametrize('offender',range(3))
@pytest.mark.parametrize('penalty',['fixed_9000','reverse_mangan','penalty_20000'])
def test_chombo_refund_and_penalty_on_three_player_table(offender,penalty):
    g=sanma();g.hu_class='hu_first';g.ron_player_index=offender;g.open_cuohe=True
    g.detailed_config['chombo_penalty']=penalty;g.result_dict={'hu_first':dict(han=0,fu=0,no_yaku=True,yaku=[])}
    payer=(offender+1)%3;g.player_list[payer].score-=1000;g.player_list[payer].riichi_paid_this_round=True;g.riichi_sticks=2
    asyncio.run(g._settle_cuohe())
    tick=g.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert len(tick[6])==3 and sum(tick[6])==1000 and g.riichi_sticks==1
    assert sum(p.score for p in g.player_list)==105000
    assert g.player_list[offender].chombo_result_penalty==(20 if penalty=='penalty_20000' else 0)


@pytest.mark.parametrize('rinshan',[False,True])
def test_nuki_replacement_is_never_haitei_even_when_rinshan_yaku_disabled(rinshan):
    g=sanma();g.tiles_list=[21]*14;p=g.player_list[1];g.current_player_index=1
    p.hand_tiles=[21,22,23,24,25,26,31,32,33,36,37,38,29,29];p.has_draw_slot=True
    p.tag_list=['riichi'];p.huapai_list=[44];actions={0:[],1:[],2:[]}
    check_hepai(g,actions,29,1,'tsumo',is_get_gang_tile=rinshan,is_nuki_tile=True)
    assert '海底捞月' not in g.result_dict['hu_self']['yaku']
    assert ('岭上开花' in g.result_dict['hu_self']['yaku']) is rinshan


@pytest.mark.parametrize('riichi',[False,True])
@pytest.mark.parametrize('timeout',[False,True])
def test_pass_or_timeout_commits_nuki_and_creates_furiten(riichi,timeout):
    async def run():
        g=sanma();g.player_list[0].hand_tiles.append(44);g.player_list[0].has_draw_slot=True
        p=g.player_list[1];p.hand_tiles=[21,21,22,22,24,24,26,26,31,31,35,35,44]
        p.tag_list=['riichi'] if riichi else []
        await begin_nuki(g)
        if timeout:
            g.step_time=0
            for other in g.player_list:other.remaining_time=0
            await wait_action(g)
        else:
            from ..public.ai.get_action import get_ai_action
            task=asyncio.create_task(wait_action(g));await asyncio.sleep(0)
            await get_ai_action(g,1,'pass',False,None,None,None);await task
        assert p.riichi_furiten if riichi else p.temp_furiten
        assert g.player_list[0].huapai_list==[44] and g.game_status=='deal_card_after_nuki'
        assert g._pending_kan is None and g.total_kans==0
    asyncio.run(run())


@pytest.mark.parametrize('shape',['open','wrong_size','non_yaochuu'])
def test_kokushi_only_robbing_rejects_other_shapes(shape):
    g=sanma();g.detailed_config['nuki_ron']='kokushi';p=g.player_list[1]
    p.hand_tiles=[11,19,21,29,31,39,41,42,43,45,46,47,47]
    if shape=='open':p.combination_tiles=['k45']
    elif shape=='wrong_size':p.hand_tiles.pop()
    else:p.hand_tiles[0]=22
    assert check_nuki_ron(g)[1]==[]


@pytest.mark.parametrize('enabled',[False,True])
def test_eight_replacements_do_not_consume_indicator_slots_or_create_ninth_kan(enabled):
    async def run():
        g=sanma();g.detailed_config['kan_dora']=enabled
        for p in g.player_list:p.hand_tiles=[]
        init_riichi_tiles(g);indicators=g._dora_slots[:];uras=g._ura_slots[:]
        p=g.player_list[0]
        for i in range(8):
            p.get_gang_tile(g.tiles_list,g)
            if i in (0,2,4,6):await g._reveal_kan_dora()
        assert g.rinshan_count==8 and g.dead_wall_count==14
        assert g.kan_dora_indicators==(indicators[1:] if enabled else [])
        assert g.ura_kan_dora_indicators==(uras[1:] if enabled else [])
        p.hand_tiles.append(44);p.has_draw_slot=True
        assert not can_nuki(g,0)
        assert not any(a in check_action_hand_action(g,0)[0] for a in ('nuki','angang','jiagang'))
    asyncio.run(run())


@pytest.mark.parametrize('normal_draws', [0, 20, 46])
def test_replacement_and_normal_draws_never_consume_hidden_dora_slots(normal_draws):
    g = sanma()
    # Distinct physical identities expose indicator collisions even for equal faces.
    g.tiles_list = list(range(1000, 1069))
    g.dead_wall_count = 14
    g.rinshan_count = 0
    indicators = set(g.tiles_list[-14:-4])
    player = g.player_list[0]
    for _ in range(normal_draws):
        player.get_tile(g.tiles_list)
    for _ in range(8):
        assert len(g.tiles_list) > 14
        player.get_gang_tile(g.tiles_list, g)
    while len(g.tiles_list) > 14:
        player.get_tile(g.tiles_list)
    assert indicators <= set(g.tiles_list)
    assert not indicators.intersection(player.hand_tiles)
    assert len(g.tiles_list) == 14


@pytest.mark.parametrize('winners',[[0],[1],[2],[0,1],[1,2],[0,1,2]])
@pytest.mark.parametrize('mode',['loss','split'])
def test_nagashi_mangan_for_all_three_player_winner_sets(winners,mode):
    g=sanma();g.detailed_config['sanma_tsumo']=mode
    for seat in winners:g.player_list[seat].discard_origin_tiles=g.player_list[seat].discard_tiles=[11,44]
    changes,actual=draw_payments(g,[])
    assert actual==winners and sum(changes.values())==0 and len(changes)==3
