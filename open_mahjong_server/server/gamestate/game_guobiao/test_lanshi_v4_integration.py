"""第4版四人蓝十：生产状态机入口、配置、隐私及记录接入。"""
import asyncio

import pytest

from .test_opening_wins import make_state
from .action_check import check_action_after_cut, check_action_hand_action, refresh_waiting_tiles
from .combination_mask_view import get_combination_fields_for_viewer, build_revealed_angang_masks
from .lanshi_scoring import calculate_lanshi_score_changes
from ...game_calculation.lanshi_v4 import RULE_VERSION
from ...game_calculation.test_lanshi_v4 import tiles
from ...room.test_guobiao_flowers import room_manager
from ..public.game_record_manager import build_game_title_data


def state():
    gs=make_state('guobiao/lanshi')
    for p in gs.player_list:
        p.hand_tiles=tiles('12345m12345p123s')
        p.discard_tiles=[18]  # 排除首巡偶然番
    return gs


@pytest.mark.parametrize('event',[False,True])
@pytest.mark.parametrize('limit',[1,5,8,64])
def test_room_configuration_matches_authoritative_fixed_rules(event,limit):
    manager=room_manager()
    if event:
        response=asyncio.run(manager.create_empty_event_room('event1','guobiao',dict(sub_rule='guobiao/lanshi',hepai_limit=limit,use_flowers=True,open_cuohe=False,cuohe_type=0)))
    else:
        response=asyncio.run(manager.create_GB_room('connection','蓝十四人测试',4,'',20,5,False,sub_rule='guobiao/lanshi',hepai_limit=limit,use_flowers=True,open_cuohe=False,cuohe_type=0))
    assert response.success, response.message
    config=response.room_info
    assert config['max_player']==4
    assert (config['hepai_limit'],config['use_flowers'],config['open_cuohe'],config['cuohe_type'])==(5,False,True,1)


def test_native_waits_do_not_advertise_knitted_straight():
    gs=state()
    gs.player_list[0].hand_tiles=tiles('147m258p369s1112z')
    refresh_waiting_tiles(gs,0)
    assert gs.player_list[0].waiting_tiles==set()
    gs.sub_rule='guobiao/standard'
    refresh_waiting_tiles(gs,0)
    assert 42 in gs.player_list[0].waiting_tiles


@pytest.mark.parametrize('wrong_win_enabled', [False, True])
def test_zero_point_complete_hand_can_only_be_claimed_as_wrong_win(wrong_win_enabled):
    gs=state();gs.current_player_index=0;gs.open_cuohe=wrong_win_enabled
    p=gs.player_list[1]
    p.hand_tiles=tiles('234456p678s5z');p.combination_tiles=['s12']
    gs.player_list[0].discard_tiles.append(45)
    actions=check_action_after_cut(gs,45)
    assert 45 in p.waiting_tiles
    assert ('hu_first' in actions[1]) is wrong_win_enabled
    if wrong_win_enabled:
        assert gs.result_dict['hu_first']==(0,[])


@pytest.mark.parametrize('claimant', range(4))
def test_stopped_player_cannot_claim_discard_even_when_all_claim_shapes_exist(claimant):
    """规则书4.1.3：停和同时禁止吃、碰、直杠、和，不只隐藏和牌按钮。"""
    gs=state();gs.current_player_index=(claimant-1)%4
    p=gs.player_list[claimant]
    p.hand_tiles=tiles('12333m123p123s55z')
    gs.player_list[gs.current_player_index].discard_tiles.append(13)
    allowed=check_action_after_cut(gs,13)[claimant]
    assert {'chi_left','peng','gang'} <= set(allowed)
    assert any(action.startswith('hu_') for action in allowed)
    p.tag_list.append('peida')
    assert check_action_after_cut(gs,13)[claimant]==[]


@pytest.mark.parametrize('player',range(4))
@pytest.mark.parametrize('kong',['angang','jiagang'])
def test_stopped_player_keeps_own_kongs_and_discard(player,kong):
    """规则书4.1.3：停和者仍可补杠和暗杠。"""
    gs=state();gs.current_player_index=player
    p=gs.player_list[player];p.tag_list.append('peida')
    if kong=='angang':
        p.hand_tiles=[45]*4+[11,12,13,21,22,23,31,32,33,44]
    else:
        p.hand_tiles=[11,12,13,21,22,23,31,32,33,44,45]
        p.combination_tiles=['k45']
    refresh_waiting_tiles(gs,player,True)
    assert set(check_action_hand_action(gs,player)[player])=={kong,'cut'}


@pytest.mark.parametrize('winner',range(4))
def test_user_hand_self_draw_is_exactly_five(winner):
    gs=state(); gs.current_player_index=winner
    p=gs.player_list[winner]
    p.hand_tiles=tiles('11345m334455p234s')
    refresh_waiting_tiles(gs,winner,is_first_action=True)
    actions=check_action_hand_action(gs,winner)
    assert 'hu_self' in actions[winner]
    assert gs.result_dict['hu_self']==(5,['一般高*1','门前清','喜相逢*1','自摸'])


