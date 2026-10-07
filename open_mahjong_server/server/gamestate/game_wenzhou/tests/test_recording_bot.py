"""Complete games, replay arithmetic, bot visibility and lifecycle timing."""

import asyncio
import copy
from types import SimpleNamespace

import pytest

from ..actions import empty_actions
from ..bot import choose_action, connections, pick_cut, play_bot
from ..state_machine import Phase as P
from .helpers import STANDARD, WAIT, SocketRecorder, assert_conserved, fixture_state, make_state, response_map
from .replay_check import reduce_round
from .test_flow import JUNK, cut, open_claim, open_turn


def run_full_draw(seed=20241002, *, tips=False):
    state = make_state(random_seed=seed, tips=tips)
    state.start_game_recording()
    state.initialize_round()
    state.start_round_recording()
    state.open_action_window(state.opening_window())
    for _ in range(200):
        if state.machine.phase == P.END:
            break
        if state.machine.phase in (P.TURN, P.DISCARD_ONLY):
            actor = state.current_player_index
            cut(state, state.player_list[actor].hand_tiles[-1], drawn=state.player_list[actor].has_draw_slot)
        else:
            state.apply_action_results(state.live_pending_window, response_map(state.live_pending_window))
        assert_conserved(state)
    assert state.machine.phase == P.END
    state.finalize_round_recording()
    state.finalize_game_recording()
    return state


def test_full_seeded_hand_exhausts_exactly_66_draws_then_replays_without_randomness():
    state = run_full_draw()
    assert len(state.tiles_list) == 4
    assert sum(state.natural_draw_count.values()) == 66
    assert sum(len(p.discard_tiles) for p in state.player_list) == 67
    assert [len(p.hand_tiles) for p in state.player_list] == [16] * 4
    round_data = state.game_record["game_round"]["round_index_1"]
    before = copy.deepcopy(round_data)
    result = reduce_round(round_data)
    assert result["wall"] == state.tiles_list
    assert [sorted(h) for h in result["hands"]] == [sorted(p.hand_tiles) for p in state.player_list]
    assert result["rivers"] == [p.discard_tiles for p in state.player_list]
    assert result["scores"] == [0] * 4
    assert round_data == before
    state.finalize_round_recording()
    assert state.game_record["game_round"]["round_index_1"] == before


def test_full_bot_match_uses_live_queue_lifecycle_and_every_round_replays():
    async def run():
        state = make_state(player_list=[1,2,3,4], bot_speed="instant")
        await asyncio.wait_for(state.run_game_loop(), timeout=30)
        assert state.machine.phase == P.FINISHED and state.lifecycle_completed
        assert state.dealer_changes == 4
        assert sum(p.score for p in state.player_list) == 0
        assert state.waiting_players_list == [] and not any(state.action_dict.values())
        assert all(task.done() for task in state.bot_tasks)
        assert len(state.game_record["game_round"]) >= 4
        assert "end_time" in state.game_record["game_title"]
        for round_data in state.game_record["game_round"].values():
            reduce_round(round_data)
        assert_conserved(state)
        await state.complete_game_lifecycle()
        return state
    asyncio.run(run())


def test_single_self_draw_replay_stores_rule_identity_and_exact_scores():
    state = fixture_state({0: STANDARD})
    state.start_game_recording()
    state.start_round_recording()
    window = open_turn(state)
    state.apply_action_results(window, {0: {"action_type": "hu_self"}})
    state.finalize_round_recording()
    title = state.game_record["game_title"]
    assert title["rule"] == "wenzhou" and title["sub_rule"] == "wenzhou/mil2024"
    assert title["rule_version"] == "mil-wenzhou-2024-om1"
    round_data = state.game_record["game_round"]["round_index_1"]
    assert round_data["wenzhou"]["caishen"] == 45
    assert [tick for tick in round_data["action_ticks"] if tick[0] == "wenzhou" and tick[1] == "win_source"]
    result = reduce_round(round_data)
    assert result["scores"] == [p.score for p in state.player_list]
    before = copy.deepcopy(round_data)
    state.record_visible_action(dict(action="hu_self", player=0, tile=43))
    assert round_data == before


