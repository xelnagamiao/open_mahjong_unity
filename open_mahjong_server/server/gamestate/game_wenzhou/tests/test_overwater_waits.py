"""MIL VI.6 uses a multiplier threshold across all winning tile identities."""

from copy import deepcopy

import pytest

from ..actions import claim_actions, empty_actions
from ..state_machine import Phase as P
from .helpers import STANDARD, WAIT, assert_conserved, fixture_state, make_state, response_map
from .test_flow import JUNK, cut, open_claim, open_turn


MIXED_WAIT = [11,12,14,15,16,21,22,23,31,32,33,41,41,41,42,42]


def local_request(payload):
    assert set(payload) == {'__local__'}
    return payload['__local__'][0]['local_request']


def reference_rows(payload, cut=None):
    """Independent oracle for the inputs transported to the client."""
    from ....game_calculation.wenzhou.rules import hand_multiplier, waiting_tiles
    request = local_request(payload)
    hand = list(request['hand'])
    context = request['context']
    melds, caishen = context['meld_records'], context['caishen']
    threshold = context['passed_multiplier']
    if cut is not None:
        threshold = max(threshold, hand_multiplier(hand, melds, caishen=caishen, winning_tile=cut) or 0)
        hand.remove(cut)
    result = {}
    for tile in waiting_tiles(hand, melds, caishen=caishen):
        ron = hand_multiplier(hand+[tile], melds, caishen=caishen, winning_tile=tile) or 0
        own = hand_multiplier(hand+[tile], melds, caishen=caishen, winning_tile=tile, self_draw=True) or 0
        result[tile] = dict(ron=ron>threshold, self_draw=own>0, ron_multiplier=ron, self_draw_multiplier=own)
    return result


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


def test_wait_hint_preserves_physical_tiles_and_observer_specific_overwater(monkeypatch):
    state = fixture_state({1: MIXED_WAIT}, caishen=11, actor=0, tips=True)
    state.player_list[1].passed_win_multiplier = 1
    info = state.build_game_start_payload(1)["game_info"]
    request = local_request(info['wenzhou_waits'])
    assert request['hand'] == MIXED_WAIT and request['context']['passed_multiplier'] == 1
    assert not hasattr(state, '_waits_for_hand')
    assert state.build_game_start_payload(1)['game_info']['wenzhou_waits'] == info['wenzhou_waits']
    waits = reference_rows(info['wenzhou_waits'])
    assert waits[13]["ron"] and waits[13]["ron_multiplier"] == 2
    assert not waits[14]["ron"] and waits[14]["ron_multiplier"] == 1
    assert waits[14]["self_draw"] and waits[14]["self_draw_multiplier"] == 2
    assert 46 in waits and not waits[46]["ron"]
    other = state.build_game_start_payload(2)["game_info"]["wenzhou_waits"]
    assert local_request(other)['hand'] == state.player_list[2].hand_tiles
    assert state.client_context(-1) == {} and state.client_context(4) == {}


def test_drawn_hand_hints_include_each_physical_cut_and_own_discard_threshold():
    hand = MIXED_WAIT + [14]
    state = fixture_state({0: hand}, caishen=11, tips=True)
    cache = state.client_context(0)
    assert local_request(cache)['hand'] == hand
    waits = reference_rows(cache, 14)
    assert waits[13]["ron"]
    assert not waits[14]["ron"]
    assert waits[14]["self_draw"]
    # Computing eligibility from a copied request must not alter the original.
    intrinsic = deepcopy(cache)
    local_request(intrinsic)['hand'].remove(14)
    local_request(intrinsic)['context']['passed_multiplier'] = 0
    assert reference_rows(intrinsic)[14]['ron'] is True
    assert local_request(cache)['hand'] == hand


@pytest.mark.parametrize('tips,count_tips', [(False, False), (False, True), (True, False), (True, True)])
@pytest.mark.parametrize('drawn', [False, True])
def test_wait_eligibility_and_overwater_survive_count_only_mode(tips, count_tips, drawn):
    hand = MIXED_WAIT + ([14] if drawn else [])
    state = fixture_state({1: hand}, caishen=11, actor=1 if drawn else 0, tips=True)
    state.player_list[1].passed_win_multiplier = 1
    expected = state.client_context(1)
    waits = reference_rows(expected, 14 if drawn else None)
    assert waits[13]['ron'] and not waits[14]['ron'] and waits[14]['self_draw']

    state.tips, state.count_tips = tips, count_tips
    assert state.client_context(1) == (expected if tips or count_tips else {})
    info = state.build_game_start_payload(1)['game_info']
    assert info['wenzhou_waits'] == (expected if tips or count_tips else {})
    assert info['tips'] is tips and info['count_tips'] is count_tips
    assert state.client_context(1, for_record=True) == expected
    other = state.build_game_start_payload(2)['game_info']['wenzhou_waits']
    if other:
        assert local_request(other)['hand'] == state.player_list[2].hand_tiles




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


def test_local_inputs_follow_new_round_and_caishen_without_server_preview_cache():
    state = make_state(tips=True)
    state.initialize_round()
    first = state.client_context(0)
    saved = deepcopy(first)
    state.caishen = 11 if state.caishen != 11 else 12
    newer = state.client_context(0)
    assert local_request(newer)['context']['caishen'] == state.caishen
    assert first == saved and newer != first
    state.initialize_round()
    fresh = local_request(state.client_context(0))
    assert fresh['hand'] == state.player_list[0].hand_tiles
    assert fresh['context']['caishen'] == state.caishen
    assert first == saved
    assert not hasattr(state, '_wait_cache')
