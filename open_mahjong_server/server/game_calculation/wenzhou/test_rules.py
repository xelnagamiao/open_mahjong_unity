"""MIL 温州原书条款、物理牌验证与计分分支回归。"""

from copy import deepcopy
from itertools import product
import json
from pathlib import Path

import pytest

from .rules import (
    EDITION, TILES, WHITE, _score_features, _valid, caishen_payments, dealer_multiplier, kong_payments,
    hand_multiplier, is_complete, natural_tile, normalize_config, parse_meld, payments, score_hand, waiting_tiles,
)


STANDARD = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
HAND_CASES = json.loads((Path(__file__).parent / "testdata/hand_cases.json").read_text(encoding="utf-8"))


@pytest.mark.parametrize("raw", [None, {}, {"edition": EDITION}])
def test_fixed_configuration(raw):
    assert normalize_config(raw) == {"edition": EDITION}


@pytest.mark.parametrize("raw", [False, [], "", {"caishen": 11}, {"eight_pairs": False}, {"edition": "custom"}, {"edition": None}])
def test_no_invented_optional_rules(raw):
    with pytest.raises(ValueError):
        normalize_config(raw)


@pytest.mark.parametrize("caishen", TILES)
def test_white_is_fixed_natural_identity(caishen):
    assert natural_tile(46, caishen) == caishen
    assert natural_tile(caishen, caishen) == caishen
    for tile in (t for t in TILES if t != 46):
        assert natural_tile(tile, caishen) == tile


@pytest.mark.parametrize("tile,caishen", [(True, 11), (0, 11), (11, False), (46, 48)])
def test_invalid_mapping_input(tile, caishen):
    with pytest.raises(ValueError):
        natural_tile(tile, caishen)


def test_physical_white_id_matches_project_riichi_converter_and_unity_faces():
    # Project protocol is 45=中, 46=白, 47=发, independently of display sorting.
    assert WHITE == 46
    assert natural_tile(46, 11) == 11
    assert natural_tile(47, 11) == 47


@pytest.mark.parametrize("code,kind,physical,logical", [
    ("s12", "sequence", [11, 12, 13], [11, 12, 13]),
    ("k41", "triplet", [41] * 3, [41] * 3),
    ("g46", "kong", [46] * 4, [19] * 4),
    ("G46", "kong", [46] * 4, [19] * 4),
])
def test_legacy_meld_identity(code, kind, physical, logical):
    meld = parse_meld(code, caishen=19)
    assert meld.kind == kind
    assert list(meld.physical) == physical
    assert list(meld.logical) == logical
    assert meld.concealed == (code[0] == "G")
    assert meld.as_dict()["physical"] == physical


@pytest.mark.parametrize("with_logical", [False, True])
def test_mixed_white_sequence_preserves_physical_order(with_logical):
    value = {"code": "s12", "physical": [12, 46, 13]}
    if with_logical:
        value["logical"] = [12, 11, 13]
    meld = parse_meld(value, caishen=11)
    assert meld.physical == (12, 46, 13)
    assert meld.logical == (12, 11, 13)
    assert meld.code == "s12"


@pytest.mark.parametrize("value,caishen", [
    (None, 19), (12, 19), ("", 19), ("x11", 19), ("s11", 19),
    ("s19", 19), ("s42", 19), ("k5a", 19), ("k51", 19), ("k11", 99),
    ("k19", 19), ("s18", 19),
    ({"code": "k19", "physical": [19] * 3}, 19),
    ({"code": "s12", "physical": [12, 46]}, 11),
    ({"code": "s12", "physical": "12,46,13"}, 11),
    ({"code": "s12", "physical": [12, False, 13]}, 11),
    ({"code": "s12", "physical": [12, 46, 13], "logical": "12,11,13"}, 11),
    ({"code": "s12", "physical": [12, 46, 13], "logical": [12, 11]}, 11),
    ({"code": "s12", "physical": [12, 46, 13], "logical": [12, True, 13]}, 11),
    ({"code": "s12", "physical": [12, 46, 13], "logical": [12, 14, 13]}, 11),
    ({"code": "s12", "physical": [12, 46, 13], "logical": [11, 12, 13]}, 11),
])
def test_meld_validation_rejects_illegal_physical_or_logical(value, caishen):
    with pytest.raises(ValueError):
        parse_meld(value, caishen=caishen)