def test_recording_does_not_duplicate_win_or_wait_snapshots():
    state = fixture_state({0: STANDARD}, tips=True)
    state.start_game_recording()
    state.start_round_recording()
    window = open_turn(state)
    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    before = copy.deepcopy(ticks)
    state.record_waits()
    assert ticks == before
    state.apply_action_results(window, {0: {"action_type": "hu_self"}})
    before = copy.deepcopy(ticks)
    state.record_visible_action(dict(action="hu_self", player=0, tile=43))
    assert ticks == before
    state.finalize_round_recording()
    reduce_round(state.game_record["game_round"]["round_index_1"])


@pytest.mark.parametrize("tips", [False, True])
@pytest.mark.parametrize("drawn_preview", [False, True])
def test_replay_wait_snapshots_are_independent_of_live_tips(tips, drawn_preview):
    from .test_overwater_waits import MIXED_WAIT

    seat = 0 if drawn_preview else 1
    hand = MIXED_WAIT + [39] if drawn_preview else MIXED_WAIT
    state = fixture_state({seat: hand}, caishen=11, tips=tips)
    key = ",".join(map(str, sorted(MIXED_WAIT)))
    state.start_game_recording()
    state.start_round_recording()
    round_data = state.game_record["game_round"]["round_index_1"]
    initial = copy.deepcopy(round_data["wenzhou_waits"][seat])
    assert key in initial
    rows = {row["tile"]: row for row in initial[key]}
    assert rows[13]["ron"] and rows[13]["ron_multiplier"] == 2
    assert rows[14]["ron"] and rows[14]["self_draw_multiplier"] == 2
    assert state.game_record["game_title"]["tips"] is tips
    live = state.build_game_start_payload(seat)["game_info"]["wenzhou_waits"]
    assert live == (initial if tips else {})

    window = open_turn(state)
    ticks = round_data["action_ticks"]
    saved = [tick for tick in ticks if tick[:3] == ["wenzhou", "waits", seat]]
    assert saved[-1][3] == initial
    before = copy.deepcopy(ticks)
    state.record_waits()
    assert ticks == before

    # Advance a real discard and any ordinary claim window. The saved cache
    # must follow the new hand in a tips-disabled room as well as an enabled one.
    tile = 39 if drawn_preview else state.player_list[0].hand_tiles[-1]
    window = cut(state, tile, drawn=True)
    if state.machine.phase == P.RESPONSE:
        state.apply_action_results(window, response_map(window))
    saved = [tick for tick in ticks if tick[:3] == ["wenzhou", "waits", seat]]
    assert saved[-1][3] != initial
    state.tips = True
    expected = copy.deepcopy(state.authoritative_waits(seat))
    state.tips = tips
    assert saved[-1][3] == expected
    assert state.build_game_start_payload(seat)["game_info"]["wenzhou_waits"] == (expected if tips else {})
    assert round_data["wenzhou_waits"][seat] == initial
    assert_conserved(state)


