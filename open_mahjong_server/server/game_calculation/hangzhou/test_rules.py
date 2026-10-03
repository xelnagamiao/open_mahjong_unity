"""Independent MIL Hangzhou book expectations, including negative branches."""

import json

import pytest

from .rules import (FAN_CAP, JOKER, RULE_VERSION, TILES, Fan, WinContext, WinScore,
                    _valid, meld_tiles, next_dealer, normalize_config, payments,
                    score_ten_winds, settle_win, ten_winds_eligible)


@pytest.mark.parametrize("raw", [None, {}, {"rule_version": RULE_VERSION}])
def test_only_the_published_config_is_accepted(raw):
    assert normalize_config(raw) == {"rule_version": RULE_VERSION}


@pytest.mark.parametrize("raw", [[], 1, {"flowers": True}, {"rule_version": "other"}])
def test_unknown_house_rule_and_version_are_rejected(raw):
    with pytest.raises(ValueError):
        normalize_config(raw)


def test_white_tile_matches_server_and_unity_protocol():
    assert JOKER == 46
    assert len(TILES) == len(set(TILES)) == 34
    assert 47 in TILES


@pytest.mark.parametrize("code,expected", [
    ("s12", (11, 12, 13)), ("s28", (27, 28, 29)),
    ("s35", (34, 35, 36)), ("k47", (47, 47, 47)),
    ("g41", (41, 41, 41, 41)), ("G45", (45, 45, 45, 45)),
])
def test_existing_meld_encoding_and_green_is_not_a_joker(code, expected):
    assert meld_tiles(code) == expected


@pytest.mark.parametrize("code", [None, 12, "", "k111", "q11", "sxx", "s11", "s19",
                                  "s42", "k51", "k46", "g46", "G46"])
def test_bad_or_joker_exposed_melds_are_rejected(code):
    with pytest.raises(ValueError):
        meld_tiles(code)


@pytest.mark.parametrize("hand,melds", [
    (None, []), ([11] * 14, None), ([], ["s12"] * 5), ([11] * 13, []),
    ([11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 47, True], []),
    ([11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 47, 51], []),
    ([11] * 11, ["sxx"]), ([11, 11], ["G11", "s22", "s25", "s32"]),
])
def test_physical_hand_validation_rejects_invalid_and_fifth_copy(hand, melds):
    assert _valid(hand, melds, True) is None


def test_kong_uses_four_physical_copies_but_three_structure_slots():
    assert _valid([11, 11], ["G19", "s22", "s25", "s32"], True)
    assert _valid([11], ["G19", "s22", "s25", "s32"], False)


@pytest.mark.parametrize("kwargs", [
    {"self_drawn": 1}, {"after_kong": "true"}, {"cai_piao_count": True},
    {"cai_piao_count": -1}, {"cai_piao_count": 5}, {"pre_draw_tiles": "123"},
])
def test_context_rejects_invalid_types_and_counts(kwargs):
    with pytest.raises(ValueError):
        WinContext(**kwargs)


def test_context_owns_an_immutable_pre_draw_snapshot():
    hand = [11, 12]
    context = WinContext(pre_draw_tiles=hand)
    hand.append(13)
    assert context.pre_draw_tiles == (11, 12)


# Six number pairs and an isolated number are deliberately not a standard win.
TEN_HAND = [11, 11, 14, 14, 21, 21, 24, 24, 31, 31, 34, 34, 19]
TEN_DISCARDS = [41, 41, 42, 42, 43, 43, 44, 44, 45, 47]


@pytest.mark.parametrize("hand_joker,discard_joker,raw,fan", [
    (False, False, 4, 4), (True, False, 3, 3),
    (False, True, 5, 4), (True, True, 4, 4),
])
def test_ten_winds_all_four_joker_combinations(hand_joker, discard_joker, raw, fan):
    hand = TEN_HAND[:-1] + [JOKER if hand_joker else 19]
    history = TEN_DISCARDS[:-1] + [JOKER if discard_joker else 47]
    score = score_ten_winds(hand, history)
    assert score.shape == "ten_winds"
    assert score.raw_fan == raw and score.fan_total == fan
    assert score.points == 2 ** fan
    assert ("no_joker" in score.fan_ids) == (not hand_joker)
    assert score.as_dict()["fan_cap"] == FAN_CAP
    assert json.loads(json.dumps(score.as_dict()))["points"] == score.points


@pytest.mark.parametrize("melds,acted", [([], True), ([], 1), (["G11"], False),
                                         (["k11"], False), (["s12"], False)])
