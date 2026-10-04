"""Room switches exercise authoritative actions, payments, replay and resets."""
import itertools
from dataclasses import replace

import pytest

from .test_flow import make_state, open_round, assert_conservation
from .test_branches import table, act, opening_flow
from .bot import choose_action
from .state_machine import HongKongPhase as P
from .action_check import claim_actions, ready_discards
from .hints import private_hints
from ...game_calculation.hongkong.models import HongKongRules, PROFILES, ORPHANS
from ...game_calculation.hongkong.test_hongkong_calculation import tiles
from ...room.hongkong_room import HongKongRoomValidator

PROFILES_AND_VERSIONS = [(PROFILES[0],"gametower"),(PROFILES[1],"gametower"),(PROFILES[1],"lianhuise"),(PROFILES[2],"gametower")]
LHS = {"new13_version":"lianhuise"}


def test_every_room_switch_combination_survives_validation_and_normalization():
    for profile,version in PROFILES_AND_VERSIONS:
        flowers = (False,True) if profile==PROFILES[0] or version=="lianhuise" else (profile==PROFILES[2],)
        for claim,dealer,flags,flower in itertools.product(("default","head_bump","multiple"),("default","rotate","win_or_draw"),itertools.product((False,True),repeat=5),flowers):
            detail=dict(new13_version=version,flowers=flower,win_claim=claim,dealer_mode=dealer,liability_limit=True)
            detail.update(zip(("self_draw_only","liability_twelve","liability_dragons","liability_kong","new13_full_shoot"),flags))
            room=HongKongRoomValidator(room_name="馆规测试",game_round=1,round_timer=20,step_timer=5,sub_rule=profile,detailed_config=detail)
            assert room.detailed_config==detail
            assert HongKongRules.from_room(room.model_dump()).room_config()==detail


@pytest.mark.parametrize("key",["new13_version","win_claim","dealer_mode","flowers","self_draw_only","liability_twelve","liability_dragons","liability_kong","new13_full_shoot"])
@pytest.mark.parametrize("value",[None,1,[],{},"unsupported"])
def test_invalid_setting_values_are_rejected_at_room_boundary(key,value):
    with pytest.raises(ValueError):
        HongKongRoomValidator(room_name="验证",game_round=1,round_timer=20,step_timer=5,sub_rule=PROFILES[1],detailed_config={**LHS,key:value})


@pytest.mark.parametrize("profile,version",PROFILES_AND_VERSIONS)
@pytest.mark.parametrize("claim",["default","head_bump","multiple"])
@pytest.mark.parametrize("self_only",[False,True])
def test_discard_multiwinner_switch_and_self_draw_only_authority(profile,version,claim,self_only):
    first=list(tiles("123456789m 5551z")); second=list(tiles("123456789p 6661z"))
    actor=list(tiles("234567789s 22331z"))
    if profile==PROFILES[2]:
        first+=list(tiles("123s"));second+=list(tiles("456s"));actor+=list(tiles("234m"))
    detail=dict(new13_version=version,win_claim=claim,self_draw_only=self_only)
    state,window=table(profile,hands={0:actor,1:first,2:second},detailed_config=detail)
    state.tips=True
    assert all(h['ron'] is False for hints in private_hints(state,1).values() for h in hints) if self_only else True
    window=act(state,window,**{"0":dict(action_type="cut",TileId=41)})
    if self_only:
        assert all("hu" not in a for a in window["actions"].values())
        before=list(state.player_list[0].discard_tiles)
        with pytest.raises(ValueError): state.settle_winners([1],"discard",41,payer=0)
        assert state.player_list[0].discard_tiles==before
        return
    assert "hu" in window["actions"][1] and "hu" in window["actions"][2]
    act(state,window,**{"1":"hu","2":"hu"})
    multiple=claim=="multiple" or claim=="default" and profile==PROFILES[2]
    assert [r["winner"] for r in state.deferred_hu_settlements]==([1,2] if multiple else [1])
    assert state.won_tiles==[41]
    state.apply_deferred_score_changes(); assert_conservation(state)


