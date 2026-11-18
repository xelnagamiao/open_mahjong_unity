from collections import Counter
import asyncio

import pytest
from ...database.statistics_fans import fan_names
from ...database.subrule_fans import collect_subrule_fans
from ...game_calculation.new_chinese import rules
from ...room.new_chinese_room import NewChineseRoomValidator
from .actions import claim_actions, kong_tiles, ready_cuts
from .bot import choose
from .NewChineseGameState import NewChineseGameState
from .state_machine import Phase as P


def table(exchange=False):
    s = NewChineseGameState(room_data=dict(room_id=31, game_round=4, player_list=[101,102,103,104],
        room_rule="new_chinese", sub_rule=rules.SUB_RULE, random_seed=37, detailed_config={"exchange_three":exchange},
        round_timer=20, step_timer=5))
    s.initialize_round()
    return s


def move(state, window, responses):
    choices = {i: {"action_type":"pass"} for i, items in window["actions"].items() if items}
    choices.update({i: ({"action_type":data} if isinstance(data,str) else data) for i,data in responses.items()})
    return state.apply_action_results(window, choices)


def start(state, window):
    return state.open_action_window(window)


def test_deal_rounds_and_reserve():
    s = table()
    assert [len(p.hand_tiles) for p in s.player_list] == [14,13,13,13]
    assert len(s.supplements) == 10 and len(s.tiles_list) == 73
    assert Counter(t for p in s.player_list for t in p.hand_tiles)+Counter(s.tiles_list+s.supplements) == Counter({t:4 for t in rules.TILES})
    s.machine.transition(P.TURN)
    s.tiles_list.clear()
    assert s.draw_for(1)["status"] == "END"
    assert s.round_changes == [0]*4
    s.machine.transition(P.READY)
    s.advance_round_after_ready()
    assert s.current_round == 1 and s.repeat == 1
    for circles in (4,8,12,16):
        assert NewChineseRoomValidator(room_name="四人桌", game_round=circles).game_round == circles
    for invalid in (1,3,5,15,True):
        with pytest.raises(ValueError):
            NewChineseRoomValidator(room_name="四人桌",game_round=invalid)


@pytest.mark.parametrize("seats", [[], [0], [0,2], [0,1,3], [0,1,2,3]])
def test_arbitrary_exchange_skips_opt_out_and_preserves_instances(seats):
    s=table(True)
    before = [list(p.hand_tiles) for p in s.player_list]
    submitted = {i:[before[i][0],before[i][4],before[i][8]] for i in seats}
    w=start(s,s.opening_window())
    with pytest.raises(ValueError):
        s.validate_response(0,dict(action_type="exchange_three",target_tile=[99,99,99]),w["actions"][0])
    move(s,w,{i:dict(action_type="exchange_three",target_tile=cards) for i,cards in submitted.items()})
    for i,p in enumerate(s.player_list):
        expected=Counter(before[i])
        if i in seats and len(seats)>1:
            expected.subtract(submitted[i]);expected.update(submitted[seats[seats.index(i)-1]])
        assert Counter(p.hand_tiles)==+expected
    assert s.game_status==P.TURN and s.player_list[0].has_draw_slot


def test_eligibility_and_theoretical_nine():
    hand = [12,12,12,23,23,23,34,34,34,16,16,27,27]
    assert rules.score(hand,(),16,"self_draw")["fans"]["四暗刻"] == 130
    assert not rules.score(hand,(),16,"discard")
    held=[12,12,12,23,23,23,34,34,34,16,16,16,27]
    assert rules.score(held,(),27,"discard")["fans"]["四暗刻单骑救主"] == 258
    nine=[12,13,14,14,14,14,15,16,16,16,16,17,18]
    assert not {14,16}&rules.waits(nine)
    assert rules.score(nine,(),11,"self_draw")["fans"]["准听九"]==400
    green=[32,33,34]*3+[36,36,36,47]
    assert "绿一色" in rules.score(green,(),47,"self_draw")["fans"]
    assert "绿一色" not in rules.score(green[:-1]+[46],(),46,"self_draw")["fans"]


