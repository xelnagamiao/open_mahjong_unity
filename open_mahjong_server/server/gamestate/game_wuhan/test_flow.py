import asyncio
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .WuhanGameState import WuhanGameState
from tests.local_hint_oracles.wuhan import compute_hand
from ...game_calculation.wuhan import rules as book
from ..public.game_record_manager import init_game_record, init_game_round


HAND = [11,12,13,21,22,23,31,32,33,25]
PURE = [11,12,13,14,15,16,17,18,19,15]


def make_state(**extra):
    server=SimpleNamespace(user_id_to_connection={},gamestate_manager=SimpleNamespace(),room_manager=SimpleNamespace())
    room=dict(room_id=880101,player_list=[101,102,103,104],round_timer=20,step_timer=5,game_round=1,
              tips=False,count_tips=False,room_type='custom',allow_spectator=False)
    state=WuhanGameState(server,room|extra,None,None,'wuhan-test')
    for i,p in enumerate(state.player_list): p.player_index=p.original_player_index=i
    state.tiles_list=[11,12,13,14,15,16]*8
    init_game_record(state);init_game_round(state)
    return state


def indicator(s,tile=38):
    s.indicator=tile;s.joker,s.skins=book.special_tiles(tile)


def hand(s,index,tiles,melds=()):
    p=s.player_list[index];p.hand_tiles=list(tiles);p.combination_tiles=list(melds)
    p.combination_mask=[[0,int(c[1:])]*len(book.meld_tiles(c)) for c in melds]
    return p


def draw(p,tile):
    p.hand_tiles.append(tile);p.last_drawn_tile=tile;p.has_draw_slot=True


def ticks(s):return s.game_record['game_round']['round_index_1']['action_ticks']


def test_deal_inventory_and_rule_policy():
    s=make_state();s.master_seed=2025;s.init_tiles()
    c=Counter(s.tiles_list+[t for p in s.player_list for t in p.hand_tiles])
    assert sum(c.values())==136 and set(c.values())=={4}
    assert [len(p.hand_tiles) for p in s.player_list]==[14,13,13,13]
    assert s.indicator is None and s.player_list[0].has_draw_slot
    assert not s.rules.public_ready_enabled and s.rules.multi_win_mode=='head_bump'
    assert not s.enter_water(0) and s.ready_candidate_cuts(0)=={}


def test_indicator_after_first_discard_and_single_red():
    for special in [False,True]:
        s=make_state();s.tiles_list[0]=38
        p=hand(s,0,[45] if special else [11]);draw(p,25)
        asyncio.run(s.execute_cut(0,dict(TileId=45 if special else 11,CutIndex=0,IsMoCut=False)))
        assert s.indicator==38 and s.joker==39 and s.skins==(37,38,45)
        assert len([t for t in ticks(s) if t[:2]==['wuhan','indicator']])==1
        assert p.huapai_list==([45] if special else [])
        assert p.discard_tiles==([] if special else [11])


@pytest.mark.parametrize('tile',[37,38,39,45])
@pytest.mark.parametrize('supplement',[False,True])
def test_single_kong_choice_and_no_claim(tile,supplement):
    s=make_state();indicator(s);p=hand(s,0,HAND+[tile],['s26'])
    if supplement:asyncio.run(s.execute_angang(0,tile))
    else:asyncio.run(s.execute_cut(0,dict(TileId=tile,CutIndex=10,IsMoCut=False)))
    assert p.huapai_list==[tile] and len(p.hand_tiles)==10
    assert s.game_status==('deal_card_after_gang' if supplement else 'deal_card')
    assert not any(s.check_discard_actions(tile).values())
    assert not s.kong_allowed(0,tile,'added') and not s.kong_allowed(0,tile,'direct')


@pytest.mark.parametrize('tile',[None,True,'11',48])
def test_invalid_kong_cannot_mutate(tile):
    s=make_state();indicator(s);p=hand(s,0,[11]*4)
    before=deepcopy(p.hand_tiles)
    asyncio.run(s.execute_angang(0,tile));asyncio.run(s.execute_jiagang(0,tile))
    assert p.hand_tiles==before and not p.combination_tiles


def test_ordinary_kongs_and_added_robbery_staging():
    s=make_state();indicator(s);p=hand(s,0,[11]*4+HAND)
    asyncio.run(s.execute_angang(0,11))
    assert p.combination_tiles==['G11'] and s.game_status=='deal_card_after_gang'
    p=hand(s,0,[25]+HAND,['k25']);p.has_draw_slot=False
    asyncio.run(s.execute_jiagang(0,25))
    if s._pending_jiagang:asyncio.run(s.finalize_jiagang())
    assert p.combination_tiles==['g25'] and p.wuhan_added=={25}
    assert book.table_fan(p.combination_tiles,p.huapai_list,s.joker,p.wuhan_added)==2


