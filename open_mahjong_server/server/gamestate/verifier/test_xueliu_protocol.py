"""Replay and WS-state contracts for the two blood-flow opening profiles.

These assert component state, not the appearance of real Unity GameObjects.
"""
from copy import deepcopy
import pytest
from .unity_sim import UnitySim
from .record_sim import RecordSim, stringify_tick


def table(sub="sichuan/xueliu_exchange", count=13):
    hand=[11,12,13,14,15,16,17,18,19,21,22,23,24][:count]
    return {"type":"gamestate/sichuan/game_start", "game_info":{
        "room_rule":sub.split("/")[0], "sub_rule":sub, "step_time":5, "action_tick":10,
        "players_info":[dict(user_id=101+i,player_index=i,hand_tiles_count=len(hand),
                             hand_tiles=list(hand) if i==0 else None,score=0,huapai_list=[],
                             combination_tiles=[],combination_mask=[],discard_tiles=[],dingque_suit=3) for i in range(4)]}}


def sim_table(sub="sichuan/xueliu_exchange", count=13):
    s=UnitySim(viewer_user_id=101); s.apply(table(sub,count)); return s


def mid_win(tick=11, winner=0, **extra):
    info=dict(action_tick=tick,hepai_player_index=winner,hu_class="hu_self",hepai_tile=29,
        player_to_score={0:3,1:-1,2:-1,3:-1},round_continues=True,next_status="round_continue",
        suppress_hand_reveal=True,score_changes=[3,-1,-1,-1])
    info.update(extra)
    return {"type":"gamestate/sichuan/show_result","show_result_info":info}


@pytest.mark.parametrize("action,exchange", [("xueliu_throw_three",False),("xueliu_exchange_three",True)])
def test_opening_uses_independent_selector_and_preserves_action_tick(action,exchange):
    s=sim_table()
    s.apply({"type":"gamestate/sichuan/broadcast_hand_action","ask_hand_action_info":{
        "action_list":[action],"player_index":0,"remaining_time":10,"action_tick":11}})
    assert s.opening==dict(three_tiles=True,exchange=exchange,dingque=False,seconds=10)
    assert not s.action_button["visible"] and s.gsm["LastAskActionTick"]==11
    s.apply(table())
    assert not s.opening["three_tiles"]


def test_dingque_uses_own_panel_then_restores_all_seat_marks():
    s=sim_table()
    s._begin_timer({"remaining_time": 10, "step_remaining": 5})
    s.apply({"type":"gamestate/sichuan/ask_dingque","ask_hand_action_info":{"remaining_time":7,"action_tick":14}})
    assert s.opening["dingque"] and s.opening["seconds"]==7 and s.gsm["LastAskActionTick"]==14
    assert not s.action_button["visible"]
    assert s.timer == dict(remaining_time=7, step_remaining=0, total_seconds=7, running=True)
    s.apply({"type":"gamestate/sichuan/dingque_done","show_result_info":{"player_to_dingque":{0:2,1:1,2:3,3:2}}})
    assert not s.opening["dingque"]
    assert [s._seat_info(s.pos_of(i))["dingque_suit"] for i in range(4)]==[2,1,3,2]


