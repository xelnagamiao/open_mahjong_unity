"""Independent all-candidate oracle for small wildcard counts.

This intentionally expands 0--2 wildcard targets in tests only, then uses a
literal remove-pair/first-tile partitioner.  It compares every logical target
multiset, every group template and every winning-component attribution, not
just existence or the first returned witness.
"""

from collections import Counter
from dataclasses import replace
from functools import lru_cache
from itertools import product
import random

import pytest

from . import (ORPHANS, TILES, ExternalMeld, JokerPolicy, can_form_melds,
               can_form_pairs, can_win, iter_winning_shapes, structural_waits)

REVIEW_COUNTS = {"full_candidate_comparisons": 0, "witnesses_checked": 0,
                 "logical_multisets_checked": 0, "group_templates_checked": 0,
                 "winning_component_variants_checked": 0,
                 "fast_slow_public_comparisons": 0,
                 "pairs_fast_slow_comparisons": 0,
                 "logical_projection_comparisons": 0,
                 "logical_projection_witnesses": 0}


def _canonical(groups):
    return tuple(sorted(groups))


def _logical_projection(shape):
    """Exactly the opt-in feature key; physical allocation is not a feature."""
    return (shape.kind, _canonical(tuple(
        (g.kind, g.logical, g.external, g.concealed) for g in shape.groups)),
        shape.winning_logical_tile)


def _assert_physical_witness(shape, hand, policy, winning_index):
    assert shape.winning_index == winning_index
    closed = [g for g in shape.groups if not g.external]
    assert sorted(i for g in closed for i in g.indices) == list(range(len(hand)))
    assert len(shape.assignment) == len(hand)
    for group in closed:
        assert len(group.indices) == len(group.physical) == len(group.logical)
        for index, physical, logical in zip(group.indices, group.physical, group.logical):
            assert hand[index] == physical
            assert shape.assignment[index] == logical
            if physical in policy.joker_tiles:
                assert logical in (policy.logical_tiles if policy.joker_targets is None else policy.joker_targets)
            else:
                assert policy.natural(physical) == logical
    expected_uses = {(i, tile, policy.natural(tile), shape.assignment[i])
                     for i, tile in enumerate(hand) if tile in policy.joker_tiles}
    actual_uses = {(u.index, u.physical, u.natural, u.logical) for u in shape.joker_uses}
    assert actual_uses == expected_uses
    assert shape.is_natural == all(not u.substituted for u in shape.joker_uses + shape.external_joker_uses)
    if winning_index >= 0:
        assert winning_index in shape.groups[shape.winning_group].indices
        assert shape.winning_logical_tile == shape.assignment[winning_index]
    else:
        assert shape.winning_group == -1 and shape.winning_logical_tile == 0


def _assert_projection_matches(hand, policy, melds=(), winning_index=-1, *, full=None):
    if full is None:
        full = {_logical_projection(s) for s in iter_winning_shapes(
            hand, melds, policy, winning_index=winning_index)}
    projected = set()
    count = 0
    for shape in iter_winning_shapes(hand, melds, policy, winning_index=winning_index,
                                     equivalence="logical_structure"):
        _assert_physical_witness(shape, hand, policy, winning_index)
        projected.add(_logical_projection(shape))
        count += 1
    assert projected == full, (hand, policy, full - projected, projected - full)
    assert count == len(projected), "Logical projection emitted a repeated feature key"
    REVIEW_COUNTS["logical_projection_comparisons"] += 1
    REVIEW_COUNTS["logical_projection_witnesses"] += count
    return count


@lru_cache(maxsize=32768)
def _literal_sets(values):
    if not values:
        return frozenset({()})
    counts = Counter(values)
    first = values[0]
    results = set()
    choices = []
    if counts[first] >= 3:
        choices.append(("triplet", (first,) * 3))
    if first < 40 and first % 10 <= 7 and counts[first + 1] and counts[first + 2]:
        choices.append(("sequence", (first, first + 1, first + 2)))
    for group in choices:
        remainder = list(values)
        for tile in group[1]:
            remainder.remove(tile)
        for following in _literal_sets(tuple(remainder)):
            results.add(_canonical((group,) + following))
    return frozenset(results)