def test_all_scores_and_stats_have_canonical_labels():
    assert len(rules.CATALOG)==62 and set(fan_names(rules.SUB_RULE))==set(rules.VALUES)
    hand=[32,33,34]*3+[36]
    score=rules.score(hand,("s33",),36,"discard")
    assert score["fans"]["一色四同顺"]==150 and "清一色" not in score["fans"]
    assert "门清" not in score["fans"]
    s=table();p=s.player_list[1];p.hand_tiles=hand;p.declared=True;p.combination_tiles=["s33"];p.combination_mask=[[1,32,0,33,0,34]]
    s.ready_order=[1];s.start_game_recording();s.start_round_recording()
    s.machine.transition(P.TURN);s.machine.transition(P.RESPONSE)
    s.player_list[0].discard_tiles=[36];s.player_list[0].riichi_flags=[False]
    s.settle(1,36,"discard",0);s.finalize_round_recording()
    counts=collect_subrule_fans(s.game_record,p.original_player_index,rules.SUB_RULE)
    assert counts["一色四同顺"]==counts["绿一色"]==1 and "门清" not in counts
    assert s.deferred_hu_settlements[0]["score_changes"][0] == -s.deferred_hu_settlements[0]["points"]


@pytest.mark.parametrize("src",rules.BASE)
def test_ledger_is_balanced_for_each_payment_grade(src):
    for grade,(high,low) in enumerate(rules.BASE[src]):
        for win in range(4):
            for payer in range(4):
                if win==payer:continue
                detail=dict(source=src,base=grade,fan=5)
                changes=rules.payments(detail,win,payer)
                if src=="black":
                    assert -changes[payer]==(high if 0 in (win,payer) else low)+(15 if win==0 else 10)
                    assert sum(v!=0 for v in changes)==2
                else:
                    assert [-changes[i] for i in range(4) if i!=win]==[(high if 0 in (win,i) else low)+5 for i in range(4) if i!=win]
                assert sum(changes)==0


@pytest.mark.parametrize(("source", "method", "pairs"), [
    ("discard", "平点", ("2/1", "4/2", "20/10", "20/10")),
    ("self_draw", "自摸", ("4/2", "8/4", "32/16", "32/16")),
    ("black", "点黑", ("9/6", "18/12", "60/40", "60/40")),
    ("bao_draw", "摸宝", ("8/4", "16/8", "40/20", "40/20")),
    ("bao_seen", "通宝", ("8/4", "24/16", "48/32", "56/32")),
])
def test_settlement_labels_show_published_base_pair_first(source, method, pairs):
    for grade, (shape, pair) in enumerate(zip(("边和", "夹和", "碰碰和", "七小对"), pairs)):
        detail = dict(source=source, base=grade, fans={"门清": rules.VALUES["门清"]})
        assert rules.settlement_labels(detail) == [
            f"{method}{shape}（{pair}）", f"门清（{rules.VALUES['门清']}番）"]


@pytest.mark.parametrize(("winner", "points"), [(0, 12), (1, 8)])
def test_base_only_win_shows_same_row_live_on_restore_and_in_replay(winner, points):
    s = table()
    p = s.player_list[winner]
    p.hand_tiles = [11,12,13,24,25,26,27,27,37,38,39]
    p.combination_tiles, p.combination_mask = ["k42"], [[1,42,0,42,0,42]]
    p.declared = p.has_draw_slot = True
    s.start_game_recording()
    s.start_round_recording()
    s.machine.transition(P.TURN)
    s.settle(winner, 39, "draw")
    labels = ["自摸边和（4/2）"]
    for viewer in range(4):
        result = s.build_final_settlement_payload(viewer)["show_result_info"]
        assert result["hu_fan"] == labels and result["hu_score"] == points
        assert s.restore_payloads(viewer)[1]["show_result_info"]["hu_fan"] == labels
    s.finalize_round_recording()
    rd = s.game_record["game_round"]["round_index_1"]
    index, tick = next((i, t) for i, t in enumerate(rd["action_ticks"]) if t[0] == "hu_self")
    assert tick[3] == labels and rd["win_fan_by_tick"][str(index)] == 0
    assert collect_subrule_fans(s.game_record, p.original_player_index, rules.SUB_RULE) == {}


