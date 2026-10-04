"""Exercise configurable branches independently of the four bundled presets."""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_rule_branches import game
from .rule_logic import apply_pao, draw_payments, finalize_scores, record_pao_call
from ...game_calculation.riichi.rule_config import catalog, normalize_riichi_config


@pytest.mark.parametrize('key,value',[(o['key'],v) for o in catalog()['options'] for v in o['values']])
def test_every_catalog_value_survives_validation_without_type_coercion(key,value):
    actual=normalize_riichi_config({key:value})[key]
    assert actual==value and type(actual) is type(value)


@pytest.mark.parametrize('rounds',[1,2,3,4])
@pytest.mark.parametrize('extend',[False,True])
def test_round_count_and_extension_are_obeyed(rounds,extend):
    g=game(scores=[26000,25000,25000,24000]);g.max_round=rounds;g.open_xiru=extend
    g.current_round=rounds*4
    assert not g._riichi_match_should_end(False,rounds*4)
    assert g._riichi_match_should_end(False,rounds*4+1) is (not extend or rounds==4)
    g.detailed_config['extension_rounds']=0
    assert g._riichi_match_should_end(False,rounds*4+9) is (not extend or rounds==4)
    g.detailed_config['target_score']=25000
    assert g._riichi_match_should_end(False,rounds*4+1)


@pytest.mark.parametrize('tenpai',[False,True])
@pytest.mark.parametrize('renchan',[False,True])
def test_dealer_draw_continuance_uses_actual_tenpai_and_config(tenpai,renchan):
    g=game();g.hu_class='ryuukyoku';p=g.player_list[0]
    p.waiting_tiles={16} if tenpai else set();p.ryuukyoku_declared_tenpai=True
    g.detailed_config['tenpai_renchan']=renchan
    assert g._compute_last_renchan() is (tenpai and renchan)


@pytest.mark.parametrize('mode',['winner','shared','discard'])
def test_two_remaining_sticks_and_tied_top_have_exact_100_point_rounding(mode):
    g=game('mleague',[30000,30000,30000,8000]);g.riichi_sticks=2
    g.detailed_config['end_deposits']=mode
    finalize_scores(g)
    expected={'winner':[32000,30000,30000,8000],'shared':[30800,30600,30600,8000],'discard':[30000,30000,30000,8000]}
    assert [p.score for p in g.player_list]==expected[mode]
    assert sum(p.score for p in g.player_list)+g.riichi_sticks*1000==100000
    assert g.game_record['game_title']['riichi_final_scores']==expected[mode]
    assert g.game_record['game_title']['riichi_final_sticks']==g.riichi_sticks


def test_unequal_start_scores_do_not_create_an_extra_oka():
    g=game('mleague',[40000,30000,30000,20000]);g.starting_scores=[40000,30000,30000,20000]
    finalize_scores(g)
    assert sum(p.riichi_points for p in g.player_list)==0


@pytest.mark.parametrize('scope',['liable','all'])
@pytest.mark.parametrize('honba',['liable','discarder'])
def test_pao_scope_and_ron_honba_routing(scope,honba):
    g=game();g.detailed_config.update(pao_scope=scope,pao_honba=honba)
    g.hu_class='hu_first';g.current_player_index=0;g.honba=2
    g.player_list[1].pao_liability={'大三元':3}
    changes={0:-64600,1:64600,2:0,3:0}
    result={'yakuman_multiplier':2,'yakuman_components':{'大三元':1,'字一色':1}}
    apply_pao(g,changes,1,result,True)
    liability=(16000 if scope=='liable' else 32000)+(600 if honba=='liable' else 0)
    assert changes=={0:-64600+liability,1:64600,2:0,3:-liability}
    g.detailed_config['pao']=False
    before=changes.copy();apply_pao(g,changes,1,result,True);assert changes==before


@pytest.mark.parametrize('enabled',[False,True])
def test_four_kan_liability_can_be_switched_off(enabled):
    g=game();g.detailed_config['pao_suukantsu']=enabled
    p=g.player_list[1];p.combination_tiles=['G11','g22','G33','g44']
    record_pao_call(g,1,2,'gang',44)
    assert ('四杠子' in p.pao_liability) is enabled


@pytest.mark.parametrize('enabled',[False,True])
def test_nagashi_switch_reverts_to_noten_payment(enabled):
    g=game();g.detailed_config['nagashi_mangan']=enabled
    p=g.player_list[1];p.discard_origin_tiles=[11,41];p.discard_tiles=[11,41]
    changes,winners=draw_payments(g,[0])
    assert winners==([1] if enabled else [])
    assert changes[0]==(-4000 if enabled else 3000)


@pytest.mark.parametrize('enabled',[False,True])
def test_four_kan_indicators_follow_dead_wall_positions_and_stop_at_four(enabled):
    async def run():
        g=game();g.detailed_config['kan_dora']=enabled
        g.tiles_list=list(range(40));g.kan_dora_indicators=[];g.ura_kan_dora_indicators=[];g.rinshan_count=0
        with patch('server.gamestate.game_riichi.RiichiGameState.broadcast_update_dora',new=AsyncMock()):
            for i in range(4):
                g.tiles_list.pop();g.rinshan_count+=1;g.dead_wall_count-=1
                await g._reveal_kan_dora()
            await g._reveal_kan_dora()
        assert g.kan_dora_indicators==([32,30,28,26] if enabled else [])
        assert g.ura_kan_dora_indicators==([33,31,29,27] if enabled else [])
    asyncio.run(run())
