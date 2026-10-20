import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .ShenyangGameState import ShenyangGameState
from tests.local_hint_oracles import shenyang as hints
from ...game_calculation.shenyang import rules as b
from ...game_calculation.shenyang.test_rules import READY
from ..public.game_record_manager import init_game_record, init_game_round


def make_state():
    server=SimpleNamespace(user_id_to_connection={},gamestate_manager=SimpleNamespace(),room_manager=SimpleNamespace())
    room=dict(room_id='123456',player_list=[10000101,10000102,10000103,10000104],round_timer=20,step_timer=5,
              game_round=1,tips=True,room_type='custom',allow_spectator=False)
    s=ShenyangGameState(server,room,None,None,'shenyang-test')
    s.bot_speed='instant'  # Rule tests bypass the live manager's bot pacing setup.
    for i,p in enumerate(s.player_list):p.player_index=p.original_player_index=i
    s.tiles_list=list(b.TILES)
    init_game_record(s);init_game_round(s)
    return s


def hand(s,index,tiles,codes=(),drawn=None,ready=False):
    p=s.player_list[index]
    p.hand_tiles=list(tiles);p.combination_tiles=list(codes)
    p.combination_mask=[[v for t in b.parse_meld(c).tiles for v in (2 if c.startswith('G') else 0,t)] for c in codes]
    p.has_draw_slot=drawn is not None;p.last_drawn_tile=drawn
    p.declared_ready=p.ready_locked=ready;p.opened=not b.is_closed(codes,ready)
    return p


def test_deal_and_winner_dealer():
    s=make_state();s.master_seed=2025;s.init_tiles()
    assert [len(p.hand_tiles) for p in s.player_list]==[14,13,13,13]
    assert Counter(s.tiles_list+[t for p in s.player_list for t in p.hand_tiles])==Counter({t:4 for t in b.TILES})
    assert len(s.tiles_list)==83 and s.dead_wall_count==0
    for i in range(4):s.pending_winners=[dict(index=i)];assert s.next_dealer()==i
    s.pending_winners=[];assert s.next_dealer()==0


def test_chow_requires_open_and_nearest_seat():
    s=make_state();p=hand(s,1,[12,13,22,23,24,31,32,33,45,45,45,19,19])
    assert 'chi_right' not in s.check_discard_actions(11)[1]
    p.opened=True
    assert 'chi_right' in s.check_discard_actions(11)[1]
    hand(s,2,p.hand_tiles);s.player_list[2].opened=True
    assert not any(a.startswith('chi') for a in s.check_discard_actions(11)[2])
    p.ready_locked=True;assert 'chi_right' not in s.check_discard_actions(11)[1]


@pytest.mark.parametrize('code,tiles,replacement', [('Ydragon',[45,46,47],False),('Ywind',[41,42,43,44],True)])
def test_special_kong_physical_state_and_replacement(code,tiles,replacement):
    s=make_state();p=hand(s,0,tiles+[11,12,13,21,22,23,31,32,33,19,19][:14-len(tiles)],drawn=19)
    before=len(s.tiles_list)
    assert asyncio.run(s.execute_special(0,code))
    assert len(p.hand_tiles)==14-len(tiles) and p.combination_tiles==[code]
    assert len(s.tiles_list)==before and not p.opened
    assert s.game_status==('deal_card_after_gang' if replacement else 'waiting_hand_action')
    assert p.has_draw_slot is not replacement


def test_specials_forbidden_after_claim_ready_or_tail():
    s=make_state();p=hand(s,0,[45,46,47]+READY[:11])
    assert not s.special_allowed(0,'Ydragon')
    p.has_draw_slot=True;assert s.special_allowed(0,'Ydragon')
    p.ready_locked=True;assert not s.special_allowed(0,'Ydragon')
    p.ready_locked=False;s.final_four=True;assert not s.special_allowed(0,'Ydragon')


def test_declared_ready_lock_and_kong_wait_change_allowed():
    s=make_state();p=hand(s,0,[15,15,15,15,16,11,12,13,21,22,23,31,32,33],drawn=15,ready=True)
    assert s.kong_allowed(0,15,'concealed')
    asyncio.run(s.execute_angang(0,15));assert p.combination_tiles==['G15']
    s.tiles_list[-1]=16;asyncio.run(s._deal_supplement())
    before=list(p.hand_tiles);asyncio.run(s.execute_cut(0,{'TileId':11}))
    assert p.hand_tiles==before and p.declared_ready