def test_bao_visibility_real_natural_vs_wild_and_order():
    s=table()
    h=[11,11,14,14,22,22,25,25,33,33,36,36,41]
    for i in (1,3):
        s.player_list[i].hand_tiles=list(h);s.player_list[i].declared=True
    s.ready_order=[3,1];s.bao=41;s.supplements=[42]
    assert s.view(0)["bao"]==s.view(2)["bao"]==0
    assert s.view(3)["bao"]==41 and s.quote(1,41,"seen")["points"]==135
    p=s.player_list[1];p.hand_tiles.append(41);p.has_draw_slot=True
    assert s.quote(1,41,"draw")["points"]==95
    p.hand_tiles.pop();p.has_draw_slot=False
    s.machine.transition(P.TURN);s.machine.transition(P.RESPONSE)
    s.after_bao=(2,19);s.published_bao_cut=False;s.bao_offers=[3,1]
    w=start(s,s.next_bao_offer())
    assert w["player"]==3 and s.build_pending_action_payload(3)["game_info"]["new_chinese"]["acting"]==3
    w=move(s,w,{3:"pass"});assert w["player"]==1
    w=move(s,w,{1:"hu_self"});assert w["status"]=="END" and s.round_changes[1]==135
    assert all(not a for a in claim_actions(s,0,41).values())


def test_consecutive_passes_are_by_face_and_new_bao_unlocks():
    s=table();p=s.player_list[1]
    p.pass_win("discard",14,{14,17});assert p.level==1
    p.pass_win("discard",14,{14,17});assert p.level==1
    p.pass_win("black",17,{14,17});assert p.level==2
    p.pass_win("self_draw",14,{14,17});assert p.level==2
    p.pass_win("self_draw",17,{14,17});assert p.level==3
    p.pass_win("bao_draw",22,{14,17});assert p.level==4
    s.bao=22;s.supplements=[37];p.declared=True;s.ready_order=[1]
    assert s.reveal_bao() and s.bao==37 and p.level==3 and s.supplements==[]
    assert not s.reveal_bao() and not kong_tiles(s,1,"angang")


def test_chow_ready_precedes_pung_and_forces_ready_cut():
    s=table()
    s.player_list[2].hand_tiles=[12,13,21,21,21,32,33,34,41,41,41,45,46]
    s.player_list[1].hand_tiles=[11,11,13,14,15,26,27,28,33,34,35,41,44]
    s.player_list[0].discard_tiles=[11];s.player_list[0].riichi_flags=[False]
    s.machine.transition(P.TURN)
    a=claim_actions(s,0,11)
    assert "chi_right_ting" in a[2] and "chi_right" not in a[2] and "peng" in a[1]
    w=start(s,s.window(P.RESPONSE,0,11,a))
    w=move(s,w,{1:"peng",2:"chi_right_ting"})
    assert w["player"]==2 and w["actions"][2]==["riichi_cut"] and s.player_list[0].discard_tiles==[]
    cut=next(iter(s.candidate_cuts))
    w=move(s,w,{2:dict(action_type="riichi_cut",TileId=cut,cutClass=False)})
    assert s.player_list[2].declared and s.ready_order==[2] and s.bao!=0


def test_head_bump_robbed_kong_and_discard_identity():
    for rob in (False,True):
        s=table();s.machine.transition(P.TURN)
        for i in (0,2):
            p=s.player_list[i];p.hand_tiles=[11,11,14,14,22,22,25,25,33,33,36,36,41];p.declared=True
        payer=s.player_list[1];payer.hand_tiles=[41]+[15]*4;payer.has_draw_slot=False
        if rob:
            payer.combination_tiles=["k41"];payer.combination_mask=[[1,41,0,41,0,41]]
            w=start(s,s.rob_window(1,41,"jiagang",None))
        else:
            payer.discard_tiles=[41];payer.riichi_flags=[False]
            w=start(s,s.window(P.RESPONSE,1,41,claim_actions(s,1,41)))
        w=move(s,w,{0:"hu",2:"hu"})
        assert w["status"]=="END" and s.round_settlement["winner"]==2 and s.round_changes[0]==s.round_changes[3]==0
        if rob: assert payer.combination_tiles==["k41"] and 41 not in payer.hand_tiles

    s=table();p=s.player_list[0];w=start(s,s.opening_window())
    tile=p.hand_tiles[-1]
    with pytest.raises(ValueError):
        s.cut_identity(0,dict(action_type="cut",TileId=98,cutClass=False))
    assert s.cut_identity(0,dict(action_type="cut",TileId=tile,cutClass=True,cutIndex=0))==(13,True)
    asyncio.run(s.submit_action(0,"cut",TileId=tile,cutClass=True,cutIndex=0))
    with pytest.raises(ValueError):
        asyncio.run(s.submit_action(0,"cut",TileId=tile,cutClass=True))
    w=move(s,w,{0:dict(action_type="cut",TileId=tile,cutClass=True)})
    assert p.discard_tiles[-1]==tile


