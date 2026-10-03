"""The custom clear-thirteen book through authoritative action windows."""
import itertools
import pytest

from .test_branches import table, act, opening_flow
from .test_flow import make_state, open_round, assert_conservation
from .bot import choose_action
from .state_machine import HongKongPhase as P
from .action_check import rob_kong_actions
from ...game_calculation.hongkong.models import QINGZHANG_REMIX as REMIX, GAMETOWER, LIANHUISE, ORPHANS
from ...game_calculation.hongkong.test_hongkong_calculation import tiles
from ...room.hongkong_room import HongKongRoomValidator


@pytest.mark.parametrize("profile,starting",[(REMIX,1200),(GAMETOWER,0),(LIANHUISE,0)])
def test_starting_scores_reach_live_snapshots_and_replay(profile,starting):
    state=make_state(profile)
    assert [p.score for p in state.player_list]==[starting]*4
    open_round(state)
    assert state.game_record["game_title"]["starting_score"]==starting
    assert state.game_record["game_round"]["round_index_1"]["hongkong"]["start_scores"]==[starting]*4
    for seat in range(4):
        info=state.build_game_start_payload(seat)["game_info"]
        assert [p["score"] for p in info["players_info"]]==[starting]*4
        assert info["hongkong_info"]["starting_score"]==starting


@pytest.mark.parametrize("dealer_mode",["rotate","win_or_draw"])
@pytest.mark.parametrize("hands,finished,graces",[
    ([[0,1200,1200,2400],[0,1200,1200,2400]],False,[[],[]]),
    ([[-5,1200,1200,2405],[-1,1200,1200,2401]],True,[[0],[0]]),
    ([[-5,1200,1200,2405],[0,1200,1200,2400]],False,[[0],[]]),
    ([[-5,1200,1200,2405],[5,1200,1200,2395]],False,[[0],[]]),
    ([[-5,-10,1200,3615],[0,-5,1200,3605]],True,[[0,1],[1]]),
    ([[-5,1200,1200,2405],[0,-5,1200,3605],[0,0,1200,3600]],False,[[0],[1],[]]),
    ([[-5,1200,1200,2405],[0,1200,1200,2400],[-5,1200,1200,2405],[0,1200,1200,2400]],False,[[0],[],[0],[]]),
])
def test_one_recovery_hand_survives_rotation_and_repeat_and_is_not_spent_by_reconnect(dealer_mode,hands,finished,graces):
    state=make_state(REMIX,detailed_config={"dealer_mode":dealer_mode})
    state.max_round=4
    open_round(state)
    for index,scores in enumerate(hands):
        for p in state.player_list:
            p.score=scores[p.original_player_index]
        state.end_draw()
        for viewer in range(4):
            payload=state.build_final_settlement_payload(viewer)["show_result_info"]
            expected_end=finished and index==len(hands)-1
            assert payload["next_status"]==("match_end" if expected_end else "round_end_by_ready")
            assert payload["hongkong_info"]["negative_score_grace"]==graces[index]
            assert state.match_finishing is expected_end
            reconnect=state.restore_payloads(viewer)
            assert reconnect[0]["game_info"]["hongkong_info"]["negative_score_grace"]==graces[index]
        state.machine.transition(P.READY)
        state.finalize_round_recording()
        ticks=state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]
        assert ticks[-2][2]["negative_score_grace"]==graces[index]
        if index<len(hands)-1:
            state.advance_round_after_ready()
            open_round(state)
            assert sorted(state.negative_score_grace)==graces[index]
    assert state.match_finishing is finished


def test_grace_does_not_extend_the_scheduled_last_hand():
    state=make_state(REMIX,detailed_config={"dealer_mode":"rotate"})
    open_round(state)
    state.current_round=4
    state.player_list[0].score=-5
    state.player_list[1].score=2405
    state.end_draw()
    info=state.build_final_settlement_payload(0)["show_result_info"]
    assert info["next_status"]=="match_end"


def test_unrecovered_negative_score_ends_match_even_after_dealer_win():
    state,window=table(REMIX,hands={0:list(tiles("111m 222p 333s 55566z"))})
    state.player_list[0].score=2405
    state.player_list[1].score=-5
    state.negative_score_grace={1}
    act(state,window,**{"0":"hu_self"})
    info=state.build_final_settlement_payload(0)["show_result_info"]
    assert info["next_status"]=="match_end"
    assert state.player_list[1].score < -5
    assert sum(p.score for p in state.player_list)==4800


def test_new_minimum_qualification_controls_the_actual_claim_window():
    state,_=table(REMIX,hands={1:list(tiles("2224565m 123789p"))})
    assert not state.can_win(1,"discard",15,payer=0)
    state.dealer_streak=1
    assert state.can_win(1,"discard",15,payer=0)


