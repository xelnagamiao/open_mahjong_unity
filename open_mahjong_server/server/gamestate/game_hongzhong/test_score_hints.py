"""Ordinary wait scores and persistent eligibility, independent of incidental fans."""
from copy import deepcopy

import pytest

from .hints import compute_hand, match_hint
from .test_flow import HAND, hand, make_state, ticks
from ...game_calculation.hongzhong import rules as book


@pytest.mark.parametrize("tiles,melds", [
    (HAND, ()), (HAND[:-1] + [45], ()),
    ([11,11,12,12,13,13,14,14,15,15,16,16,17], ()),
    ([24,24,24,28,28,29,29,16,16,38,38,39,39], ()),
    ([28,28,19,45], ("k15","k16","k27")),
    ([11,12,15,15,16,16,17,17,19,19], ("G13",)),
    ([11,11,12,12,13,13,14,14,15,45,45,45,45], ()),
])
def test_every_structural_wait_has_current_basic_score(tiles, melds):
    payload, _ = compute_hand(tuple(tiles), melds, None, False)
    assert set(payload["waiting_details"]) == set(payload["waiting_tiles"]) == book.waits(tiles, melds)
    for tile, score in payload["waiting_details"].items():
        actual = book.score(tiles + [tile], melds, winning_tile=tile, replacement=False)
        assert score == {"fan": actual["fan"], "base_score": actual["base_score"]}


def test_zero_fan_wait_is_legal_and_other_wait_pays_more():
    payload, _ = compute_hand(tuple(HAND), (), None, False)
    assert payload["waiting_details"] == {28: {"fan": 1, "base_score": 2}, 45: {"fan": 0, "base_score": 1}}


def test_cut_preview_and_stable_projection_share_scores_and_cap():
    closed = [11,11,12,12,13,13,14,14,15,15,16,16,17]
    payload, _ = compute_hand(tuple(closed + [39]), (), 39, False)
    projected = match_hint(payload, list(reversed(closed)), [])
    stable, _ = compute_hand(tuple(closed), (), None, False)
    assert projected["waiting_tiles"] == stable["waiting_tiles"]
    assert projected["waiting_details"] == stable["waiting_details"]
    assert projected["waiting_details"][17] == {"fan": 4, "base_score": 16}
    assert projected["hint_version"] == 2
    assert projected["waiting_by_discard"] == projected["waiting_details_by_discard"] == {}


def test_hint_scores_do_not_guess_kong_replacement_or_change_actual_score():
    complete = tuple(HAND + [28])
    ordinary, detail = compute_hand(complete, (), 28, False)
    replacement, kong_detail = compute_hand(complete, (), 28, True)
    assert ordinary == replacement
    assert detail["fan"] == 1 and kong_detail["fan"] == 2
    assert replacement["waiting_details_by_discard"][28][28] == {"fan": 1, "base_score": 2}


def test_peida_keeps_structural_waits_and_marks_only_qualification():
    state = make_state()
    player = hand(state, 0, HAND)
    payload, _ = compute_hand(tuple(HAND), (), None, False)
    cached = deepcopy(payload)
    state._hint_cache[0] = payload
    assert state.build_private_game_info_fields(0)["hongzhong_hints"]["win_blocked"] is False
    state._record_hints(0)
    player.tag_list.append("peida")
    private = state.build_private_game_info_fields(0)["hongzhong_hints"]
    assert private["waiting_tiles"] == [28,45] and private["win_blocked"] is True
    assert private["waiting_details"] == cached["waiting_details"]
    assert state._hint_cache[0] == cached
    state._record_hints(0)
    state._record_hints(0)
    recorded = [tick for tick in ticks(state) if tick[:2] == ["hongzhong","hints"]]
    assert [tick[3]["win_blocked"] for tick in recorded] == [False,True]


def test_old_structural_snapshots_project_without_invented_scores():
    old = {"source_hand_tiles": HAND + [39], "source_melds": [],
           "waiting_tiles": [], "waiting_by_discard": {39: [28,45]}}
    projected = match_hint(old, HAND, [])
    assert projected["waiting_tiles"] == [28,45]
    assert projected["waiting_details"] == {} and projected["hint_version"] == 1
    assert match_hint(old, HAND, ["k11"]) is None


@pytest.mark.parametrize("tips,count", [(False,False), (False,True), (True,False), (True,True)])
def test_private_hints_are_available_for_either_hint_toggle(tips, count):
    state = make_state(tips=tips, count_tips=count)
    hand(state, 0, HAND)
    payload, _ = compute_hand(tuple(HAND), (), None, False)
    state._hint_cache[0] = payload
    fields = state.build_private_game_info_fields(0)
    assert ("hongzhong_hints" in fields) is (tips or count)
    assert fields["hongzhong_info"]["self_has_draw_slot"] is False
    if tips or count:
        assert fields["hongzhong_hints"]["waiting_tiles"] == [28,45]
        assert fields["hongzhong_hints"]["waiting_details"][45] == {"fan":0,"base_score":1}