def test_moqie_and_efficiency_bot():
    s=table();p=s.player_list[0];p.user_id=0
    p.hand_tiles=[11,11,11,12,13,14,25,26,27,37,38,39,41,41];p.has_draw_slot=True
    a=choose(s,0,["riichi_cut","hu_self","cut","angang"])
    assert a["action_type"]=="cut" and a["cutClass"] and a["TileId"]==41
    assert choose(s,0,["pass","hu","peng"])["action_type"]=="pass"
    assert choose(s,0,["exchange_three","pass"])["action_type"]=="pass"
    p.user_id=1;s.candidate_cuts=ready_cuts(p.hand_tiles,[])
    assert choose(s,0,["riichi_cut","cut"])["action_type"]=="riichi_cut"
    assert choose(s,0,["hu_self","cut"])["action_type"]=="hu_self"


@pytest.mark.parametrize(("source", "winner", "payer", "expected"), [
    ("discard", 1, 3, [-12, 34, -11, -11]),
    ("discard", 0, 2, [36, -12, -12, -12]),
    ("self_draw", 1, None, [-14, 38, -12, -12]),
    ("self_draw", 0, None, [42, -14, -14, -14]),
    ("bao_draw", 1, None, [-18, 46, -14, -14]),
    ("bao_seen", 1, None, [-18, 46, -14, -14]),
    ("black", 1, 0, [-29, 29, 0, 0]),
    ("black", 1, 2, [0, 26, -26, 0]),
    ("black", 0, 2, [39, 0, -39, 0]),
])
def test_published_payout_examples_with_ten_fan(source, winner, payer, expected):
    assert rules.payments(dict(source=source, base=0, fan=10), winner, payer) == expected


def test_robbed_added_kong_chow_then_ordinary_ron_recycles_its_own_discard():
    s = table()
    p0, _, p2, p3 = s.player_list
    p0.hand_tiles = [15,16,17,18,23,24,25,35,37,38,11]
    p0.combination_tiles, p0.combination_mask = ["k11"], [[1,11,0,11,0,11]]
    p2.hand_tiles = [12,13,21,21,21,32,33,34,41,41,41,45,46]
    p3.hand_tiles = [14,14,17,17,24,24,27,27,32,32,36,36,46]
    p3.declared, s.ready_order, s.bao = True, [3], 18
    s.start_game_recording()
    s.start_round_recording()
    w = start(s, s.opening_window())
    w = move(s, w, {0: {"action_type":"jiagang", "target_tile":11}})
    assert w["status"] == P.ROB_KONG
    w = move(s, w, {2:"chi_right_ting"})
    assert len(p0.hand_tiles) == 10 and p0.combination_tiles == ["k11"] and not p0.discard_tiles
    assert p2.combination_tiles == ["s12"] and s.must_declare == 2
    w = move(s, w, {2: {"action_type":"riichi_cut", "TileId":46, "cutClass":False}})
    assert w["before_bao"] and s.bao == 18
    w = move(s, w, {3:"hu"})
    assert w["status"] == P.END and not p2.discard_tiles
    entry = s.deferred_hu_settlements[0]
    assert entry["fan_details"]["source"] == "discard" and entry["recycle_discard"]
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ["new_chinese", "rob_kan", 0, 11, "jiagang", None, 1] in ticks
    hu = next(tick for tick in ticks if tick[0].startswith("hu"))
    assert list(map(int,hu[7:9])) == [2,1]


def test_robbed_direct_kong_uses_original_discard_and_charges_declarer():
    s=table()
    p0,p1,p2,_=s.player_list
    p0.discard_tiles=[11];p0.riichi_flags=[False]
    p1.hand_tiles=[11,11,11,16,17,18,23,24,25,35,37,38,39]
    p2.hand_tiles=[12,13,21,21,21,32,33,34,41,41,41,45,45]
    p2.declared=True;s.ready_order=[2];s.bao=18
    s.machine.transition(P.TURN);s.machine.transition(P.RESPONSE)
    w=start(s,s.rob_window(1,11,"gang",0))
    w=move(s,w,{2:"hu"})
    assert w["status"]==P.END and p0.discard_tiles==[]
    assert p1.hand_tiles.count(11)==3 and p1.combination_tiles==[]
    assert s.round_changes[2]==-s.round_changes[1]>0 and s.round_changes[0]==s.round_changes[3]==0