def test_hand_validation_includes_physical_kong_not_logical_kong():
    hand = STANDARD[3:]
    # Four white tiles are four physical whites, not four physical 11s.
    valid = _valid(hand, [{"code": "G11", "physical": [46] * 4}], 11, complete=True)
    assert valid is not None
    assert valid.physical.count(46) == 4
    assert valid.physical.count(11) == 0
    assert _valid(hand[:-1] + [46], ["G46"], 11, complete=True) is None


@pytest.mark.parametrize("hand,melds,caishen,complete", [
    (None, [], 46, True), (STANDARD, None, 46, True),
    (STANDARD, [], 99, True), ([], ["k11"] * 6, 46, True),
    (STANDARD[:-1], [], 46, True), (STANDARD, [], 46, False),
    (STANDARD[:-1] + [False], [], 46, True),
    (STANDARD[3:], ["s42"], 46, True),
    ([11] * 5 + STANDARD[5:], [], 46, True),
    ([46] * 4 + STANDARD[4:], [], 46, True),
])
def test_invalid_hand_stops_before_solver(hand, melds, caishen, complete):
    assert _valid(hand, melds, caishen, complete=complete) is None


def test_valid_concealed_and_ready_hand_sizes():
    assert _valid(STANDARD, [], 46, complete=True)
    assert _valid(STANDARD[:-1], [], 46, complete=False)
    assert _valid(STANDARD[3:], ["s12"], 46, complete=True)
    assert _valid(STANDARD[3:-1], ["s12"], 46, complete=False)


@pytest.mark.parametrize("repeat", range(4))
def test_dealer_multiplier_original_table(repeat):
    assert dealer_multiplier(repeat) == [2, 4, 6, 8][repeat]


@pytest.mark.parametrize("repeat", [-1, 4, 1.0, True, None])
def test_invalid_dealer_repeat(repeat):
    with pytest.raises(ValueError):
        dealer_multiplier(repeat)


@pytest.mark.parametrize("winner,dealer,repeat,multiplier", list(product(range(4), range(4), range(4), (1, 2, 4))))
def test_every_winner_dealer_repeat_and_fan_payment(winner, dealer, repeat, multiplier):
    changes = payments(winner, multiplier, dealer=dealer, repeats=repeat)
    assert sum(changes) == 0
    for opponent in range(4):
        if opponent != winner:
            expected = multiplier * (2 * (repeat + 1) if dealer in (winner, opponent) else 1)
            assert changes[opponent] == -expected
    assert changes[winner] > 0


@pytest.mark.parametrize("player,dealer,repeat,kind", list(product(range(4), range(4), range(4), ("ming", "angang", "jiagang"))))
def test_every_kong_kind_and_dealer_relationship(player, dealer, repeat, kind):
    changes = kong_payments(player, kind, dealer=dealer, repeats=repeat)
    assert sum(changes) == 0
    if kind == "jiagang":
        assert changes == [0] * 4
    else:
        assert changes == payments(player, 1 if kind == "ming" else 2, dealer=dealer, repeats=repeat)


@pytest.mark.parametrize("winner,multiplier,dealer,repeat", [
    (True, 1, 0, 0), (4, 1, 0, 0), (0, True, 0, 0), (0, 3, 0, 0),
    (0, 1, -1, 0), (0, 1, False, 0), (0, 1, 0, 4),
])
def test_invalid_payments(winner, multiplier, dealer, repeat):
    with pytest.raises(ValueError):
        payments(winner, multiplier, dealer=dealer, repeats=repeat)


@pytest.mark.parametrize("kind", [None, [], True, "g", "kong"])
def test_unknown_kong_has_no_silent_default(kind):
    with pytest.raises(ValueError):
        kong_payments(0, kind)


def test_original_book_caishen_example():
    assert caishen_payments([0, 0, 1, 2]) == [-3, -3, 1, 5]


