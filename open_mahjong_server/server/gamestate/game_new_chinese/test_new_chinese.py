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


def table(exchange=False, *, wall=None):
    s = NewChineseGameState(room_data=dict(room_id=31, game_round=4, player_list=[101,102,103,104],
        room_rule="new_chinese", sub_rule=rules.SUB_RULE, random_seed=37, detailed_config={"exchange_three":exchange},
        round_timer=20, step_timer=5))
    s.initialize_round(wall=wall)
    return s


def move(state, window, responses):
    choices = {i: {"action_type":"pass"} for i, items in window["actions"].items() if items}
    choices.update({i: ({"action_type":data} if isinstance(data,str) else data) for i,data in responses.items()})
    return state.apply_action_results(window, choices)


def start(state, window):
    return state.open_action_window(window)


def prior_discards(state, index, *tiles):
    player = state.player_list[index]
    player.discard_tiles.extend(tiles)
    player.discard_origin_tiles.extend(tiles)
    player.riichi_flags.extend(False for _ in tiles)
    state.discard_log.extend((index, tile) for tile in tiles)
    state.last_discard_offsets[index] = len(state.discard_log) - 1


def test_deal_rounds_and_reserve():
    deck = sorted(rules.TILES, reverse=True) * 4
    s = table(wall=deck)
    for i, player in enumerate(s.player_list):
        expected = deck[4*i:4*i+4] + deck[16+4*i:20+4*i] + deck[32+4*i:36+4*i] + [deck[48+i]]
        if i == s.dealer_index:
            expected.append(deck[52])
        assert s.build_game_start_payload(i)["game_info"]["self_hand_tiles"] == player.hand_tiles == expected
        assert player.has_draw_slot == (i == s.dealer_index)
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


@pytest.mark.parametrize("exchange_draw", [False, True])
def test_exchange_keeps_main_hand_order_and_timeout_discards_the_draw(exchange_draw, monkeypatch):
    s = table(True)
    # The dealer has two 13s in the main hand and a third in the draw slot.
    s.player_list[0].hand_tiles = [29, 13, 17, 11, 13, 36, 24, 22, 34, 19, 27, 32, 25, 13]
    s.player_list[1].hand_tiles = [39, 13, 38, 16, 37, 41, 35, 21, 33, 18, 14, 23, 28]
    outgoing = [13, 13, 13] if exchange_draw else [13, 13, 29]
    incoming = [13, 16, 41]
    now = 20.0
    monkeypatch.setattr(s, "_timing_now", lambda: now)
    opening = start(s, s.opening_window())
    showing = move(s, opening, {0: dict(action_type="exchange_three", target_tile=outgoing),
                                1: dict(action_type="exchange_three", target_tile=incoming)})
    now = showing["deadline"]
    receipt = move(s, showing, {})
    main = [17, 11, 36, 24, 22, 34, 19, 27, 32, 25]
    expected = [29, *main, *incoming] if exchange_draw else [*main, *incoming, 13]
    assert s.build_game_start_payload(0)["game_info"]["self_hand_tiles"] == expected
    assert s.player_list[1].hand_tiles == [39, 38, 37, 35, 21, 33, 18, 14, 23, 28, *outgoing]
    now = receipt["deadline"]
    move(s, receipt, {})
    timed = s._build_timeout_action(0)
    assert (timed["TileId"], timed["cutIndex"], timed["cutClass"]) == (expected[-1], 13, True)