def test_ready_kong_rejects_not_tenpai_and_added():
    s=make_state();p=hand(s,0,[15]*4+[11,12,13,21,22,23,31,32,34,39],drawn=15,ready=True)
    assert not s.kong_allowed(0,15,'concealed')
    p.combination_tiles=['k15'];p.hand_tiles=[15]+READY[:10]
    assert not s.kong_allowed(0,15,'added')


def test_ready_requires_closed_draw_and_legal_completion():
    s=make_state();p=hand(s,0,READY+[28],drawn=28)
    assert 28 in s.ready_candidate_cuts(0)
    p.opened=True;assert not s.ready_candidate_cuts(0)
    p.opened=False;p.has_draw_slot=False;assert not s.ready_candidate_cuts(0)


def test_egg_privacy_choice_and_idempotency():
    s=make_state();hand(s,0,READY+[31],drawn=31)
    s.egg_offer=31
    assert s.check_hand_actions(0)[0]==['sy_reveal','sy_hide']
    assert s.view(1)['egg_offer'] is None
    asyncio.run(s.resolve_egg(True));assert [p.score for p in s.player_list]==[30,-10,-10,-10]
    asyncio.run(s.resolve_egg(True));assert len(s.egg_ledger)==1
    s.egg_offer=21;asyncio.run(s.resolve_egg(False));assert len(s.egg_ledger)==1


def test_tail_no_discard_no_kong_and_private_pass():
    s=make_state();s.current_player_index=3;s.tiles_list=[28,29,38,39]
    for i in range(4):hand(s,i,READY)
    for i in range(4):
        asyncio.run(s._deal_normal());assert s.current_player_index==i and s.final_four
        assert s.check_hand_actions(i)[i]==['sy_pass']
        before=list(s.player_list[i].hand_tiles);asyncio.run(s.execute_cut(i,{'TileId':19}))
        assert s.player_list[i].hand_tiles==before
        asyncio.run(s.pass_tail(i));assert s.view((i+1)%4)['tail_tiles'][i]==0
    asyncio.run(s._deal_normal());assert s.game_status=='END' and len(s.tail_tiles)==4


def test_last_pre_tail_draw_can_win_but_not_kong():
    s=make_state();s.current_player_index=3;s.tiles_list=[19,28,29,38,39]
    hand(s,0,READY)
    asyncio.run(s._deal_normal())
    assert not s.final_four and 'hu_self' in s.check_hand_actions(0)[0]
    assert not s.can_establish_kong()


def test_last_pre_tail_draw_allows_xi_without_using_four_reserved_tiles():
    s=make_state();s.tiles_list=[28,29,38,39]
    hand(s,0,[45,46,47,11,12,13,21,22,23,31,32,33,19,19],drawn=19)
    assert 'sy_xi' in s.check_hand_actions(0)[0]
    assert asyncio.run(s.execute_special(0,'Ydragon'))
    assert s.tiles_list==[28,29,38,39] and not s.final_four
    assert 'hu_self' in s.check_hand_actions(0)[0]


def test_last_pre_tail_discard_can_be_claimed_without_supplement():
    s=make_state();s.tiles_list=[28,29,38,39]
    p=hand(s,1,[11,11,11,12,13,21,22,23,31,32,33,19,19]);p.opened=True
    choices=s.check_discard_actions(11)[1]
    assert 'peng' in choices and 'chi_right' in choices and 'gang' not in choices
    s.player_list[0].discard_tiles=[11]
    asyncio.run(s.execute_claim(1,'peng'))
    assert p.combination_tiles==['k11'] and s.tiles_list==[28,29,38,39]
    assert not s.final_four
    hand(s,1,[41,42,43,44,11,12,13,21,22,23,31,32,33,19],drawn=19)
    assert not s.special_allowed(1,'Ywind')


def test_shunhe_capped_fan_clears_only_unready_draw():
    s=make_state();p=hand(s,1,READY)
    detail=s.score_candidate(1,'discard',19);assert detail
    p.passed_fan=detail['fan'];assert s.score_candidate(1,'discard',19) is None
    p.passed_fan=6;s.last_draw_after_kong=True
    assert s.score_candidate(1,'robbing_kong',19) is None
    p.hand_tiles.append(19);p.last_drawn_tile=19;p.has_draw_slot=True
    assert s.score_candidate(1,'self_draw')
    p.declared_ready=True;asyncio.run(s._process_drawn_flowers(1,'normal'));assert p.passed_fan==6
    p.declared_ready=False;asyncio.run(s._process_drawn_flowers(1,'normal'));assert p.passed_fan==-1