@pytest.mark.parametrize('offset,action',[(1,'hu_first'),(2,'hu_second'),(3,'hu_third')])
def test_below_minimum_ron_and_occasional_last_tile(offset,action):
    gs=state(); gs.open_cuohe=False
    p=gs.player_list[offset]
    p.hand_tiles=tiles('11345m334455p23s')
    assert action not in check_action_after_cut(gs,34)[offset]
    gs.tiles_list=[]
    actions=check_action_after_cut(gs,34)
    assert action in actions[offset]
    assert gs.result_dict[action]==(5,['海底捞月'])
    assert all(not set(v)&{'chi_left','chi_mid','chi_right','peng','gang'} for v in actions.values())


@pytest.mark.parametrize('win',[11,12,15,19])
def test_dealer_starting_nine_gates_exception_reaches_calculator(win):
    gs=state(); gs.current_player_index=gs.dealer_index=0
    for p in gs.player_list: p.discard_tiles=[]
    hand=tiles('11123455678999m'); hand.remove(win); hand.append(win)
    gs.player_list[0].hand_tiles=hand
    refresh_waiting_tiles(gs,0,True)
    actions=check_action_hand_action(gs,0,is_first_action=True)
    assert 'hu_self' in actions[0]
    assert gs.result_dict['hu_self']==(100,['九莲宝灯'])


@pytest.mark.parametrize('caller',range(4))
@pytest.mark.parametrize('meld',['s12','k11','g11','G11'])
def test_any_meld_breaks_non_dealer_opening_win(caller,meld):
    gs=state(); gs.current_player_index=1
    for p in gs.player_list: p.discard_tiles=[]
    gs.player_list[0].discard_tiles=[19]
    # 常规3分，首摸时天和补到5；对任何副露（含暗杠）失效。
    gs.player_list[1].hand_tiles=tiles('123567m789p123s55z')
    refresh_waiting_tiles(gs,1,True)
    check_action_hand_action(gs,1)
    assert gs.result_dict['hu_self']==(5,['天和'])
    if caller==1:
        gs.player_list[1].hand_tiles=tiles('567m789p123s55z')
        gs.player_list[1].combination_tiles=[meld]
        # 此处只检查事件上下文，手牌若因测试副露不合法也不能得到天和。
    else: gs.player_list[caller].combination_tiles=[meld]
    gs.result_dict={}; gs.open_cuohe=False
    refresh_waiting_tiles(gs,1,True)
    check_action_hand_action(gs,1)
    assert all('天和' not in fans for _,fans in gs.result_dict.values())


def test_kong_availability_and_last_wall_boundary():
    gs=state(); p=gs.player_list[0]
    p.hand_tiles=tiles('1111m234p567s2233z')
    assert 'angang' in check_action_hand_action(gs,0)[0]
    gs.tiles_list=[]
    assert 'angang' not in check_action_hand_action(gs,0)[0]
    p.hand_tiles=tiles('1m234p567s2233z'); p.combination_tiles=['k11']
    gs.tiles_list=[19]
    assert 'jiagang' in check_action_hand_action(gs,0)[0]
    gs.tiles_list=[]
    assert 'jiagang' not in check_action_hand_action(gs,0)[0]


@pytest.mark.parametrize('owner',range(4))
@pytest.mark.parametrize('viewer',range(4))
def test_concealed_kong_private_until_round_reveal(owner,viewer):
    gs=state(); p=gs.player_list[owner]
    p.combination_tiles=['G22']; p.combination_mask=[[2,22]*4]
    tokens,masks=get_combination_fields_for_viewer(p,viewer)
    assert tokens==(['G22'] if owner==viewer else ['G0'])
    assert masks==([[2,22]*4] if owner==viewer else [[2,0]*4])
    assert build_revealed_angang_masks(gs.player_list)=={owner:[[2,22]*4]}
    assert p.combination_tiles==['G22'] and p.combination_mask==[[2,22]*4]


@pytest.mark.parametrize('winner',range(4))
@pytest.mark.parametrize('points',[5,6,24,99,100])
def test_all_four_seat_payment_branches_and_conservation(winner,points):
    for discarder in [None]+[i for i in range(4) if i!=winner]:
        changes=calculate_lanshi_score_changes(range(4),winner,points,discarder)
        assert sum(changes.values())==0 and changes[winner]==6*points
        assert sorted(v for i,v in changes.items() if i!=winner)==([-2*points]*3 if discarder is None else [-4*points,-points,-points])


def test_new_record_version_and_standard_isolation():
    gs=state()
    title=build_game_title_data(gs)
    assert title['sub_rule']=='guobiao/lanshi'
    assert title['rule_version']==RULE_VERSION and title['hepai_limit']==5 and title['use_flowers'] is False
    standard=make_state()
    assert 'rule_version' not in build_game_title_data(standard)
