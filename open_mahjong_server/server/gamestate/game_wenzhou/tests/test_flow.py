"""Real joker scoring through valid physical action windows and settlements."""

import copy
from collections import Counter

import pytest

from ..actions import CHOWS, claim_actions, empty_actions, turn_actions
from ..state_machine import Phase as P
from .helpers import BASE, SOFT, STANDARD, WAIT, assert_conserved, fixture_state, response_map, snapshot


JUNK = [21, 22, 24, 25, 27, 28, 31, 32, 34, 35, 37, 38, 41, 43]


def open_claim(state, actor, tile, *, rob=False):
    phase = P.KONG if rob else P.RESPONSE
    return state.open_action_window(state._window(phase, actor, tile, claim_actions(state, actor, tile, rob=rob)))


def open_turn(state, actor=0):
    return state.open_action_window(state.begin_turn(actor))


def cut(state, tile, *, drawn=None):
    window = state.live_pending_window
    actor = window["player"]
    return state.apply_action_results(window, {actor: dict(action_type="cut", TileId=tile, cutClass=drawn)})


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("streak", range(4))
def test_self_draw_pays_from_all_three_and_caishen_ledger_has_no_dealer_factor(winner, streak):
    state = fixture_state({winner: STANDARD}, actor=winner)
    state.dealer_streak = streak
    counts = [p.hand_tiles.count(45) for p in state.player_list]
    assert state.can_win(winner, "self_draw", 43)
    window = open_turn(state, winner)
    state.apply_action_results(window, {winner: {"action_type": "hu_self"}})
    assert state.machine.phase == P.END
    assert state.round_settlement["score"]["multiplier"] == 2
    expected = [4 * n - sum(counts) for n in counts]
    for payer in range(4):
        if payer != winner:
            paid = 2 * (2 * (streak + 1) if 0 in (payer, winner) else 1)
            expected[payer] -= paid
            expected[winner] += paid
    assert state.round_changes == expected and sum(expected) == 0
    assert state.caishen_changes == [4 * n - sum(counts) for n in counts]
    first = state.build_final_settlement_payload(winner)
    second = state.build_final_settlement_payload((winner + 1) % 4)
    assert first["show_result_info"]["score_changes"] == dict(enumerate(expected))
    assert second["show_result_info"]["score_changes"] == dict(enumerate(expected))
    assert [p.score for p in state.player_list] == expected
    assert len(state.deferred_hu_settlements) == 1
    assert all(len(p.score_history) == 1 for p in state.player_list)
    with pytest.raises(ValueError):
        state.settle_win(winner, "self_draw", 43)
    assert_conserved(state)


@pytest.mark.parametrize("complete,factor", [(STANDARD, 2), (SOFT, 1)])
@pytest.mark.parametrize("winner", [1, 2, 3])
def test_discard_win_recovers_only_river_tile_and_all_three_pay(complete, factor, winner):
    hand = list(complete)
    hand.remove(43)
    state = fixture_state({winner: hand}, actor=0, phase=P.RESPONSE, drawn=False, river={0: [43]})
    window = open_claim(state, 0, 43)
    assert "hu" in window["actions"][winner]
    responses = response_map(window, **{str(winner): "hu"})
    state.apply_action_results(window, responses)
    assert state.round_settlement["score"]["multiplier"] == factor
    assert state.player_list[0].discard_tiles == []
    assert state.player_list[0].discard_origin_tiles == [43]
    assert state.player_list[winner].hand_tiles == sorted(hand)
    assert state.player_list[winner].won_tiles == [43]
    assert state.deferred_hu_settlements[0]["recycle_discard"]
    wins = next(entry["changes"] for entry in state.ledger if entry["kind"] == "win")
    assert all(wins[payer] < 0 for payer in range(4) if payer != winner)
    assert_conserved(state)


def test_discarded_caishen_can_be_won_and_moves_only_its_count_to_winner():
    hand = BASE + [43]
    state = fixture_state({1: hand}, actor=0, phase=P.RESPONSE, drawn=False, river={0: [45]})
    before = [p.hand_tiles.count(45) for p in state.player_list]
    window = open_claim(state, 0, 45)
    assert "hu" in window["actions"][1]
    assert all(not set(CHOWS + ("peng", "gang")).intersection(actions) for actions in window["actions"].values())
    state.apply_action_results(window, response_map(window, **{"1": "hu"}))
    before[1] += 1
    assert state.caishen_counts == before
    assert state.player_list[1].won_tiles == [45]
    assert_conserved(state)


