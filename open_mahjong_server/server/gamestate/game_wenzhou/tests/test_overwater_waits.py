"""MIL VI.6 uses a multiplier threshold across all winning tile identities."""

import random

import pytest

from ..actions import claim_actions, empty_actions
from ..state_machine import Phase as P
from .helpers import STANDARD, WAIT, assert_conserved, fixture_state, make_state, response_map
from .test_flow import JUNK, cut, open_claim, open_turn


MIXED_WAIT = [11,12,14,15,16,21,22,23,31,32,33,41,41,41,42,42]


def test_passing_soft_win_blocks_all_equal_soft_waits_but_allows_higher_hard_win():
    state = fixture_state({2: MIXED_WAIT}, caishen=11, actor=0, phase=P.RESPONSE,
                          drawn=False, river={0: [11]})
    assert state.score_win(2, "discard", 11)["multiplier"] == 1
    assert state.score_win(2, "discard", 14)["multiplier"] == 1
    assert state.score_win(2, "discard", 13)["multiplier"] == 2
    window = open_claim(state, 0, 11)
    assert "hu" in window["actions"][2]
    state.apply_action_results(window, response_map(window))
    assert state.player_list[2].passed_win_multiplier == 1
    assert not state.can_win(2, "discard", 11)
    assert not state.can_win(2, "discard", 14)
    assert state.can_win(2, "discard", 13)
    assert not state.can_win(2, "rob_kong", 14)
    state.remember_pass(2, 13)
    assert state.player_list[2].passed_win_multiplier == 2
    assert not state.can_win(2, "discard", 13)
    state.remember_pass(2, 14)
    assert state.player_list[2].passed_win_multiplier == 2
    assert_conserved(state)


def test_own_discard_sets_ron_threshold_not_self_draw_double():
    hand = MIXED_WAIT + [14]
    state = fixture_state({0: hand}, caishen=11)
    assert state.score_win(0, "self_draw", 14)["multiplier"] == 2
    open_turn(state)
    cut(state, 14, drawn=True)
    assert state.player_list[0].passed_win_multiplier == 1
    assert state.can_win(0, "discard", 13)
    assert not state.can_win(0, "discard", 14)
    assert_conserved(state)


def test_actual_next_draw_clears_passed_threshold_before_win_check():
    state = fixture_state({1: MIXED_WAIT}, caishen=11, actor=0, phase=P.RESPONSE,
                          drawn=False, river={0: [39]})
    p = state.player_list[1]
    p.passed_win_multiplier = 4
    # Draw the valid natural winning tile from the actual wall front.
    at = state.tiles_list.index(13)
    state.tiles_list[0], state.tiles_list[at] = state.tiles_list[at], state.tiles_list[0]
    window = open_claim(state, 0, 39)
    state.apply_action_results(window, response_map(window))
    assert p.passed_win_multiplier == 0 and p.has_draw_slot
    assert state.can_win(1, "self_draw", 13)
    assert "hu_self" in state.action_dict[1]
    assert_conserved(state)


@pytest.mark.parametrize("retains_shape", [False, True])
def test_pung_clears_overwater_only_when_post_pung_hand_is_nonwinning(retains_shape):
    # 13 closed tiles remain before claiming; remove the pair used by the pung
    # and the remaining 14 form four melds + pair only in the second fixture.
    rest = [11,12,13,21,22,23,31,32,33,41,41,41,43,43] if retains_shape else JUNK
    state = fixture_state({1: [19,19] + rest}, actor=0, phase=P.RESPONSE,
                          drawn=False, river={0: [19]})
    state.player_list[1].passed_win_multiplier = 4
    window = open_claim(state, 0, 19)
    assert "peng" in window["actions"][1]
    state.apply_action_results(window, response_map(window, **{"1": "peng"}))
    assert state.player_list[1].passed_win_multiplier == (4 if retains_shape else 0)
    assert state.action_dict[1] == ["cut"]
    assert_conserved(state)


def test_chow_does_not_clear_passed_win_threshold():
    state = fixture_state({1: [11,13] + JUNK}, caishen=12, actor=0,
                          phase=P.RESPONSE, drawn=False, river={0: [46]})
    state.player_list[1].passed_win_multiplier = 2
    window = open_claim(state, 0, 46)
    state.apply_action_results(window, response_map(window, **{"1": "chi_mid"}))
    assert state.player_list[1].passed_win_multiplier == 2
    assert_conserved(state)


