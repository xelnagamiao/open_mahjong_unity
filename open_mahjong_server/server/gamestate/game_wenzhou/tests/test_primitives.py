"""Wall, identity, state guards and pure payment/lifecycle branches.

These assertions intentionally do not need the joker solver. The integrated
flow suite exercises production scoring when its adapter is available.
"""

import copy

import pytest

from ..actions import empty_actions, kong_tiles, natural, needed_tiles, physical_for
from ..WenzhouGameState import TILES
from ..state_machine import Phase as P, StateMachine
from .helpers import assert_conserved, make_state, snapshot


@pytest.mark.parametrize("seed", [1, 2, 702024, 20241002, 2**31 - 1])
def test_deal_is_seeded_136_tile_conserving_and_reserves_indicator(seed):
    state = make_state(random_seed=seed)
    state.initialize_round()
    first = (state.caishen, list(state.dice), list(state.tiles_list), [list(p.hand_tiles) for p in state.player_list])
    assert [len(p.hand_tiles) for p in state.player_list] == [17, 16, 16, 16]
    assert len(state.tiles_list) == 70
    assert sum(p.hand_tiles.count(state.caishen) for p in state.player_list) + state.tiles_list.count(state.caishen) == 3
    assert [p.has_draw_slot for p in state.player_list] == [True, False, False, False]
    assert_conserved(state)
    state.initialize_round()
    assert first == (state.caishen, state.dice, state.tiles_list, [p.hand_tiles for p in state.player_list])


@pytest.mark.parametrize("dice", [[1, 1, 1, 1], [6, 6, 6, 6], [2, 3, 5, 6], [3, 4, 4, 5]])
def test_explicit_wall_two_dice_throws_and_four_packet_deal(dice):
    state = make_state()
    wall = [t for t in TILES for _ in range(4)]
    state.initialize_round(wall=wall, dice=dice)
    first, second = sum(dice[:2]), sum(dice[2:])
    indicator = ((first - 1) % 4) * 34 + 2 * (first - 1)
    offset = ((((first - 1) % 4 + second - 1) % 4) * 34 + second * 2) % 136
    physical_order = [i for i in list(range(offset, 136)) + list(range(offset)) if i != indicator]
    drawn_order = [wall[i] for i in physical_order]
    for seat, p in enumerate(state.player_list):
        packets = [drawn_order[r * 16 + seat * 4:r * 16 + seat * 4 + 4] for r in range(4)]
        expected = sorted([t for packet in packets for t in packet])
        if seat == 0:
            expected += drawn_order[64:65]
        assert p.hand_tiles == expected
    assert state.indicator_index == indicator
    assert state.indicator == wall[indicator]
    assert state.tiles_list == drawn_order[65:]
    assert_conserved(state)


@pytest.mark.parametrize("bad", [[1, 2, 3], [1, 2, 3, 4, 5], [0, 1, 2, 3], [1, 2, 3, 7], [True, 1, 2, 3], [1, "2", 3, 4]])
def test_invalid_dice_rejected_before_existing_state_is_changed(bad):
    state = make_state()
    state.initialize_round()
    before = snapshot(state)
    with pytest.raises(ValueError):
        state.initialize_round(dice=bad)
    assert snapshot(state) == before


@pytest.mark.parametrize("bad", [[], [11] * 136, list(range(136)), [True] * 136])
def test_invalid_wall_rejected(bad):
    with pytest.raises(ValueError):
        make_state().initialize_round(wall=bad)


@pytest.mark.parametrize("option", ["use_flowers", "open_cuohe", "tactical_call", "claim_protection"])
def test_fixed_rules_reject_incompatible_options(option):
    with pytest.raises(ValueError):
        make_state(**{option: True})


def test_unknown_rule_and_config_rejected():
    with pytest.raises(ValueError):
        make_state(sub_rule="wenzhou/unknown")
    with pytest.raises(ValueError):
        make_state(detailed_config={"use_flowers": True})


@pytest.mark.parametrize("source", list(P))
@pytest.mark.parametrize("target", list(P))
def test_state_graph_accepts_only_declared_edges(source, target):
    # The independent domain contract: a claim is followed by a cut or a
    # replacement draw, and every round end crosses the ready boundary.
    expected_edges = {
        P.START: {P.TURN},
        P.TURN: {P.RESPONSE,P.KONG,P.END},
        P.DISCARD_ONLY: {P.RESPONSE},
        P.RESPONSE: {P.TURN,P.DISCARD_ONLY,P.END},
        P.KONG: {P.TURN,P.DISCARD_ONLY,P.END},
        P.END: {P.READY},
        P.READY: {P.START,P.FINISHED},
        P.FINISHED: set(),
    }
    machine = StateMachine()
    machine.phase = source
    if target == source or target in expected_edges[source]:
        machine.transition(target)
        assert machine.phase == target
        assert machine.version == int(target != source)
    else:
        with pytest.raises(RuntimeError):
            machine.transition(target)
        assert machine.phase == source and machine.version == 0