@pytest.mark.parametrize("caishen,hand,factor", [
    (46, [11,11,14,14,17,17,21,21,24,24,27,27,31,31,34,34,42], 4),
    (46, [46,46,46,11,14,17,21,24,27,31,34,37,41,42,43,44,45], 2),
    (46, [46,46,46,14,15,16,21,22,23,31,32,33,41,41,41,42,42], 4),
    (11, [11,12,13,14,15,16,21,22,23,31,32,33,41,41,41,42,42], 2),
    (11, [46,12,13,14,15,16,21,22,23,31,32,33,41,41,41,42,42], 2),
])
def test_special_wins_are_accepted_by_authoritative_state(caishen, hand, factor):
    state = fixture_state({0: hand}, caishen=caishen)
    before = list(state.player_list[0].hand_tiles)
    window = open_turn(state)
    assert "hu_self" in window["actions"][0]
    state.apply_action_results(window, {0: {"action_type": "hu_self"}})
    assert state.round_settlement["score"]["multiplier"] == factor
    assert state.player_list[0].hand_tiles == before
    assert_conserved(state)


def test_all_response_bodies_validate_before_any_pass_or_tile_mutation():
    # Both rivals legally wait on 43; each has no more than two physical 41s.
    first = [11,12,13,14,15,16,21,22,23,31,32,33,41,41,43,43]
    second = [11,12,13,17,18,19,24,25,26,34,35,36,42,42,43,43]
    # The displayed last discard is 41, first player's triplet completion.
    state = fixture_state({1: first, 2: second}, actor=0, phase=P.RESPONSE, drawn=False, river={0: [41]})
    window = state.open_action_window(state._window(P.RESPONSE, 0, 41,
        {0: [], 1: ["hu", "pass"], 2: ["pass"], 3: []}))
    before = snapshot(state)
    with pytest.raises(ValueError):
        state.apply_action_results(window, {1: {"action_type": "pass"}, 2: {"action_type": "hu"}})
    assert snapshot(state) == before


@pytest.mark.parametrize("action,tile,held", [
    ("chi_left", 13, [11, 46]), ("chi_mid", 46, [11, 13]), ("chi_right", 11, [46, 13])])
def test_white_mixed_chow_preserves_physical_mask_and_forces_discard(action, tile, held):
    state = fixture_state({1: held + JUNK}, caishen=12, actor=0, phase=P.RESPONSE,
                          drawn=False, river={0: [tile]})
    state.start_game_recording()
    state.start_round_recording()
    window = open_claim(state, 0, tile)
    assert action in window["actions"][1]
    state.apply_action_results(window, response_map(window, **{"1": action}))
    player = state.player_list[1]
    assert state.machine.phase == P.DISCARD_ONLY
    assert state.action_dict[1] == ["cut"]
    assert player.combination_tiles == ["s12"]
    assert player.meld_records[0]["logical"] == [12 if t == 46 else t for t in player.meld_records[0]["physical"]]
    assert Counter(player.meld_records[0]["physical"]) == Counter([11, 46, 13])
    assert player.combination_mask[0][:2] == [1, tile]
    assert not player.has_draw_slot
    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ["wenzhou", "meld", 1, player.combination_mask[0], "s12"] in ticks
    assert_conserved(state)