def test_claim_direction_and_immediate_kong():
    s=make_state();indicator(s)
    p=hand(s,1,[11]*3+[12,13,21,22,23,24,25,26,27,28])
    hand(s,2,[12,13]+HAND)
    assert 'peng' in s.check_discard_actions(11)[1] and 'gang' in s.check_discard_actions(11)[1]
    assert 'chi_right' in s.check_discard_actions(11)[1]
    assert not any(a.startswith('chi') for a in s.check_discard_actions(11)[2])
    s.player_list[0].discard_tiles=[11]
    asyncio.run(s.execute_claim(1,'peng'))
    assert p.combination_tiles==['k11'] and 'jiagang' in s.after_claim_actions()[1]


def test_pair_wait_labels_and_server_consistency():
    s=make_state();indicator(s)
    p=hand(s,0,PURE,['s12'])
    rows=compute_hand(p.hand_tiles,p.combination_tiles,38,s.table_snapshot(),0)
    assert rows['waiting_details'][15]['label']=='仅自摸'
    for q in s.player_list[1:]:q.huapai_list=[45]
    rows=compute_hand(p.hand_tiles,p.combination_tiles,38,s.table_snapshot(),0)
    assert rows['waiting_details'][15]['label'].endswith('点')
    s.current_player_index=1
    assert s.score_candidate(0,'discard',15)['total']==rows['waiting_details'][15]['points']
    p=hand(s,0,HAND,['s26'])
    rows=compute_hand(p.hand_tiles,p.combination_tiles,38,s.table_snapshot(),0)
    assert rows['waiting_details'][25]['label']=='未起和'
    rows=compute_hand(p.hand_tiles,p.combination_tiles,38,s.table_snapshot(),0,True)
    assert rows['waiting_details'][25]['label']=='未满足'
    p.hand_tiles[-1]=24
    rows=compute_hand(p.hand_tiles,p.combination_tiles,38,s.table_snapshot(),0)
    assert rows['waiting_details'][24]['label']=='未满足'


def test_hint_discard_map_private_and_stale_cache():
    s=make_state(tips=True);indicator(s);p=hand(s,0,PURE+[25],['s12'])
    asyncio.run(s.prepare_private_fields([0]))
    payload=s.build_private_game_info_fields(0)['wuhan_hints']
    assert 'waiting_by_discard' not in payload
    assert payload['local_request']['hand']==PURE+[25]
    assert payload['local_request']['melds']==['s12']
    assert 'wuhan_hints' not in s.build_private_game_info_fields(1)
    p.combination_tiles=['s23']
    assert 'wuhan_hints' not in s.build_private_game_info_fields(0)


def test_head_bump_and_score_blockers():
    s=make_state();indicator(s)
    for i in [1,2]:hand(s,i,PURE,['s12'])
    for p in s.player_list:p.huapai_list=[45]
    s.action_dict=s.check_discard_actions(15);s.player_list[0].discard_tiles=[15]
    responses={i:{'action_type':next(a for a in s.action_dict[i] if a.startswith('hu'))} for i in [1,2]}
    asyncio.run(s.resolve_discard_responses(responses,s.action_dict))
    assert [w['index'] for w in s.pending_winners]==[1]
    s.player_list[1].tag_list.append('peida')
    assert s.score_candidate(1,'discard',15) is None


@pytest.mark.parametrize('wall_count',[13,14])
def test_four_sea_draws_then_exhaustion(wall_count):
    s=make_state();indicator(s);s.tiles_list=[11]*wall_count
    for i in range(4):hand(s,i,HAND,['s26'])
    for _ in range(4):
        asyncio.run(s._deal_normal())
        assert s.sea_phase and s.action_dict[s.current_player_index]==['pass']
        assert not s.can_establish_kong() and not any(s.check_discard_actions(11).values())
        s.game_status='deal_card'
    assert len(s.tiles_list)==wall_count-4 and not s.can_take_normal_tile()
    asyncio.run(s._deal_normal())
    assert s.game_status=='END'


def test_tail_upper_lower_and_13_tile_kong_guard():
    s=make_state();s.tiles_list=list(range(20))
    assert s._take_supplement_tile()==18
    s.tiles_list.pop(0)
    assert s._take_supplement_tile()==19 and s._take_supplement_tile()==16
    s.tiles_list=[11]*13
    assert not s.can_establish_kong()