@pytest.mark.parametrize("mask", range(16))
def test_exchange_shows_all_choices_before_passing_to_next_participant(mask, monkeypatch):
    seats = [i for i in range(4) if mask & (1 << i)]
    s = table(True)
    now = 10.0
    monkeypatch.setattr(s, "_timing_now", lambda: now)
    s.start_game_recording()
    s.start_round_recording()
    before = [list(p.hand_tiles) for p in s.player_list]
    submitted = {i:[before[i][0],before[i][4],before[i][8]] for i in seats}
    w=start(s,s.opening_window())
    with pytest.raises(ValueError):
        s.validate_response(0,dict(action_type="exchange_three",target_tile=[99,99,99]),w["actions"][0])
    assert all("exchange_choices" not in s.view(i) for i in range(4))
    s.outbound_payloads.clear()
    showing = move(s,w,{i:dict(action_type="exchange_three",target_tile=cards) for i,cards in submitted.items()})
    duration = 3750 if len(seats) >= 2 else 1000
    choices = [i in seats for i in range(4)]
    assert s.game_status == P.EXCHANGE_CHOICES and not s.waiting_players_list
    assert [p.hand_tiles for p in s.player_list] == before
    assert len(s.outbound_payloads) == 4
    for i, packet in enumerate(s.outbound_payloads):
        assert packet["do_action_info"]["action_list"] == ["new_chinese_exchange_choices"]
        assert packet["tile"] is None and packet["game_info"]["self_hand_tiles"] == before[i]
        assert [p["hand_tiles"] is not None for p in packet["game_info"]["players_info"]] == [i == j for j in range(4)]
        view = packet["game_info"]["new_chinese"]
        assert view["exchange_choices"] == choices and view["exchange_remaining_ms"] == duration
        assert view["exchange_duration_ms"] == duration and view.get("exchange_outgoing") == submitted.get(i)
        assert "exchange_incoming" not in view
        assert "submissions" not in view
    now += 0.4
    for i in range(4):
        snapshot, = s.restore_payloads(i)
        assert snapshot["game_info"]["new_chinese"]["exchange_remaining_ms"] == duration - 400
        assert snapshot["game_info"]["new_chinese"].get("exchange_outgoing") == submitted.get(i)
        assert snapshot["game_info"]["self_hand_tiles"] == before[i]
    delayed = s.outbound_payloads[-1]
    s._refresh_clock_payload(3, delayed)
    assert delayed["game_info"]["new_chinese"]["exchange_remaining_ms"] == duration - 400
    assert "exchange_outgoing" not in s.view(None)
    with pytest.raises(ValueError):
        move(s,showing,{})
    s.outbound_payloads.clear()
    now = showing["deadline"]
    next_window = move(s,showing,{})
    if len(seats) >= 2:
        assert s.machine.phase == P.EXCHANGE_RECEIPT and not s.waiting_players_list
        assert len(s.outbound_payloads) == 4
        now += .25
        for i in range(4):
            snapshot, = s.restore_payloads(i)
            receipt = s.outbound_payloads[i]
            view = snapshot["game_info"]["new_chinese"]
            assert view["exchange_remaining_ms"] == 500 and "exchange_outgoing" not in view
            assert "exchange_choices" not in view
            assert view.get("exchange_incoming") == (submitted[seats[seats.index(i)-1]] if i in seats else None)
            assert receipt["game_info"]["new_chinese"].get("exchange_incoming") == view.get("exchange_incoming")
            assert receipt["game_info"]["new_chinese"]["phase"] == P.EXCHANGE_RECEIPT
        assert "exchange_incoming" not in s.view(None)
        with pytest.raises(ValueError):
            move(s,next_window,{})
        now = next_window["deadline"]
        move(s,next_window,{})
    for i in range(4):
        packets = [p for p in s.outbound_payloads if p["player_index"] == i]
        if len(seats) >= 2:
            assert packets[0]["do_action_info"]["action_list"] == ["new_chinese_exchange"]
            assert packets[0]["game_info"]["self_hand_tiles"] == s.player_list[i].hand_tiles
        assert len(packets) == (2 if len(seats) >= 2 else 1)
        assert packets[-1]["ask_hand_action_info"]["player_index"] == 0
        assert packets[-1]["game_info"]["new_chinese"]["phase"] == P.TURN
        assert "exchange_choices" not in s.restore_payloads(i)[0]["game_info"]["new_chinese"]
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[0][2]["exchange_choices"] == choices and ticks[0][2]["exchange_remaining_ms"] == duration
    assert ticks[0][2]["exchange_packets"] == [submitted.get(i, []) for i in range(4)]
    assert "exchange_packets" not in s.spectator_record_tick(ticks[0])[2]
    assert all("exchange_packets" not in s.view(i) for i in range(4))
    assert [t[1] for t in ticks] == (["state","exchange","state","state"] if len(seats) >= 2 else ["state","state"])
    assert ticks[-1][2]["phase"] == P.TURN
    for i,p in enumerate(s.player_list):
        expected=Counter(before[i])
        if i in seats and len(seats)>1:
            expected.subtract(submitted[i]);expected.update(submitted[seats[seats.index(i)-1]])
        assert Counter(p.hand_tiles)==+expected
    assert s.game_status==P.TURN and s.player_list[0].has_draw_slot


