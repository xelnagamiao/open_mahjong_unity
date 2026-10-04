import asyncio
from collections import Counter
from copy import deepcopy

import pytest

from .YixingGameState import YixingGameState
from .actions import claim_actions, forbidden_after_chow, kong_tiles, legal_cuts
from .bot import choose_action
from .state_machine import Phase as P, StateMachine, TRANSITIONS
from ...game_calculation.yixing.rules import TILES, FLOWERS, RULE_VERSION, parse_meld

PLAIN = [11,12,13,14,15,16,21,22,23,34,35,36,28,28]
OTHER = [11,13,15,17,19,21,24,27,29,32,35,38,42]


def state(seed=1, **config):
    room = YixingGameState._default_room_data()
    room.update(random_seed=seed,allow_spectator=False,claim_protection=False,bot_speed="instant",**config)
    return YixingGameState(room_data=room,calculation_service=object())


def start(s):
    s.start_game_recording()
    s.initialize_round()
    s.start_round_recording()
    return s.open_action_window(s.opening_window())


def physical(s):
    count = Counter(s.tiles_list)
    for p in s.player_list:
        count.update(p.hand_tiles)
        count.update(p.discard_tiles)
        count.update(p.huapai_list)
        count.update(p.won_tiles)
        for code in p.combination_tiles:
            count.update(parse_meld(code).tiles)
    return count


def act(s, actor=None, action=None, **fields):
    responses = {i:{"action_type":"pass"} for i,a in s.live_pending_window["actions"].items() if a}
    if actor is not None:
        responses[actor] = dict(action_type=action,**fields)
    return s.apply_action_results(s.live_pending_window,responses)


def turn(hand=PLAIN, index=0, **config):
    s = state(**config)
    s.initialize_round()
    for p in s.player_list:
        p.hand_tiles=list(OTHER)
    s.tiles_list = [19]*25
    s.player_list[index].hand_tiles=list(hand)
    s.player_list[index].has_draw_slot=True
    s.open_action_window(s.begin_turn(index))
    return s


def river(s, actor, tile):
    s.player_list[actor].discard_tiles.append(tile)
    s.player_list[actor].discard_origin_tiles.append(tile)
    s.current_player_index=actor
    return s.open_action_window(s._window(P.RESPONSE,actor,tile,claim_actions(s,actor,tile)))


def set_melds(s,index,codes):
    p=s.player_list[index]
    p.combination_tiles=list(codes)
    p.combination_mask=[[v for n,t in enumerate(parse_meld(c).tiles) for v in (2 if c[0]=="G" else 1 if n==0 else 0,t)] for c in codes]


@pytest.mark.parametrize("seed,seven", [(1,False),(2,False),(9,False),(41,False),(95,False),(2,True),(41,True)])
def test_whole_offline_hand_conserves_144_physical_tiles_and_points(seed,seven):
    s=state(seed,detailed_config={"seven_pairs":seven})
    window=start(s)
    expected=Counter({t:4 for t in TILES})+Counter(FLOWERS)
    assert physical(s)==expected
    for step in range(500):
        if s.machine.phase==P.END:
            break
        responses={i:choose_action(s,i,a) for i,a in window["actions"].items() if a}
        window=s.apply_action_results(window,responses)
        assert physical(s)==expected
    else:
        pytest.fail("finite wall did not terminate")
    s.apply_deferred_score_changes()
    old=[p.score for p in s.player_list]
    s.apply_deferred_score_changes()
    assert [p.score for p in s.player_list]==old and sum(old)==0
    s.machine.transition(P.READY)
    s.finalize_round_recording()
    assert s.game_record["game_title"]["rule_version"]==RULE_VERSION
    assert s.game_record["game_title"]["detailed_config"]["seven_pairs"]==seven
    ticks=s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[-1]==["end"] and ticks[-2][:2]==["yixing","state"]
    assert sum(t[0]=="bu" for t in ticks) <= 8