def white_rob_kong_replay():
    """Construct a full, physically legal hand from its 17/16 starting deal."""
    wait_white = [11,13,21,22,23,31,32,33,17,18,19,41,41,41,43,43]
    state = fixture_state({1:[46,46,46] + JUNK[:13], 2:wait_white}, caishen=12)
    # Locate the fourth white and swap it into the dealer's drawn slot without
    # adding, dropping or duplicating any physical tile.
    dealer = state.player_list[0]
    if 46 in dealer.hand_tiles:
        where = dealer.hand_tiles.index(46)
        dealer.hand_tiles[where], dealer.hand_tiles[-1] = dealer.hand_tiles[-1], dealer.hand_tiles[where]
    else:
        source = state.tiles_list if 46 in state.tiles_list else state.player_list[3].hand_tiles
        where = source.index(46)
        source[where], dealer.hand_tiles[-1] = dealer.hand_tiles[-1], source[where]
    # Seat 2 first passes the opening white, then a real unhelpful draw clears
    # overwater; it can subsequently rob the later added white.
    where = state.tiles_list.index(39)
    state.tiles_list[where], state.tiles_list[0] = state.tiles_list[0], state.tiles_list[where]
    assert_conserved(state)
    state.start_game_recording()
    state.start_round_recording()
    open_turn(state)
    window = cut(state, 46, drawn=True)
    state.apply_action_results(window, response_map(window, **{"1":"peng"}))
    window = cut(state, 41, drawn=False)
    state.apply_action_results(window, response_map(window))
    assert state.current_player_index == 2 and state.player_list[2].hand_tiles[-1] == 39
    for actor in (2,3,0):
        assert state.current_player_index == actor
        window = cut(state, state.player_list[actor].hand_tiles[-1], drawn=True)
        state.apply_action_results(window, response_map(window))
        assert_conserved(state)
    assert state.current_player_index == 1
    window = state.live_pending_window
    assert "jiagang" in window["actions"][1]
    pending = state.apply_action_results(window,{1:dict(action_type="jiagang",target_tile=46)})
    assert "hu" in pending["actions"][2]
    state.apply_action_results(pending,response_map(pending,**{"2":"hu"}))
    assert_conserved(state)
    state.finalize_round_recording()
    state.finalize_game_recording()
    return state


def white_concealed_kong_replay():
    state = fixture_state({0:[46]*4 + JUNK[:13]},caishen=12)
    state.start_game_recording()
    state.start_round_recording()
    window = open_turn(state)
    pending = state.apply_action_results(window,{0:dict(action_type="angang",target_tile=46)})
    state.apply_action_results(pending,{})
    for _ in range(200):
        if state.machine.phase == P.END:
            break
        if state.machine.phase in (P.TURN,P.DISCARD_ONLY):
            actor=state.current_player_index
            cut(state,state.player_list[actor].hand_tiles[-1],drawn=state.player_list[actor].has_draw_slot)
        else:
            state.apply_action_results(state.live_pending_window,response_map(state.live_pending_window))
        assert_conserved(state)
    state.finalize_round_recording()
    state.finalize_game_recording()
    return state


def test_white_rob_added_kong_replay_starts_from_complete_deal_and_preserves_pung():
    state = white_rob_kong_replay()
    result = reduce_round(state.game_record["game_round"]["round_index_1"])
    assert result["melds"][1] == [{"code":"k12","physical":[46,46,46]}]
    assert result["won"][2] == [46]
    assert result["scores"] == [p.score for p in state.player_list]
    assert result["final_meta"]["score_details"]["source"] == "rob_kong"


def test_white_concealed_kong_replay_removes_white_physical_ids_and_retains_draw_scores():
    state = white_concealed_kong_replay()
    result = reduce_round(state.game_record["game_round"]["round_index_1"])
    assert result["melds"][0] == [{"code":"G12","physical":[46]*4}]
    assert result["scores"] == [12,-4,-4,-4]
    assert result["final_meta"]["score_details"]["source"] == "draw"


def test_record_persistence_none_success_and_failure_paths():
    state = make_state()
    assert state.persist_game_record() is None
    state.start_game_recording()
    calls = []
    state.db_manager = SimpleNamespace(store_wenzhou_game_record=lambda *args: calls.append(args) or "WZ_TEST")
    assert state.persist_game_record() == "WZ_TEST"
    assert calls[0][0] is state.game_record
    def failure(*args):
        raise RuntimeError("injected persistence failure")
    state.db_manager = SimpleNamespace(store_wenzhou_game_record=failure)
    assert state.persist_game_record() is None


def test_bot_prefers_win_ready_and_natural_kongs_only():
    state = fixture_state({0: STANDARD})
    assert choose_action(state, 0, ["cut", "hu_self"]) == {"action_type": "hu_self"}
    assert choose_action(state, 0, ["ready"]) == {"action_type": "ready"}
    assert choose_action(state, 0, ["hu", "pass"]) == {"action_type": "hu"}
    state = fixture_state({0: [46] * 4 + JUNK[:13]}, caishen=12)
    assert choose_action(state, 0, ["angang", "cut"]) == {"action_type": "angang", "target_tile": 46}
    state = fixture_state({0: JUNK[:13] + [46]}, caishen=12, melds={0: [("k12", [46] * 3)]})
    assert choose_action(state, 0, ["jiagang", "cut"]) == {"action_type": "jiagang", "target_tile": 46}