def test_exchange_pending_response_and_timeout_are_private_until_all_seats_finish(monkeypatch):
    async def run():
        s = table(True)
        s.player_list[3].remaining_time = s.step_time = 0
        start(s, s.opening_window())
        for i in (2, 0):
            await s.submit_action(i, "exchange_three", target_tile=s.player_list[i].hand_tiles[:3])
            assert s.view(i)["exchange_outgoing"] == s.player_list[i].hand_tiles[:3]
        assert "exchange_outgoing" not in s.view(1) and "exchange_outgoing" not in s.view(None)
        assert len(s.restore_payloads(0)) == 1
        assert "exchange_choices" not in s.restore_payloads(1)[0]["game_info"]["new_chinese"]
        pending = asyncio.create_task(s.resolve_action_window())
        await asyncio.sleep(0)
        assert s.waiting_players_list == [1] and not pending.done()
        assert len(s.restore_payloads(0)) == 1 and len(s.restore_payloads(1)) == 2
        assert not any(p.get("action") == "new_chinese_exchange_choices" for p in s.outbound_payloads)
        await s.submit_action(1, "pass")
        assert s.view(1)["exchange_outgoing"] == []
        await pending
        assert s.view(0)["exchange_choices"] == [True, False, True, False]
        with pytest.raises(ValueError):
            await s.submit_action(0, "cut", TileId=s.player_list[0].hand_tiles[-1], cutClass=True)
        before = [p.hand_tiles.copy() for p in s.player_list]
        now = s.live_pending_window["deadline"] - 0.75
        monkeypatch.setattr(s, "_timing_now", lambda: now)
        async def finish_delay(seconds):
            nonlocal now
            assert seconds == pytest.approx(0.75)
            if s.machine.phase == P.EXCHANGE_CHOICES:
                assert [p.hand_tiles for p in s.player_list] == before
            now += seconds
        monkeypatch.setattr(asyncio, "sleep", finish_delay)
        await s.resolve_action_window(timeout=0)
        assert s.game_status == P.EXCHANGE_RECEIPT
        await s.resolve_action_window(timeout=0)
        assert s.game_status == P.TURN
        assert s.player_list[1].hand_tiles == before[1] and s.player_list[3].hand_tiles == before[3]
    asyncio.run(run())


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
    assert len(rules.CATALOG)==63 and set(fan_names(rules.SUB_RULE))==set(rules.VALUES)
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


@pytest.mark.parametrize("showdown", [False, True])
def test_showdown_option_reveals_all_hands_on_exhaustion(showdown):
    s = table()
    s.detailed_config["showdown"] = showdown
    s.bao = 17
    s.player_list[0].declared = True
    start(s, s.opening_window())
    expected = {i: list(player.hand_tiles) for i, player in enumerate(s.player_list)}
    for viewer in range(4):
        live = s.build_game_start_payload(viewer)
        assert live["game_info"]["new_chinese"]["bao"] == (17 if viewer == 0 else 0)
        assert [player["hand_tiles"] for player in live["game_info"]["players_info"]] == [
            expected[i] if i == viewer else None for i in range(4)
        ]
    s.finish()
    for viewer in range(4):
        final = s.build_final_settlement_payload(viewer)
        restored = s.restore_payloads(viewer)
        for payload in (final, *restored):
            assert payload["game_info"]["new_chinese"]["bao"] == 17
            assert [player["hand_tiles"] for player in payload["game_info"]["players_info"]] == [
                expected[i] if showdown or i == viewer else None for i in range(4)
            ]
        for payload in (final, restored[1]):
            assert payload["show_result_info"]["revealed_hands"] == (expected if showdown else {})


