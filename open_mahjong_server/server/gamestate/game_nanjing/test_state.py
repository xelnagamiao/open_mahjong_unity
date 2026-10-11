import asyncio
from collections import Counter
from copy import deepcopy
from unittest.mock import AsyncMock

import pytest

from .NanjingGameState import NanjingGameState
from .actions import claim_actions, kong_tiles
from .bot import choose_action
from .hints import build_hints
from .state_machine import Phase as P
from ...game_calculation.nanjing.rules import PHYSICAL, parse_meld

PLAIN=[11,12,13,24,25,26,37,38,39,41,41,41,22,22]


def state(seed=1):
    room=NanjingGameState._default_room_data()
    room["random_seed"]=seed
    return NanjingGameState(room_data=room)


def turn(hand=PLAIN,actor=0):
    s=state()
    for p in s.player_list:
        p.hand_tiles=[11,14,17,21,24,27,31,34,37,41,42,43,44]
    p=s.player_list[actor]
    p.hand_tiles=list(hand)
    p.has_draw_slot=True
    s.current_player_index=actor
    s.tiles_list=[18,28,38,42,43]
    s.open_action_window(s.begin_turn(actor))
    return s


def act(s,actions=None,**data):
    responses={i:{"action_type":"pass"} for i,a in s.action_dict.items() if a}
    if actions:
        responses.update({i:dict(action_type=a) if isinstance(a,str) else a for i,a in actions.items()})
    if data:
        responses[s.current_player_index]=data
    return s.apply_action_results(s.live_pending_window,responses)


def physical(s):
    c=Counter(s.tiles_list)
    for p in s.player_list:
        c.update(p.hand_tiles)
        c.update(p.discard_tiles)
        c.update(p.huapai_list)
        for m in p.combination_tiles:c.update(parse_meld(m).tiles)
    if s.deferred_hu_settlements:
        entry=s.deferred_hu_settlements[0]
        if entry["source"]!="self_draw" and not entry.get("forced"):c.update([entry["tile"]])
    return c


def test_opening_and_flower_rounds_preserve_physical_wall_and_seats():
    s=state(23)
    s.initialize_round()
    seats=[p.user_id for p in s.player_list]
    assert [len(p.hand_tiles) for p in s.player_list]==[14,13,13,13]
    s.open_action_window(s.opening_window())
    for _ in range(25):
        if s.machine.phase!=P.REPLACEMENT:break
        act(s,action_type="buhua")
        assert physical(s)==Counter(PHYSICAL)
    assert s.machine.phase==P.TURN and s.current_player_index==0
    assert [len(p.hand_tiles) for p in s.player_list]==[14,13,13,13]
    assert [p.user_id for p in s.player_list]==seats
    assert all(t<45 for p in s.player_list for t in p.hand_tiles)


def test_initial_flowers_use_second_pass_for_new_flowers():
    s=turn()
    s.machine.phase=P.START
    s.player_list[0].hand_tiles[0]=45
    s.player_list[1].hand_tiles[0]=46
    s.tiles_list=[19,18,47]
    s.open_action_window(s.opening_window())
    act(s,action_type="buhua")
    assert s.current_player_index==1
    act(s,action_type="buhua")
    assert s.current_player_index==0
    act(s,action_type="buhua")
    assert s.machine.phase==P.TURN and s.player_list[0].huapai_list==[45,47]


@pytest.mark.parametrize("flowers,tile",[([45]*3,45),([46]*3,46),([47]*3,47),([51,52,53],54),([55,56,57],58)])
def test_flower_kong_pays_once_and_replacement_is_kong(flowers,tile):
    s=turn(PLAIN[:-1]+[tile])
    s.player_list[0].huapai_list=flowers
    act(s,action_type="buhua")
    assert s.round_changes==[30,-10,-10,-10]
    assert s.player_list[0].after_kong and s.player_list[0].draw_kind=="kong"
    assert not s.flower_kong(0,tile)


def test_mandatory_flower_no_cut_and_exhaustion():
    s=turn(PLAIN[:-1]+[45])
    assert s.action_dict[0]==["buhua"]
    with pytest.raises(ValueError):act(s,action_type="cut",TileId=11)
    s.tiles_list=[]
    act(s,action_type="buhua")
    assert s.machine.phase==P.END


def test_no_chow_no_kong_after_peng_and_dead_wall_zero():
    s=turn(PLAIN[:-1]+[18])
    s.player_list[1].hand_tiles=[18,18,19,19,19,19,24,25,26,37,38,39,22]
    act(s,action_type="cut",TileId=18,cutClass=True)
    assert all(not a.startswith("chi") for choices in s.action_dict.values() for a in choices)
    act(s,{1:"peng"})
    assert s.machine.phase==P.DISCARD_ONLY and s.action_dict[1]==["cut"]
    assert not kong_tiles(s,1,"angang")
    assert s.dead_wall_count==0