def test_no_global_flower_round_only_acting_player_replaces_one_flower_per_click():
    s=state()
    s.initialize_round()
    for p in s.player_list:
        p.hand_tiles=list(OTHER)
    s.player_list[0].hand_tiles += [51]
    s.player_list[1].hand_tiles[-1]=52
    s.tiles_list=[19,18,17,53]
    s.open_action_window(s.opening_window())
    assert s.replacement_pending==(0,51,False)
    assert all(not p.huapai_list for p in s.player_list)
    act(s,0,"buhua")
    assert s.player_list[0].huapai_list==[51]
    assert s.replacement_pending==(0,53,False)
    act(s,0,"buhua")
    assert s.player_list[0].huapai_list==[51,53]
    assert s.machine.phase==P.TURN and s.current_player_index==0
    assert [len(p.hand_tiles) for p in s.player_list]==[14,13,13,13]
    assert [p.has_draw_slot for p in s.player_list]==[True,False,False,False]
    assert not s.player_list[1].huapai_list and 52 in s.player_list[1].hand_tiles
    actions=[e["action"] for e in s.domain_events]
    assert actions==["buhua","deal_buhua_tile"]*2
    act(s,0,"cut",TileId=17)
    act(s)
    assert s.machine.phase==P.REPLACEMENT and s.current_player_index==1
    assert s.action_dict[1]==["buhua"]
    act(s,1,"buhua")
    assert s.player_list[1].huapai_list==[52] and s.current_player_index==1
    assert s.machine.phase==P.TURN


def test_early_peng_with_unreplaced_flowers_forces_buhua_before_discard():
    s=turn()
    s.player_list[2].hand_tiles=[11,11,51]+OTHER[1:11]
    s.player_list[2].has_draw_slot=False
    river(s,0,11)
    act(s,2,"peng")
    assert s.machine.phase==P.REPLACEMENT and s.action_dict[2]==["buhua"]
    assert not s.action_dict[1]
    act(s,2,"buhua")
    assert s.machine.phase==P.DISCARD_ONLY and s.action_dict[2]==["cut"]
    assert s.player_list[2].huapai_list==[51]


def test_kong_flower_replacement_never_retains_kong_open_factor():
    s=turn([11]*4+[21,22,23,34,35,36,28,28,41,41])
    s.tiles_list=[19,45,51]
    act(s,0,"angang",target_tile=11)
    assert not any(s.action_dict.values())
    act(s)
    assert s.machine.phase==P.REPLACEMENT and s.player_list[0].after_kong
    act(s,0,"buhua")
    assert s.machine.phase==P.TURN and not s.player_list[0].after_kong
    assert s.player_list[0].draw_kind=="flower" and s.player_list[0].hand_tiles[-1]==45


def test_last_flower_is_revealed_before_exhaustive_draw():
    s=turn()
    s.player_list[0].hand_tiles[-1]=51
    s.tiles_list=[]
    s.open_action_window(s._replacement_window(0,51))
    act(s,0,"buhua")
    assert s.machine.phase==P.END
    assert s.player_list[0].huapai_list==[51]
    assert s.round_changes==[0]*4


@pytest.mark.parametrize("action,tile,size,expected", [
    ("chi_mid",15,13,{15}), ("chi_left",16,13,{13,16}),
    ("chi_right",13,13,{13,16}), ("chi_left",13,13,{13}),
    ("chi_right",17,13,{17}), ("chi_mid",15,4,set()),
    ("chi_left",16,4,set()), ("chi_right",13,4,set()), ("peng",15,13,set()),
])
def test_kuikae_and_big_single_exception(action,tile,size,expected):
    assert forbidden_after_chow(action,tile,size)==expected


def test_chow_is_only_next_seat_and_cannot_cross_suit_or_use_honors():
    s=turn()
    for p in s.player_list:
        p.hand_tiles=[18,19,21,22,23,24,25,41,42,43,44,45,46]
    actions=claim_actions(s,0,23)
    assert all(a in actions[1] for a in ("chi_left","chi_mid","chi_right"))
    assert all(not any(a.startswith("chi") for a in actions[i]) for i in (0,2,3))
    assert not any(a.startswith("chi") for a in claim_actions(s,0,41)[1])
    assert "chi_mid" not in claim_actions(s,0,19)[1]
    assert "chi_left" not in claim_actions(s,0,21)[1]


def test_chow_restricts_actual_cut_but_four_tile_hand_can_pass_through():
    s=turn()
    s.player_list[1].hand_tiles=[14,15,13,16,21,22,23,34,35,36,28,28,29]
    river(s,0,13)
    act(s,1,"chi_right")
    assert s.machine.phase==P.DISCARD_ONLY and s.player_list[1].combination_tiles==["s14"]
    assert {13,16}.isdisjoint(legal_cuts(s,1))
    with pytest.raises(ValueError):
        act(s,1,"cut",TileId=13)
    act(s,1,"cut",TileId=29)
    assert not s.player_list[1].forbidden_discards
    s=turn()
    set_melds(s,1,["s22","s25","s35"])
    s.player_list[1].hand_tiles=[14,15,13,16]
    river(s,0,13)
    act(s,1,"chi_right")
    assert legal_cuts(s,1)=={13,16}
    act(s,1,"cut",TileId=13)
    assert len(s.player_list[1].hand_tiles)==1