def test_winning_hand_reveals_all_hands_without_showdown_option():
    s = table()
    winner = 1
    tile = 39
    s.player_list[0].hand_tiles[-1] = tile
    player = s.player_list[winner]
    player.hand_tiles = [11, 12, 13, 24, 25, 26, 27, 27, 37, 38]
    player.combination_tiles, player.combination_mask = ["k42"], [[1, 42, 0, 42, 0, 42]]
    player.declared = True
    window = start(s, s.opening_window())
    window = move(s, window, {0: dict(action_type="cut", TileId=tile, cutClass=True)})
    assert "hu" in window["actions"][winner]
    assert move(s, window, {winner: "hu"})["status"] == P.END

    expected = {i: list(other.hand_tiles) for i, other in enumerate(s.player_list)}
    expected[winner].append(tile)
    for viewer in range(4):
        final = s.build_final_settlement_payload(viewer)
        for payload in (final, s.restore_payloads(viewer)[1]):
            result = payload["show_result_info"]
            assert result["hepai_player_index"] == winner
            assert result["hepai_player_hand"] == expected[winner]
            assert result["revealed_hands"] == expected


@pytest.mark.parametrize(("hand", "melds", "winning", "fans"), [
    ([27,28,29,37,38,39,42,42,42,46], ["s18"], 46, {"精五门": 50}),
    ([11,12,13,21,22,23,31,32,33,41,41,41,45], [], 45, {"精五门": 50, "门清": 5}),
    ([21,22,23,31,32,33,41,41,41,44], ["s12"], 44, {"全带幺": 20, "三色三同顺": 20}),
    ([21,22,23,37,38,39,41,41,41,45], ["s12"], 45, {"五门齐": 20, "全带幺": 20}),
    ([22,23,24,32,33,34,41,41,41,45], ["s13"], 45, {"五门齐": 20, "三色三同顺": 20}),
])
def test_jingwumen_requires_all_three_fans_and_preserves_others(hand, melds, winning, fans):
    quote = rules.score(hand, melds, winning, "self_draw")
    assert quote["fans"] == fans
    assert quote["fan"] == sum(fans.values())


def test_jingwumen_settlement_and_record_statistics():
    s = table()
    p = s.player_list[1]
    p.hand_tiles = [21,22,23,31,32,33,41,41,41,45,45]
    p.combination_tiles, p.combination_mask = ["s12"], [[1,11,0,12,0,13]]
    p.declared = p.has_draw_slot = True
    s.start_game_recording()
    s.start_round_recording()
    s.machine.transition(P.TURN)
    s.settle(1, 45, "draw")
    result = s.build_final_settlement_payload(1)["show_result_info"]
    assert result["hu_fan"] == ["自摸边和（4/2）", "精五门（50番）"]
    assert result["hu_score"] == 158
    s.finalize_round_recording()
    assert collect_subrule_fans(s.game_record, p.original_player_index, rules.SUB_RULE) == {"精五门": 1}


def test_bao_visibility_real_natural_vs_wild_and_order():
    s=table()
    h=[11,11,14,14,22,22,25,25,33,33,36,36,41]
    for i in (1,3):
        s.player_list[i].hand_tiles=list(h);s.player_list[i].declared=True
    s.ready_order=[3,1];s.bao=41;s.supplements=[42]
    assert s.view(0)["bao"]==s.view(2)["bao"]==0
    assert s.view(3)["bao"]==41 and s.quote(1,41,"seen")["points"]==120
    p=s.player_list[1];p.hand_tiles.append(41);p.has_draw_slot=True
    assert s.quote(1,41,"draw")["points"]==80
    p.hand_tiles.pop();p.has_draw_slot=False
    s.machine.transition(P.TURN);s.machine.transition(P.RESPONSE)
    s.after_bao=(2,19);s.published_bao_cut=False;s.bao_offers=[3,1]
    w=start(s,s.next_bao_offer())
    assert w["player"]==3 and s.build_pending_action_payload(3)["game_info"]["new_chinese"]["acting"]==3
    w=move(s,w,{3:"pass"});assert w["player"]==1
    w=move(s,w,{1:"hu_self"});assert w["status"]=="END" and s.round_changes[1]==120
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
    s.machine.transition(P.TURN)
    prior_discards(s, 0, 22, 22, 22)
    assert s.after_declaration(0, 22, False)["status"] == P.BAO_REVEAL
    assert s.bao==37 and p.level==3 and s.supplements==[] and not kong_tiles(s,1,"angang")


