from collections import Counter
from copy import deepcopy
import pytest
from .VietnameseGameState import VietnameseGameState
from .state_machine import Phase as P
from .bot import choose_action
from ...game_calculation.vietnamese.tiles import *
from ...game_calculation.vietnamese.test_rules import NORMAL


def state(seed=1,**config):
    data=VietnameseGameState._default_room_data();data.update(random_seed=seed,detailed_config=config,bot_speed='instant')
    return VietnameseGameState(room_data=data)


def act(s,replies=None):
    window=s.live_pending_window
    return s.apply_action_results(window,replies if replies is not None else {i:choose_action(s,i) for i,a in window['actions'].items() if a})


def physical(s):
    return Counter(s.tiles_list+[t for p in s.player_list for t in p.hand_tiles+p.huapai_list+p.discard_tiles+p.won_tiles+[t for m in p.combination_mask for t in m[1::2]]])


def turn(hand,**config):
    s=state(**config);s.tiles_list=[18]*40;s.opening_queue=None;s.claim_options={i:[] for i in range(4)}
    s.pending_kong=None;s.replacement_batch=0;s.discard_safety={};s.safe_special=None
    p=s.player_list[0];p.hand_tiles=list(hand);p.has_draw_slot=True;p.last_drawn_tile=hand[-1]
    s.open_action_window(s.begin_turn(0));return s


@pytest.mark.parametrize('seed',range(8))
def test_whole_round_tile_conservation_private_views_and_idempotent_settlement(seed):
    s=state(seed);s.initialize_round();s.start_game_recording();s.start_round_recording()
    s.open_action_window(s.opening_window());steps=0
    while s.machine.phase!=P.END:
        assert physical(s)==Counter(PHYSICAL)
        payload=s.build_game_start_payload(0)
        assert all(p['hand_tiles'] is None for p in payload['game_info']['players_info'][1:])
        act(s);steps+=1
        assert steps<700
    assert physical(s)==Counter(PHYSICAL)
    assert len(s.tiles_list)>=26
    s.finalize_round_recording();s.finalize_round_recording()
    assert sum(p.score for p in s.player_list)==1200
    assert all(len(p.score_history)==1 and not p.record_counter.recorded_fans for p in s.player_list)
    assert s.game_record['game_title']['rule']=='vietnamese'


def test_last_draw_discard_can_win_but_no_meld_or_replacement():
    s=turn(NORMAL);s.tiles_list=[11]*26
    assert not s.kong_tiles(0,'angang') and not s.replacement_candidates(0)
    s.player_list[1].hand_tiles=list(NORMAL[:-1])
    s.open_action_window(s.discard(0,dict(TileId=25,cutClass=True)))
    assert s.action_dict[1]==['hu','pass']
    act(s,{1:dict(action_type='hu')})
    assert s.machine.phase==P.END and len(s.tiles_list)==26


def test_scoped_jokers_can_be_claimed_and_competing_pungs_are_ranked():
    s=turn(NORMAL)
    s.player_list[1].hand_tiles=[25,ANY]+[11,12,13,31,32,33,41,41,41,19,19]
    s.player_list[2].hand_tiles=[25,25]+[11,12,13,31,32,33,42,42,42,19,19]
    opts1=s.build_claim_options(1,0,25);opts2=s.build_claim_options(2,0,25)
    assert any(o['kind']=='k' and o['joker_count']==1 for o in opts1)
    assert any(o['kind']=='k' and o['joker_count']==0 for o in opts2)
    assert not s.build_claim_options(1,0,51)


def test_bounce_swaps_physical_joker_to_bonus_and_draws_from_tail():
    s=turn([11,12,13,21,22,23,31,32,33,45,25]);p=s.player_list[0]
    p.combination_tiles=['k25'];p.combination_mask=[[1,25,0,25,0,ANY]]
    s.tiles_list[-1]=46;s.open_action_window(s.begin_turn(0))
    act(s,{0:dict(action_type='vn_bounce',target_tile=ANY)})
    assert p.combination_mask[0][1::2]==[25,25,25]
    assert p.huapai_list==[ANY] and p.hand_tiles[-1]==46 and len(s.tiles_list)==39


def test_added_kong_rob_is_provisional():
    s=turn([11,12,13,21,22,23,31,32,33,45,25]);p=s.player_list[0]
    p.combination_tiles=['k25'];p.combination_mask=[[1,25,0,25,0,25]]
    s.player_list[1].hand_tiles=list(NORMAL[:-1]);s.open_action_window(s.begin_turn(0))
    act(s,{0:dict(action_type='jiagang',target_tile=25)})
    assert s.machine.phase==P.ROB and p.combination_tiles==['k25'] and p.hand_tiles[-1]==25
    act(s,{1:dict(action_type='hu')})
    assert p.combination_tiles==['k25'] and len(p.hand_tiles)==10 and s.player_list[1].won_tiles==[25]