def test_white_is_46_and_cannot_be_confused_with_green_dragon():
    assert natural(46, 12) == 12
    assert natural(47, 12) == 47
    assert natural(46, 46) == 46
    assert physical_for([11, 46, 13, 12], [11, 12, 13], 12) == [11, 46, 13]
    assert physical_for([11, 12, 13], [11, 12, 13], 12) is None
    assert physical_for([46] * 4, [46] * 4, 46) is None
    assert physical_for([46] * 4, [12] * 4, 12) == [46] * 4


@pytest.mark.parametrize("caishen", TILES)
def test_added_kong_cannot_be_punged_or_konged_with_four_copy_supply(caishen):
    # Excluding the physical caishen makes fixed natural mapping injective.
    # An existing pung plus its added tile already consumes all four copies;
    # a rival cannot possess the two/three further copies needed to call it.
    inverse = {}
    for physical in TILES:
        if physical != caishen:
            inverse.setdefault(natural(physical, caishen), []).append(physical)
    assert all(len(physical) == 1 for physical in inverse.values())
    assert all(3 + 1 + claim_size > 4 for claim_size in (2, 3))


@pytest.mark.parametrize("action,expected", [("peng", [12, 12]), ("gang", [12, 12, 12]),
    ("chi_left", [10, 11]), ("chi_mid", [11, 13]), ("chi_right", [13, 14])])
def test_logical_needed_tiles(action, expected):
    assert needed_tiles(action, 12) == expected


def test_kong_candidates_require_draw_slot_available_tail_and_natural_tiles():
    state = make_state()
    state.caishen = 12
    state.tiles_list = [31] * 5
    player = state.player_list[0]
    player.hand_tiles = [46] * 4 + [12] * 3 + [31, 31, 31, 31, 41]
    player.has_draw_slot = True
    assert kong_tiles(state, 0, "angang") == {46, 31}
    player.combination_tiles = ["k41", "k12", "g31"]
    assert kong_tiles(state, 0, "jiagang") == {46, 41}
    with pytest.raises(ValueError):
        kong_tiles(state, 0, "other")
    player.has_draw_slot = False
    assert not kong_tiles(state, 0, "angang")
    player.has_draw_slot = True
    state.tiles_list = [31] * 4
    assert not kong_tiles(state, 0, "angang")
    assert not kong_tiles(state, 0, "jiagang")


@pytest.mark.parametrize("count,allowed", [(0, False), (3, False), (4, False), (5, True), (6, True)])
def test_tail_reserve_is_four_playable_tiles_plus_separate_indicator(count, allowed):
    state = make_state()
    state.tiles_list = [11] * count
    assert state.can_draw() == allowed


@pytest.mark.parametrize("data,expected", [
    ({"TileId": 13, "cutClass": True, "cutIndex": 3}, (13, 3, True)),
    ({"TileId": 13, "cutClass": True}, (13, 3, True)),
    ({"TileId": 11, "cutClass": False, "cutIndex": 2}, (11, 0, False)),
    ({"TileId": 13}, (13, 3, True)),
    ({"TileId": 12}, (12, 1, False)),
    ({"TileId": 11, "cutIndex": 2}, (11, 2, False)),
])
def test_cut_identity_uses_physical_identity_and_draw_slot(data, expected):
    state = make_state()
    state.player_list[0].hand_tiles = [11, 12, 11, 13]
    state.player_list[0].has_draw_slot = True
    assert state._cut_identity(0, data) == expected


@pytest.mark.parametrize("data", [
    {"TileId": True}, {"TileId": "11"}, {"TileId": 19}, {"TileId": 11, "cutIndex": -2},
    {"TileId": 11, "cutIndex": 4}, {"TileId": 11, "cutIndex": True},
    {"TileId": 11, "cutClass": "false"}, {"TileId": 11, "cutClass": True},
    {"TileId": 13, "cutClass": False}, {"TileId": 13, "cutClass": True, "cutIndex": 2},
    {"TileId": 11, "cutIndex": 1},
])
def test_cut_identity_rejects_forged_or_stale_requests(data):
    state = make_state()
    state.player_list[0].hand_tiles = [11, 12, 11, 13]
    state.player_list[0].has_draw_slot = True
    with pytest.raises(ValueError):
        state._cut_identity(0, data)


def test_held_discard_without_draw_slot_is_never_moqie():
    state = make_state()
    state.player_list[0].hand_tiles = [11, 12, 13]
    assert state._cut_identity(0, {"TileId": 13, "cutClass": False}) == (13, 2, False)
    with pytest.raises(ValueError):
        state._cut_identity(0, {"TileId": 13, "cutClass": True})


@pytest.mark.parametrize("seat,body,offers", [(-1, {}, []), (4, {}, []), (True, {}, []),
    (0, [], []), (0, {"action_type": "hu"}, ["pass"]),
    (0, {"action_type": "angang", "target_tile": True}, ["angang"]),
    (0, {"action_type": "jiagang", "target_tile": 41}, ["jiagang"])])