def test_ten_winds_disallows_every_meld_including_concealed_kong(melds, acted):
    assert not ten_winds_eligible(TEN_HAND, TEN_DISCARDS, melds, had_meld_action=acted)


@pytest.mark.parametrize("history", [None, TEN_DISCARDS[:9], TEN_DISCARDS + [45],
                                     TEN_DISCARDS[:9] + [11], TEN_DISCARDS[:9] + [True],
                                     [41] * 5 + [42] * 5])
def test_ten_winds_requires_first_ten_real_honor_discards(history):
    assert score_ten_winds(TEN_HAND, history) is None


def test_ten_winds_rejects_bad_hand_and_fifth_copy_across_discards():
    assert score_ten_winds(TEN_HAND[:12], TEN_DISCARDS) is None
    hand = TEN_HAND[:-3] + [41, 41, 41]
    assert score_ten_winds(hand, TEN_DISCARDS) is None


@pytest.mark.parametrize("dealer", range(4))
@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("streak,multiplier", [(1, 2), (2, 4), (3, 8)])
def test_all_dealer_and_winner_seats_for_every_old_dealer_stage(dealer, winner, streak, multiplier):
    result = payments(winner, 2, dealer=dealer, dealer_streak=streak)
    losses = {seat: 2 * (multiplier if dealer in (seat, winner) else 1)
              for seat in range(4) if seat != winner}
    assert result[winner] == sum(losses.values())
    assert all(result[seat] == -value for seat, value in losses.items())
    assert sum(result) == 0


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("mode", ["normal", "contract", "reverse", "both"])
def test_contract_direction_and_both_relationships_are_additive(winner, mode):
    counts = [0] * 4
    upper, lower = (winner - 1) % 4, (winner + 1) % 4
    if mode in ("contract", "both"):
        counts[winner] = 3
    if mode in ("reverse", "both"):
        counts[lower] = 3
    score = WinScore("standard", (Fan("plain", "平和", 0),))
    result = settle_win(score, winner, winner, 1, counts)
    assert result.normal_total == 6
    if mode == "normal":
        assert len(result.transfers) == 3 and result.changes[winner] == 6
    elif mode == "contract":
        assert result.changes[upper] == -6 and result.changes[winner] == 6
        assert len(result.transfers) == 1
    elif mode == "reverse":
        assert result.changes[lower] == -12 and result.changes[winner] == 12
        assert len(result.transfers) == 1
    else:
        assert result.changes[upper] == -6 and result.changes[lower] == -12
        assert result.changes[winner] == 18 and len(result.transfers) == 2
    assert sum(result.changes) == 0
    assert result.as_dict()["dealer_multiplier"] == 2
    assert score.fan_names == ["平和（0番）"]


def test_three_chows_threshold_and_unrelated_liability_do_not_change_payments():
    normal = payments(0, 1, dealer=0, dealer_streak=1)
    assert payments(0, 1, dealer=0, dealer_streak=1, chi_counts=[2, 2, 4, 3]) == normal
    assert payments(0, 1, dealer=0, dealer_streak=1, chi_counts=[4, 0, 0, 0]) == [6, 0, 0, -6]


@pytest.mark.parametrize("kwargs", [
    {"winner": True}, {"winner": 4}, {"dealer": -1}, {"dealer": False},
    {"points": 0}, {"points": True}, {"dealer_streak": 0}, {"dealer_streak": 4},
    {"dealer_streak": True}, {"chi_counts": None}, {"chi_counts": [0, 0, 0]},
    {"chi_counts": [0, 0, 0, -1]}, {"chi_counts": [0, 0, 0, 5]},
    {"chi_counts": [0, 0, 0, True]},
])
def test_settlement_rejects_bad_authoritative_context(kwargs):
    arguments = dict(winner=0, points=1, dealer=0, dealer_streak=1)
    arguments.update(kwargs)
    with pytest.raises(ValueError):
        payments(**arguments)


def test_settlement_requires_a_scored_win():
    with pytest.raises(ValueError):
        settle_win(None, 0, 0, 1)


@pytest.mark.parametrize("streak,next_streak", [(1, 2), (2, 3), (3, 3)])
def test_draw_and_dealer_win_keep_dealer_and_advance_with_cap(streak, next_streak):
    assert next_dealer(2, streak, None) == (2, next_streak)
    assert next_dealer(2, streak, 2) == (2, next_streak)
    assert next_dealer(2, streak, 0) == (0, 1)


@pytest.mark.parametrize("args", [(False, 1), (4, 1), (0, True), (0, 4), (0, 1, False), (0, 1, 4)])
def test_next_dealer_invalid_parameters(args):
    with pytest.raises(ValueError):
        next_dealer(*args)
