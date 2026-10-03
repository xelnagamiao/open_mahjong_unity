import pytest

from . import ZhongyongGameState, NanqueGameState
from .boardcast import final_settlement_payloads, mid_win_payload
from .settlement import ZhongyongSettlementPolicy


def state(cls=ZhongyongGameState):
    s=cls()
    s.initialize_round()
    return s


WAIT=[11,12,13,21,22,23,31,32,33,45,45,45,41]


def ready_on_east(s, *players):
    for i in players:
        s.player_list[i].hand_tiles=list(WAIT)
        s.player_list[i].waiting_tiles={41}


@pytest.mark.parametrize("points,responsible,expected",[
    (1,0,[-1,3,-1,-1]), (25,0,[-25,75,-25,-25]),
    (70,0,[-160,210,-25,-25]), (70,None,[-70,210,-70,-70]),
])
def test_official_payoff_examples(points,responsible,expected):
    assert ZhongyongSettlementPolicy.score_changes(1,points,responsible)==expected


def test_same_round_immunity_transfers_responsibility_but_never_blocks_win():
    s=state()
    s.discard_log=[(1,11),(2,41),(3,12),(0,41)]
    s.last_discard_offsets[1]=0
    assert ZhongyongSettlementPolicy.responsible_player(s,1,41,0)==2
    s.discard_log[0]=(1,41)
    assert ZhongyongSettlementPolicy.responsible_player(s,1,41,0) is None
    s.player_list[1].discard_win_lockout_tiles={41}
    assert s.can_win_by_discard(1,41)


def test_standard_ends_by_head_bump_and_keeps_dead_wall():
    s=state(); ready_on_east(s,1,2,3)
    s.player_list[0].discard_tiles=[41]
    result=s.resolve_discard_win_responses(0,41,{3:"hu",2:"hu",1:"hu"})
    assert result["winners"]==[1] and s.hu_count==1 and s.game_status=="END"
    s=state(); s.tiles_list=[11]*14
    assert s.draw_after_discard_resolution(0) is None and s.game_status=="END"


def test_nanque_multiron_retires_three_and_settles_once():
    s=state(NanqueGameState); ready_on_east(s,1,2,3)
    s.player_list[0].discard_tiles=[41]
    result=s.resolve_discard_win_responses(0,41,{3:"hu",2:"hu",1:"hu"})
    assert result["winners"]==[1,2,3] and s.hu_count==3 and s.game_status=="END"
    assert [p.score for p in s.player_list]==[0]*4
    payloads=final_settlement_payloads(s,0)
    assert len(payloads)==4 and payloads[-1]["show_result_info"]["liuju_status_final"]
    scores=[p.score for p in s.player_list]
    assert scores[0]<0 and all(n>0 for n in scores[1:]) and sum(scores)==0
    final_settlement_payloads(s,1)
    assert scores==[p.score for p in s.player_list]
    assert all(len(p.score_history)==1 for p in s.player_list)


@pytest.mark.parametrize("winners", [(1, 2), (1, 2, 3)])
def test_nanque_last_discard_records_all_simultaneous_winners(winners):
    s = state(NanqueGameState)
    hands = [
        [11,12,13,14,15,16,21,22,23,31,32,33,41],
        [12,13,14,15,16,17,22,23,24,32,33,34,41],
        [13,14,15,16,17,18,23,24,25,33,34,35,41],
    ]
    for index, hand in enumerate(hands, 1):
        s.player_list[index].hand_tiles = hand
        s.player_list[index].waiting_tiles = {41}
    s.player_list[0].discard_tiles = [41]
    s.tiles_list = []
    result = s.continue_after_discard_responses(0, 41, {index: "hu" for index in winners})
    assert result["status"] == "END"
    assert s.hu_count == len(winners)
    assert [entry["winner"] for entry in s.deferred_hu_settlements] == list(winners)
    assert all(entry["multi_ron"] for entry in s.deferred_hu_settlements)
    assert s.player_list[0].discard_tiles == []
    final_settlement_payloads(s, 0)
    scores = [player.score for player in s.player_list]
    assert sum(scores) == 0
    assert all(scores[index] > 0 for index in winners)
    final_settlement_payloads(s, 3)
    assert scores == [player.score for player in s.player_list]
    assert all(len(player.score_history) == 1 for player in s.player_list)


def test_nanque_self_draw_broadcasts_next_draw_and_keeps_scores_secret():
    s=state(NanqueGameState); ready_on_east(s,0)
    s.player_list[0].hand_tiles.append(41)
    s.start_game_recording(); s.start_round_recording()
    window=s.begin_hand_action(0)
    s.apply_action_results(window,{0:{"action_type":"hu_self"}})
    assert s.game_status=="waiting_hand_action" and s.current_player_index==1
    mid=mid_win_payload(s,1,0)["show_result_info"]
    assert mid["hepai_tile"]==0 and mid["defer_score_settlement"]
    assert "hu_score" not in mid and "hepai_player_hand" not in mid
    assert any(p.get("do_action_info",{}).get("action_list")==["deal_tile"] for p in s.outbound_payloads)
    snapshot=s.build_game_start_payload(1)["game_info"]["players_info"][0]
    assert snapshot["hand_tiles"] is None and snapshot["hand_tiles_count"]==13 and snapshot["is_hu"]
    ticks=s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[0][0]=="hu_self" and ticks[0][4]==[0]*4 and ticks[-1][0]=="d"