def test_liability_clear_rain_all_requester_and_robbing():
    s=make_state();indicator(s);p=hand(s,1,[15],['s12','s15','s18','k11'])
    s.rain[1]=dict(suit=1,payers={0})
    s._track_liability_cut(2,21);s._track_liability_cut(3,12)
    hand(s,0,[12,15,18]);d={'fan_names':['清一色']}
    assert s.liabilities(1,'discard',0,d)=={0,2}
    assert s.liabilities(1,'robbing_kong',3,{'fan_names':['抢杠']})=={3}
    s.rain={};assert not s.liabilities(1,'discard',0,d)
    hand(s,0,[12,39]);assert s.liabilities(1,'discard',0,d)=={0}


def test_public_push_gold_stops_when_every_pair_qualifies():
    s=make_state();indicator(s)
    hand(s,0,[],['g11','g21','g31']);s.player_list[0].huapai_list=[45]
    assert s.push_gold_active()
    s.push_liability={2}
    assert s.liabilities(1,'self_draw',None,{'fan_names':[]})=={2}
    for p in s.player_list[1:]:p.huapai_list=[45,45,45]
    assert not s.push_gold_active() and not s.liabilities(1,'self_draw',None,{'fan_names':[]})


@pytest.mark.parametrize('kind',['七皮','十杠'])
def test_bonus_once_and_settlement_statistics(kind):
    from ...database.shared_record_metrics import analyze_shared_record_for_player
    s=make_state();indicator(s);s.current_round=4
    p=hand(s,0,PURE,['s12'])
    p.huapai_list=[37]*3+[38]*3+[45] if kind=='七皮' else [39]*4+[45]*2
    for q in s.player_list[1:]:q.huapai_list=[45]
    asyncio.run(s._check_bonus(0));asyncio.run(s._check_bonus(0))
    assert [q.score for q in s.player_list]==[300,-100,-100,-100]
    assert len([t for t in ticks(s) if t[:2]==['wuhan','bonus']])==1
    draw(p,15);s.result_dict['hu_self']=s.score_candidate(0,'self_draw');s.accept_self_draw(0)
    asyncio.run(s._settle_hand({i:0 for i in range(4)}))
    asyncio.run(s._settle_hand({i:0 for i in range(4)}))
    for i,q in enumerate(s.player_list):
        stats=analyze_shared_record_for_player(s.game_record,q.original_player_index)
        assert stats['total_round_score']==q.score
        assert stats['self_draw_count']==int(i==0)
        assert len(q.score_history)==1
    assert len(s._terminal_result['show_result_info']['revealed_hands'])==4


def test_claim_room_clock_revision_late_and_reconnect():
    s=make_state();indicator(s);s.game_status='waiting_action_after_cut';s.action_dict={0:[],1:['peng','pass'],2:[],3:[]}
    clock=s.action_clock;now=[1.0];clock.now=lambda:now[0]
    s.prepare_action_window();s.on_action_window_broadcast();s.on_action_window_delivered(1)
    assert s.claim_clock(s.player_list[1])==(20,5)
    now[0]=2;s.action_queues[1].put_nowait({'action_type':'peng'})
    now[0]=3;s.action_queues[1].put_nowait({'action_type':'pass'})
    assert s.claim_clock(s.player_list[1],True)==(20,3)
    s.on_action_window_delivered(1);s.prepare_action_window()
    now[0]=26;s.action_queues[1].put_nowait({'action_type':'peng'})
    responses,_=asyncio.run(s.collect_action_responses())
    assert responses=={1:{'action_type':'pass'}} and s.player_list[1].remaining_time==20


def test_robbing_window_and_hand_budget():
    s=make_state();indicator(s);now=[1.0];s.action_clock.now=lambda:now[0]
    s.game_status='waiting_action_qianggang';s.action_dict={0:[],1:['hu_left','pass'],2:[],3:[]}
    s.on_action_window_broadcast();s.on_action_window_delivered(1)
    now[0]=4.5
    assert s.claim_clock(s.player_list[1],True)==(20,2)
    s.game_status='waiting_hand_action';s.action_dict={0:['cut'],1:[],2:[],3:[]}
    s.prepare_action_window();s.on_action_window_broadcast();s.on_action_window_delivered(0)
    now[0]=11.5;s.action_queues[0].put_nowait({'action_type':'cut','TileId':11})
    responses,_=asyncio.run(s.collect_action_responses())
    assert responses[0]['TileId']==11 and s.player_list[0].remaining_time==18