def test_repeated_self_draw_scores_without_result_panel_and_continue_is_idempotent():
    s=sim_table(); original=list(s.gsm["selfHandTiles"])
    s.apply({"type":"gamestate/sichuan/do_action","do_action_info":{"action_list":["deal_tile"],"action_player":0,"deal_tile":29}})
    s.apply(mid_win())
    assert s.gsm["selfHandTiles"]==original
    assert not s.end_result["show_result"] and s.game3d["self"]["flowers"]==[29]
    assert s._seat_info("self")["score"]==3 and not s._seat_info("self")["is_hu"]
    s.apply(mid_win()) # delayed duplicate cannot double-count the marker
    assert s._seat_info("self")["win_count"]==1 and s.game3d["self"]["flowers"]==[29]
    info=table()["game_info"]
    info["players_info"][0].update(hand_tiles=original,huapai_list=[29],has_won=True,win_count=1,post_hu_lock=True)
    s.apply({"type":"gamestate/sichuan/xueliu_continue","game_info":info})
    assert s.gsm["selfHandTiles"]==original and s.game3d["self"]["flowers"]==[29]
    assert s._seat_info("self")["score"]==3 # continuation does not replay obsolete snapshot scores
    s.apply({"type":"gamestate/sichuan/do_action","do_action_info":{"action_list":["deal_tile"],"action_player":0,"deal_tile":29}})
    s.apply(mid_win(12,player_to_score={0:6,1:-2,2:-2,3:-2}))
    assert s._seat_info("self")["win_count"]==2 and s.game3d["self"]["flowers"]==[29,29]
    assert s.gsm["selfHandTiles"]==original and not s.end_result["show_result"]


def test_reconnect_snapshot_blocks_old_win_event_and_keeps_unknown_other_hand():
    start=table(); start["game_info"]["action_tick"]=50
    start["game_info"]["players_info"][1].update(huapai_list=[29],has_won=True,win_count=1,post_hu_lock=True)
    s=UnitySim(); s.apply(start); s.apply(mid_win(49,1))
    assert s.game3d["right"]["flowers"]==[29] and s._seat_info("right")["win_count"]==1
    assert "hand_tiles" not in s._seat_info("right")


@pytest.mark.parametrize("suffix,actions", [("broadcast_hand_action",["cut"]),("ask_other_action",["hu","pass"])])
def test_reconnect_clock_uses_remaining_step_budget(suffix,actions):
    s=sim_table()
    field="ask_hand_action_info" if suffix=="broadcast_hand_action" else "ask_other_action_info"
    info=dict(action_list=actions,player_index=0,remaining_time=20,action_tick=11,step_remaining=2)
    s.apply({"type":"gamestate/sichuan/"+suffix,field:info})
    assert s.timer["total_seconds"]==22 and s.timer["step_remaining"]==2
    info["step_remaining"]=0
    s.apply({"type":"gamestate/sichuan/"+suffix,field:info})
    assert s.timer["total_seconds"]==20
    del info["step_remaining"]
    s.apply({"type":"gamestate/sichuan/"+suffix,field:info})
    assert s.timer["total_seconds"]==25


def record(sub, ticks=(), **title):
    return {"game_title":{"rule":"sichuan","sub_rule":sub,**title},"game_round":{"round_index_1":{
        "p0_tiles":[11,12,13,14,15,16,17,18,19,21,21,21,22],"p1_tiles":[22]*13,
        "p2_tiles":[23]*13,"p3_tiles":[24]*13,"tiles_list":[29,29,29],"seats":[0,1,2,3],
        "action_ticks":list(ticks)}}}


@pytest.mark.parametrize("sub", ["sichuan/xueliu","sichuan/xueliu_exchange"])
def test_xueliu_replay_never_infers_dingque_or_retires_winners(sub):
    data=record(sub,[["d",29],["hu_self",0,1,[],[3,-1,-1,-1],29],
        ["reset",0],["d",29],["hu_self",0,1,[],[3,-1,-1,-1],29]],blood_battle=False)
    sim=RecordSim(data); frames=sim.apply_all("round_index_1")
    assert not sim.is_sichuan_blood() and not sim.players[0].is_hu
    assert sim.players[0].huapai_list==[29,29] and len(sim.players[0].tile_list)==13
    assert [p.score for p in sim.players.values()]==[6,-2,-2,-2]
    sim.apply_tick(["reset","0"]); sim.apply_tick(["c","11","F"])
    assert sim.players[0].dingque_suit==0
    assert frames[-1]==RecordSim(data).goto_action("round_index_1",6)


