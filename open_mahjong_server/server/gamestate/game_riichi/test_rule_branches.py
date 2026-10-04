"""Outcome-based coverage of preset-dependent actions, endings and payments."""
import asyncio
from itertools import combinations

import pytest

from .test_double_riichi import make_game
from .action_check import check_action_hand_action, check_hepai, refresh_waiting_tiles
from .rule_logic import draw_payments, finalize_scores, apply_pao, record_pao_call
from .wait_action import _is_valid_cut_action
from ...game_calculation.riichi.rule_config import preset_room_config


def game(preset='tenhou', scores=None):
    g,_=make_game(0)
    config=preset_room_config(preset)
    for key in ('starting_score','open_tobi','open_xiru','hepai_way','detailed_config'):
        setattr(g,key,config[key])
    g.max_round=2
    g.hu_class='hu_first'
    for i,p in enumerate(g.player_list):
        p.score=(scores or [25000]*4)[i]
        p.discard_tiles=[];p.discard_origin_tiles=[];p.combination_tiles=[]
    return g


@pytest.mark.parametrize('preset,enabled', [('majsoul',True),('tenhou',True),('jpml_a',False),('mleague',False)])
def test_abortive_draw_switches(preset,enabled):
    g=game(preset)
    for p in g.player_list:
        p.discard_tiles=[41];p.discard_origin_tiles=[41];p.tag_list=['riichi']
    g.xunmu=2  # The old xunmu>1 check suppressed every four-wind abort.
    assert asyncio.run(g._check_four_wind_abort()) is enabled
    assert asyncio.run(g._check_four_player_riichi_abort()) is enabled
    p=g.player_list[0];p.discard_tiles=[];p.discard_origin_tiles=[]
    p.hand_tiles=[11,19,21,29,31,39,41,42,43,44,45,46,47,47]
    assert g._can_declare_kyuushu(0) is enabled


@pytest.mark.parametrize('seat', [1,2,3])
def test_child_first_draw_can_score_chiihou(seat):
    g=game();g.current_player_index=seat
    p=g.player_list[seat];p.hand_tiles=[12,13,14,22,23,24,33,34,35,36,37,38,28,28]
    p.waiting_tiles={28}
    actions={i:[] for i in range(4)}
    check_hepai(g,actions,28,seat,'tsumo')
    assert '地和' in g.result_dict['hu_self']['yaku']
    g.player_list[0].combination_tiles=['G41']
    check_hepai(g,actions,28,seat,'tsumo')
    assert '地和' not in g.result_dict['hu_self']['yaku']


@pytest.mark.parametrize('preset,live,allowed', [('tenhou',3,False),('tenhou',4,True),('mleague',1,True),('mleague',0,False)])
def test_last_turn_riichi_rule(preset,live,allowed):
    g=game(preset);g.tiles_list=[11]*(g.dead_wall_count+live)
    assert ('riichi_cut' in check_action_hand_action(g,0)[0]) is allowed


def test_post_riichi_cannot_choose_a_different_discard():
    g=game();p=g.player_list[0];p.tag_list=['riichi'];p.pending_riichi=False
    assert not _is_valid_cut_action(g,0,dict(action_type='cut',TileId=11))
    assert _is_valid_cut_action(g,0,dict(action_type='cut',TileId=p.hand_tiles[-1]))
    p.tag_list=[];p.riichi_candidate_cuts={47:[16]}
    assert not _is_valid_cut_action(g,0,dict(action_type='riichi_cut',TileId=11))
    assert _is_valid_cut_action(g,0,dict(action_type='riichi_cut',TileId=47))


def test_open_hand_wait_refresh_ignores_draw_slot():
    g=game();p=g.player_list[0]
    p.hand_tiles=[22,23,24,33,34,35,36,37,28,28,47];p.combination_tiles=['s13']
    refresh_waiting_tiles(g,0)
    assert p.waiting_tiles=={32,35,38}


@pytest.mark.parametrize('preset', ['majsoul','tenhou','jpml_a','mleague'])
def test_south_three_never_skips_last_hand(preset):
    g=game(preset,[40000,20000,20000,20000]);g.current_round=7
    assert not g._riichi_match_should_end(False,8)


@pytest.mark.parametrize('preset,stops', [('majsoul',True),('tenhou',True),('jpml_a',False),('mleague',False)])
def test_last_dealer_stop_and_abort_are_different(preset,stops):
    g=game(preset,[40000,20000,20000,20000]);g.current_round=8
    assert g._riichi_match_should_end(True,8) is stops
    g.hu_class='ryuukyoku'
    assert g._riichi_match_should_end(True,8) is stops
    g.hu_class='four_riichi_abort'
    assert not g._riichi_match_should_end(True,8)


def test_extended_round_cap_and_dealer_priority():
    g=game(scores=[28000,27000,23000,22000]);g.current_round=8
    assert not g._riichi_match_should_end(False,9)
    g.current_round=12
    assert g._riichi_match_should_end(False,13)
    g.player_list[1].score=32000
    assert not g._riichi_match_should_end(True,10)
    assert g._riichi_match_should_end(False,10)