def test_shunhe_allows_strictly_higher_fan_below_cap():
    s=make_state()
    for p in s.player_list:p.opened=True
    p=hand(s,1,READY);p.opened=True
    detail=s.score_candidate(1,'discard',19)
    p.passed_fan=detail['fan']
    assert s.score_candidate(1,'discard',19) is None
    s.kong_discard=True
    higher=s.score_candidate(1,'discard',19)
    assert higher and higher['fan']==detail['fan']+1


@pytest.mark.parametrize('complete',[False,True])
def test_pung_keeps_missed_win_only_if_claim_leaves_complete_hand(complete):
    s=make_state();s.player_list[0].discard_tiles=[45]
    cards=[45,45,11,12,13,21,22,23,31,32,33,19,19] if complete else READY
    p=hand(s,1,cards);p.passed_fan=6
    asyncio.run(s.execute_claim(1,'peng'))
    assert p.opened and p.combination_tiles==['k45']
    assert p.passed_fan==(6 if complete else -1)


def test_ready_direct_kong_preserves_closed_and_can_change_wait():
    s=make_state();s.player_list[0].discard_tiles=[15]
    p=hand(s,1,[15,15,15,16,11,12,13,21,22,23,31,32,33],ready=True)
    assert 'gang' in s.check_discard_actions(15)[1]
    asyncio.run(s.execute_claim(1,'gang'))
    assert p.combination_tiles==['g15'] and not p.opened and p.ready_locked
    s.tiles_list[-1]=16;asyncio.run(s._deal_supplement())
    assert s.score_candidate(1,'self_draw') is not None


def test_head_bump_and_ron_priority():
    s=make_state();s.player_list[0].discard_tiles=[19]
    hand(s,1,READY);hand(s,2,READY)
    allowed=s.check_discard_actions(19)
    asyncio.run(s.resolve_discard_responses({1:{'action_type':'hu_first'},2:{'action_type':'hu_second'}},allowed))
    assert len(s.pending_winners)==1 and s.pending_winners[0]['index']==1


def test_long_hair_first_four_and_next_draw():
    s=make_state()
    for i,p in enumerate(s.player_list):
        p.discard_count=1;p.last_discarded_tile=41
    s._finalize_ready_after_discard(3,False);assert s.long_hair
    s.previous_draw=True;s._reset_hand_runtime();assert s.long_hair


def test_hints_priority_and_complete_wire_roundtrip():
    s=make_state();p=hand(s,0,[12,13,14,22,23,24,32,33,34,15,15,15,26])
    value=hints.compute(*hints.request(s,0));assert value['waiting'][26]['status']=='unmet'
    hand(s,0,[11,12,13,21,22,23,31,32,33,19,19,45,46])
    value=hints.compute(*hints.request(s,0));assert value['waiting'][47]['label']=='仅自摸'
    hand(s,0,READY)
    value=hints.compute(*hints.request(s,0));assert value['waiting'][19]['status']=='win'
    p.passed_fan=6;value=hints.compute(*hints.request(s,0));assert value['waiting'][19]['label']=='仅自摸'
    asyncio.run(s.prepare_private_fields((0,)))
    from ...response import GameInfo
    packet=GameInfo(**s.build_private_game_info_fields(0),room_id='123456',gamestate_id='sy-test',tips=True,
        current_player_index=0,action_tick=0,max_round=1,tile_count=20,current_round=1,
        step_time=5,round_time=20,room_type='custom',room_rule='shenyang',players_info=[])
    preview=packet.model_dump()['shenyang']['hints']
    assert 'waiting' not in preview
    assert preview['local_request']['hand']==list(READY)
    assert preview['local_request']['context']['passed_fan']==6