@pytest.mark.parametrize("owner", [1, 2, 3])
def test_white_pung_uses_called_orientation_and_cannot_immediately_kong(owner):
    hand = [46, 46] + [31] * 4 + [21,22,24,25,27,28,34,35,37,38]
    state = fixture_state({owner: hand}, caishen=12, actor=0, phase=P.RESPONSE, drawn=False, river={0: [46]})
    window = open_claim(state, 0, 46)
    assert "peng" in window["actions"][owner]
    state.apply_action_results(window, response_map(window, **{str(owner): "peng"}))
    p = state.player_list[owner]
    assert p.combination_tiles == ["k12"]
    assert p.meld_records[0]["physical"] == [46] * 3
    assert state.action_dict[owner] == ["cut"]
    marked = [i // 2 for i in range(0, 6, 2) if p.combination_mask[0][i] == 1]
    assert marked == [{1: 0, 2: 1, 3: 2}[owner]]
    assert_conserved(state)


def test_direct_white_kong_collects_from_all_and_draws_wall_tail():
    state = fixture_state({1: [46] * 3 + JUNK[:13]}, caishen=12, actor=0,
                          phase=P.RESPONSE, drawn=False, river={0: [46]})
    tail, front, before_len = state.tiles_list[-1], state.tiles_list[0], len(state.tiles_list)
    window = open_claim(state, 0, 46)
    assert "gang" in window["actions"][1]
    state.apply_action_results(window, response_map(window, **{"1": "gang"}))
    p = state.player_list[1]
    assert p.combination_tiles == ["g12"] and p.meld_records[0]["physical"] == [46] * 4
    assert p.hand_tiles[-1] == tail and p.has_draw_slot and p.draw_kind == "kong"
    assert state.tiles_list[0] == front and len(state.tiles_list) == before_len - 1
    assert [p.score for p in state.player_list] == [-2, 4, -1, -1]
    assert_conserved(state)


def test_concealed_white_kong_waits_for_empty_commit_window_then_scores():
    state = fixture_state({0: [46] * 4 + JUNK[:13]}, caishen=12)
    tail = state.tiles_list[-1]
    before = snapshot(state)
    window = open_turn(state)
    assert "angang" in window["actions"][0]
    next_window = state.apply_action_results(window, {0: {"action_type": "angang", "target_tile": 46}})
    assert state.machine.phase == P.KONG and not any(next_window["actions"].values())
    assert len(state.player_list[0].hand_tiles) == 17 and state.player_list[0].combination_tiles == []
    assert state.round_changes == [0] * 4
    state.apply_action_results(next_window, {})
    assert state.pending_kong is None
    assert state.player_list[0].combination_tiles == ["G12"]
    assert state.player_list[0].combination_mask == [[2, 46] * 4]
    assert state.player_list[0].hand_tiles[-1] == tail
    assert state.round_changes == [12, -4, -4, -4]
    assert_conserved(state)


def test_added_white_kong_zero_points_preserves_mask_and_no_impossible_calls():
    state = fixture_state({0: JUNK[:13] + [46]}, caishen=12,
                          melds={0: [("k12", [46] * 3)]})
    window = open_turn(state)
    assert "jiagang" in window["actions"][0]
    pending = state.apply_action_results(window, {0: {"action_type": "jiagang", "target_tile": 46}})
    assert all("peng" not in offers and "gang" not in offers for offers in pending["actions"].values())
    assert state.player_list[0].combination_tiles == ["k12"]
    state.apply_action_results(pending, response_map(pending))
    p = state.player_list[0]
    assert p.combination_tiles == ["g12"]
    assert p.combination_mask[0][:4] == [3, 46, 1, 46]
    assert state.round_changes == [0] * 4
    assert state.ledger == [{"kind": "jiagang", "player": 0, "changes": [0] * 4}]
    assert_conserved(state)


def test_rob_added_white_kong_retains_original_pung_and_collects_no_kong_fee():
    winner_hand = [11,13,21,22,23,31,32,33,17,18,19,41,41,41,43,43]
    state = fixture_state({0: JUNK[:13] + [46], 1: winner_hand}, caishen=12,
                          melds={0: [("k12", [46] * 3)]})
    window = open_turn(state)
    pending = state.apply_action_results(window, {0: {"action_type": "jiagang", "target_tile": 46}})
    assert "hu" in pending["actions"][1]
    state.apply_action_results(pending, response_map(pending, **{"1": "hu"}))
    assert state.round_settlement["source"] == "rob_kong"
    assert state.player_list[0].combination_tiles == ["k12"]
    assert state.player_list[0].hand_tiles.count(46) == 0
    assert state.player_list[1].won_tiles == [46]
    assert not state.deferred_hu_settlements[0]["recycle_discard"]
    assert not any(entry["kind"] in ("ming", "angang", "jiagang") for entry in state.ledger)
    assert_conserved(state)


def test_head_bump_picks_nearest_winner_over_other_claims():
    # First and second have two distinct standard waits with no physical overlap
    # beyond the four legal copies of 43 needed in their two pairs.
    first = [11,12,13,14,15,16,21,22,23,31,32,33,41,41,41,43]
    second = [11,12,13,17,18,19,24,25,26,34,35,36,42,42,42,43]
    state = fixture_state({1: first, 2: second}, actor=0, phase=P.RESPONSE, drawn=False, river={0: [43]})
    window = open_claim(state, 0, 43)
    assert "hu" in window["actions"][1] and "hu" in window["actions"][2]
    state.apply_action_results(window, response_map(window, **{"1": "hu", "2": "hu"}))
    assert state.round_settlement["winner"] == 1
    assert state.player_list[2].is_hu is False and state.player_list[2].won_tiles == []
    assert_conserved(state)


def test_pung_beats_next_player_chow():
    state = fixture_state({1: [11, 13] + JUNK, 2: [46,46] + JUNK}, caishen=12,
                          actor=0, phase=P.RESPONSE, drawn=False, river={0: [46]})
    window = open_claim(state, 0, 46)
    assert "chi_mid" in window["actions"][1] and "peng" in window["actions"][2]
    state.apply_action_results(window, response_map(window, **{"1": "chi_mid", "2": "peng"}))
    assert state.current_player_index == 2
    assert state.player_list[1].combination_tiles == []
    assert state.player_list[2].combination_tiles == ["k12"]
    assert_conserved(state)


def test_no_claim_draws_next_seat_from_wall_front_and_resets_overwater():
    state = fixture_state(actor=3, phase=P.RESPONSE, drawn=False, river={3: [39]})
    state.player_list[0].passed_win_multiplier = 4
    front, previous_size = state.tiles_list[0], len(state.tiles_list)
    window = open_claim(state, 3, 39)
    state.apply_action_results(window, response_map(window))
    assert state.current_player_index == 0 and state.machine.phase == P.TURN
    assert state.player_list[0].hand_tiles[-1] == front
    assert state.player_list[0].passed_win_multiplier == 0
    assert state.natural_draw_count[0] == 1
    assert len(state.tiles_list) == previous_size - 1
    assert_conserved(state)
