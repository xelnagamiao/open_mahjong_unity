"""MIL 2023 pp. 10–13 payments and all four seats of the printed horse table."""

from copy import deepcopy
import json

import pytest

from ..settlement import kong_payments, refund_kongs, win_payments, winning_horses


# Independent transcription of PDF p.13 and the project's TileIdOrder.cs:
# 41 east, 42 south, 43 west, 44 north, 45 red, 46 white, 47 green.
BOOK_HORSES = (
    {11, 15, 19, 21, 25, 29, 31, 35, 39, 41},
    {12, 16, 22, 26, 32, 36, 42, 45},
    {13, 17, 23, 27, 33, 37, 43, 47},
    {14, 18, 24, 28, 34, 38, 44, 46},
)
PHYSICAL_TILES = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10)) + tuple(range(41, 48)) + (55, 56, 57, 58)


def zero_sum(row):
    assert set(row) == {0, 1, 2, 3}
    assert all(type(value) is int for value in row.values())
    assert sum(row.values()) == 0


@pytest.mark.parametrize("dealer", range(4))
@pytest.mark.parametrize("winner", range(4))
def test_all_thirty_eight_physical_tiles_against_all_rotations_of_printed_horse_table(dealer, winner):
    expected = BOOK_HORSES[(winner - dealer) % 4]
    for tile in PHYSICAL_TILES:
        assert winning_horses([tile], winner, dealer) == ([tile] if tile in expected else [])


@pytest.mark.parametrize("count", range(5))
def test_horse_wall_shortage_is_not_filled_and_ghosts_never_hit(count):
    tiles = [11, 55, 15, 19][:count]
    assert winning_horses(tiles, 0) == [tile for tile in tiles if tile != 55]
    assert winning_horses([55, 56, 57, 58][:count], 0) == []


def test_horse_hits_preserve_wall_order_and_duplicate_physical_copies():
    assert winning_horses([15, 55, 11, 15], 0) == [15, 11, 15]
    assert winning_horses([43, 46, 47, 33], 2) == [43, 47, 33]
    assert winning_horses([44, 47, 46, 34], 3) == [44, 46, 34]


@pytest.mark.parametrize("tiles", [[11] * 5, [51], [105], [10], [48], [True], [11.0], ["11"], [None]])
def test_horse_validation_rejects_excess_count_nonphysical_tiles_and_wrong_types(tiles):
    with pytest.raises(ValueError):
        winning_horses(tiles, 0)


@pytest.mark.parametrize("field", ["winner", "dealer"])
@pytest.mark.parametrize("value", [-1, 4, True, 1.0, "0", None])
def test_horse_seats_are_strictly_validated(field, value):
    with pytest.raises(ValueError):
        winning_horses([], **{"winner": 0, "dealer": 0, field: value})


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("hits", range(5))
@pytest.mark.parametrize("base", [0, 4, 1024])
def test_normal_self_draw_each_opponent_pays_base_and_one_per_horse(winner, hits, base):
    result = win_payments(winner, base, horse_hits=hits)
    assert result["base_changes"] == {seat: base * (3 if seat == winner else -1) for seat in range(4)}
    assert result["horse_changes"] == {seat: hits * (3 if seat == winner else -1) for seat in range(4)}
    assert result["changes"] == {seat: (base + hits) * (3 if seat == winner else -1) for seat in range(4)}
    for row in result.values():
        zero_sum(row)


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("rob_kong", [False, True])
def test_discard_win_and_robbed_added_kong_charge_only_the_correct_payer(winner, rob_kong):
    for payer in set(range(4)) - {winner}:
        amount = 12 if rob_kong else 4
        result = win_payments(winner, 4, payer=payer, rob_kong=rob_kong)
        assert result["base_changes"] == {seat: amount if seat == winner else -amount if seat == payer else 0 for seat in range(4)}
        assert result["horse_changes"] == dict.fromkeys(range(4), 0)
        assert result["changes"] == result["base_changes"]
        zero_sum(result["changes"])


@pytest.mark.parametrize("winner", range(4))
@pytest.mark.parametrize("hits", range(5))
def test_direct_kong_supplement_win_charges_three_shares_and_horses_to_one_payer(winner, hits):
    for payer in set(range(4)) - {winner}:
        result = win_payments(winner, 4, horse_hits=hits, direct_kong_payer=payer)
        assert result["base_changes"] == {seat: 12 if seat == winner else -12 if seat == payer else 0 for seat in range(4)}
        assert result["horse_changes"] == {seat: 3 * hits if seat == winner else -3 * hits if seat == payer else 0 for seat in range(4)}
        assert result["changes"][winner] == 3 * (4 + hits)
        for row in result.values():
            zero_sum(row)