def test_explicit_dingque_survives_initial_hand_with_multiple_missing_suits():
    data=record("sichuan/xueliu_exchange")
    data["game_round"]["round_index_1"]["dingque_suits"]={"0":3,"1":1,"2":2,"3":3}
    sim=RecordSim(data); sim.load_round("round_index_1")
    assert [p.dingque_suit for p in sim.players.values()]==[3,1,2,3]


@pytest.mark.parametrize("value", [False,"false","False"])
def test_old_nonblood_record_flag_is_not_string_truthiness(value):
    assert not RecordSim(record("sichuan/standard",blood_battle=value)).is_sichuan_blood()


def test_exchange_record_applies_explicit_kong_points_and_flower_pig_payment():
    data=record("sichuan/xueliu_exchange",[["ag",21,"F","gs",6,-2,-2,-2],
       ["liuju","chajiao",1,"hua_zhu","[]",[8,-8,0,0],1]])
    data["game_round"]["round_index_1"]["p0_tiles"].append(21)
    sim=RecordSim(data); sim.apply_all("round_index_1")
    assert [p.score for p in sim.players.values()]==[14,-10,-2,-2]


def test_nanque_middle_win_hides_panel_and_score_until_final_settlement():
    s=sim_table(sub="zhongyong/nanque")
    s.gsm["IsSelfActionRequired"] = True
    s.gsm["allowActionList"] = ["hu", "pass"]
    s.action_button = {"buttons": ["hu", "pass"], "visible": True}
    payload=mid_win(defer_score_settlement=True,blood_battle_step="mid_win",hepai_player_index=1,hepai_tile=0)
    del payload["show_result_info"]["player_to_score"]
    s.apply(payload)
    assert not s.end_result["show_result"] and s._seat_info("right")["is_hu"]
    assert s.game3d["right"]["win_marker"]==0 and s._seat_info("right")["score"]==0
    assert not s.gsm["IsSelfActionRequired"] and not s.gsm["allowActionList"]
    assert not s.action_button["visible"]
    s.apply({"type":"gamestate/zhongyong/show_result","show_result_info":{
        "blood_battle_step":"settle_hu","liuju_status_final":True,"player_to_score":{0:-6,1:6,2:0,3:0},"hu_score":1}})
    assert s.end_result["show_result"] and s._seat_info("right")["score"]==6


def test_multi_ron_keeps_both_waiting_hands_and_removes_source_once():
    data=record("sichuan/xueliu_exchange",[["c",22,"F"],
        ["hu_first",1,1,[],[-2,2,0,0],22,1,0,0],
        ["hu_second",2,1,[],[-2,0,2,0],22,1,0,1]])
    sim=RecordSim(data); frames=sim.apply_all("round_index_1")
    assert frames[2]["players"]["0"]["discard_tiles"]==[22]
    assert frames[3]["players"]["0"]["discard_tiles"]==[]
    assert all(len(sim.players[i].tile_list)==13 for i in (1,2))
    assert all(sim.players[i].huapai_list==[22] and not sim.players[i].is_hu for i in (1,2))
    assert [p.score for p in sim.players.values()]==[-4,2,2,0]


def test_robbed_kong_reverts_only_meld_and_keeps_old_discard():
    data=record("sichuan/xueliu_exchange",[
        ["c",21,"F"],["p",21,1,[1,21,0,21,0,21]],
        ["reset",1],["d",21],["jg",21,"T"],
        ["hu_first",2,1,["抢杠"],[0,-2,2,0],21,0,1,1]])
    data["game_round"]["round_index_1"]["p1_tiles"]=[21,21,11,12,13,14,15,16,17,18,19,29,29]
    data["game_round"]["round_index_1"]["tiles_list"]=[21]
    sim=RecordSim(data); sim.apply_all("round_index_1")
    assert sim.players[1].combination_tiles==["k21"]
    assert len(sim.players[1].combination_masks[0])==6
    assert sim.players[2].huapai_list==[21] and not sim.players[2].is_hu
