"""Independent scoring witnesses from MIL 2023 pp. 10–12, not a structural solver.

The state/core integration suite must establish that a physical hand actually has
each interpretation. These tests isolate scoring a known valid interpretation.
The no-ghost and discarded-ghost coefficients multiply, as explicitly confirmed
by the user for this platform; this is not described as an official MIL erratum.
"""

from collections import Counter
from dataclasses import replace

import pytest

from ..config import GHOSTS, ORPHANS
from ..scoring import Context, Interpretation, coefficient, score_interpretation


def standard(groups, pair, *, winning=None):
    groups = tuple(tuple(group) for group in groups)
    hand = tuple(tile for group in groups for tile in group) + (pair, pair)
    return Interpretation("standard", hand, groups=groups, pair=pair, winning_logical=winning)


PLAIN = standard(((11, 12, 13), (24, 25, 26), (34, 35, 36), (41, 41, 41)), 47)
PUNGS = standard(((12,) * 3, (25,) * 3, (36,) * 3, (41,) * 3), 47)
HALF = standard(((11, 12, 13), (14, 15, 16), (17,) * 3, (41,) * 3), 47)
FULL = standard(((11, 12, 13), (14, 15, 16), (17, 18, 19), (12,) * 3), 18)
TERMINALS = standard(((11,) * 3, (19,) * 3, (41,) * 3, (47,) * 3), 29)
LITTLE_DRAGONS = standard(((45,) * 3, (46,) * 3, (11, 12, 13), (24, 25, 26)), 47)
LITTLE_WINDS = standard(((41,) * 3, (42,) * 3, (43,) * 3, (11, 12, 13)), 44)
BIG_DRAGONS = standard(((45,) * 3, (46,) * 3, (47,) * 3, (22,) * 3), 31)
BIG_WINDS = standard(((41,) * 3, (42,) * 3, (43,) * 3, (44,) * 3), 15)
PAIRS = Interpretation("seven_pairs", tuple(tile for tile in (11, 12, 23, 24, 35, 36, 47) for _ in range(2)))
THIRTEEN = Interpretation("thirteen_orphans", tuple(sorted(ORPHANS)) + (11,))
FOUR_GHOST_HAND = (11, 12, 13, 21, 22, 23, 31, 32, 33, 41, *GHOSTS)
FOUR_GHOSTS = Interpretation("four_ghosts", FOUR_GHOST_HAND)
NINE = standard(((11,) * 3, (12, 13, 14), (16, 17, 18), (19,) * 3), 15, winning=15)


def score(shape=PLAIN, *, melds=(), physical=None, **context):
    return score_interpretation(shape.logical_hand if physical is None else physical, melds, shape, Context(**context))


# Expected IDs and totals are transcribed from the printed table and implication
# rule, rather than deriving expectations from the production FANS dictionary.
@pytest.mark.parametrize("shape,context,expected,fan", [
    (PLAIN, {"self_draw": True}, {"self_draw", "concealed"}, 2),
    (PLAIN, {}, {"concealed"}, 1),
    (PUNGS, {}, {"all_pungs", "concealed"}, 5),
    (HALF, {}, {"half_flush", "concealed"}, 5),
    (PLAIN, {"rob_kong": True}, {"rob_kong", "concealed"}, 5),
    (PLAIN, {"last_tile": True}, {"last_tile", "concealed"}, 5),
    (PLAIN, {"self_draw": True, "kong_flower": True}, {"kong_flower", "concealed"}, 6),
    (PAIRS, {}, {"seven_pairs"}, 6),
    (FULL, {}, {"full_flush", "concealed"}, 9),
    (TERMINALS, {}, {"terminals_honors", "concealed"}, 13),
    (LITTLE_DRAGONS, {}, {"little_three_dragons", "concealed"}, 13),
    (LITTLE_WINDS, {}, {"little_four_winds", "half_flush", "concealed"}, 17),
    (PLAIN, {"self_draw": True, "heavenly": True}, {"heavenly"}, 12),
    (PLAIN, {"self_draw": True, "earthly": True}, {"earthly"}, 12),
    (FOUR_GHOSTS, {}, {"four_ghosts"}, 12),
    (NINE, {}, {"nine_gates"}, 16),
    (BIG_DRAGONS, {}, {"big_three_dragons", "all_pungs", "concealed"}, 21),
    (BIG_WINDS, {}, {"big_four_winds", "half_flush", "concealed"}, 21),
    (THIRTEEN, {}, {"thirteen_orphans"}, 16),
])
def test_all_nineteen_patterns_and_compulsory_exclusions(shape, context, expected, fan):
    result = score(shape, **context)
    assert set(result["fan_ids"]) == expected
    assert result["fan"] == fan
    assert len(result["fan_names"]) == len(expected)
    assert all(label.startswith("GD|") for label in result["fan_names"])
    assert sum(int(label.split("|")[2]) for label in result["fan_names"]) == fan