def test_peng_wins_over_chow_and_win_wins_over_peng():
    s=turn()
    s.player_list[1].hand_tiles=[14,15]+OTHER[:-2]
    s.player_list[2].hand_tiles=[13,13]+OTHER[:-2]
    window=river(s,0,13)
    results={i:{"action_type":"pass"} for i,a in window["actions"].items() if a}
    results.update({1:{"action_type":"chi_right"},2:{"action_type":"peng"}})
    s.apply_action_results(window,results)
    assert s.current_player_index==2 and s.player_list[2].combination_tiles==["k13"]
    s=turn()
    s.player_list[1].hand_tiles=PLAIN[:-1]
    s.player_list[2].hand_tiles=PLAIN[:-1]
    s.player_list[3].hand_tiles=[28,28]+OTHER[:-2]
    window=river(s,0,28)
    results={1:{"action_type":"hu"},2:{"action_type":"hu"},3:{"action_type":"peng"}}
    s.apply_action_results(window,results)
    assert s.round_settlement["winner"]==1 and not s.player_list[2].is_hu
    assert s.player_list[1].won_tiles==[28] and not s.player_list[0].discard_tiles
    assert s.round_changes==[-4,4,0,0]


def test_passed_win_and_pung_are_tile_specific_until_next_own_draw():
    s=turn()
    s.player_list[1].hand_tiles=PLAIN[:-1]
    window=river(s,0,28)
    # Resolve the decline without advancing that player's draw yet.
    s._remember_responses(window,{1:{"action_type":"pass"}})
    assert not s.can_win(1,"discard",28)
    s.player_list[1].hand_tiles=[28,28]+OTHER[:-2]
    window["actions"][1]=["peng","pass"]
    s._remember_responses(window,{1:{"action_type":"pass"}})
    assert "peng" not in claim_actions(s,0,28)[1]
    assert s.player_list[1].passed_wins=={28} and s.player_list[1].passed_pungs=={28}
    s.open_action_window(s.draw_for(1))
    assert not s.player_list[1].passed_wins and not s.player_list[1].passed_pungs


@pytest.mark.parametrize("action", ["angang","jiagang"])
def test_kongs_commit_only_after_window_and_take_from_tail(action):
    s=turn([11]*4+[21,22,23,34,35,36,28,28,41,41] if action=="angang" else [11,21,22,23,34,35,36,28,28,41,41])
    if action=="jiagang":
        set_melds(s,0,["k11"])
        s.open_action_window(s.begin_turn(0))
    old=list(s.player_list[0].hand_tiles)
    s.tiles_list=[18,17,16]
    act(s,0,action,target_tile=11)
    assert s.machine.phase==P.KONG and s.player_list[0].hand_tiles==old
    act(s)
    assert s.player_list[0].combination_tiles==["G11" if action=="angang" else "g11"]
    assert s.player_list[0].after_kong and s.player_list[0].hand_tiles[-1]==16
    assert s.tiles_list==[18,17]


def test_direct_kong_cannot_be_robbed_and_flowerless_kong_is_legal():
    s=turn()
    s.player_list[1].hand_tiles=[11]*3+OTHER[1:11]
    river(s,0,11)
    act(s,1,"gang")
    assert s.machine.phase==P.TURN
    assert s.player_list[1].combination_tiles==["g11"]
    assert s.player_list[1].after_kong


def test_added_kong_rob_keeps_original_pung_and_pays_three_once():
    s=turn([28,11,12,13,21,22,23,34,35,36,41])
    set_melds(s,0,["k28"])
    s.player_list[1].hand_tiles=[11,12,13,21,22,23,34,35,36,26,27,41,41]
    s.open_action_window(s.begin_turn(0))
    act(s,0,"jiagang",target_tile=28)
    assert "hu" in s.action_dict[1]
    act(s,1,"hu")
    assert s.player_list[0].combination_tiles==["k28"]
    assert 28 not in s.player_list[0].hand_tiles
    assert s.round_changes==[-12,12,0,0]
    assert s.player_list[1].won_tiles==[28]
    assert not any(e["action"]=="jiagang" for e in s.domain_events)