@pytest.mark.parametrize("profile,version",PROFILES_AND_VERSIONS)
@pytest.mark.parametrize("dealer",["default","rotate","win_or_draw"])
@pytest.mark.parametrize("winner",[None,0,1])
def test_dealer_override_round_transition_and_final_draw(profile,version,dealer,winner):
    state=make_state(profile,detailed_config=dict(new13_version=version,dealer_mode=dealer))
    open_round(state); state.current_round=4
    state.end_draw()
    repeats=dealer=="win_or_draw" or dealer=="default" and (profile==PROFILES[2] or version=="lianhuise")
    assert state.match_finishing is (not repeats)
    if winner is not None: state.deferred_hu_settlements=[dict(winner=winner)]
    state.machine.transition(P.READY)
    old_seed_index=state.round_index
    state.advance_round_after_ready()
    assert state.round_index==old_seed_index+1
    assert state.current_round==(4 if repeats and winner in (None,0) else 5)
    assert state.machine.phase==P.STARTING


def test_sixteen_final_draw_collects_outstanding_pulls_once_with_rotated_seats():
    state=make_state(PROFILES[2],detailed_config={"dealer_mode":"rotate"})
    open_round(state)
    state.advance_dealer_after_round()
    state.current_round=4
    state.round_start_scores=[p.score for p in state.player_list]
    state.ledger.apply_win({(0,2):30,(3,1):12},winners=[0,3])
    expected=[{0:30,1:-12,2:-30,3:12}[p.original_player_index] for p in state.player_list]
    before=[p.score for p in state.player_list]
    state.end_draw()
    assert state.match_finishing and not state.ledger.debts
    assert [p.score-s for p,s in zip(state.player_list,before)]==expected
    payload=state.build_final_settlement_payload(0)["show_result_info"]
    assert payload["next_status"]=="match_end"
    assert list(payload["score_changes"].values())==expected
    assert state.domain_events[-1]["reason"]=="终局收账"
    state.end_draw();state.apply_deferred_score_changes()
    assert [p.score-s for p,s in zip(state.player_list,before)]==expected


@pytest.mark.parametrize("profile,version",PROFILES_AND_VERSIONS)
def test_multiple_setting_never_allows_two_self_draw_winners(profile,version):
    state=make_state(profile,detailed_config={"new13_version":version,"win_claim":"multiple"})
    open_round(state)
    with pytest.raises(ValueError,match="one winner"):
        state.settle_winners([0,1],"self_draw",11)
    assert not state.deferred_hu_settlements


@pytest.mark.parametrize("claim",["default","head_bump","multiple"])
def test_lianhuise_thirteen_orphans_take_priority_over_nearer_normal_win(claim):
    state,window=table(PROFILES[1],hands={
        0:list(tiles("123456m 123p 234s 21z")),
        1:list(tiles("123456789p 5551z")),
        2:sorted(ORPHANS-{41})+[11]},detailed_config={**LHS,"win_claim":claim})
    window=act(state,window,**{"0":dict(action_type="cut",TileId=41)})
    assert "hu" in window["actions"][1] and "hu" in window["actions"][2]
    act(state,window,**{"1":"hu","2":"hu"})
    assert [r["winner"] for r in state.deferred_hu_settlements]==([2,1] if claim=="multiple" else [2])


@pytest.mark.parametrize("enabled",[False,True])
@pytest.mark.parametrize("profile,version",PROFILES_AND_VERSIONS[:3])
def test_twelve_liability_switch_changes_claim_responsibility(profile,version,enabled):
    state,window=table(profile,melds={1:["s12","s25","s38"]},hands={1:[11,11,45,45]},
        detailed_config=dict(new13_version=version,liability_twelve=enabled))
    window=act(state,window,**{"0":dict(action_type="cut",TileId=11)})
    act(state,window,**{"1":"peng"})
    assert (state.player_list[1].liability_payer is not None)==enabled