@pytest.mark.parametrize("initial", [True, False])
def test_bao_pauses_every_seat_and_existing_bao_does_not_retrigger(initial, monkeypatch):
    async def run():
        s = table()
        hand = [11,11,14,14,22,22,25,25,33,33,36,36,41]
        actor = 3 if initial else 2
        for i in ([3] if initial else [3,1]):
            p = s.player_list[i]
            p.hand_tiles = list(hand)
            if not initial:
                p.declared = True
                s.ready_order.append(i)
        actor_hand = s.player_list[actor]
        actor_hand.hand_tiles.append(18)
        actor_hand.has_draw_slot = True
        if not initial:
            s.bao, s.bao_revision = 18, 1
            prior_discards(s, 0, 18, 18)
        prior_discards(s, 1, 19, 19, 19)
        s.supplements = [19,41]
        monkeypatch.setattr(s.rng, "randrange", lambda size: 0)
        now = 10.
        monkeypatch.setattr(s, "_timing_now", lambda: now)
        s.player_list[1].user_id = 2
        turn = start(s, s.turn(actor))
        s.outbound_payloads.clear()
        showing = move(s, turn, {actor: dict(action_type="riichi_cut" if initial else "cut", TileId=18, cutClass=True)})
        wall = list(s.tiles_list)
        banks = [p.remaining_time for p in s.player_list]
        for n, face in enumerate((19,)):
            assert showing["status"] == P.BAO_REVEAL and s.bao == face
            assert s.waiting_players_list == [] and s._action_clocks == {}
            name = "new_chinese_bao_reveal" if initial and n == 0 else "new_chinese_bao_change"
            for i in range(4):
                actions = [p["do_action_info"]["action_list"] for p in s.outbound_payloads if p["player_index"] == i]
                assert actions == ([["cut"], [name]] if n == 0 else [[name]])
                with pytest.raises(ValueError):
                    await s.submit_action(i, "pass")
            s.schedule_bot_actions()
            assert not s.bot_tasks
            with pytest.raises(ValueError, match="宝牌展示尚未结束"):
                move(s, showing, {})
            now += .4
            for i in range(4):
                snapshot, = s.restore_payloads(i)
                info = snapshot["game_info"]["new_chinese"]
                assert info["bao_remaining_ms"] == 600
                assert info["bao_initial"] == (initial and n == 0)
                assert info["bao"] == (face if s.player_list[i].declared else 0)

            async def finish_caption(seconds):
                nonlocal now
                assert seconds == pytest.approx(.6)
                assert s.tiles_list == wall and [p.remaining_time for p in s.player_list] == banks
                now += seconds
            monkeypatch.setattr(asyncio, "sleep", finish_caption)
            s.outbound_payloads.clear()
            showing = await s.resolve_action_window(timeout=0)
        assert showing["status"] == P.RESPONSE
        assert s.bao_revision == (1 if initial else 2)
        assert s.supplements == [41]
    asyncio.run(run())


