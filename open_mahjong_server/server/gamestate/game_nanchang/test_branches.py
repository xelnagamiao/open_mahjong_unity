from copy import deepcopy
import pytest

from .test_state import state,cut,NORMAL
from .actions import claim_actions,kong_tiles,legal_cuts
from .state_machine import Phase as P


def answers(window,chosen):
    return {i:dict(action_type=chosen.get(i,'pass')) for i,a in window['actions'].items() if a}


@pytest.mark.parametrize('kind,tile,unit',[('angang',21,2),('angang',45,10),('jiagang',21,1),('jiagang',45,10)])
def test_actual_concealed_and_added_kong_commit_before_supplement(kind,tile,unit):
    s=state();p=s.player_list[0]
    p.hand_tiles=[11,12,13,31,32,33,41,41,42,42]+[tile]*(4 if kind=='angang' else 1)
    p.has_draw_slot=True
    if kind=='jiagang':p.combination_tiles=[f'k{tile}'];p.combination_mask=[[0,tile,1,tile,0,tile]]
    s.tiles_list=[18,19];s.indicator_index=0
    window=s.open_action_window(s.begin_turn(0))
    result=s.apply_action_results(window,{0:dict(action_type=kind,target_tile=tile)})
    assert s.kong_ledger[-1]['changes']==[unit*3,-unit,-unit,-unit]
    assert p.combination_tiles==[f'{"G" if kind=="angang" else "g"}{tile}']
    assert len(p.combination_mask[0])==8
    assert p.hand_tiles[-1]==18 and p.after_kong and p.has_draw_slot
    assert result['status']==P.TURN.value
    assert not any(e['action']=='rob_kong' for e in s.domain_events)


def test_claim_priority_and_head_bump_choose_nearest_winner():
    s=state()
    for i in (1,2):s.player_list[i].hand_tiles=NORMAL[:-1];s.player_list[i].has_draw_slot=False
    s.player_list[3].hand_tiles=[41,41,11,12,13,21,22,23,31,32,33,45,47]
    s.player_list[0].hand_tiles=NORMAL.copy();s.player_list[0].has_draw_slot=True
    w=cut(s,0,41)
    assert 'hu' in w['actions'][1] and 'hu' in w['actions'][2] and 'peng' in w['actions'][3]
    s.apply_action_results(w,answers(w,{1:'hu',2:'hu',3:'peng'}))
    assert s.round_settlement['winner']==1 and len(s.deferred_hu_settlements)==1
    assert s.player_list[1].won_tiles==[41] and not s.player_list[2].won_tiles
    assert not s.player_list[3].combination_tiles


def test_peng_precedes_upstream_honor_chow():
    s=state();s.player_list[1].hand_tiles=[42,44]+NORMAL[:9]+[45,45]
    s.player_list[2].hand_tiles=[41,41]+NORMAL[:9]+[45,47]
    s.player_list[0].hand_tiles=NORMAL.copy();s.player_list[0].has_draw_slot=True
    w=cut(s,0,41)
    s.apply_action_results(w,answers(w,{1:'chi_mid',2:'peng'}))
    assert s.current_player_index==2 and s.machine.phase==P.DISCARD_ONLY
    assert s.player_list[2].combination_tiles==['k41']
    assert s.player_list[1].combination_tiles==[]


def test_declared_jingdiao_may_claim_direct_kong_then_must_win():
    s=state();p=s.player_list[1]
    p.hand_tiles=[11]*3+[21,22,23,31,32,33,42,42,42,45]
    p.has_draw_slot=False;p.declared_jingdiao=True
    s.player_list[0].hand_tiles=NORMAL[:-1]+[11];s.player_list[0].has_draw_slot=True
    s.tiles_list=[46,19]
    w=cut(s,0,11)
    assert w['actions'][1]==['gang','pass']
    result=s.apply_action_results(w,answers(w,{1:'gang'}))
    assert p.combination_tiles==['g11'] and p.after_kong
    assert result['actions'][1]==['hu_self']
    s.apply_action_results(result,{1:dict(action_type='hu_self')})
    assert {'精吊','杠开'}.issubset(s.round_settlement['score']['fan_names'])


def test_declared_jingdiao_added_kong_requires_the_drawn_tile():
    s=state();p=s.player_list[0]
    p.hand_tiles=[21,22,23,31,32,33,42,42,42,45,11]
    p.combination_tiles=['k11'];p.combination_mask=[[0,11,1,11,0,11]]
    p.has_draw_slot=True;p.declared_jingdiao=True
    assert kong_tiles(s,0,'jiagang')=={11}
    p.hand_tiles[-1],p.hand_tiles[-2]=p.hand_tiles[-2],p.hand_tiles[-1]
    assert kong_tiles(s,0,'jiagang')==set()


@pytest.mark.parametrize('complete',[True,False])
def test_post_claim_cut_clears_pass_limit_only_for_noncomplete_hand(complete):
    s=state();p=s.player_list[0]
    p.combination_tiles=['k41'];p.has_draw_slot=False;p.missed_fan=5
    p.hand_tiles=[11,12,13,21,22,23,31,32,33,42,42 if complete else 43]
    s.machine.transition(P.RESPONSE)
    w=s.open_action_window(s.begin_turn(0,discard_only=True))
    s.apply_action_results(w,{0:dict(action_type='cut',TileId=43 if not complete else 42,cutClass=False)})
    assert p.missed_fan==(5 if complete else -1)


def test_normal_draw_clears_pass_limit():
    s=state();p=s.player_list[1];p.hand_tiles=NORMAL[:-1];p.has_draw_slot=False;p.missed_fan=6
    s.draw_for(1)
    assert p.missed_fan==-1 and p.has_draw_slot