def test_subsequent_added_or_concealed_kong_uses_normal_self_draw_payment():
    direct = win_payments(0, 10, horse_hits=2, direct_kong_payer=1)["changes"]
    subsequent = win_payments(0, 10, horse_hits=2)["changes"]
    assert direct == {0: 36, 1: -36, 2: 0, 3: 0}
    assert subsequent == {0: 36, 1: -12, 2: -12, 3: -12}


@pytest.mark.parametrize("override", [
    {"winner": -1}, {"winner": 4}, {"winner": True}, {"winner": "0"},
    {"base_score": -1}, {"base_score": True}, {"base_score": 4.0}, {"base_score": "4"}, {"base_score": None},
    {"horse_hits": -1}, {"horse_hits": 5}, {"horse_hits": True}, {"horse_hits": 1.0}, {"horse_hits": "1"},
    {"payer": 0}, {"payer": 4}, {"payer": True},
    {"direct_kong_payer": 0}, {"direct_kong_payer": -1}, {"direct_kong_payer": False},
    {"payer": 1, "horse_hits": 1}, {"payer": 1, "direct_kong_payer": 2},
    {"rob_kong": True}, {"rob_kong": True, "direct_kong_payer": 1}, {"rob_kong": 1},
])
def test_inconsistent_win_payment_inputs_are_rejected(override):
    with pytest.raises(ValueError):
        win_payments(**{"winner": 0, "base_score": 4, **override})


@pytest.mark.parametrize("player", range(4))
@pytest.mark.parametrize("drawn", [False, True])
def test_direct_kong_only_charges_the_discarder_three_points(player, drawn):
    for payer in set(range(4)) - {player}:
        result = kong_payments(player, "direct", payer=payer, drawn=drawn)
        assert result == {seat: 3 if seat == player else -3 if seat == payer else 0 for seat in range(4)}
        zero_sum(result)


@pytest.mark.parametrize("player", range(4))
@pytest.mark.parametrize("drawn", [False, True])
def test_concealed_kong_charges_every_opponent_two_points(player, drawn):
    result = kong_payments(player, "concealed", drawn=drawn)
    assert result == {seat: 6 if seat == player else -2 for seat in range(4)}
    zero_sum(result)


@pytest.mark.parametrize("player", range(4))
@pytest.mark.parametrize("drawn", [False, True])
def test_added_kong_only_scores_when_using_the_just_drawn_tile(player, drawn):
    result = kong_payments(player, "added", drawn=drawn)
    expected = {seat: 3 if seat == player else -1 for seat in range(4)} if drawn else dict.fromkeys(range(4), 0)
    assert result == expected
    zero_sum(result)


@pytest.mark.parametrize("override", [
    {"player": -1}, {"player": 4}, {"player": True}, {"player": "0"},
    {"kind": "unknown"}, {"kind": None}, {"drawn": 1}, {"drawn": "true"},
    {"payer": 1}, {"kind": "added", "payer": 1},
    {"kind": "direct"}, {"kind": "direct", "payer": 0},
    {"kind": "direct", "payer": True}, {"kind": "direct", "payer": 4},
])
def test_kong_payment_rejects_bad_seats_types_and_payer_combinations(override):
    with pytest.raises(ValueError):
        kong_payments(**{"player": 0, "kind": "concealed", **override})


@pytest.mark.parametrize("string_keys", [False, True])
def test_draw_refunds_all_kong_entries_exactly_and_preserves_ledger(string_keys):
    ledger = [{"changes": row} for row in (
        kong_payments(0, "direct", payer=2), kong_payments(1, "added"),
        kong_payments(2, "concealed"), kong_payments(3, "added", drawn=False),
        kong_payments(1, "direct", payer=3), kong_payments(3, "concealed"),
    )]
    accrued = {seat: sum(event["changes"][seat] for event in ledger) for seat in range(4)}
    if string_keys:
        ledger = json.loads(json.dumps(ledger))
    original = deepcopy(ledger)
    refund = refund_kongs(ledger)
    assert ledger == original
    assert refund == {seat: -accrued[seat] for seat in range(4)}
    assert {seat: accrued[seat] + refund[seat] for seat in range(4)} == dict.fromkeys(range(4), 0)
    zero_sum(refund)


def test_empty_and_sparse_zero_sum_kong_ledgers_are_safe_to_refund():
    assert refund_kongs([]) == dict.fromkeys(range(4), 0)
    assert refund_kongs([{"changes": {0: 3, 1: -3}}]) == {0: -3, 1: 3, 2: 0, 3: 0}
    assert refund_kongs([{"changes": {"0": 3, "1": -3}}]) == {0: -3, 1: 3, 2: 0, 3: 0}


@pytest.mark.parametrize("row", [
    {0: 3, 1: -2}, {0: "3", 1: -3}, {0: 3.0, 1: -3},
    {0: True, 1: -1}, {0: None, 1: 0},
])
def test_refund_rejects_non_zero_sum_or_non_integer_entries(row):
    with pytest.raises(ValueError):
        refund_kongs([{"changes": row}])