def _literal_templates(values, policy, external_count):
    counts = Counter(values)
    if "standard" in policy.shapes:
        for pair, number in counts.items():
            if number < 2 or (policy.pair_tiles is not None and pair not in policy.pair_tiles):
                continue
            remainder = list(values)
            remainder.remove(pair)
            remainder.remove(pair)
            for groups in _literal_sets(tuple(remainder)):
                if len(groups) == policy.meld_count - external_count:
                    yield "standard", _canonical(groups + (("pair", (pair, pair)),))
    if external_count:
        return
    if "seven_pairs" in policy.shapes and len(values) == 14 and all(n % 2 == 0 for n in counts.values()):
        yield "seven_pairs", _canonical(tuple(("pair", (t, t))
                                             for t, n in counts.items() for _ in range(n // 2)))
    if "eight_pairs" in policy.shapes and len(values) == 17 and sum(n % 2 for n in counts.values()) == 1:
        groups = tuple(("pair", (t, t)) for t, n in counts.items() for _ in range(n // 2))
        singleton = next(t for t, n in counts.items() if n % 2)
        yield "eight_pairs", _canonical(groups + (("singleton", (singleton,)),))
    if "thirteen_orphans" in policy.shapes and len(values) == 14 and set(counts) == set(ORPHANS):
        pair = next(t for t, n in counts.items() if n == 2)
        if policy.pair_tiles is None or pair in policy.pair_tiles:
            yield "thirteen_orphans", _canonical(tuple(
                ("pair" if n == 2 else "singleton", (t,) * n) for t, n in counts.items()))


def _physical_external(meld):
    if isinstance(meld, ExternalMeld):
        return meld.physical
    value = int(meld[1:])
    return ((value - 1, value, value + 1) if meld[0] == "s"
            else (value,) * (3 if meld[0] == "k" else 4))


def _brute_candidates(hand, melds, policy, winning_index):
    templates, targets, winners = set(), set(), set()
    if len(hand) != 3 * (policy.meld_count - len(melds)) + 2:
        return templates, targets, winners
    supply = Counter(hand)
    supply.update(t for meld in melds for t in _physical_external(meld))
    if any(number > dict(policy.physical_limits).get(tile, 1 if tile >= 51 else 4)
           for tile, number in supply.items()):
        return templates, targets, winners
    wild = [i for i, t in enumerate(hand) if t in policy.joker_tiles]
    mapping = dict(policy.natural_map)
    allowed = policy.logical_tiles if policy.joker_targets is None else policy.joker_targets
    # Full-domain 0--2, or deliberately tiny 3--4-target domains up to four
    # ghosts, remain exhaustive while keeping this test oracle bounded.
    assert len(wild) <= 2 or (len(wild) <= 4 and len(allowed) <= 4)
    for replacements in product(allowed, repeat=len(wild)):
        assignment = [mapping.get(t, t) for t in hand]
        for i, target in zip(wild, replacements):
            assignment[i] = target
        if any(t not in policy.logical_tiles for t in assignment):
            continue
        if policy.closed_cap and max(Counter(assignment).values(), default=0) > policy.closed_cap:
            continue
        logical = tuple(sorted(assignment))
        for kind, groups in _literal_templates(logical, policy, len(melds)):
            templates.add((kind, groups))
            targets.add((kind, logical))
            for group in groups:
                if assignment[winning_index] in group[1]:
                    winners.add((kind, groups, assignment[winning_index], group))
    return templates, targets, winners


def _assert_all_candidates(hand, policy, melds=(), winning_index=None):
    if winning_index is None:
        winning_index = len(hand) - 1
    expected_templates, expected_targets, expected_winners = _brute_candidates(hand, melds, policy, winning_index)
    templates, targets, winners, projection = set(), set(), set(), set()
    count = 0
    for shape in iter_winning_shapes(hand, melds, policy, winning_index=winning_index):
        count += 1
        projection.add(_logical_projection(shape))
        assert count <= 100000, "Tiny oracle fixture unexpectedly produced over 100,000 witnesses"
        groups = _canonical(tuple((g.kind, g.logical) for g in shape.groups if not g.external))
        templates.add((shape.kind, groups))
        targets.add((shape.kind, tuple(sorted(shape.closed_logical_tiles))))
        winning_group = shape.groups[shape.winning_group]
        winners.add((shape.kind, groups, shape.winning_logical_tile,
                     (winning_group.kind, winning_group.logical)))
        assert winning_index in winning_group.indices
        assert shape.winning_index == winning_index
        assert tuple(hand[i] for i in winning_group.indices) == winning_group.physical
        indices = [i for group in shape.groups if not group.external for i in group.indices]
        assert sorted(indices) == list(range(len(hand)))
        for group in shape.groups:
            assert group.tiles == group.logical
            if group.external:
                continue
            for i, physical, logical in zip(group.indices, group.physical, group.logical):
                assert hand[i] == physical and shape.assignment[i] == logical
                if physical in policy.joker_tiles:
                    assert logical in (policy.logical_tiles if policy.joker_targets is None else policy.joker_targets)
                else:
                    assert dict(policy.natural_map).get(physical, physical) == logical
        assert shape.is_natural == all(not use.substituted for use in shape.joker_uses)
    details = (hand, melds, policy)
    assert targets == expected_targets, ("target multiset", details, expected_targets - targets, targets - expected_targets)
    assert templates == expected_templates, ("template", details, expected_templates - templates, templates - expected_templates)
    assert winners == expected_winners, ("winning group", details, expected_winners - winners, winners - expected_winners)
    assert can_win(hand, melds, policy) == bool(expected_templates)
    _assert_projection_matches(hand, policy, melds, winning_index, full=projection)
    REVIEW_COUNTS["full_candidate_comparisons"] += 1
    REVIEW_COUNTS["witnesses_checked"] += count
    REVIEW_COUNTS["logical_multisets_checked"] += len(targets)
    REVIEW_COUNTS["group_templates_checked"] += len(templates)
    REVIEW_COUNTS["winning_component_variants_checked"] += len(winners)
    return count


def test_all_candidates_for_96_restricted_domain_and_pair_cap_cases():
    rng = random.Random(2026100208)
    domain = (11, 12, 13, 14, 15, 16, 17, 21, 22, 23, 41)
    witness_count = 0
    for case in range(48):
        size = 1 + case % 2
        ghosts = (case // 2) % 3
        hand = rng.sample([t for t in domain for _ in range(4)], 3 * size + 2 - ghosts) + [55, 56][:ghosts]
        for cap in (None, 4):
            targets = None if case % 3 == 0 else domain[:7] if case % 3 == 1 else ()
            pairs = None if case % 4 < 2 else (11, 14, 41)
            policy = JokerPolicy(joker_tiles=(55, 56), logical_tiles=domain,
                                 physical_tiles=domain + (55, 56), meld_count=size,
                                 closed_cap=cap, joker_targets=targets, pair_tiles=pairs)
            witness_count += _assert_all_candidates(hand, policy)
    assert witness_count > 0


@pytest.mark.parametrize("cap", [None, 4])
@pytest.mark.parametrize("targets", [None, tuple(range(11, 20)), (41, 42)])
@pytest.mark.parametrize("hand", [
    [11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 55, 56],
    [11, 11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 42, 55, 56],
    [11, 12, 13, 14, 14, 14, 14, 15, 15, 16, 16, 17, 17, 55],
])
def test_all_four_meld_target_templates_and_winning_components(hand, cap, targets):
    policy = JokerPolicy(joker_tiles=(55, 56), physical_tiles=TILES + (55, 56),
                         closed_cap=cap, joker_targets=targets,
                         shapes=("standard", "seven_pairs", "thirteen_orphans"))
    _assert_all_candidates(hand, policy)


@pytest.mark.parametrize("cap", [None, 4])
@pytest.mark.parametrize("ghosts", [0, 1, 2])
def test_seventeen_tiles_all_candidates_and_eight_pairs_singleton(cap, ghosts):
    ordinary = [11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 21, 21, 22, 22, 23]
    hand = ordinary[:17 - ghosts] + [55, 56][:ghosts]
    policy = JokerPolicy(joker_tiles=(55, 56), physical_tiles=TILES + (55, 56),
                         meld_count=5, closed_cap=cap, shapes=("standard", "eight_pairs"))
    _assert_all_candidates(hand, policy)


@pytest.mark.parametrize("cap", [None, 1, 2, 4])
@pytest.mark.parametrize("targets,pairs", [(None, None), (ORPHANS, (11,)), ((11, 19), None), ((), None)])
def test_orphans_all_pair_choices_and_restricted_jokers(cap, targets, pairs):
    hand = list(ORPHANS[1:]) + [55, 56]
    policy = JokerPolicy(joker_tiles=(55, 56), physical_tiles=TILES + (55, 56),
                         closed_cap=cap, joker_targets=targets, pair_tiles=pairs,
                         shapes=("thirteen_orphans",))
    _assert_all_candidates(hand, policy)


@pytest.mark.parametrize("winning_index", [0, 3, 4, 16])
def test_wenzhou_natural_white_mapping_and_joker_winning_identity(winning_index):
    hand = [46, 12, 13, 11, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
    policy = JokerPolicy(joker_tiles=(11,), natural_map=((46, 11),), meld_count=5,
                         shapes=("standard", "eight_pairs"))
    _assert_all_candidates(hand, policy, winning_index=winning_index)
    meld = ExternalMeld("sequence", (46, 12, 13), (11, 12, 13))
    _assert_all_candidates(hand[3:], policy, (meld,), winning_index=0)


def test_aliases_share_logical_count_but_physical_supply_remains_separate():
    hand = [46, 46, 46, 46, 11, 12, 13, 21, 22, 23, 31, 32, 33, 55]
    policy = JokerPolicy(joker_tiles=(55,), physical_tiles=TILES + (55,),
                         natural_map=((46, 41),), closed_cap=4)
    _assert_all_candidates(hand, policy)
    _assert_all_candidates(hand, replace(policy, closed_cap=None))
    hard = [46, 12, 13, 11, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
    _assert_all_candidates(hard, JokerPolicy(natural_map=((46, 11),), meld_count=5))


def test_mapped_white_winning_in_sequence_or_pung_when_same_logical_tile_is_native():
    hand = [46, 11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42, 11]
    policy = JokerPolicy(natural_map=((46, 11),), meld_count=5)
    _assert_all_candidates(hand, policy, winning_index=0)
    assert {shape.groups[shape.winning_group].kind for shape in iter_winning_shapes(
        hand, policy=policy, winning_index=0)} == {"sequence", "triplet"}


def test_external_kong_does_not_consume_concealed_logical_cap():
    hand = [11, 12, 15, 15, 16, 16, 17, 17, 19, 19, 55]
    policy = JokerPolicy(joker_tiles=(55,), physical_tiles=TILES + (55,), closed_cap=4)
    _assert_all_candidates(hand, policy, ("G13",))


def test_empty_target_domain_still_allows_natural_hands_but_not_wildcards():
    policy = JokerPolicy(joker_tiles=(46,), joker_targets=(), meld_count=1)
    _assert_all_candidates([11, 12, 13, 41, 41], policy)
    _assert_all_candidates([11, 12, 46, 41, 41], policy)


@pytest.mark.parametrize("cap", [None, 1, 4])
@pytest.mark.parametrize("pairs", [None, (11, 41), ()])
def test_pure_joker_pair_all_targets_and_empty_natural_pool(cap, pairs):
    policy = JokerPolicy(joker_tiles=(55, 56), physical_tiles=TILES + (55, 56),
                         meld_count=0, closed_cap=cap, pair_tiles=pairs)
    _assert_all_candidates([55, 56], policy)


@pytest.mark.parametrize("cap", [None, 4])
@pytest.mark.parametrize("meld_count", [1, 4, 5])
@pytest.mark.parametrize("ghosts", [3, 4])
def test_three_four_jokers_all_candidates_in_small_target_domain(cap, meld_count, ghosts):
    prefix = [21, 22, 23, 31, 32, 33, 41, 41, 41, 14, 15, 16][:3 * (meld_count - 1)]
    hand = prefix + [11] * (5 - ghosts) + [55, 56, 57, 58][:ghosts]
    policy = JokerPolicy(joker_tiles=(55, 56, 57, 58), physical_tiles=TILES + (55, 56, 57, 58),
                         meld_count=meld_count, closed_cap=cap, joker_targets=(11, 12, 13, 41))
    _assert_all_candidates(hand, policy)


def test_helpers_respect_restricted_targets_and_physical_limit():
    policy = JokerPolicy(joker_tiles=(55,), physical_tiles=TILES + (55,),
                         joker_targets=(13,), meld_count=1)
    assert can_form_melds([11, 12, 55], policy=policy)
    assert not can_form_melds([21, 22, 55], policy=policy)
    assert can_form_melds([11, 12, 55], policy=replace(policy, meld_count=4), meld_count=1)
    assert can_form_pairs([13, 55], 1, policy)
    assert not can_form_pairs([11, 55], 1, policy)
    assert structural_waits([11, 12, 41, 41], policy=policy) == {13, 55}


def test_all_physical_joker_orders_preserve_natural_and_soft_variants():
    hand = [12, 13, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 11, 14]
    policy = JokerPolicy(joker_tiles=(11, 14), joker_targets=(11, 14))
    _assert_all_candidates(hand, policy, winning_index=12)
    shapes = list(iter_winning_shapes(hand, policy=policy, winning_index=12))
    natural_targets = [shape for shape in shapes if Counter(shape.assignment[12:]) == Counter((11, 14))]
    assert {shape.assignment[12:] for shape in natural_targets} == {(11, 14), (14, 11)}
    assert {shape.is_natural for shape in natural_targets} == {True, False}
    identical = hand[:-2] + [46, 46]
    policy = replace(policy, joker_tiles=(46,))
    _assert_all_candidates(identical, policy, winning_index=13)
    assert {shape.winning_logical_tile for shape in iter_winning_shapes(
        identical, policy=policy, winning_index=13)} == {11, 14}


def test_public_validation_and_sparse_pair_domain_branches():
    assert not can_win([11, 11])
    assert not structural_waits([11, 11])
    assert not list(iter_winning_shapes([11, 11]))
    assert not can_form_melds(None)
    assert not can_win([11, 11], [123])
    alias_over_cap = [46] * 4 + [41, 11, 12, 13, 21, 22, 23, 31, 32, 33]
    assert not can_win(alias_over_cap, policy=JokerPolicy(natural_map=((46, 41),), closed_cap=4))
    seven_faces = (11, 14, 17, 21, 24, 27, 31)
    pair_policy = JokerPolicy(logical_tiles=seven_faces, shapes=("seven_pairs",))
    _assert_all_candidates([t for t in seven_faces for _ in range(2)], pair_policy)
    incomplete_orphan_domain = tuple(t for t in TILES if t != 11)
    policy = JokerPolicy(logical_tiles=incomplete_orphan_domain, shapes=("thirteen_orphans",))
    assert not can_win([12, 13, 14, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42], policy=policy)


def test_external_joker_target_is_validated_and_cannot_be_called_natural():
    policy = JokerPolicy(joker_tiles=(55,), physical_tiles=TILES + (55,),
                         joker_targets=(13,), meld_count=1)
    legal = ExternalMeld("sequence", (11, 12, 55), (11, 12, 13))
    forbidden = ExternalMeld("sequence", (12, 13, 55), (12, 13, 14))
    assert not can_win([41, 41], (forbidden,), policy)
    assert can_win([41, 41], (legal,), policy)
    shape = next(iter_winning_shapes([41, 41], (legal,), policy))
    assert not shape.is_natural
    assert len(shape.external_joker_uses) == 1
    assert shape.external_joker_uses[0].index == -1
    assert (shape.external_joker_uses[0].physical, shape.external_joker_uses[0].logical) == (55, 13)
    assert shape.joker_uses == ()
    # A fixed ordinary alias is still natural; physical!=logical alone is not
    # sufficient to mark an external group as containing substituted jokers.
    alias_policy = JokerPolicy(natural_map=((46, 11),), meld_count=1)
    alias = ExternalMeld("sequence", (46, 12, 13), (11, 12, 13))
    assert next(iter_winning_shapes([41, 41], (alias,), alias_policy)).is_natural
    natural_policy = JokerPolicy(joker_tiles=(11,), joker_targets=(11,), meld_count=1)
    natural = next(iter_winning_shapes([41, 41], ("s12",), natural_policy))
    assert natural.is_natural and not natural.external_joker_uses[0].substituted


@pytest.mark.parametrize("winning_tile", [99, 13.0, False])
def test_explicit_unknown_winning_index_does_not_bypass_tile_validation(winning_tile):
    policy = JokerPolicy(meld_count=1)
    assert not list(iter_winning_shapes([11, 12, 13, 41, 41], policy=policy,
                                       winning_tile=winning_tile, winning_index=-1))


def test_fast_uncapped_path_matches_equivalent_exact_resource_policies():
    rng = random.Random(2026100209)
    natural = tuple(t for t in TILES if t != 46)

    def hand_of(melds, ghosts, pair, winning):
        size = 3 * melds + 2 * pair
        while True:
            if winning:
                hand = []
                for _ in range(melds):
                    if rng.randrange(2):
                        hand.extend([rng.choice(natural)] * 3)
                    else:
                        first = rng.choice((11, 12, 13, 14, 15, 16, 17,
                                            21, 22, 23, 24, 25, 26, 27,
                                            31, 32, 33, 34, 35, 36, 37))
                        hand.extend((first, first + 1, first + 2))
                if pair:
                    hand.extend([rng.choice(natural)] * 2)
            else:
                hand = rng.sample([t for t in natural for _ in range(4)], size)
            for index in rng.sample(range(size), ghosts):
                hand[index] = 46
            if max(Counter(hand).values(), default=0) <= 4:
                return hand

    for melds in range(6):
        fast = JokerPolicy(joker_tiles=(46,), meld_count=melds)
        slow = replace(fast, joker_targets=TILES)
        slow_pair = replace(fast, pair_tiles=TILES)
        for pair in (False, True):
            for ghosts in range(min(4, 3 * melds + 2 * pair) + 1):
                for winning in (False, True):
                    hand = hand_of(melds, ghosts, pair, winning)
                    query = can_win if pair else can_form_melds
                    expected = query(hand, policy=slow)
                    assert query(hand, policy=fast) == expected, (hand, fast, pair)
                    assert query(hand, policy=slow_pair) == expected, (hand, fast, pair)
                    REVIEW_COUNTS["fast_slow_public_comparisons"] += 2
                    if pair and winning and ghosts in (0, 4):
                        assert structural_waits(hand[:-1], policy=fast) == structural_waits(hand[:-1], policy=slow)
                        REVIEW_COUNTS["fast_slow_public_comparisons"] += 1
    alias = JokerPolicy(joker_tiles=(13,), natural_map=((46, 13),), meld_count=5)
    hand = [46] * 4 + [13] * 3 + [21, 22, 23, 31, 32, 33, 41, 41, 41, 42]
    assert can_win(hand, policy=alias)
    assert can_win(hand, policy=replace(alias, joker_targets=TILES))
    REVIEW_COUNTS["fast_slow_public_comparisons"] += 1


def test_seven_eight_pairs_fast_path_matches_exact_dp_and_parity_reference():
    rng = random.Random(2026100211)
    natural = tuple(t for t in TILES if t != 46)
    for singleton in (False, True):
        size = 17 if singleton else 14
        pair_count = 8 if singleton else 7
        policy = JokerPolicy(joker_tiles=(46,), meld_count=5 if singleton else 4,
                             shapes=("eight_pairs" if singleton else "seven_pairs",))
        exact = replace(policy, joker_targets=TILES)
        for ghosts in range(5):
            for case in range(20):
                if case % 2:
                    hand = [tile for tile in rng.sample(natural, pair_count) for _ in range(2)]
                    if singleton:
                        hand.append(rng.choice(natural))
                    for index in rng.sample(range(size), ghosts):
                        hand[index] = 46
                else:
                    hand = rng.sample([tile for tile in natural for _ in range(4)], size - ghosts) + [46] * ghosts
                odd = sum(n % 2 for t, n in Counter(hand).items() if t != 46)
                needed = abs(odd - int(singleton))
                expected = needed <= ghosts and (ghosts - needed) % 2 == 0
                assert can_win(hand, policy=policy) == expected
                assert can_win(hand, policy=exact) == expected
                REVIEW_COUNTS["pairs_fast_slow_comparisons"] += 1
    for pair_count in range(9):
        policy = JokerPolicy(joker_tiles=(46,))
        exact = replace(policy, joker_targets=TILES)
        for ghosts in range(min(4, pair_count * 2) + 1):
            hand = rng.sample([t for t in natural for _ in range(4)], pair_count * 2 - ghosts) + [46] * ghosts
            assert can_form_pairs(hand, pair_count, policy) == can_form_pairs(hand, pair_count, exact)
            REVIEW_COUNTS["pairs_fast_slow_comparisons"] += 1


def test_eight_pairs_singleton_can_be_fixed_white_alias_or_joker_natural_identity():
    pairs = [tile for tile in (12, 14, 17, 21, 24, 27, 31, 34) for _ in range(2)]
    policy = JokerPolicy(joker_tiles=(11,), natural_map=((46, 11),), meld_count=5,
                         shapes=("eight_pairs",))
    for singleton in (46, 11):
        hand = pairs + [singleton]
        assert can_win(hand, policy=policy)
        assert can_win(hand, policy=replace(policy, joker_targets=TILES))
        _assert_all_candidates(hand, policy)
        assert any(s.is_natural for s in iter_winning_shapes(hand, policy=policy))
        REVIEW_COUNTS["pairs_fast_slow_comparisons"] += 1
    mapped_ghost = JokerPolicy(joker_tiles=(55,), natural_map=((55, 11),),
                               physical_tiles=TILES + (55,), meld_count=5,
                               shapes=("eight_pairs",))
    _assert_all_candidates(pairs + [55], mapped_ghost)
    assert any(s.is_natural and s.winning_logical_tile == 11 for s in iter_winning_shapes(
        pairs + [55], policy=mapped_ghost, winning_index=16))


@pytest.mark.parametrize("winning_index", [-1, 0, 10, 13])
@pytest.mark.parametrize("cap", [None, 4])
def test_logical_projection_four_distinct_jokers_and_unknown_winning_component(winning_index, cap):
    hand = [11, 11, 14, 14, 21, 21, 24, 24, 31, 31, 55, 56, 57, 58]
    policy = JokerPolicy(joker_tiles=(55, 56, 57, 58),
                         physical_tiles=TILES + (55, 56, 57, 58),
                         closed_cap=cap, shapes=("standard", "seven_pairs", "thirteen_orphans"))
    assert _assert_projection_matches(hand, policy, winning_index=winning_index) > 500


@pytest.mark.parametrize("winning_index", [-1, 0, 4])
def test_logical_projection_preserves_external_joker_and_fixed_alias(winning_index):
    policy = JokerPolicy(joker_tiles=(55, 56), physical_tiles=TILES + (55, 56),
                         natural_map=((46, 11),), meld_count=2)
    melds = (ExternalMeld("sequence", (46, 12, 55), (11, 12, 13)),)
    hand = [21, 22, 56, 41, 41]
    assert _assert_projection_matches(hand, policy, melds, winning_index) == 1
    shape = next(iter_winning_shapes(hand, melds, policy, winning_index=winning_index,
                                     equivalence="logical_structure"))
    assert not shape.is_natural
    assert shape.external_joker_uses[0].physical == 55
    assert shape.external_joker_uses[0].logical == 13
    assert shape.external_joker_uses[0].index == -1


def test_logical_projection_explicit_mode_validation_and_winning_tile_selection():
    policy = JokerPolicy(joker_tiles=(46,), meld_count=1)
    hand = [11, 12, 46, 41, 41]
    for mode in (None, "", "logical", "all", 1):
        with pytest.raises(ValueError, match="equivalence"):
            list(iter_winning_shapes(hand, policy=policy, equivalence=mode))
    for winning in (46, 41):
        full = {_logical_projection(s) for s in iter_winning_shapes(hand, policy=policy, winning_tile=winning)}
        projected = {_logical_projection(s) for s in iter_winning_shapes(
            hand, policy=policy, winning_tile=winning, equivalence="logical_structure")}
        assert full == projected
    # The default must still retain physical/winning-group distinctions.
    original = list(iter_winning_shapes(hand, policy=policy, winning_tile=46))
    assert original == list(iter_winning_shapes(hand, policy=policy, winning_tile=46, equivalence="full"))
    for invalid in (99, 46.0, False):
        assert not list(iter_winning_shapes(hand, policy=policy, winning_tile=invalid,
                                           winning_index=-1, equivalence="logical_structure"))