@pytest.mark.parametrize("counts", [c for c in product(range(4), repeat=4) if sum(c) <= 3])
def test_every_reachable_caishen_distribution_is_zero_sum(counts):
    changes = caishen_payments(counts)
    assert sum(changes) == 0
    assert changes == [3 * counts[i] - sum(counts[j] for j in range(4) if j != i) for i in range(4)]


@pytest.mark.parametrize("counts", [None, 4, [], [0] * 3, [0] * 5, [True, 0, 0, 0], [-1, 0, 0, 0], [4, 0, 0, 0], [1, 1, 1, 1]])
def test_invalid_caishen_distribution(counts):
    with pytest.raises(ValueError):
        caishen_payments(counts)


@pytest.mark.parametrize("standard,eight,natural_standard,natural_eight,n,self_draw,factor,shape", [
    (False, False, False, False, 0, False, None, None),
    (False, False, False, False, 2, True, None, None),
    (True, False, False, False, 1, False, 1, "standard"),
    (True, False, False, False, 1, True, 2, "standard"),
    (True, False, True, False, 0, False, 2, "standard"),
    (True, False, True, False, 1, False, 2, "standard"),
    (False, True, False, False, 1, False, 1, "eight_pairs"),
    (False, True, False, False, 1, True, 4, "eight_pairs"),
    (False, True, False, True, 0, False, 4, "eight_pairs"),
    (False, True, False, True, 1, False, 4, "eight_pairs"),
    (True, True, True, False, 1, False, 2, "standard"),
    (True, True, False, False, 1, False, 1, "eight_pairs"),
    (True, True, False, False, 1, True, 4, "eight_pairs"),
    (True, True, True, True, 0, False, 4, "eight_pairs"),
    (False, False, False, False, 3, False, 2, "three_caishen"),
    (False, False, False, False, 3, True, 2, "three_caishen"),
    (True, False, False, False, 3, False, 4, "three_caishen"),
    (False, True, False, False, 3, False, 4, "three_caishen"),
    (True, True, True, True, 3, True, 4, "three_caishen"),
])
def test_score_table_all_distinct_categories(standard, eight, natural_standard, natural_eight, n, self_draw, factor, shape):
    score = _score_features(standard=standard, eight_pairs=eight,
                           natural_standard=natural_standard, natural_eight_pairs=natural_eight,
                           caishen_count=n, self_draw=self_draw)
    if factor is None:
        assert score is None
        return
    assert score["multiplier"] == factor
    assert score["shape"] == shape
    assert score["hard"] == (factor >= 2)
    assert score["natural_win"] == (natural_standard or natural_eight)
    assert score["fan_names"][0] == {1: "软和", 2: "硬和", 4: "双番"}[factor]


@pytest.mark.parametrize("case", HAND_CASES, ids=lambda c: c["id"])
def test_real_hand_rulebook_categories_and_identity(case):
    hand = deepcopy(case["hand"])
    melds = deepcopy(case.get("melds", []))
    original = deepcopy((hand, melds))
    score = score_hand(hand, melds, caishen=case["caishen"], self_draw=case.get("self_draw", False))
    assert hand_multiplier(hand, melds, caishen=case["caishen"], self_draw=case.get("self_draw", False)) == case["multiplier"]
    assert (hand, melds) == original
    assert is_complete(hand, melds, caishen=case["caishen"]) == (case["multiplier"] is not None)
    if case["multiplier"] is None:
        assert score is None
        return
    assert score["multiplier"] == case["multiplier"]
    for field in ("natural_win", "standard", "eight_pairs"):
        if field in case:
            assert score[field] == case[field], field
    assert score["physical_hand"] == hand
    assert score["caishen_count"] == hand.count(case["caishen"])
    assert json.loads(json.dumps(score, ensure_ascii=False)) == score


@pytest.mark.parametrize("hand,kwargs", [
    (STANDARD[:-1], {}), (STANDARD, {"self_draw": 1}),
    (STANDARD, {"winning_tile": True}), (STANDARD, {"winning_tile": 39}),
    (STANDARD, {"winning_tile": 51}),
])
def test_public_score_invalid_inputs(hand, kwargs):
    assert score_hand(hand, caishen=46, **kwargs) is None
    assert hand_multiplier(hand, caishen=46, **kwargs) is None