@pytest.mark.parametrize("meld", ["k41", "g41", "G41"])
def test_only_concealed_kong_preserves_concealed_hand(meld):
    shape = standard(((11, 12, 13), (24, 25, 26), (34, 35, 36)), 47)
    result = score(shape, melds=(meld,), self_draw=True)
    assert ("concealed" in result["fan_ids"]) is (meld == "G41")
    assert result["fan"] == (2 if meld == "G41" else 1)


@pytest.mark.parametrize("meld", ["k45", "g45", "G45"])
def test_exposed_and_concealed_melds_both_participate_in_structural_pattern_score(meld):
    shape = standard(((46,) * 3, (47,) * 3, (11, 12, 13)), 41)
    result = score(shape, melds=(meld,))
    expected = {"big_three_dragons", "half_flush"}
    if meld == "G45":
        expected.add("concealed")
    assert set(result["fan_ids"]) == expected
    assert result["fan"] == (21 if meld == "G45" else 20)


def test_seven_pairs_may_stack_flush_but_does_not_count_pungs_or_terminals():
    pure = Interpretation("seven_pairs", tuple(tile for tile in range(11, 18) for _ in range(2)))
    assert set(score(pure)["fan_ids"]) == {"seven_pairs", "full_flush"}
    assert score(pure)["fan"] == 14
    orphan_pairs = Interpretation("seven_pairs", tuple(tile for tile in (11, 19, 21, 29, 31, 39, 41) for _ in range(2)))
    assert score(orphan_pairs)["fan_ids"] == ["seven_pairs"]
    repeated_pair = Interpretation("seven_pairs", (11,) * 4 + tuple(tile for tile in (12, 23, 24, 35, 46) for _ in range(2)))
    assert score(repeated_pair)["fan_ids"] == ["seven_pairs"]


def test_honors_only_stacks_wind_and_terminal_patterns_without_inventing_a_flush():
    shape = standard(((41,) * 3, (42,) * 3, (43,) * 3, (44,) * 3), 45)
    result = score(shape)
    assert set(result["fan_ids"]) == {"concealed", "big_four_winds", "terminals_honors"}
    assert result["fan"] == 29


def test_noncompulsory_compound_patterns_survive_implication_exclusions():
    shape = standard(((41,) * 3, (42,) * 3, (43,) * 3, (11,) * 3), 44)
    result = score(shape)
    assert set(result["fan_ids"]) == {"concealed", "half_flush", "little_four_winds", "terminals_honors"}
    assert result["fan"] == 29


@pytest.mark.parametrize("flag,fan", [("heavenly", 12), ("earthly", 12), ("last_tile", 4), ("kong_flower", 5), ("rob_kong", 4)])
def test_four_ghosts_only_stack_the_five_permitted_accidental_patterns(flag, fan):
    result = score(FOUR_GHOSTS, self_draw=flag != "rob_kong", **{flag: True})
    assert set(result["fan_ids"]) == {"four_ghosts", flag}
    assert result["fan"] == 12 + fan
    assert not ({"self_draw", "concealed", "full_flush", "all_pungs"} & set(result["fan_ids"]))


def test_four_ghosts_do_not_import_the_alternative_interpretation_patterns():
    physical = NINE.logical_hand[:-4] + GHOSTS
    four = replace(NINE, kind="four_ghosts")
    assert score(four, physical=physical, self_draw=True)["fan_ids"] == ["four_ghosts"]
    # A separately proved non-four-ghost interpretation retains its own score;
    # choosing the highest valid interpretation belongs to the real solver suite.
    other = score(NINE, physical=physical)
    assert other["fan_ids"] == ["nine_gates"] and other["fan"] == 16


def test_last_supplement_may_stack_kong_flower_and_last_tile_once():
    result = score(PLAIN, self_draw=True, kong_flower=True, last_tile=True)
    assert set(result["fan_ids"]) == {"concealed", "kong_flower", "last_tile"}
    assert result["fan"] == 10


@pytest.mark.parametrize("shape", [PAIRS, THIRTEEN])
def test_special_shape_can_add_self_draw_when_not_an_implied_pattern(shape):
    base = score(shape)
    drawn = score(shape, self_draw=True)
    assert drawn["fan"] == base["fan"] + 1
    assert "self_draw" in drawn["fan_ids"] and "concealed" not in drawn["fan_ids"]