def test_peida_restrictions_keep_jing_and_do_not_collect_kong_income():
    s=state();p=s.player_list[1];p.tag_list=['peida'];p.hand_tiles=[45,47,11]
    assert legal_cuts(s,1)=={11}
    p.hand_tiles=[45,47];assert legal_cuts(s,1)=={45,47}
    assert claim_actions(s,0,45)[1]==[]
    s.record_kong(1,21,'angang');s.end_draw()
    assert s.round_settlement['kong_changes']==[0]*4
    assert s.round_settlement['jing']['charges'][1]==[0]*4


@pytest.mark.parametrize('bad',[
    dict(action_type='cut',TileId=True),dict(action_type='cut',TileId=11,cutIndex=True),
    dict(action_type='cut',TileId=11,cutIndex=20),dict(action_type='cut',TileId=11,cutClass=1),
    dict(action_type='cut',TileId=11,cutClass=True),dict(action_type='angang',target_tile=11),
])
def test_rejected_physical_action_is_atomic(bad):
    s=state();p=s.player_list[0];p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
    w=s.open_action_window(s.begin_turn(0));before=deepcopy((p.hand_tiles,p.discard_tiles,s.domain_events,s.kong_ledger))
    with pytest.raises(ValueError):s.apply_action_results(w,{0:bad})
    assert (p.hand_tiles,p.discard_tiles,s.domain_events,s.kong_ledger)==before
    assert s.live_pending_window is w


def test_jing_discard_horizontal_and_literal_transfer_in_protocol_and_record():
    s=state();p=s.player_list[0];p.hand_tiles=NORMAL[:-1]+[45];p.has_draw_slot=True
    s.player_list[1].hand_tiles=[45,45]+NORMAL[:9]+[41,41];s.player_list[1].has_draw_slot=False
    s.start_game_recording();s.start_round_recording()
    w=cut(s,0,45)
    packets=[x for x in s.outbound_payloads if x.get('do_action_info',{}).get('action_list')==['cut']]
    assert packets and all(x['do_action_info']['is_riichi_horizontal'] for x in packets)
    assert s._player_view(0,0)['discard_riichi_flags']==[True]
    s.apply_action_results(w,answers(w,{1:'peng'}))
    assert s.jing_transfers==[dict(payer=0,receiver=1,tile=45)]
    assert p.discard_tiles==[] and p.discard_origin_tiles==[45]


def test_self_draw_record_uses_integer_absent_payer_and_finalizes_once():
    s=state();p=s.player_list[0];p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
    s.start_game_recording();s.start_round_recording();s.settle_win(0,'self_draw',41)
    s.finalize_round_recording()
    ticks=s.game_record['game_round']['round_index_1']['action_ticks']
    source=next(t for t in ticks if t[:2]==['nanchang','win_source'])
    assert source[3]==-1 and type(source[3]) is int
    before=deepcopy(ticks)
    s.finalize_round_recording()
    assert ticks==before and sum(t[0]=='end' for t in ticks)==1


@pytest.mark.parametrize('self_draw',[True,False])
def test_win_turn_counts_actual_discards_after_another_player_claims_the_river(self_draw):
    s=state();p=s.player_list[0]
    p.hand_tiles=NORMAL[:-1]+[45];p.has_draw_slot=True
    s.player_list[1].hand_tiles=[45,45,21,22,23,24,25,26,31,32,33,46,46]
    s.player_list[1].has_draw_slot=False
    s.tiles_list=[18,19,41,11] if self_draw else [18,41,19,11]
    p.record_counter.win_turn=7
    s.start_game_recording();s.start_round_recording()
    w=cut(s,0,45)
    s.apply_action_results(w,answers(w,{1:'peng'}))
    assert p.discard_tiles==[] and p.discard_origin_tiles==[45]
    w=cut(s,1,46);s.apply_action_results(w,answers(w,{}))
    w=cut(s,2,18);s.apply_action_results(w,answers(w,{}))
    w=cut(s,3,19 if self_draw else 41)
    if self_draw:
        w=s.apply_action_results(w,answers(w,{}))
        assert 'hu_self' in w['actions'][0]
        s.apply_action_results(w,{0:dict(action_type='hu_self')})
    else:
        assert 'hu' in w['actions'][0]
        s.apply_action_results(w,answers(w,{0:'hu'}))
    assert p.record_counter.win_turn==9  # Previous 7 + one real discard + winning turn.
    assert p.record_counter.zimo_times==int(self_draw)
    assert p.record_counter.dianhe_times==int(not self_draw)
    assert p.record_counter.win_score==s.deferred_hu_settlements[-1]['points']
    s.finalize_round_recording();s.finalize_round_recording()
    assert p.record_counter.win_turn==9


def test_german_hint_range_covers_dezhongde_without_exposing_opponent_hands():
    from .hints import private_hints,hand_key
    s=state();s.tips=True;p=s.player_list[1];p.hand_tiles=NORMAL[:-1];p.has_draw_slot=False
    first=deepcopy(private_hints(s,1))
    row=next(r for r in first[hand_key(p.hand_tiles)] if r['tile']==41)
    assert row['points_max']>row['points_min']>0
    for i in (0,2,3):s.player_list[i].hand_tiles=[11]*3+[21,22,23,31,32,33,42,42,43,44]
    s._wait_cache={}
    assert first==private_hints(s,1)
    assert row['points_min']<=s.score_win(1,'discard',41,payer=0,preview=True)['points']<=row['points_max']