@pytest.mark.parametrize("enabled",[False,True])
def test_dragon_liability_switch_and_priority_over_existing_twelve(enabled):
    state,window=table(PROFILES[1],melds={1:["k45","k46"]},hands={
        0:list(tiles("123m 123p 123s 11227z")),1:[47,47,22,23,24,32,32]},
        detailed_config={**LHS,"liability_dragons":enabled})
    state.player_list[1].liability_payer=3
    state.player_list[1].liability_kind="twelve"
    window=act(state,window,**{"0":dict(action_type="cut",TileId=47)})
    act(state,window,**{"1":"peng"})
    assert state.player_list[1].liability_payer==(0 if enabled else 3)
    assert state.player_list[1].liability_kind==("dragons" if enabled else "twelve")


def test_twelve_does_not_replace_big_dragons_payer_and_follow_exception():
    state,window=table(PROFILES[1],melds={1:["s12","s25","s38"]},hands={1:[11,11,45,45]},detailed_config=LHS)
    p=state.player_list[1];p.liability_payer=3;p.liability_kind="dragons"
    window=act(state,window,**{"0":dict(action_type="cut",TileId=11)})
    act(state,window,**{"1":"peng"})
    assert p.liability_payer==3
    state,window=table(PROFILES[1],melds={1:["s12","s25","s38"]},hands={1:[11,11,45,45]},detailed_config=LHS)
    # Historical discard log is distinct from physical river tiles that can be called.
    state.discard_log=[(1,11)]
    window=act(state,window,**{"0":dict(action_type="cut",TileId=11)})
    act(state,window,**{"1":"peng"})
    assert state.player_list[1].liability_payer is None


@pytest.mark.parametrize("enabled",[False,True])
@pytest.mark.parametrize("fresh",[False,True])
def test_fresh_open_kong_liability_only_applies_to_immediate_kong_draw(enabled,fresh):
    state,window=table(PROFILES[1],hands={1:list(tiles("111m 234p 567s 5556z"))},tail=46,
        detailed_config={**LHS,"liability_kong":enabled})
    if not fresh: state.discard_log=[(2,11)]
    window=act(state,window,**{"0":dict(action_type="cut",TileId=11)})
    window=act(state,window,**{"1":"gang"})
    assert "hu_self" in window["actions"][1]
    act(state,window,**{"1":"hu_self"})
    changes=state.deferred_hu_settlements[0]["score_changes"]
    assert (changes[2]==changes[3]==0)==(enabled and fresh)
    assert sum(changes)==0


def test_lianhuise_opening_replaces_new_flowers_before_moving_to_next_seat():
    state,window=opening_flow(PROFILES[1],{
        0:[51]+list(tiles("123456m 123p 1234s")),1:[52]+list(tiles("456789p 456789s")),
    },tail=[46,47,53],detailed_config=LHS)
    for seat in (0,0,1):
        assert window["player"]==seat and window["actions"][seat]==["buhua"]
        window=act(state,window,**{str(seat):"buhua"})
    assert [e["is_mo_buhua"] for e in state.domain_events if e["action"]=="buhua"]==[False,True,False]
    assert "riichi" not in window["actions"][0]


@pytest.mark.parametrize("winner",[0,2])
@pytest.mark.parametrize("count",[7,8])
@pytest.mark.parametrize("decline",[False,True])
def test_lianhuise_flower_win_choice_and_metadata(winner,count,decline):
    size=14 if winner==0 else 13
    hand=list(range(51,51+count))+[11,12,13,21,22,23,31][:size-count]
    state,window=opening_flow(PROFILES[1],{winner:hand},tail=[45,44,43,42,41,39,38,37],detailed_config=LHS)
    assert state.machine.phase==P.FLOWER and window["player"]==winner
    window=act(state,window,**{str(winner):"pass" if decline else "hu_self"})
    if decline:
        assert not state.deferred_hu_settlements
        assert ("seven" if count==7 else "eight") in state.player_list[winner].flower_choice_declined
        assert all(p.score==0 for p in state.player_list)
    else:
        win=state.deferred_hu_settlements[0]
        assert win["flower_win"]==("seven" if count==7 else "eight")
        assert win["points"]==(13 if winner==0 else 3 if count==7 else 8)
        if winner==0: assert win["fan_ids"]==["HK|flower_heavenly|13|天和"]
        info=state.build_final_settlement_payload(winner)["show_result_info"]
        assert info["hepai_tile"] is None and info["hongkong_flower_win"]==win["flower_win"]
    assert_conservation(state)