def test_pung_pass_blocks_until_draw():
    s=turn(PLAIN[:-1]+[18])
    s.player_list[1].hand_tiles=[18,18]+PLAIN[:11]
    act(s,action_type="cut",TileId=18,cutClass=True)
    s._remember_responses(s.live_pending_window,{1:{"action_type":"pass"}})
    assert "peng" not in claim_actions(s,2,18)[1]
    s.draw_for(1)
    assert not s.player_list[1].passed_pungs


def test_direct_added_concealed_and_kong_chain_payments():
    s=turn()
    s.settle_kong(0,"direct",2)
    assert s.round_changes==[10,0,-10,0]
    s.settle_kong(0,"concealed")
    assert s.round_changes==[25,0,-25,0]
    s.settle_kong(0,"flower")
    assert s.round_changes==[55,0,-55,0]
    s.kong_chain_actor=s.kong_chain_payer=None
    s.kong_chain_count=0
    s.settle_kong(1,"added",3)
    assert s.round_changes==[55,10,-55,-10]


def test_follow_and_four_discards_and_winning_exception():
    s=turn()
    s.discard_log=[(2,18),(3,18),(0,18),(1,18)]
    s.settle_follow(1,18)
    assert s.round_changes==[5,5,-15,5]
    s.settle_follow(1,18)
    assert len(s.ledger)==1
    s=turn(PLAIN[:-1]+[22])
    s.player_list[0].discard_origin_tiles=[22]*3
    s.player_list[1].hand_tiles=PLAIN[:-1]
    act(s,action_type="cut",TileId=22,cutClass=True)
    act(s,{1:"hu"})
    assert not any(e["kind"]=="four_discards" for e in s.ledger)
    s=turn(PLAIN[:-1]+[18])
    s.player_list[0].discard_origin_tiles=[18]*3
    act(s,action_type="cut",TileId=18,cutClass=True)
    act(s)
    assert any(e["kind"]=="four_discards" for e in s.ledger)


def test_multiple_ron_one_physical_tile_and_discarder_pays_each():
    s=turn(PLAIN[:-1]+[22])
    for i in (1,2):s.player_list[i].hand_tiles=PLAIN[:-1]
    act(s,action_type="cut",TileId=22,cutClass=True)
    act(s,{1:"hu",2:"hu"})
    assert len(s.deferred_hu_settlements)==2 and s.machine.phase==P.END
    assert s.round_changes[0]==-s.round_changes[1]-s.round_changes[2]
    assert not s.player_list[0].discard_tiles
    assert [x["show_result_info"]["next_status"] for x in s.build_settlement_payloads(0)]==["round_continue","round_end_by_ready"]


def test_added_kong_rob_preserves_pung_and_does_not_pay_kong():
    s=turn([22]+PLAIN[:10])
    p=s.player_list[0]
    p.combination_tiles=["k22"];p.combination_mask=[[1,22,0,22,0,22]];p.meld_sources=[2]
    s.player_list[1].hand_tiles=PLAIN[:-1]
    s.open_action_window(s.begin_turn(0))
    act(s,action_type="jiagang",target_tile=22)
    act(s,{1:"hu"})
    assert p.combination_tiles==["k22"] and not s.ledger
    q=s.deferred_hu_settlements[0]
    assert q["score_changes"][1]==3*q["points"]


def test_concealed_kong_is_public_two_middle_faces():
    s=turn([18]*4+PLAIN[:10])
    act(s,action_type="angang",target_tile=18)
    assert not any(s.action_dict.values())
    act(s)
    assert s.player_list[0].combination_mask[0]==[2,18,0,18,0,18,2,18]
    assert s._player_view(0,2)["combination_tiles"]==["G18"]
    assert s.round_changes==[15,-5,-5,-5]


def test_contract_is_mutual_and_fourth_pung_forces_win_without_shape():
    s=turn(PLAIN[:-1]+[38])
    p=s.player_list[1]
    p.hand_tiles=[38,38,21,24]
    p.combination_tiles=["k11","k22","G33"]
    p.meld_sources=[0,0,None]
    p.combination_mask=[[1,11,0,11,0,11],[1,22,0,22,0,22],[2,33,0,33,0,33,2,33]]
    s.establish_contracts(1)
    assert s.contracts[1]=={0} and s.contracts[0]=={1}
    act(s,action_type="cut",TileId=38,cutClass=True)
    act(s,{1:"peng"})
    assert s.machine.phase==P.END and s.deferred_hu_settlements[0]["forced"]
    assert s.round_changes[0]==-s.round_changes[1] and s.round_changes[2:]==[0,0]