def test_longest_consecutive_kong_survives_a_later_discard():
    s=table();p=s.player_list[0]
    p.hand_tiles=[11]*4+[22]*4+[33]*3+[45,45,47]
    w=start(s,s.opening_window())
    for tile in (11,22):
        w=move(s,w,{0:{"action_type":"angang","target_tile":tile}})
        assert w["status"]==P.TURN
    assert p.chain==p.longest_chain==2 and len(s.supplements)==8
    w=move(s,w,{0:{"action_type":"cut","TileId":p.hand_tiles[-1],"cutClass":True}})
    assert p.chain==0 and p.longest_chain==2
    p.hand_tiles=[33]*3+[45]*3+[47];p.declared=True
    s.bao=17
    assert s.quote(0,47,"discard",1)["fans"]["杠上杠"]==rules.VALUES["杠上杠"]


def test_mobile_integer_clock_after_fractional_seconds_and_private_restore():
    s=table();w=start(s,s.opening_window())
    s.player_list[0].remaining_time=19.2
    restored=s.restore_payloads(0)
    assert type(restored[-1]["ask_hand_action_info"]["remaining_time"]) is int
    assert restored[-1]["ask_hand_action_info"]["remaining_time"]==20
    assert all(packet["game_info"]["new_chinese"]["bao"]==0 for packet in restored)
    assert [p["hand_tiles"] is not None for p in restored[0]["game_info"]["players_info"]]==[True,False,False,False]


def test_room_lifecycle_registers_then_removes_all_game_indexes():
    from unittest.mock import patch
    from ..test_room_lifecycle import USERS, make_room, make_server, parked_loop
    async def run():
        server=make_server(USERS);room=make_room(USERS,"new_chinese")
        room.update(sub_rule=rules.SUB_RULE, game_round=16)
        server.room_manager.rooms["1"]=room
        manager=server.gamestate_manager
        with patch.object(NewChineseGameState,"run_game_loop",parked_loop):
            assert await manager.start_game(str(USERS[0]),"1") is None
            state=manager.get_game_state_by_room_id("1")
            assert isinstance(state,NewChineseGameState) and state.max_round==16
            assert all(manager.get_game_state_by_user_id(uid) is state for uid in USERS)
            await manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert manager.get_game_state_by_room_id("1") is None
            assert all(manager.get_game_state_by_user_id(uid) is None for uid in USERS)
    asyncio.run(run())


def test_four_circle_match_runs_all_sixteen_hands_and_human_ready_windows():
    import random
    s = table()
    initialize = s.initialize_round
    wait_action = s.wait_action
    ready_windows = []
    def deal():
        positions = [4,5,6,7,20,21,22,23,36,37,38,39,49]
        waiting = [11,11,14,14,22,22,25,25,33,33,36,36,41]
        fixed = dict(zip(positions, waiting))
        fixed[53] = 47
        fixed[126+random.Random(s._derive_round_seed()).randrange(10)] = 41
        available = Counter({tile:4 for tile in rules.TILES})
        available.subtract(fixed.values())
        rest = list(available.elements())
        random.Random(s.round_index).shuffle(rest)
        initialize(wall=[fixed[i] if i in fixed else rest.pop() for i in range(136)])
    async def answer(*args, **kwargs):
        if s.machine.phase == P.READY:
            ready_windows.append(s.round_index)
        for i in list(s.waiting_players_list):
            options = s.action_dict[i]
            if "ready" in options:
                await s.submit_action(i,"ready")
            elif i == 1 and "hu_self" in options:
                await s.submit_action(i,"hu_self")
            elif "cut" in options:
                action = "riichi_cut" if i == 1 else "cut"
                p = s.player_list[i]
                await s.submit_action(i,action,TileId=p.hand_tiles[-1],cutIndex=len(p.hand_tiles)-1,cutClass=True)
            else:
                await s.submit_action(i,"pass")
        return await wait_action(*args, **kwargs)
    s.initialize_round = deal
    s.wait_action = answer
    asyncio.run(asyncio.wait_for(s.run_game_loop(),timeout=35))
    assert s.lifecycle_completed and s.game_status == P.FINISHED
    assert ready_windows == list(range(1,16))
    rounds = list(s.game_record["game_round"].values())
    assert len(rounds) == 16
    assert len({tuple(rd["seats"]) for rd in rounds}) == 4
    for rd in rounds:
        tick = next(t for t in rd["action_ticks"] if t[0]=="hu_self")
        assert tick[4] == [-61,135,-37,-37]
    assert all(p.score == 0 for p in s.player_list)