@pytest.mark.parametrize("self_only",[False,True])
def test_orphans_rob_concealed_kong_and_face_up_concealed_kong(self_only):
    state,window=table(PROFILES[1],hands={0:[47]*4+list(tiles("234m 123p 123s 5z")),1:sorted(ORPHANS-{47})+[11]},
        tail=46,detailed_config={**LHS,"self_draw_only":self_only})
    window=act(state,window,**{"0":dict(action_type="angang",target_tile=47)})
    assert ("hu" in window["actions"][1]) is (not self_only)
    if not self_only:
        act(state,window,**{"1":"hu"})
        assert state.player_list[0].hand_tiles.count(47)==3
        assert state.deferred_hu_settlements[0]["score_changes"]==[-384,384,0,0]
    else:
        act(state,window)
        visible=state.build_game_start_payload(1)["game_info"]["players_info"][0]
        assert visible["combination_tiles"]==["G47"]
        assert all(mask==0 for mask in visible["combination_mask"][0][::2])


def test_pass_win_blocks_all_waits_but_allows_higher_fan_and_resets_on_draw():
    state,_=table(PROFILES[1],hands={1:list(tiles("1112223335557m"))},detailed_config=LHS)
    assert state.can_win(1,"discard",16,payer=0)
    state.remember_pass(1,16,payer=0)
    assert not state.can_win(1,"discard",16,payer=2)
    assert state.can_win(1,"discard",17,payer=2)
    state.begin_new_draw(1,normal=False)
    assert state.can_win(1,"discard",16,payer=0)


def test_pass_pung_and_chow_forbidden_tile_with_replacement_reset():
    state,_=table(PROFILES[1],hands={1:list(tiles("11234m 123p 123s 55z"))},detailed_config=LHS)
    assert "peng" in claim_actions(state,0,11)[1]
    state.player_list[1].passed_claim_tiles.add(11)
    assert "peng" not in claim_actions(state,0,11)[1]
    state.begin_new_draw(1,normal=False)
    assert "peng" in claim_actions(state,0,11)[1]
    state,window=table(PROFILES[1],melds={1:["k25","k35","k45"]},hands={1:[12,13,14,14]},detailed_config=LHS)
    window=act(state,window,**{"0":dict(action_type="cut",TileId=11)})
    assert "chi_right" in window["actions"][1]
    act(state,window,**{"1":"chi_right"})
    assert state.legal_discard_tiles(1)=={14}
    assert not ready_discards(state,1)


@pytest.mark.parametrize("flowers",[False,True])
@pytest.mark.parametrize("self_only",[False,True])
@pytest.mark.parametrize("seed",[17,241,902])
def test_complete_lianhuise_hands_replay_metadata_and_private_reconnect(flowers,self_only,seed):
    state=make_state(PROFILES[1],seed=seed,detailed_config={**LHS,"flowers":flowers,"self_draw_only":self_only})
    window=open_round(state)
    for _ in range(1000):
        assert_conservation(state)
        if state.machine.phase==P.END: break
        decisions={i:choose_action(state,i,a) for i,a in window["actions"].items() if a}
        window=state.apply_action_results(window,decisions)
    else: pytest.fail("Physical wall did not terminate")
    state.apply_deferred_score_changes(); state.machine.transition(P.READY); state.finalize_round_recording()
    title=state.game_record["game_title"]
    assert title["rule_version"]=="lianhuise-new13-20260930-om1"
    assert title["detailed_config"]["new13_version"]=="lianhuise"
    assert title["detailed_config"]["self_draw_only"]==self_only
    for seat in range(4):
        snapshot=state.build_game_start_payload(seat)["game_info"]
        assert snapshot["detailed_config"]["new13_version"]=="lianhuise"
        assert snapshot["hepai_limit"]==3
        for i,player in enumerate(snapshot["players_info"]):
            if i!=seat and not state.player_list[i].is_hu: assert player["hand_tiles"] is None
    assert_conservation(state)