def test_rob_added_kong_rolls_back_fourth_tile_and_never_pays_egg():
    s=make_state()
    declarer=hand(s,0,[11,12,13,21,22,23,31,32,33,45,19],('k19',),drawn=19)
    hand(s,1,[17,18,21,22,23,31,32,33,45,45,45,11,11])
    wall=list(s.tiles_list)
    async def run():
        await s.execute_jiagang(0,19)
        assert s.game_status=='waiting_action_qianggang' and declarer.combination_tiles==['g19']
        await s.resolve_rob_kong_responses({1:{'action_type':'hu_first'}},s.action_dict)
    asyncio.run(run())
    assert declarer.combination_tiles==['k19'] and 19 not in declarer.hand_tiles
    assert s.tiles_list==wall and not s.egg_ledger
    assert s.pending_winners[0]['source']=='robbing_kong'
    assert any(t[0]=='sy' and t[1]['kind']=='robbed' for t in s.game_record['game_round']['round_index_1']['action_ticks'])


def test_clock_rejects_stale_and_duplicate_response_and_preserves_remaining_bank():
    s=make_state();clock=s.room_action_clock;now=[100.0];clock.now=lambda:now[0]
    s.game_status='waiting_action_qianggang';s.server_action_tick=3;s.action_dict={0:[],1:['pass'],2:[],3:[]}
    clock.begin();clock.delivered(1)
    now[0]=106.25
    s.action_queues[1].put_nowait({'action_type':'pass','_action_tick':2})
    assert clock.is_pending(1)
    s.action_queues[1].put_nowait({'action_type':'pass','_action_tick':3})
    assert not clock.is_pending(1) and clock.snapshot(s.player_list[1])==(19,0)
    now[0]=108
    s.action_queues[1].put_nowait({'action_type':'pass','_action_tick':3})
    assert clock.snapshot(s.player_list[1])==(19,0)


def test_discarding_winning_tile_updates_preview_shunhe():
    s=make_state();hand(s,0,READY+[19],drawn=19)
    value=hints.compute(*hints.request(s,0))
    assert value['by_discard'][19][19]['status']=='self_draw'


@pytest.mark.parametrize('seed',[1,99,2025])
def test_complete_bot_hand_preserves_all_physical_tiles(seed):
    s=make_state();s.master_seed=seed;s.init_tiles()
    for p in s.player_list:p.user_id=2
    init_game_round(s)
    asyncio.run(asyncio.wait_for(s._run_hand(),10))
    assert s.game_status=='END' and s.draw_reason!='invalid_state'
    tiles=list(s.tiles_list)+list(s.tail_tiles.values())
    for p in s.player_list:
        tiles+=p.hand_tiles+p.discard_tiles
        tiles += [t for c in p.combination_tiles for t in b.parse_meld(c).tiles]
    assert Counter(tiles)==Counter({t:4 for t in b.TILES})


@pytest.mark.parametrize('action,offline',[('sy_hide',False),('sy_pass',False),('sy_hide',True),('sy_pass',True)])
def test_simple_bot_and_offline_special_phase_fallback(action,offline):
    s=make_state();p=s.player_list[0]
    if offline:p.tag_list.append('offline')
    else:p.user_id=0
    s.game_status='waiting_hand_action';s.action_dict={0:[action],1:[],2:[],3:[]}
    s.on_action_window_broadcast();s.on_action_window_delivered(0)
    assert s.action_queues[0].get_nowait()['action_type']==action


def test_full_four_hand_lifecycle_persists_and_cleans_up(monkeypatch):
    s=make_state();s.room_random_seed=2025
    for p in s.player_list:p.user_id=2
    s.db_manager=SimpleNamespace(store_shenyang_game_record=Mock(return_value='shenyang-match-test'))
    s.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
    s.run_hu_result_ready_phase=AsyncMock()
    monkeypatch.setattr('server.gamestate.game_shenyang.lifecycle.liuju_ready_wait_seconds',lambda:0)
    asyncio.run(asyncio.wait_for(s.game_loop_chinese(),45))
    assert len(s.game_record['game_round'])==4
    assert all(len(p.score_history)==4 and p.round_number_history==[1,2,3,4] for p in s.player_list)
    assert sum(p.score for p in s.player_list)==0
    assert all(sum(map(int,p.score_history))==p.score for p in s.player_list)
    s.db_manager.store_shenyang_game_record.assert_called_once()
    s.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once_with(gamestate_id=s.gamestate_id)
    assert s.game_record['game_title']['rule_version']==b.VERSION
    for round in s.game_record['game_round'].values():
        assert len([tick for tick in round['action_ticks'][:4] if tick[0]=='sy' and tick[1]['kind']=='hints'])==4