def test_slow_bao_broadcast_reserves_full_caption_before_next_draw(monkeypatch):
    from types import SimpleNamespace

    async def run():
        s = table()
        now = 10.
        monkeypatch.setattr(s, "_timing_now", lambda: now)
        s.bao, s.bao_revision, s.supplements = 18, 1, [41]
        prior_discards(s, 0, 18, 18)
        s.player_list[2].hand_tiles.append(18)
        s.player_list[2].has_draw_slot = True
        turn = start(s, s.turn(2))
        s.outbound_payloads.clear()
        showing = move(s, turn, {2: dict(action_type="cut", TileId=18, cutClass=True)})
        wall = list(s.tiles_list)
        received = []

        async def send(packet):
            nonlocal now
            words = (packet.get("do_action_info") or {}).get("action_list", [])
            if words == ["new_chinese_bao_change"]:
                assert packet["game_info"]["new_chinese"]["bao_remaining_ms"] == 1000
                now += .35
                assert s.view(0)["bao_remaining_ms"] == 1000
            received.append((words, now))
        s.game_server = SimpleNamespace(user_id_to_connection={
            s.player_list[3].user_id: SimpleNamespace(websocket=SimpleNamespace(send_json=send))})
        await s.flush_outbound_payloads()
        word, delivered_at = received[-1]
        assert word == ["new_chinese_bao_change"] and delivered_at == pytest.approx(10.35)
        assert showing["deadline"] == pytest.approx(11.35)
        now += .25
        await s.flush_outbound_payloads()
        snapshot, = s.restore_payloads(3)
        assert snapshot["game_info"]["new_chinese"]["bao_remaining_ms"] == 750

        async def finish_caption(seconds):
            nonlocal now
            assert seconds == pytest.approx(.75)
            assert s.tiles_list == wall and not s.waiting_players_list and not s._action_clocks
            now += seconds
        monkeypatch.setattr(asyncio, "sleep", finish_caption)
        assert (await s.resolve_action_window())["status"] == P.RESPONSE
        assert (await s.resolve_action_window())["status"] == P.TURN
        await s.flush_outbound_payloads()
        draw_time = next(at for words, at in received if words == ["deal_tile"])
        assert draw_time - delivered_at == pytest.approx(1)
        assert s.tiles_list == wall[1:] and set(s._action_clocks) == {3}
    asyncio.run(run())


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


@pytest.mark.parametrize(("bao", "earlier_discards"), [(11, 2), (12, 2), (11, 1)])
def test_chow_ready_counts_the_claimed_discard_but_not_tiles_from_hand(bao, earlier_discards, monkeypatch):
    s = table()
    prior_discards(s, 1, *([bao] * earlier_discards))
    s.player_list[0].hand_tiles[-1] = 11
    s.player_list[2].hand_tiles = [12,13,21,21,21,32,33,34,41,41,41,45,46]
    s.player_list[3].hand_tiles = [22,23,24,26,27,28,35,36,37,42,42,42,47]
    s.supplements, s.tiles_list[0] = [bao,45], 11
    monkeypatch.setattr(s.rng, "randrange", lambda size: 0)
    now = 0.
    monkeypatch.setattr(s, "_timing_now", lambda: now)

    w = start(s, s.opening_window())
    w = move(s, w, {0: dict(action_type="cut", TileId=11, cutClass=True)})
    w = move(s, w, {2: "chi_right_ting"})
    assert s.player_list[0].discard_tiles == [] and s.player_list[2].combination_tiles == ["s12"]
    w = move(s, w, {2: dict(action_type="riichi_cut", TileId=46, cutClass=False)})
    assert s.player_list[2].riichi_flags == [True]
    assert w["status"] == P.BAO_REVEAL and w["initial"] and s.bao == bao
    assert s.river_counts()[bao] == earlier_discards
    assert s.visible_counts()[bao] == earlier_discards + 1
    now = w["deadline"]
    w = move(s, w, {})
    if bao == 12:
        assert w["status"] == P.RESPONSE and s.bao_revision == 1
        assert s.bao == 12 and s.supplements == [45]
        return
    if earlier_discards == 2:
        assert w["status"] == P.RESPONSE and s.bao_revision == 1
        assert s.bao == 11 and s.supplements == [45]
        return
    if earlier_discards == 1:
        assert w["status"] == P.RESPONSE and s.bao_revision == 1
        w = move(s, w, {})
        assert w["status"] == P.TURN and w["player"] == 3
        w = move(s, w, {3: dict(action_type="riichi_cut", TileId=11, cutClass=True)})
        assert s.player_list[3].riichi_flags == [True] and s.ready_order == [2,3]
    assert w["status"] == P.BAO_REVEAL and not w["initial"] and s.bao == 45
    assert s.bao_revision == 2 and s.supplements == []