def test_bot_cut_ignores_opponents_hidden_hands_and_wall_order():
    state = fixture_state({0: WAIT + [39]})
    selected = pick_cut(state, 0)
    assert selected in state.legal_discard_tiles(0)
    expected = choose_action(state, 0, ["cut"])
    for player in state.player_list[1:]:
        player.hand_tiles.reverse()
        player.hand_tiles[:] = [11] * len(player.hand_tiles)
    state.tiles_list.reverse()
    assert pick_cut(state, 0) == selected
    assert choose_action(state, 0, ["cut"]) == expected
    assert connections([45,45], 45) > connections([11,19],45)


@pytest.mark.parametrize("action,tile,held", [("peng",46,[46,46]), ("chi_mid",46,[11,13])])
def test_bot_calls_legal_natural_sets_without_breaking_an_existing_wait(action,tile,held):
    state = fixture_state({1: held + JUNK}, caishen=12, actor=0, phase=P.RESPONSE,
                          drawn=False, river={0: [tile]})
    window = open_claim(state, 0, tile)
    assert choose_action(state, 1, [action,"pass"]) == {"action_type": action}
    state.player_list[1].hand_tiles = WAIT
    assert choose_action(state, 1, [action,"pass"]) == {"action_type": "pass"}
    assert choose_action(state, 1, ["gang","pass"]) == {"action_type": "gang"}
    assert choose_action(state, 1, ["pass"]) == {"action_type": "pass"}


def test_bot_stale_tick_does_not_enqueue_and_single_schedule_uses_actual_queue():
    async def run():
        state = fixture_state({0: STANDARD})
        state.player_list[0].user_id = 1
        state.bot_speed = "instant"
        window = open_turn(state)
        await play_bot(state, 0, state.server_action_tick - 1)
        assert state.action_queues[0].empty()
        state.schedule_bot_actions()
        state.schedule_bot_actions()
        assert len(state.bot_tasks) == 1
        await asyncio.gather(*list(state.bot_tasks))
        assert state.action_queues[0].qsize() == 1
        response = await state.wait_action()
        assert response[0]["action_type"] == "hu_self"
        state.apply_action_results(window, response)
        assert state.machine.phase == P.END
    asyncio.run(run())


def test_wait_action_wakes_for_async_response_and_cancels_pending_waiters():
    async def run():
        state = fixture_state({0: STANDARD})
        state.step_time = 2
        window = open_turn(state)
        waiter = asyncio.create_task(state.wait_action())
        await asyncio.sleep(0)
        await state.submit_action(0, "hu_self")
        result = await asyncio.wait_for(waiter, 1)
        assert result[0]["action_type"] == "hu_self"
        state.apply_action_results(window, result)
        await asyncio.sleep(0)
        assert not [t for t in asyncio.all_tasks() if t is not asyncio.current_task() and not t.done()]
    asyncio.run(run())


def test_ready_explicit_reply_keeps_round_time_and_final_payloads_reach_all_views():
    async def run():
        state = fixture_state({0: STANDARD})
        sockets = {101+i: SocketRecorder() for i in range(4)}
        state.game_server = SimpleNamespace(user_id_to_connection={uid:SimpleNamespace(websocket=sock) for uid,sock in sockets.items()})
        state.settle_win(0, "self_draw", 43)
        await state.present_final_settlements()
        assert all(len(s.messages) == 1 for s in sockets.values())
        state.machine.transition(P.READY)
        state.player_list[0].remaining_time = 10
        state.action_dict = {**empty_actions(), 0:["ready"]}
        state.waiting_players_list = [0]
        await state.submit_action(0, "ready")
        result = await state.wait_action(timeout=2)
        assert result[0]["action_type"] == "ready"
        assert state.player_list[0].remaining_time == 10
    asyncio.run(run())