def test_no_kong_without_a_replacement_tile():
    s=turn([11]*4+OTHER[:10])
    s.tiles_list=[]
    assert not kong_tiles(s,0,"angang")
    s.open_action_window(s.begin_turn(0))
    assert "angang" not in s.action_dict[0]


def test_sea_tile_four_passes_leave_it_hidden_and_dealer_repeats():
    s=turn()
    s.machine.transition(P.RESPONSE)
    s.tiles_list=[28]
    s.open_action_window(s.draw_for(1))
    for index in (1,2,3,0):
        assert s.machine.phase==P.LAST_DRAW and s.current_player_index==index
        act(s,index,"pass")
    assert s.machine.phase==P.END and s.tiles_list==[28]
    assert s.next_dealer==0 and not s.match_finishing


def test_sea_take_selfdraw_double_or_forced_physical_tsumogiri_and_ron_triple():
    s=turn()
    s.player_list[1].hand_tiles=PLAIN[:-1]
    s.machine.transition(P.RESPONSE)
    s.tiles_list=[28]
    s.open_action_window(s.draw_for(1))
    act(s,1,"yixing_last_draw")
    assert s.can_win(1,"self_draw",28)
    assert legal_cuts(s,1)=={28}
    with pytest.raises(ValueError):
        act(s,1,"cut",TileId=28,cutClass=False)
    with pytest.raises(ValueError):
        act(s,1,"cut",TileId=11)
    act(s,1,"hu_self")
    assert s.round_changes==[-8,24,-8,-8]
    s=turn()
    s.player_list[1].hand_tiles=OTHER[:]
    s.player_list[2].hand_tiles=PLAIN[:-1]
    s.machine.transition(P.RESPONSE)
    s.tiles_list=[28]
    s.open_action_window(s.draw_for(1))
    act(s,1,"yixing_last_draw")
    act(s,1,"cut",TileId=28,cutClass=True)
    assert s.action_dict[2]==["hu","pass"]
    act(s,2,"hu")
    assert s.round_changes==[0,-12,12,0]


@pytest.mark.parametrize("winner,round_number,next_dealer,finished", [(0,4,0,False),(1,4,1,True),(3,2,1,False)])
def test_dealer_repeat_and_next_dealer_is_old_dealers_next_seat(winner,round_number,next_dealer,finished):
    s=turn(index=winner)
    s.current_round=round_number
    act(s,winner,"hu_self")
    assert s.next_dealer==next_dealer and s.match_finishing==finished
    before=[p.user_id for p in s.player_list]
    s.machine.transition(P.READY)
    if finished:
        with pytest.raises(RuntimeError):
            s.advance_round_after_ready()
    else:
        s.advance_round_after_ready()
        assert [p.user_id for p in s.player_list]==(before[1:]+before[:1] if next_dealer else before)
        assert s.current_round==round_number+next_dealer and s.round_index==2


@pytest.mark.parametrize("data", [dict(TileId=51),dict(TileId=True),dict(TileId=28,cutIndex=50),
    dict(TileId=28,cutIndex=True),dict(TileId=28,cutClass=1),dict(TileId=11,cutClass=True),
    dict(TileId=28,cutClass=True,cutIndex=1),dict(TileId=28,cutIndex=0)])
def test_forged_cuts_are_rejected_atomically(data):
    s=turn()
    old=deepcopy(vars(s.player_list[0]))
    old["record_counter"]=vars(old["record_counter"])
    with pytest.raises(ValueError):
        act(s,0,"cut",**data)
    actual=dict(vars(s.player_list[0]))
    actual["record_counter"]=vars(actual["record_counter"])
    assert actual==old


def test_stale_window_duplicate_seat_client_settlement_and_invalid_kong_rejected():
    s=turn()
    window=s.live_pending_window
    with pytest.raises(ValueError):
        s.apply_action_results(window,{},settlements={0:{"points":999}})
    with pytest.raises(ValueError):
        s.apply_action_results(window,{})
    with pytest.raises(ValueError):
        act(s,0,"angang",target_tile=11)
    act(s,0,"cut",TileId=28)
    with pytest.raises(ValueError):
        s.apply_action_results(window,{0:{"action_type":"cut","TileId":11}})


def test_machine_rejects_all_unlisted_transitions():
    for origin in P:
        for target in P:
            m=StateMachine()
            m.phase=origin
            if target==origin or target in TRANSITIONS[origin]:
                m.transition(target)
                assert m.phase==target
            else:
                with pytest.raises(RuntimeError):
                    m.transition(target)