def test_exposed_old_bao_meld_triggers_change_before_post_meld_cut(monkeypatch):
    s = table()
    s.bao, s.bao_revision = 14, 1
    s.supplements = [45, 46]
    prior_discards(s, 0, 14, 14)
    s.player_list[0].hand_tiles[-1] = 12
    s.player_list[1].hand_tiles = [13, 14, 11, 15, 16, 17, 18, 25, 26, 27, 28, 29, 31]
    monkeypatch.setattr(s.rng, "randrange", lambda size: 0)

    w = start(s, s.opening_window())
    w = move(s, w, {0: dict(action_type="cut", TileId=12, cutClass=True)})
    w = move(s, w, {1: "chi_right"})

    assert s.visible_counts()[14] == 3
    assert s.bao == 45 and s.bao_revision == 2
    assert w["status"] == P.BAO_REVEAL and not w["initial"]
    assert s._bao_resume_turn == (1, True)

    w["deadline"] = s._timing_now() - 1
    w = move(s, w, {})
    assert w["status"] == P.AFTER_MELD and w["player"] == 1
    assert s._bao_resume_turn is None


def test_exposed_pung_of_an_old_bao_counts_toward_the_next_change():
    s = table()
    s.bao, s.bao_revision = 12, 1
    s.player_list[1].combination_tiles = ["k11"]
    assert s.visible_counts()[11] == 3
    assert not s.bao_discarded_three_times()
    s.bao = 11
    assert s.bao_discarded_three_times()


def test_exhausted_supplements_keep_old_bao_and_offer_only_the_new_declarer():
    s = table()
    s.bao, s.bao_revision, s.supplements = 15, 2, []
    prior_discards(s, 0, 15, 15, 15)
    first, second = s.player_list[1], s.player_list[2]
    first.declared, s.ready_order = True, [1]
    first.hand_tiles = [14,16,22,23,24,32,33,34,42,42,42,46,46]
    second.hand_tiles = [14,16,21,22,23,31,32,33,43,43,43,45,45,47]
    second.has_draw_slot = True
    assert s.quote(1, 15, "seen")
    w = start(s, s.turn(2))
    s.outbound_payloads.clear()
    w = move(s, w, {2: dict(action_type="riichi_cut", TileId=47, cutClass=True)})
    assert s.bao == 15 and s.bao_revision == 2 and not s.changed_bao
    assert s.ready_order == [1, 2] and w["status"] == P.BAO and w["player"] == 2
    assert w["actions"] == {2: ["hu_self", "pass"]}
    assert [p["do_action_info"]["action_list"] for p in s.outbound_payloads if p.get("do_action_info")] == [["cut"]] * 4
    assert move(s, w, {2: "hu_self"})["status"] == P.END
    assert s.deferred_hu_settlements[0]["fan_details"]["source"] == "bao_seen"


@pytest.mark.parametrize("action", ["peng", "gang"])
def test_pung_and_direct_kong_only_contribute_the_original_discard(action):
    s = table()
    s.player_list[0].hand_tiles[-1] = 11
    s.player_list[1].hand_tiles = [11,11,11,16,17,18,23,24,25,35,37,38,39]
    w = start(s, s.opening_window())
    w = move(s, w, {0: dict(action_type="cut", TileId=11, cutClass=True)})
    w = move(s, w, {1: action})
    if action == "gang":
        assert w["status"] == P.ROB_KONG
        move(s, w, {})
    assert s.discard_log == [(0,11)] and s.river_counts()[11] == 0
    assert s.visible_counts()[11] == (3 if action == "peng" else 4)


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
    s.discard_log = [(1,11)]
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
    assert s.discard_log == [(1,11)]
    w = move(s, w, {2: {"action_type":"riichi_cut", "TileId":46, "cutClass":False}})
    assert w["before_bao"] and s.bao == 18
    w = move(s, w, {3:"hu"})
    assert w["status"] == P.END and not p2.discard_tiles
    assert s.discard_log == [(1,11), (2,46)]
    entry = s.deferred_hu_settlements[0]
    assert entry["fan_details"]["source"] == "discard" and entry["recycle_discard"]
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ["new_chinese", "rob_kan", 0, 11, "jiagang", None, 1] in ticks
    hu = next(tick for tick in ticks if tick[0].startswith("hu"))
    assert list(map(int,hu[7:9])) == [2,1]