def test_invalid_responses_do_not_mutate():
    s=turn(NORMAL);before=deepcopy(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):act(s,{0:dict(action_type='cut',TileId=47,cutIndex=0,cutClass=False)})
    assert s.player_list[0].hand_tiles==before
    with pytest.raises(ValueError):act(s,{0:dict(action_type='vn_claim',target_tile=999)})


def test_no_flowers_leaves_safe_three_blue_offers_immediate_win():
    s=turn(NORMAL);p=s.player_list[0];p.discarded_bonus=[ANY,DOT]
    s.machine.transition(P.RESPONSE);s.current_player_index=0
    window=s.after_safe_discard(0,BAMBOO);s.open_action_window(window)
    assert s.machine.phase==P.SPECIAL
    act(s,{0:dict(action_type='hu_special')})
    assert s.machine.phase==P.END and s.deferred_hu_settlements[0]['special']


def test_wall_draw_repeats_dealer_and_winner_rotation():
    s=turn(NORMAL);s.end_hand();s.machine.transition(P.READY)
    before=[p.user_id for p in s.player_list];s.advance_round_after_ready()
    assert [p.user_id for p in s.player_list]==before and s.current_round==1
    s.machine.transition(P.TURN);s.end_hand(1);s.machine.transition(P.READY);s.advance_round_after_ready()
    assert [p.user_id for p in s.player_list]==before[1:]+before[:1] and s.current_round==2


def test_late_network_action_is_rejected_before_queue_mutation():
    import asyncio
    async def run():
        s=turn(NORMAL)
        s._active_deadlines={0:asyncio.get_running_loop().time()-0.01}
        before=list(s.player_list[0].hand_tiles)
        with pytest.raises(ValueError,match='时限'):
            await s.submit_action(0,'cut',TileId=NORMAL[-1],cutClass=True)
        assert s.action_queues[0].empty() and s.player_list[0].hand_tiles==before
    asyncio.run(run())


def test_safe_blue_joker_win_is_offered_before_nonwinning_claim_is_committed():
    s=turn(NORMAL[:-1]+(BAMBOO,));p=s.player_list[0]
    p.discarded_bonus=[ANY,DOT]
    s.player_list[1].hand_tiles=[31,31,11,12,13,14,15,16,21,22,23,41,42]
    s.open_action_window(s.discard(0,dict(TileId=BAMBOO,cutClass=True)))
    option=next(o for o in s.claim_options[1] if o['kind']=='k' and o['face']==31)
    replies={i:dict(action_type='pass') for i,a in s.action_dict.items() if a}
    replies[1]=dict(action_type='vn_claim',target_tile=option['id']);act(s,replies)
    assert s.machine.phase==P.SPECIAL and not s.player_list[1].combination_tiles
    act(s,{0:dict(action_type='hu_special')})
    assert s.deferred_hu_settlements[0]['special']


def test_no_flower_bonus_declaration_pass_resumes_claim_once():
    s=turn(NORMAL[:-1]+(BAMBOO,));s.player_list[0].discarded_bonus=[ANY,DOT]
    s.player_list[1].hand_tiles=[31,31,11,12,13,14,15,16,21,22,23,41,42]
    s.open_action_window(s.discard(0,dict(TileId=BAMBOO,cutClass=True)))
    option=next(o for o in s.claim_options[1] if o['kind']=='k' and o['face']==31)
    replies={i:dict(action_type='pass') for i,a in s.action_dict.items() if a};replies[1]=dict(action_type='vn_claim',target_tile=option['id'])
    act(s,replies);act(s,{0:dict(action_type='pass')})
    assert s.machine.phase==P.DISCARD_ONLY and s.player_list[1].combination_tiles==['k31']
    assert s.player_list[0].discarded_bonus==[ANY,DOT,BAMBOO]


def test_claim_cannot_strand_un_discardable_jokers():
    s=turn(NORMAL);p=s.player_list[1]
    p.hand_tiles=[11,11,WIND,DRAGON]
    p.combination_tiles=['s22','s32','s15'];p.combination_mask=[[0,21,0,22,0,23],[0,31,0,32,0,33],[0,14,0,15,0,16]]
    p.huapai_list=[51]
    assert not any(o['kind']=='k' for o in s.build_claim_options(1,0,11))


def test_joker_only_draw_requires_replacement_or_finishes_at_dead_wall():
    s=turn([WIND,DRAGON]);p=s.player_list[0]
    p.combination_tiles=['s22','s32','s15','s18'];p.combination_mask=[[0,21,0,22,0,23],[0,31,0,32,0,33],[0,14,0,15,0,16],[0,17,0,18,0,19]]
    p.huapai_list=[51]
    s.open_action_window(s.begin_turn(0));assert s.action_dict[0]==['vn_replace']
    act(s);assert s.machine.phase!=P.END
    p.hand_tiles=[WIND,DRAGON];p.last_drawn_tile=DRAGON;p.has_draw_slot=True;s.tiles_list=[11]*26
    s.begin_turn(0);assert s.machine.phase==P.END