def test_every_remix_room_switch_combination_is_canonical_and_independent():
    for claim,dealer,flags in itertools.product(("default","head_bump","multiple"),("default","rotate","win_or_draw"),itertools.product((False,True),repeat=3)):
        detail=dict(win_claim=claim,dealer_mode=dealer)
        detail.update(zip(("self_draw_only","liability_twelve","liability_limit"),flags))
        room=HongKongRoomValidator(room_name="魔改清章",game_round=1,round_timer=20,step_timer=5,sub_rule=REMIX,detailed_config=detail)
        assert room.detailed_config["flowers"]
        for key,value in detail.items(): assert room.detailed_config[key]==value
        assert set(detail)=={"win_claim","dealer_mode","self_draw_only","liability_twelve","liability_limit"}


@pytest.mark.parametrize("seat",[0,2])
@pytest.mark.parametrize("count",[7,8])
def test_all_initial_flowers_require_one_replacement_each_and_never_auto_win(seat,count):
    size=14 if seat==0 else 13
    hand=list(range(51,51+count))+list(tiles("123m 123p 1s"))[:size-count]
    state,window=opening_flow(REMIX,{seat:hand},tail=[45,44,43,42,41,39,38,37])
    for _ in range(count):
        assert window["player"]==seat
        assert window["actions"][seat]==["buhua"]
        before=len(state.player_list[seat].huapai_list)
        window=act(state,window,**{str(seat):"buhua"})
        assert len(state.player_list[seat].huapai_list)==before+1
    assert not state.deferred_hu_settlements
    assert len(state.player_list[seat].huapai_list)==count


def test_opening_flower_chain_finishes_current_seat_before_next_seat():
    state,window=opening_flow(REMIX,{
        0:[51]+list(tiles("123456m 123p 1234s")),
        1:[52]+list(tiles("456789p 456789s")),
    },tail=[46,47,53])
    for seat in (0,0,1):
        assert window["player"]==seat and window["actions"][seat]==["buhua"]
        window=act(state,window,**{str(seat):"buhua"})
    assert "riichi" not in window["actions"][0]
    assert [e["is_mo_buhua"] for e in state.domain_events if e["action"]=="buhua"]==[False,True,False]


@pytest.mark.parametrize("normal",[False,True])
def test_passed_tile_survives_own_draw_other_waits_remain_legal_and_own_river_is_permanent(normal):
    state,_=table(REMIX,hands={1:list(tiles("123456m 34p 55566z"))})
    assert state.can_win(1,"discard",22,payer=0)
    assert state.can_win(1,"discard",25,payer=0)
    state.remember_pass(1,22,payer=0)
    state.begin_new_draw(1,normal=normal)
    assert not state.can_win(1,"discard",22,payer=2)
    assert state.can_win(1,"discard",25,payer=2)
    state.player_list[1].discard_origin_tiles.append(25)
    assert not state.can_win(1,"discard",25,payer=2)
    state.player_list[1].discard_win_lockout_tiles.clear()
    assert state.can_win(1,"discard",22,payer=2)
    assert not state.can_win(1,"discard",25,payer=2)
    state.remember_pass(1,22,source="self_draw")
    assert state.can_win(1,"discard",22,payer=2)


def test_north_response_closes_the_circuit_after_all_pass_decisions():
    state,window=table(REMIX,actor=3,hands={
        1:list(tiles("123456m 34p 55566z")),
        3:list(tiles("123789s 678m 1134z 2p")),
    })
    state.player_list[0].discard_win_lockout_tiles.add(19)
    window=act(state,window,**{"3":dict(action_type="cut",TileId=22)})
    assert state._remix_circuit_ends and "hu" in window["actions"][1]
    act(state,window)
    assert not state._remix_circuit_ends
    assert all(not p.discard_win_lockout_tiles for p in state.player_list)
    assert state.can_win(1,"discard",22,payer=2)


@pytest.mark.parametrize("mode",["default","rotate","win_or_draw"])
@pytest.mark.parametrize("ready",[False,True])
@pytest.mark.parametrize("winner",[None,0,1])
def test_dealer_repeats_only_on_win_or_tenpai_draw_and_streak_resets(mode,ready,winner):
    hand=list(tiles("123456m 789p 5556z" if ready else "123456m 789p 1234z"))
    state,_=table(REMIX,actor=1,hands={0:hand},detailed_config={"dealer_mode":mode})
    state.current_round=4;state.dealer_streak=2
    state.end_draw()
    draw_keep=mode=="win_or_draw" or mode=="default" and ready
    assert state.match_finishing is not draw_keep
    if winner is not None: state.deferred_hu_settlements=[dict(winner=winner)]
    keep=mode!="rotate" and (winner==0 if winner is not None else draw_keep)
    state.machine.transition(P.READY)
    previous_index=state.round_index
    state.advance_round_after_ready()
    assert state.current_round==(4 if keep else 5)
    assert state.round_index==previous_index+1
    assert state.dealer_streak==(3 if keep else 0)
    state.initialize_round()
    assert all(not p.meld_suppliers and not p.discard_win_lockout_tiles for p in state.player_list)