def test_three_concealed_contracts_split_three_partners():
    s=turn()
    p=s.player_list[0]
    p.meld_sources=[None]*3
    p.combination_tiles=["G11","G22","G33"]
    s.establish_contracts(0)
    assert s.contracts[0]=={1,2,3}
    p.hand_tiles=[42]
    s.settle_forced(0,44,None,("全求独钓",),s.contracts[0])
    assert len(set(s.round_changes[1:]))==1


def test_pure_suit_fourth_pung_and_contract_precedence():
    s=turn()
    p=s.player_list[1]
    p.combination_tiles=["k11","k14","k17","k18"]
    p.hand_tiles=[22,23]
    p.pung_claimed=True
    s.machine.phase=P.RESPONSE
    result=s.forced_claim_win(1,2,18,"peng")
    assert result and s.machine.phase==P.END
    assert {"清一色","全求独钓"}.issubset(s.deferred_hu_settlements[0]["fan_names"])
    assert s.round_changes[2]==-s.round_changes[1]


def test_missed_win_threshold_released_by_draw_and_hint_priority():
    s=turn()
    p=s.player_list[1];p.hand_tiles=PLAIN[:-1]
    s.remember_missed_win(1,22)
    assert not s.can_win(1,"discard",22)
    assert build_hints(s,1)["waiting_details"][22]["kind"]=="self_only"
    p.tag_list=["peida"]
    assert build_hints(s,1)["waiting_details"][22]["kind"]=="unmet"
    p.tag_list=[]
    s.machine.phase=P.RESPONSE
    s.draw_for(1)
    assert p.passed_win_value is None
    p.hand_tiles=PLAIN[3:-1];p.combination_tiles=["k11"];p.pung_claimed=True;p.huapai_list=[]
    assert build_hints(s,1)["waiting_details"][22]["kind"]=="not_ready"
    p.huapai_list=[51,52,53,54]
    assert build_hints(s,1)["waiting_details"][22]["kind"]=="ron"


def test_stale_duplicate_and_invalid_response_are_atomic():
    s=turn()
    before=deepcopy(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):s.apply_action_results(dict(s.live_pending_window),{0:{"action_type":"cut","TileId":22}})
    with pytest.raises(ValueError):act(s,action_type="cut",TileId=22,cutClass=True,cutIndex=0)
    with pytest.raises(ValueError):act(s,action_type="angang",target_tile=22)
    assert s.player_list[0].hand_tiles==before
    async def run():
        await s.submit_action(0,"cut",TileId=22,cutClass=True)
        with pytest.raises(ValueError):await s.submit_action(0,"cut",TileId=22)
        responses=await s.wait_action()
        assert responses[0]["TileId"]==22
    asyncio.run(run())


def test_round_rotation_draw_ledger_and_idempotent_finalization():
    s=turn();ids=[p.user_id for p in s.player_list]
    s.settle_kong(0,"concealed")
    s.end_draw();s.apply_deferred_score_changes();s.apply_deferred_score_changes()
    assert [p.score for p in s.player_list]==[15,-5,-5,-5]
    assert all(len(p.score_history)==1 for p in s.player_list)
    s.machine.transition(P.READY);s.advance_round_after_ready()
    assert [p.user_id for p in s.player_list]==ids[1:]+ids[:1]
    assert s.current_round==2


def test_snapshot_hides_other_hands_and_hints_refresh_on_context():
    s=turn()
    payload=s.build_game_start_payload(1)
    players=payload["game_info"]["players_info"]
    assert players[0]["hand_tiles"] is None and players[1]["hand_tiles"]==s.player_list[1].hand_tiles
    assert payload["game_info"]["nanjing_hints"]["source_hand_tiles"]==s.player_list[1].hand_tiles
    assert len(s.restore_payloads(1))==2


@pytest.mark.parametrize("seed",[1,19,57])
def test_seeded_full_match_terminates_and_conserves_every_tile(seed,monkeypatch):
    s=state(seed);s.start_game_recording()
    for _ in range(4):
        s.initialize_round();s.start_round_recording();s.open_action_window(s.opening_window())
        for step in range(600):
            if s.machine.phase==P.END:break
            responses={i:choose_action(s,i,a) for i,a in s.action_dict.items() if a}
            s.apply_action_results(s.live_pending_window,responses)
            assert physical(s)==Counter(PHYSICAL)
        assert s.machine.phase==P.END
        s.apply_deferred_score_changes();s.finalize_round_recording()
        assert sum(p.score for p in s.player_list)==0
        assert s.game_record["game_round"][f"round_index_{s.round_index}"]["action_ticks"][-1]==["end"]
        if s.match_finishing:break
        s.machine.transition(P.READY);s.advance_round_after_ready()
    assert s.match_finishing and s.current_round==4