def test_invalid_complete_and_wait_inputs():
    assert not is_complete(STANDARD[:-1], caishen=46)
    assert waiting_tiles(STANDARD, caishen=46) == set()


@pytest.mark.parametrize("hand,melds,caishen,expected", [
    (STANDARD[:-1], [], 46, {42, 46}),
    # 23456m also accepts 7m as 234m + 567m, besides 1m/4m or mapped white.
    (STANDARD[1:], [], 11, {11, 14, 17, 46}),
    ([42], ["s12", "s15", "s22", "s32", "k41"], 46, {42, 46}),
    ([46], ["s12", "s15", "s22", "s32", "k41"], 46, set(TILES)),
    ([46], ["s22", "s25", "s32", "s35", "k41"], 11, {11, 46}),
    ([41], ["s12", "s15", "s22", "s32", "k41"], 46, {46}),
    ([46,46,46,11,14,17,21,24,27,31,34,37,41,42,43,44], [], 46, set(TILES)-{46}),
])
def test_structural_waits_return_physical_tiles_and_respect_inventory(hand, melds, caishen, expected):
    original = deepcopy((hand, melds))
    assert waiting_tiles(hand, melds, caishen=caishen) == expected
    assert (hand, melds) == original


def test_third_caishen_itself_is_in_waits_even_without_ordinary_shape():
    hand = [46,46,11,14,17,21,24,27,31,34,37,41,42,43,44,45]
    assert 46 in waiting_tiles(hand, caishen=46)


def test_three_caishen_waits_still_exclude_exhausted_physical_nonjoker():
    hand = [46,46,46,11,11,11,11,14,17,21,24,27,31,34,37,41]
    assert waiting_tiles(hand, caishen=46) == set(TILES) - {11,46}


def test_adapter_cache_is_bounded_and_cleared_without_changing_results():
    from .solver_adapter import cache_info, clear_caches
    result = score_hand(STANDARD, caishen=46)
    before = cache_info()
    assert before["flags"]["maxsize"] == 32768
    assert before["policies"]["maxsize"] == 256
    assert before["witnesses"]["maxsize"] == 4096
    clear_caches()
    assert all(value["currsize"] == 0 for value in cache_info().values())
    assert score_hand(STANDARD, caishen=46) == result


def test_missing_core_witness_fails_closed_instead_of_settling(monkeypatch):
    """Fault injection only: all normal hand conformance cases use the real core.

    A future incompatible core returning True but dropping its witness must not
    cause the rule to manufacture a decomposition or send unsupported scores.
    """
    from . import solver_adapter
    assert is_complete(STANDARD, caishen=46)
    solver_adapter.clear_caches()
    monkeypatch.setattr(solver_adapter, "iter_winning_shapes", lambda *args, **kwargs: iter(()))
    with pytest.raises(RuntimeError, match="拆分见证"):
        score_hand(STANDARD, caishen=46)


def test_cached_witness_preserves_hand_order_and_cannot_be_mutated_by_caller():
    normal = score_hand(STANDARD, caishen=46, winning_tile=42)
    reverse_hand = list(reversed(STANDARD))
    reversed_score = score_hand(reverse_hand, caishen=46, winning_tile=42)
    assert normal["witness"]["winning_index"] == 16
    assert reversed_score["witness"]["winning_index"] == 1
    assert [a["physical"] for a in reversed_score["witness"]["assignments"]] == reverse_hand
    original = deepcopy(normal)
    normal["witness"]["assignments"][0]["logical"] = 99
    normal["witness"]["groups"].clear()
    normal["fan_names"].clear()
    assert score_hand(STANDARD, caishen=46, winning_tile=42) == original


def test_lightweight_multiplier_never_requests_decomposition(monkeypatch):
    from . import solver_adapter
    def forbidden_witness(*args, **kwargs):
        raise AssertionError("lightweight hints must not request a winning witness")
    monkeypatch.setattr(solver_adapter, "winning_witness", forbidden_witness)
    for case in HAND_CASES:
        assert hand_multiplier(case["hand"], case.get("melds", []), caishen=case["caishen"],
                               self_draw=case.get("self_draw", False)) == case["multiplier"]
