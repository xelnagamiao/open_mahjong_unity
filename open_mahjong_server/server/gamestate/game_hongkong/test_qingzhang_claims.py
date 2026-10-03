"""The revised book's head-bump exemptions and independent multiple wins."""
from itertools import combinations

import pytest

from .test_branches import table,act
from .test_flow import assert_conservation
from .test_boundaries import mutation_snapshot
from .state_machine import HongKongPhase as P
from ...game_calculation.hongkong.models import QINGZHANG_REMIX as REMIX,ORPHANS
from ...game_calculation.hongkong.test_hongkong_calculation import tiles


def claim_table(actor=0,source="discard",mode="head_bump"):
    if source=="discard":
        hands={
            0:list(tiles("777333m 999s 22665z")),
            1:list(tiles("123456789m 111p 5z")),
            2:list(tiles("123456789s 222p 5z")),
            3:list(tiles("123456789p 333s 5z")),
        }
        melds={}
        action=dict(action_type="cut",TileId=45)
    else:
        hands={i:sorted(ORPHANS-{45})+[pair] for i,pair in enumerate((11,19,29),1)}
        middle=list(tiles("234456m 567s 8p"))
        hands[0]=middle+[45]*(4 if source=="concealed_kong" else 1)
        melds={} if source=="concealed_kong" else {0:["k45"]}
        action=dict(action_type="angang" if source=="concealed_kong" else "jiagang",target_tile=45)
    rotate=lambda i:(i+actor)%4
    state,window=table(REMIX,actor=actor,hands={rotate(i):v for i,v in hands.items()},
                       melds={rotate(i):v for i,v in melds.items()},detailed_config={"win_claim":mode})
    window=act(state,window,**{str(actor):action})
    assert all("hu" in window["actions"][rotate(i)] for i in (1,2,3))
    return state,window


DECLARATIONS=[group for n in (1,2,3) for group in combinations((1,2,3),n)]


@pytest.mark.parametrize("actor",range(4))
@pytest.mark.parametrize("source",["discard","added_kong","concealed_kong"])
@pytest.mark.parametrize("mode",["default","head_bump","multiple"])
@pytest.mark.parametrize("declared",DECLARATIONS)
def test_declarations_priority_payments_replay_and_reconnect(actor,source,mode,declared):
    state,window=claim_table(actor,source,mode)
    claimants=[(actor+i)%4 for i in declared]
    winners=claimants if mode=="multiple" else claimants[:1]
    bumped=[] if mode=="multiple" else claimants[1:]
    act(state,window,**{str(i):"hu" for i in reversed(claimants)})
    records=state.deferred_hu_settlements
    assert [r["winner"] for r in records]==winners
    assert sum(p.is_hu for p in state.player_list)==len(winners)
    assert state.won_tiles==[45]
    assert sum(bool(r["recycle_discard"]) for r in records)==1
    total=[0]*4
    for record in records:
        winner=record["winner"]
        relative=(winner-actor)%4
        points=(85 if relative==1 else 80) if source=="discard" else 540
        assert record["points"]==points
        expected=[-30]*4
        expected[winner]=3*points
        expected[actor]=-(3*points-60+30*len(bumped))
        for seat in bumped: expected[seat]=0
        assert record["score_changes"]==expected
        assert sum(expected)==0
        assert record["multi_ron"] is (len(winners)>1)
        total=[a+b for a,b in zip(total,expected)]
    for viewer in range(4):
        panels=state.final_settlement_payloads(viewer)
        assert len(panels)==len(winners)
        assert [p["show_result_info"]["next_status"] for p in panels]==["round_continue"]*(len(winners)-1)+["round_end_by_ready"]
        assert list(panels[-1]["show_result_info"]["hongkong_round_changes"].values())==total
        reconnect=state.restore_payloads(viewer)[0]["game_info"]
        assert [p["score"] for p in reconnect["players_info"]]==[1200+n for n in total]
    state.machine.transition(P.READY)
    state.finalize_round_recording()
    ticks=state.game_record["game_round"]["round_index_1"]["action_ticks"]
    wins=[t for t in ticks if t[0].startswith("hu")]
    assert [t[4] for t in wins]==[r["score_changes"] for r in records]
    assert ticks[-2][3]==[1200+n for n in total]
    if source!="discard":
        assert state.player_list[actor].hand_tiles.count(45)==(3 if source=="concealed_kong" else 0)
        assert state.player_list[actor].combination_tiles==([] if source=="concealed_kong" else ["k45"])
    assert_conservation(state)


@pytest.mark.parametrize("mode,winners,finishing",[("head_bump",(3,),True),("multiple",(3,0),False)])
def test_only_accepted_dealer_win_can_repeat_the_scheduled_last_hand(mode,winners,finishing):
    state,window=claim_table(actor=2,mode=mode)
    state.current_round=4
    act(state,window,**{"3":"hu","0":"hu"})
    assert tuple(r["winner"] for r in state.deferred_hu_settlements)==winners
    assert state.match_finishing is finishing


def test_invalid_win_declaration_cannot_obtain_an_exemption_and_is_atomic():
    state,window=claim_table()
    state.player_list[2].discard_win_lockout_tiles.add(45)
    before=mutation_snapshot(state)
    with pytest.raises(ValueError,match="cannot win"):
        act(state,window,**{"1":"hu","2":"hu"})
    assert mutation_snapshot(state)==before


@pytest.mark.parametrize("mode,bumped",[("multiple",(2,)),("head_bump",(0,)),("head_bump",(1,)),("head_bump",(2,2))])
def test_invalid_settlement_exemptions_are_rejected_before_mutation(mode,bumped):
    state,window=claim_table(mode=mode)
    before=mutation_snapshot(state)
    with pytest.raises(ValueError,match="head-bumped"):
        state.settle_winners([1],"discard",45,payer=0,head_bumped=bumped)
    assert mutation_snapshot(state)==before


@pytest.mark.parametrize("starting,remaining,finishing",[(-5,205,False),(-220,-10,True)])
def test_negative_score_recovery_uses_all_multiple_win_payments(starting,remaining,finishing):
    state,window=claim_table(mode="multiple")
    state.player_list[0].score+=1200-starting
    state.player_list[2].score=starting
    state.negative_score_grace={2}
    act(state,window,**{"1":"hu","2":"hu"})
    panels=state.final_settlement_payloads(0)
    assert state.player_list[2].score==remaining  # +240 for own win, -30 for the other winner.
    assert state.match_finishing is finishing
    assert panels[-1]["show_result_info"]["next_status"]==("match_end" if finishing else "round_end_by_ready")
    assert_conservation(state)