def test_validation_rejects_invalid_actor_or_offer(seat, body, offers):
    with pytest.raises(ValueError):
        make_state().validate_response(seat, body, offers)


def test_stale_or_client_settled_action_windows_rejected_atomically():
    state = make_state()
    window = {"status": P.TURN.value, "player": 0, "actions": empty_actions(), "action_tick": 10}
    state.live_pending_window, state.server_action_tick = window, 10
    before = snapshot(state)
    for supplied, kwargs in [(copy.deepcopy(window), {}), (window, {"settlements": [{"winner": 0}]})]:
        with pytest.raises(ValueError):
            state.apply_action_results(supplied, {}, **kwargs)
    state.server_action_tick = 11
    with pytest.raises(ValueError):
        state.apply_action_results(window, {})
    state.server_action_tick = 10
    with pytest.raises(ValueError):
        state.apply_action_results(window, {0: {"action_type": "pass"}})
    assert snapshot(state) == before


@pytest.mark.parametrize("streak", range(4))
@pytest.mark.parametrize("player", range(4))
@pytest.mark.parametrize("kind,base", [("ming", 1), ("angang", 2), ("jiagang", 0)])
def test_immediate_kong_payment_is_zero_sum_and_draw_keeps_it(streak, player, kind, base):
    state = make_state()
    state.dealer_streak = streak
    changes = state.settle_kong(player, kind)
    expected = [0] * 4
    for payer in range(4):
        if payer != player:
            paid = base * (2 * (streak + 1) if 0 in (player, payer) else 1)
            expected[player] += paid
            expected[payer] -= paid
    assert [changes[i] for i in range(4)] == expected
    assert [p.score for p in state.player_list] == expected
    assert sum(state.round_changes) == 0
    state.machine.transition(P.TURN)
    state.end_draw()
    state.apply_deferred_score_changes()
    state.apply_deferred_score_changes()
    assert [p.score for p in state.player_list] == expected
    assert state.caishen_changes == [0, 0, 0, 0]
    assert len(state.ledger) == 1
    assert all(len(p.score_history) == 1 for p in state.player_list)


@pytest.mark.parametrize("streak", range(4))
def test_draw_preserves_dealer_and_streak(streak):
    state = make_state()
    state.dealer_streak = streak
    state.machine.transition(P.TURN)
    state.end_draw()
    assert state.next_dealer_shift == 0 and state.next_dealer_streak == streak
    assert not state.match_finishing and state.dealer_changes == 0
    with pytest.raises(ValueError):
        state.end_draw()
    state.machine.transition(P.READY)
    state.advance_round_after_ready()
    assert state.machine.phase == P.START
    assert state.dealer_streak == streak and state.round_index == 2


@pytest.mark.parametrize("streak", range(3))
def test_dealer_win_raises_streak_without_advancing_match_count(streak):
    state = make_state()
    state.machine.transition(P.TURN)
    state.dealer_streak = streak
    state._finish_round(0)
    assert state.next_dealer_streak == streak + 1
    assert state.next_dealer_shift == 0 and state.dealer_changes == 0


def test_fourth_consecutive_dealer_win_rerolls_and_resets_even_if_same_seat():
    shifts = set()
    for seed in range(32):
        state = make_state()
        state.round_random_seed = seed
        state.dealer_streak = 3
        state.machine.transition(P.TURN)
        state._finish_round(0)
        assert len(state.next_dealer_dice) == 2
        assert state.next_dealer_shift == (sum(state.next_dealer_dice) - 1) % 4
        assert state.dealer_changes == 1 and state.next_dealer_streak == 0
        shifts.add(state.next_dealer_shift)
    assert shifts == {0, 1, 2, 3}


def test_non_dealer_win_advances_to_next_seat_and_original_identity_rotates():
    state = make_state()
    state.machine.transition(P.TURN)
    state.dealer_streak = 2
    state._finish_round(3)
    assert state.next_dealer_shift == 1 and state.next_dealer_streak == 0
    state.machine.transition(P.READY)
    state.advance_round_after_ready()
    assert [p.player_index for p in state.player_list] == [0, 1, 2, 3]
    assert [p.original_player_index for p in state.player_list] == [1, 2, 3, 0]
    assert state.current_round == 2
    state.initialize_round()
    assert_conserved(state)


def test_match_end_and_advance_guards():
    state = make_state()
    with pytest.raises(RuntimeError):
        state.advance_round_after_ready()
    state.machine.transition(P.TURN)
    state.dealer_changes = 3
    state._finish_round(1)
    assert state.match_finishing and state.is_match_end()
    state.machine.transition(P.READY)
    with pytest.raises(RuntimeError):
        state.advance_round_after_ready()
    with pytest.raises(RuntimeError):
        state.initialize_round()