def test_original_seats_break_ties_for_dealer_stop():
    g=game(scores=[35000,35000,15000,15000]);g.current_round=8
    g.player_list[0].original_player_index=3;g.player_list[1].original_player_index=0
    assert not g._riichi_match_should_end(True,8)


@pytest.mark.parametrize('preset', ['majsoul','tenhou','jpml_a','mleague'])
@pytest.mark.parametrize('score', [0,-100])
def test_tobi_zero_boundary(preset,score):
    g=game(preset,[score,40000,30000,30000]);g.current_round=1
    assert g._riichi_match_should_end(False,2) is (score<0 and g.open_tobi)


@pytest.mark.parametrize('tenpai',[list(c) for n in range(5) for c in combinations(range(4),n)])
def test_all_noten_payment_combinations(tenpai):
    g=game();changes,winners=draw_payments(g,tenpai)
    assert not winners and sum(changes.values())==0
    assert sum(max(0,x) for x in changes.values())==(3000 if 0<len(tenpai)<4 else 0)
    for seat in tenpai: assert changes[seat]>=0
    g.detailed_config['noten_penalty']=0
    assert all(v==0 for v in draw_payments(g,tenpai)[0].values())


def test_nagashi_replaces_noten_and_detects_claimed_discards():
    g=game();p=g.player_list[1];p.discard_origin_tiles=[11,41,47];p.discard_tiles=list(p.discard_origin_tiles)
    changes,winners=draw_payments(g,[0])
    assert changes=={0:-4000,1:8000,2:-2000,3:-2000} and winners==[1]
    p.discard_tiles.pop()
    assert draw_payments(g,[0])[1]==[]
    p.discard_tiles=list(p.discard_origin_tiles);p.combination_tiles=['k45']
    assert draw_payments(g,[])[1]==[1]
    g.detailed_config['nagashi_allow_calls']=False
    assert draw_payments(g,[])[1]==[]


@pytest.mark.parametrize('winner',[0,1,2,3])
@pytest.mark.parametrize('tsumo',[False,True])
def test_responsibility_payments_conserve_points(winner,tsumo):
    g=game('mleague');liable=(winner+1)%4;discarder=(winner+2)%4
    g.current_player_index=discarder;g.hu_class='hu_self' if tsumo else 'hu_first';g.honba=1
    g.player_list[winner].pao_liability={'大三元':liable}
    result=dict(yakuman_multiplier=2,yakuman_components={'大三元':1,'字一色':1})
    changes={i:0 for i in range(4)}
    if tsumo:
        for i in range(4):
            if i!=winner:
                pay=(32000 if winner==0 or i==0 else 16000)+100
                changes[i]-=pay;changes[winner]+=pay
    else:
        changes[winner]=(96000 if winner==0 else 64000)+300;changes[discarder]=-changes[winner]
    original_win=changes[winner]
    apply_pao(g,changes,winner,result,True)
    assert sum(changes.values())==0 and changes[winner]==original_win
    if tsumo: assert changes[liable]==-(48000 if winner==0 else 32000)-(16000 if winner==0 or liable==0 else 8000)-300
    else: assert changes[liable]==-(24000 if winner==0 else 16000)-300


def test_pao_trigger_includes_closed_kans_but_not_self_completed_meld():
    g=game('mleague');p=g.player_list[1];p.combination_tiles=['G45','k46','k47']
    record_pao_call(g,1,3,'peng',47)
    assert p.pao_liability=={'大三元':3}
    p.pao_liability={};record_pao_call(g,1,3,'angang',47)
    assert not p.pao_liability


@pytest.mark.parametrize('preset,expected', [('majsoul',[30.8,5.8,-9.2,-27.4]),('tenhou',[50.8,5.8,-19.2,-37.4]),('mleague',[60.8,5.8,-19.2,-47.4])])
def test_preset_final_points(preset,expected):
    g=game(preset,[40800,25800,20800,12600]);g.riichi_sticks=0
    finalize_scores(g)
    assert [p.riichi_points for p in g.player_list]==expected
    assert sum(p.riichi_points for p in g.player_list)==pytest.approx(0)


def test_final_tied_deposits_and_uma_are_split_once():
    g=game('mleague',[30000,30000,30000,9000]);g.riichi_sticks=1
    finalize_scores(g)
    assert [p.score for p in g.player_list]==[30400,30300,30300,9000]
    scores=[p.score for p in g.player_list]
    assert sum(p.riichi_points for p in g.player_list)==pytest.approx(0)
    finalize_scores(g);assert scores==[p.score for p in g.player_list]


def test_jpml_floating_rank_awards_and_leftover_deposits():
    g=game('jpml_a',[45000,28000,26000,20000]);g.riichi_sticks=1
    finalize_scores(g)
    assert [p.riichi_points for p in g.player_list]==[27,-3,-7,-18]
    assert g.riichi_sticks==1