def test_wait_hint_preserves_physical_tiles_and_observer_specific_overwater():
    state = fixture_state({1: MIXED_WAIT}, caishen=11, actor=0, tips=True)
    state.player_list[1].passed_win_multiplier = 1
    info = state.build_game_start_payload(1)["game_info"]
    key = ",".join(map(str, sorted(MIXED_WAIT)))
    assert set(info["wenzhou_waits"]) == {key}
    waits = {row["tile"]: row for row in info["wenzhou_waits"][key]}
    assert waits[13]["ron"] and waits[13]["ron_multiplier"] == 2
    assert not waits[14]["ron"] and waits[14]["ron_multiplier"] == 1
    assert waits[14]["self_draw"] and waits[14]["self_draw_multiplier"] == 2
    assert 46 in waits and not waits[46]["ron"]
    other = state.build_game_start_payload(2)["game_info"]["wenzhou_waits"]
    assert key not in other
    assert state.authoritative_waits(-1) == {} and state.authoritative_waits(4) == {}


def test_drawn_hand_hints_include_each_physical_cut_and_own_discard_threshold():
    hand = MIXED_WAIT + [14]
    state = fixture_state({0: hand}, caishen=11, tips=True)
    cache = state.authoritative_waits(0)
    assert len(cache) == len(set(hand))
    candidate = sorted(MIXED_WAIT)
    waits = {row["tile"]: row for row in cache[",".join(map(str,candidate))]}
    assert waits[13]["ron"]
    assert not waits[14]["ron"]
    assert waits[14]["self_draw"]
    # Returned overwater flags must not mutate the cached structural entries.
    intrinsic = {row["tile"]: row for row in state._waits_for_hand(candidate, [])}
    assert intrinsic[14]["ron"] is True


def test_wait_cache_bounded_identity_sensitive_and_reset_between_rounds():
    state = make_state(tips=True)
    state.initialize_round()
    deck = [tile for tile in range(11,48) if tile % 10 in range(1,10) and (tile < 40 or tile <= 47) for _ in range(4)]
    rng = random.Random(2702)
    for _ in range(140):
        rng.shuffle(deck)
        state._waits_for_hand(deck[:16], [])
    assert len(state._wait_cache) == 128
    hand = sorted(WAIT)
    first = state._waits_for_hand(hand, [])
    assert state._waits_for_hand(list(reversed(hand)), []) is first
    previous = len(state._wait_cache)
    state.caishen = 11 if state.caishen != 11 else 12
    state._waits_for_hand(hand, [])
    assert len(state._wait_cache) == min(128, previous + 1)
    state.initialize_round()
    assert not state._wait_cache


def test_can_win_guards_retired_player_invalid_source_or_missing_draw():
    state = fixture_state({0: STANDARD})
    state.player_list[0].is_hu = True
    assert not state.can_win(0, "self_draw", 43)
    state.player_list[0].is_hu = False
    assert not state.can_win(0, "unknown", 43)
    state.player_list[0].has_draw_slot = False
    assert not state.can_win(0, "self_draw", 43)
    assert state.legal_discard_tiles(0) == set(STANDARD)


@pytest.mark.parametrize("winner,source,tile,payer", [
    (4, "self_draw", 43, None), (True, "self_draw", 43, None), (0, "other", 43, None),
    (0, "self_draw", 42, None), (1, "self_draw", 43, None), (0, "self_draw", 43, 1),
    (1, "discard", 43, None), (1, "discard", 43, 1), (1, "discard", 43, 4),
    (1, "discard", 43, 0), (1, "rob_kong", 43, 0),
])
def test_settlement_validates_source_before_mutation(winner, source, tile, payer):
    state = fixture_state({0: STANDARD})
    before = [list(p.hand_tiles) for p in state.player_list]
    with pytest.raises(ValueError):
        state.settle_win(winner, source, tile, payer=payer)
    assert [p.hand_tiles for p in state.player_list] == before
    assert state.round_settlement is None


def test_rob_concealed_kong_and_nonwinning_ron_cannot_settle():
    state = fixture_state({1: JUNK + [11,13]}, actor=0, phase=P.RESPONSE,
                          drawn=False, river={0: [19]})
    with pytest.raises(ValueError, match="资格"):
        state.settle_win(1, "discard", 19, payer=0)
    state.machine.phase = P.KONG
    state.pending_kong = dict(actor=0, tile=19, concealed=True)
    with pytest.raises(ValueError, match="加杠"):
        state.settle_win(1, "rob_kong", 19, payer=0)