@pytest.mark.parametrize("enabled",[False,True])
@pytest.mark.parametrize("suppliers",[[0,0,0],[0,2,0],[None,0,0]])
def test_twelve_liability_requires_four_claims_from_the_same_supplier(enabled,suppliers):
    state,window=table(REMIX,melds={1:["s12","s25","s38"]},hands={1:[11,11,45,45]},
        detailed_config={"liability_twelve":enabled})
    state.player_list[1].meld_suppliers=list(suppliers)
    window=act(state,window,**{"0":dict(action_type="cut",TileId=11)})
    act(state,window,**{"1":"peng"})
    p=state.player_list[1]
    assert p.meld_suppliers==suppliers+[0]
    assert (p.liability_payer==0)==(enabled and suppliers==[0,0,0])
    if p.liability_payer is not None: assert p.liability_kind=="twelve"


@pytest.mark.parametrize("enabled",[False,True])
@pytest.mark.parametrize("melds,hand,tile,action",[
    (["k45","k46"],"77z 234p 22s",47,"peng"),
    (["k41","k42","k43"],"44z 22s",44,"peng"),
    (["g11","G25","g38"],"444z 2s",44,"gang"),
    (["s13","s13","s13"],"24m 22s",13,"chi_mid"),
    (["k13","k14","k15"],"66m 22s",16,"peng"),
])
def test_limit_liability_is_assigned_by_the_claim_that_guarantees_the_pattern(enabled,melds,hand,tile,action):
    # Reserve the claimed tile for East without overusing any meld tile.
    actor=list(tiles("123789p 123456s 5z"))+[tile]
    state,window=table(REMIX,melds={1:melds},hands={0:actor,1:list(tiles(hand))},tail=47,
        detailed_config={"liability_limit":enabled,"liability_twelve":False})
    state.player_list[1].meld_suppliers=[2]*len(melds)
    window=act(state,window,**{"0":dict(action_type="cut",TileId=tile)})
    assert action in window["actions"][1]
    act(state,window,**{"1":action})
    p=state.player_list[1]
    assert (p.liability_payer==0)==enabled
    if enabled: assert p.liability_kind=="limit"


@pytest.mark.parametrize("self_only",[False,True])
def test_concealed_kong_is_public_with_outer_backs_and_only_orphans_can_rob(self_only):
    state,window=table(REMIX,hands={0:[47]*4+list(tiles("234m 123p 123s 5z")),1:sorted(ORPHANS-{47})+[11]},
        tail=46,detailed_config={"self_draw_only":self_only})
    window=act(state,window,**{"0":dict(action_type="angang",target_tile=47)})
    assert ("hu" in window["actions"][1]) is (not self_only)
    if not self_only:
        act(state,window,**{"1":"hu"})
        assert state.player_list[0].hand_tiles.count(47)==3
        win=state.deferred_hu_settlements[0]
        assert "HK|rob_kong|2|抢杠|番" in win["fan_ids"]
        assert sum(win["score_changes"])==0 and win["score_changes"][1]==win["points"]*3
    else:
        act(state,window)
        visible=state.build_game_start_payload(1)["game_info"]["players_info"][0]
        assert visible["combination_tiles"]==["G47"]
        assert visible["combination_mask"]==[[2,47,0,47,0,47,2,47]]
        assert all(p.score==1200 for p in state.player_list)


def test_standard_hand_can_rob_added_kong_but_not_concealed_kong():
    state,_=table(REMIX,hands={1:list(tiles("123456m 34p 55566z"))})
    assert "hu" not in rob_kong_actions(state,0,22,concealed=True)[1]
    assert "hu" in rob_kong_actions(state,0,22,concealed=False)[1]


@pytest.mark.parametrize("profile",[REMIX,GAMETOWER,LIANHUISE])
@pytest.mark.parametrize("seed",[17,241,902])
@pytest.mark.parametrize("self_only",[False,True])
def test_complete_hands_conserve_tiles_points_and_preserve_profile_in_replay_and_reconnect(profile,seed,self_only):
    state=make_state(profile,seed=seed,detailed_config={"self_draw_only":self_only})
    window=open_round(state)
    for _ in range(1000):
        assert_conservation(state)
        if state.machine.phase==P.END: break
        decisions={i:choose_action(state,i,a) for i,a in window["actions"].items() if a}
        window=state.apply_action_results(window,decisions)
    else: pytest.fail("Physical wall did not terminate")
    state.apply_deferred_score_changes();state.machine.transition(P.READY);state.finalize_round_recording()
    title=state.game_record["game_title"]
    assert title["sub_rule"]==profile
    assert title["rule_version"]==state.rules.version
    assert title["detailed_config"]["self_draw_only"]==self_only
    for seat in range(4):
        snapshot=state.build_game_start_payload(seat)["game_info"]
        assert snapshot["sub_rule"]==profile and snapshot["hepai_limit"]==state.rules.minimum_fan
        for i,p in enumerate(snapshot["players_info"]):
            if i!=seat and not state.player_list[i].is_hu: assert p["hand_tiles"] is None
    assert_conservation(state)