def test_nanque_self_draw_distribution_after_retirement():
    from ..game_jiandan.settlement import JiandanSettlementPolicy
    s=state(NanqueGameState)
    s.player_list[0].is_hu=True
    assert JiandanSettlementPolicy.score_changes(s,1,"self_draw",3)==[0,18,-9,-9]
    s.player_list[2].is_hu=True
    assert JiandanSettlementPolicy.score_changes(s,1,"self_draw",3)==[0,18,0,-18]


def test_standard_earth_blessing_is_dealer_first_discard():
    s=state(); ready_on_east(s,1)
    s.player_list[0].hand_tiles.append(41)
    s.record_discard(0,41)
    result=s.settlement_policy.build(s,1,"discard",41,payer_index=0)
    assert "earthly_win" in result["fan_ids"]
    assert not s._settlement_context(1,"self_draw",WAIT+[41],41)["earthly_win"]


def test_concealed_kong_visibility_differs_by_profile():
    for cls,expected in ((ZhongyongGameState,11),(NanqueGameState,0)):
        s=state(cls); p=s.player_list[0]
        p.combination_tiles=["G11"]; p.combination_mask=[[2,11]*4]
        snapshot=s.build_game_start_payload(1)["game_info"]["players_info"][0]
        assert snapshot["combination_mask"][0][1]==expected


def test_robbed_added_kong_stays_pung_and_no_supplement_draw():
    s=state(NanqueGameState); ready_on_east(s,1,2)
    s.player_list[0].hand_tiles=[41,12,13,14,25,26,27,35,36,37,19]
    s.player_list[0].combination_tiles=["k41"]
    s.player_list[0].combination_mask=[[1,41,0,41,0,41]]
    remaining=len(s.tiles_list)
    s.attempt_added_kong(0,41)
    result=s.resolve_added_kong_responses(0,41,{1:"hu",2:"hu"})
    assert result["winners"]==[1,2]
    assert s.player_list[0].combination_tiles==["k41"]
    assert 41 not in s.player_list[0].hand_tiles and len(s.tiles_list)==remaining-1


def test_final_wall_discard_can_win_but_cannot_meld():
    for cls,dead in ((ZhongyongGameState,14),(NanqueGameState,0)):
        s=state(cls); ready_on_east(s,1); s.tiles_list=[11]*dead
        s.player_list[2].hand_tiles=[41]*3
        acts=s.action_policy.check_after_cut(s,41)
        assert acts[1]==["hu","pass"] and acts[2]==[]


@pytest.mark.parametrize("cls", [ZhongyongGameState, NanqueGameState])
def test_reconnect_at_ready_restores_final_panel_and_does_not_offer_discard(cls):
    import asyncio
    from unittest.mock import AsyncMock
    s = state(cls)
    s.player_list[0].user_id = 101
    s.game_status = "waiting_ready"
    s.action_dict = {0: ["ready"]}
    s.send_payload_to_player = AsyncMock()
    asyncio.run(s.player_reconnect(101))
    payloads = [call.args[1] for call in s.send_payload_to_player.call_args_list]
    assert [p["type"].rsplit("/", 1)[-1] for p in payloads] == ["game_start", "show_result", "ready_status"]
    assert s.build_pending_action_payload(0) is None
    assert payloads[0]["game_info"]["players_info"][1]["hand_tiles"] is not None


def test_live_wall_display_excludes_standard_dead_wall():
    for cls, dead in ((ZhongyongGameState, 14), (NanqueGameState, 0)):
        s = state(cls)
        assert s.build_game_start_payload(0)["game_info"]["tile_count"] == len(s.tiles_list) - dead


@pytest.mark.parametrize("cls", [ZhongyongGameState, NanqueGameState])
def test_spectator_join_between_hands_restores_final_panel(cls):
    import asyncio
    from types import SimpleNamespace
    from unittest.mock import AsyncMock
    s = state(cls)
    s.game_status = "waiting_ready"
    socket = SimpleNamespace(send_json=AsyncMock())
    s.game_server = SimpleNamespace(user_id_to_connection={999: SimpleNamespace(websocket=socket)})
    asyncio.run(s.send_realtime_spectator_snapshot(999, 1))
    payloads = [call.args[0] for call in socket.send_json.call_args_list]
    assert [p["type"].rsplit("/", 1)[-1] for p in payloads] == ["game_start", "show_result", "ready_status"]


def test_final_ranking_does_not_reorder_live_seat_or_recipient_list():
    s = state()
    for p, value in zip(s.player_list, [-30, 10, 20, 0]): p.score = value
    s.emit_game_end_payloads()
    assert [p.player_index for p in s.player_list] == [0, 1, 2, 3]
    assert [p.record_counter.rank_result for p in s.player_list] == [4, 2, 1, 3]