NINE_PARTITIONS = (
    (11, 19, ((11, 11, 11), (11, 12, 13), (14, 15, 16), (17, 18, 19))),
    (12, 12, ((11, 11, 11), (13, 14, 15), (16, 17, 18), (19, 19, 19))),
    (13, 11, ((11, 12, 13), (13, 14, 15), (16, 17, 18), (19, 19, 19))),
    (14, 19, ((11, 11, 11), (12, 13, 14), (14, 15, 16), (17, 18, 19))),
    (15, 15, ((11, 11, 11), (12, 13, 14), (16, 17, 18), (19, 19, 19))),
    (16, 11, ((11, 12, 13), (14, 15, 16), (16, 17, 18), (19, 19, 19))),
    (17, 19, ((11, 11, 11), (12, 13, 14), (15, 16, 17), (17, 18, 19))),
    (18, 18, ((11, 11, 11), (12, 13, 14), (15, 16, 17), (19, 19, 19))),
    (19, 11, ((11, 12, 13), (14, 15, 16), (17, 18, 19), (19, 19, 19))),
)


@pytest.mark.parametrize("suit_shift", [0, 10, 20])
@pytest.mark.parametrize("winning,pair,groups", NINE_PARTITIONS)
def test_nine_gates_requires_original_nine_sided_wait_all_suits_and_draws(suit_shift, winning, pair, groups):
    shifted = tuple(tuple(tile + suit_shift for tile in group) for group in groups)
    shape = standard(shifted, pair + suit_shift, winning=winning + suit_shift)
    expected = Counter([11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19, winning])
    assert Counter(tile - suit_shift for tile in shape.logical_hand) == expected
    assert score(shape)["fan_ids"] == ["nine_gates"]
    assert score(shape)["fan"] == 16


@pytest.mark.parametrize("winning", [None, 11, 47])
def test_nine_gates_is_not_given_for_unknown_or_wrong_winning_component(winning):
    result = score(replace(NINE, winning_logical=winning))
    assert set(result["fan_ids"]) == {"concealed", "full_flush"}
    assert result["fan"] == 9


def test_nine_gates_is_not_given_with_a_previously_declared_kong():
    shape = standard(((12, 13, 14), (16, 17, 18), (19,) * 3), 15, winning=15)
    result = score(shape, melds=("G11",))
    assert set(result["fan_ids"]) == {"concealed", "full_flush"}


@pytest.mark.parametrize("discarded", range(5))
@pytest.mark.parametrize("has_ghost", [False, True])
def test_current_coefficient_policy_all_discard_counts_and_physical_ghost_states(discarded, has_ghost):
    physical = PLAIN.logical_hand if not has_ghost else PLAIN.logical_hand[:-1] + (55,)
    expected = (1 << discarded) * (1 if has_ghost else 2)
    assert coefficient(physical, discarded) == expected
    result = score(PLAIN, physical=physical, self_draw=True, discarded_ghosts=discarded)
    assert result["coefficient"] == expected
    assert result["base_score"] == 2 * expected


@pytest.mark.parametrize("bad", [-1, 5, True, False, 1.0, "1", None])
def test_coefficient_rejects_non_integer_or_out_of_range_discard_count(bad):
    with pytest.raises(ValueError):
        coefficient(PLAIN.logical_hand, bad)


@pytest.mark.parametrize("require_minimum", [False, True])
def test_four_point_minimum_can_be_disabled_but_two_fan_minimum_is_always_required(require_minimum):
    physical = PLAIN.logical_hand[:-1] + (55,)
    result = score(PLAIN, physical=physical, self_draw=True, require_minimum_score=require_minimum)
    assert result["fan"] == 2 and result["base_score"] == 2
    assert result["is_win"] is (not require_minimum)
    low_fan = score(PLAIN, physical=physical, discarded_ghosts=4, require_minimum_score=require_minimum)
    assert low_fan["fan"] == 1 and low_fan["base_score"] == 16 and not low_fan["is_win"]
    exact = score(PLAIN, self_draw=True, require_minimum_score=require_minimum)
    assert exact["fan"] == 2 and exact["base_score"] == 4 and exact["is_win"]


def test_scoring_preserves_physical_joker_mapping_without_mutating_witness():
    physical = PLAIN.logical_hand[:-1] + (55,)
    shape = replace(PLAIN, substitutions=((55, 47),), winning_logical=47)
    result = score(shape, physical=physical, self_draw=True)
    assert result["substitutions"] == [{"physical": 55, "logical": 47}]
    assert result["logical_hand"] == list(PLAIN.logical_hand)
    assert physical[-1] == 55 and shape.substitutions == ((55, 47),)


@pytest.mark.parametrize("ghost_count", [0, 1, 2, 3])
def test_four_ghost_witness_requires_exactly_four_physical_ghosts(ghost_count):
    with pytest.raises(ValueError):
        score(FOUR_GHOSTS, physical=PLAIN.logical_hand[:14 - ghost_count] + GHOSTS[:ghost_count])


def test_unknown_interpretation_kind_is_rejected():
    with pytest.raises(ValueError):
        score(replace(PLAIN, kind="unknown"))