def test_robbed_direct_kong_uses_original_discard_and_charges_declarer():
    s=table()
    p0,p1,p2,_=s.player_list
    prior_discards(s, 0, 11)
    p1.hand_tiles=[11,11,11,16,17,18,23,24,25,35,37,38,39]
    p2.hand_tiles=[12,13,21,21,21,32,33,34,41,41,41,45,45]
    p2.declared=True;s.ready_order=[2];s.bao=18
    s.machine.transition(P.TURN);s.machine.transition(P.RESPONSE)
    w=start(s,s.rob_window(1,11,"gang",0))
    w=move(s,w,{2:"hu"})
    assert w["status"]==P.END and p0.discard_tiles==[]
    assert s.discard_log == [(0,11)]
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


@pytest.mark.parametrize("winner", [None, 1, 3])
def test_fixed_winds_after_dealer_change_and_repeat(winner, monkeypatch):
    state = table(exchange=True)
    state.dealer_index = 1
    state.initialize_round()
    state.machine.transition(P.TURN)
    state.finish(winner)
    state.machine.transition(P.READY)
    state.advance_round_after_ready()
    state.initialize_round()

    dealer = 2 if winner == 3 else 1
    assert state.dealer_index == state.current_player_index == dealer
    assert state.repeat == (0 if winner == 3 else 1)
    assert [(p.user_id, p.player_index, p.original_player_index) for p in state.player_list] == [
        (101+i, i, i) for i in range(4)]
    for i, p in enumerate(state.player_list):
        info = state.restore_payloads(i)[0]["game_info"]
        assert info["dealer_index"] == info["current_player_index"] == dealer
        assert info["players_info"][i]["user_id"] == p.user_id
        assert len(info["self_hand_tiles"]) == (14 if i == dealer else 13)
        assert p.has_draw_slot == (i == dealer)
    opening = start(state, state.opening_window())
    presentation = move(state, opening, {})
    monkeypatch.setattr(state, "_timing_now", lambda: presentation["deadline"])
    first_turn = move(state, presentation, {})
    assert opening["player"] == presentation["player"] == first_turn["player"] == dealer


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
        winner = (s.dealer_index + 1) % 4
        for i in list(s.waiting_players_list):
            options = s.action_dict[i]
            if "ready" in options:
                await s.submit_action(i,"ready")
            elif i == winner and "hu_self" in options:
                await s.submit_action(i,"hu_self")
            elif "cut" in options:
                action = "riichi_cut" if i == winner else "cut"
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
    for n, rd in enumerate(rounds):
        dealer = n % 4
        assert rd["seats"] == [0,1,2,3]
        assert rd["dealer_index"] == rd["start_player_index"] == dealer
        assert len(rd[f"p{dealer}_tiles"]) == 14
        tick = next(t for t in rd["action_ticks"] if t[0]=="hu_self")
        expected = [-32]*4
        expected[dealer] = -56
        expected[(dealer+1)%4] = 120
        assert tick[4] == expected
    assert all(p.score == 0 for p in s.player_list)


@pytest.mark.parametrize("mode", ["draw", "discard", "seen"])
def test_last_copy_single_wait_counts_own_pair_tile(mode):
    state = table()
    player = state.player_list[1]
    player.declared = True
    player.combination_tiles = ["s12", "s12"]
    player.hand_tiles = [12, 21, 22, 23, 41, 41, 41]
    if mode == "draw":
        player.hand_tiles.append(12)
        player.has_draw_slot = True
    elif mode == "discard":
        state.player_list[2].discard_tiles = [12]
    else:
        state.bao = 12
    assert state.view(3)["visible"][12] == (3 if mode == "discard" else 2)
    assert state.quote(1, 12, mode, 2)["fans"]["绝张"] == 5

    player.combination_tiles[0] = "s32"
    assert "绝张" not in state.quote(1, 12, mode, 2)["fans"]
