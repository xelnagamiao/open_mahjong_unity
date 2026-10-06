"""Grouped exact-resource DP, with lazy, untruncated winning witnesses.

This implementation is independently authored for the project's rule policies.
No fixed external 14-tile table and no global 34**j replacement search is used.
The fast path caches small resource bitsets, not full scoring decompositions.
"""

from collections import Counter, defaultdict
from dataclasses import replace
from functools import lru_cache
from itertools import permutations, product

from .models import (INDEX, ORPHANS, TILES, ExternalMeld, JokerPolicy, JokerUse,
                     ResolvedGroup, WinningShape)

DEFAULT_POLICY = JokerPolicy()
GROUPS = ((0, 9, True), (9, 18, True), (18, 27, True), (27, 34, False))


def _decode_meld(meld, policy):
    if isinstance(meld, str):
        if len(meld) < 3 or meld[0] not in "skgG":
            raise ValueError("invalid external meld")
        tile = int(meld[1:])
        if meld[0] == "s":
            if tile not in INDEX or tile >= 40 or not 2 <= tile % 10 <= 8:
                raise ValueError("invalid chow center")
            meld = ExternalMeld("sequence", (tile - 1, tile, tile + 1))
        else:
            meld = ExternalMeld("triplet" if meld[0] == "k" else "kong",
                                (tile,) * (3 if meld[0] == "k" else 4),
                                concealed=meld[0] == "G")
    if not isinstance(meld, ExternalMeld):
        raise ValueError("external meld must be a code or ExternalMeld")
    physical = meld.physical
    logical = meld.logical or tuple(policy.natural(t) for t in physical)
    expected = 4 if meld.kind == "kong" else 3
    if (meld.kind not in ("sequence", "triplet", "kong")
            or len(physical) != expected or len(logical) != expected
            or any(type(t) is not int or t not in policy.physical_tiles for t in physical)
            or any(type(t) is not int or t not in policy.logical_tiles for t in logical)):
        raise ValueError("invalid external meld tiles")
    if meld.kind == "sequence":
        ordered = sorted(logical)
        if ordered[0] >= 40 or ordered[0] // 10 != ordered[-1] // 10 or ordered != list(range(ordered[0], ordered[0] + 3)):
            raise ValueError("invalid external sequence")
    elif len(set(logical)) != 1:
        raise ValueError("invalid external pung/kong")
    targets = policy.logical_tiles if policy.joker_targets is None else policy.joker_targets
    for real, represented in zip(physical, logical):
        if real in policy.joker_tiles:
            if represented not in targets:
                raise ValueError("fixed joker in external meld has forbidden target")
        elif policy.natural(real) != represented:
            raise ValueError("fixed external meld changes a natural tile")
    return ResolvedGroup(meld.kind, logical, physical, (-1,) * expected, True, meld.concealed)


def _prepare(hand, melds, policy):
    try:
        hand = tuple(hand)
        external = tuple(_decode_meld(m, policy) for m in melds)
        if len(external) > policy.meld_count or len(hand) > 3 * policy.meld_count + 2:
            return None
        if any(type(t) is not int or t not in policy.physical_tiles for t in hand):
            return None
        physical = Counter(hand)
        physical.update(t for m in external for t in m.physical)
        if any(n > policy.supply(t) for t, n in physical.items()):
            return None
        natural = [0] * 34
        jokers = []
        for i, tile in enumerate(hand):
            if tile in policy.joker_tiles:
                jokers.append(i)
            else:
                logical = policy.natural(tile)
                if logical not in policy.logical_tiles:
                    return None
                natural[INDEX[logical]] += 1
        if policy.closed_cap is not None and any(n > policy.closed_cap for n in natural):
            return None
        # Counts fit in one byte (at most 17 concealed tiles). Keep global
        # cache capacities while avoiding 34-slot tuple keys and their slices.
        return hand, external, bytes(natural), tuple(jokers), physical
    except (ValueError, TypeError, KeyError, AttributeError):
        return None


def _group_engine(counts, sequences, cap, budget, allowed, targets, pairs):
    """Finite rank DP. Bits denote EXACT wildcard costs, not minimum costs."""
    mask = (1 << (budget + 1)) - 1

    def transitions(rank, prev1, prev2, pair_left):
        minimum = counts[rank]
        pending = prev1 + prev2
        maximum = min(minimum + budget, cap) if cap else minimum + budget
        if not (allowed >> rank) & 1:
            maximum = 0
        for pair in range(1 + int(bool(pair_left and (pairs >> rank) & 1))):
            for pung in range(max(0, (maximum - pending - 2 * pair) // 3 + 1)):
                base = pending + 2 * pair + 3 * pung
                max_seq = maximum - base if sequences and rank < len(counts) - 2 else 0
                for seq in range(max_seq + 1):
                    cost = base + seq - minimum
                    if cost < 0 or cost > budget or (cost and not (targets >> rank) & 1):
                        continue
                    yield pair, pung, seq, cost

    @lru_cache(maxsize=None)
    def resources(rank, prev1, prev2, pair_left):
        if rank == len(counts):
            return int(prev1 == 0 and prev2 == 0 and pair_left == 0)
        result = 0
        for pair, pung, seq, cost in transitions(rank, prev1, prev2, pair_left):
            result |= resources(rank + 1, seq, prev1, pair_left - pair) << cost
        return result & mask

    return resources, transitions


@lru_cache(maxsize=65536)
def _group_profile(counts, sequences, cap, budget, allowed, targets, pairs):
    resources, _ = _group_engine(counts, sequences, cap, budget, allowed, targets, pairs)
    return resources(0, 0, 0, 0), resources(0, 0, 0, 1)


@lru_cache(maxsize=128)
def _group_domains(policy):
    """Small shared rank masks, rather than three boolean tuples per key."""
    logical = set(policy.logical_tiles)
    targets = logical if policy.joker_targets is None else set(policy.joker_targets)
    pairs = logical if policy.pair_tiles is None else set(policy.pair_tiles)
    return tuple(tuple(sum(1 << rank for rank, tile in enumerate(TILES[start:end])
                           if tile in domain) for domain in (logical, targets, pairs))
                 for start, end, _ in GROUPS)


def _group_inputs(counts, budget, policy):
    domains = _group_domains(policy)
    for (start, end, sequences), masks in zip(GROUPS, domains):
        yield (counts[start:end], sequences, policy.closed_cap or 0, budget,
               *masks)


@lru_cache(maxsize=32768)
def _minimum_group_jokers(counts, pair, sequences):
    """Minimum cost when every logical identity and extra joker meld is legal.

    Removing a group containing the lowest natural tile enumerates every
    structural possibility; identical natural/joker instances can be swapped.
    Without a logical cap or restricted domain, any spare multiple of three
    jokers forms extra melds. Other policies must use exact-cost profiles.
    """
    first = next((i for i, count in enumerate(counts) if count), None)
    if first is None:
        return 2 if pair else 0
    best = 2 + _minimum_group_jokers(counts, False, sequences) if pair else 99
    for size in ((2, 3) if pair else (3,)):
        used = min(counts[first], size)
        remaining = list(counts)
        remaining[first] -= used
        best = min(best, size - used + _minimum_group_jokers(bytes(remaining),
                                                           pair and size != 2, sequences))
    if sequences:
        for start in range(max(0, first - 2), min(first, len(counts) - 3) + 1):
            remaining = list(counts)
            cost = 0
            for rank in range(start, start + 3):
                if remaining[rank]:
                    remaining[rank] -= 1
                else:
                    cost += 1
            best = min(best, cost + _minimum_group_jokers(bytes(remaining), pair, True))
    return best


@lru_cache(maxsize=32768)
def _standard(counts, jokers, policy, pair_required=True):
    if (policy.closed_cap is None and policy.logical_tiles == TILES
            and policy.joker_targets is None and policy.pair_tiles is None):
        needs = [_minimum_group_jokers(counts[a:b], False, sequence) for a, b, sequence in GROUPS]
        total = sum(needs)
        if not pair_required:
            return total <= jokers and (jokers - total) % 3 == 0
        for index, (a, b, sequence) in enumerate(GROUPS):
            need = total - needs[index] + _minimum_group_jokers(counts[a:b], True, sequence)
            if need <= jokers and (jokers - need) % 3 == 0:
                return True
        return False
    states = {(0, 0)}
    for args in _group_inputs(counts, jokers, policy):
        profile = _group_profile(*args)
        following = set()
        for used, eye in states:
            for pair in range(int(pair_required) - eye + 1):
                options = profile[pair]
                for cost in range(jokers - used + 1):
                    if (options >> cost) & 1:
                        following.add((used + cost, eye + pair))
        states = following
        if not states:
            return False
    return (jokers, int(pair_required)) in states


def _pairs_engine(counts, budget, policy, singleton):
    domain = set(policy.logical_tiles)
    targets = domain if policy.joker_targets is None else set(policy.joker_targets)
    mask = (1 << (budget + 1)) - 1

    def choices(i, single_left):
        n = counts[i]
        limit = min(n + budget, policy.closed_cap) if policy.closed_cap else n + budget
        if TILES[i] not in domain:
            limit = 0
        for total in range(n, limit + 1):
            odd = total % 2
            if odd > single_left or (total > n and TILES[i] not in targets):
                continue
            yield total, odd, total - n

    @lru_cache(maxsize=None)
    def resources(i, single_left):
        if i == 34:
            return int(single_left == 0)
        result = 0
        for total, odd, cost in choices(i, single_left):
            result |= resources(i + 1, single_left - odd) << cost
        return result & mask

    return resources, choices


@lru_cache(maxsize=32768)
def _pairs(counts, jokers, policy, singleton=False):
    if policy.closed_cap is None and policy.logical_tiles == TILES and policy.joker_targets is None:
        # Every odd natural count needs one partner, except the optional lone
        # tile. With no existing odd count, that singleton costs one joker.
        need = abs(sum(n % 2 for n in counts) - int(singleton))
        return need <= jokers and (jokers - need) % 2 == 0
    resources, _ = _pairs_engine(counts, jokers, policy, int(singleton))
    return bool((resources(0, int(singleton)) >> jokers) & 1)


def _orphan_templates(counts, jokers, policy):
    if not set(ORPHANS) <= set(policy.logical_tiles):
        return
    targets = policy.logical_tiles if policy.joker_targets is None else policy.joker_targets
    if any(n for i, n in enumerate(counts) if TILES[i] not in ORPHANS):
        return
    for pair in ORPHANS:
        if policy.pair_tiles is not None and pair not in policy.pair_tiles:
            continue
        cost = 0
        valid = True
        for tile in ORPHANS:
            required = 2 if tile == pair else 1
            deficit = required - counts[INDEX[tile]]
            if (deficit < 0 or (deficit and tile not in targets)
                    or (policy.closed_cap is not None and required > policy.closed_cap)):
                valid = False
                break
            cost += deficit
        if valid and cost == jokers:
            yield tuple(("pair" if tile == pair else "singleton", (tile,) * (2 if tile == pair else 1))
                        for tile in ORPHANS)


def can_win(hand, melds=(), policy=None):
    policy = policy or DEFAULT_POLICY
    prepared = _prepare(hand, melds, policy)
    if prepared is None:
        return False
    hand, external, counts, jokers, _ = prepared
    if len(hand) != 3 * (policy.meld_count - len(external)) + 2:
        return False
    g = len(jokers)
    if "standard" in policy.shapes and _standard(counts, g, policy):
        return True
    if not external:
        if "seven_pairs" in policy.shapes and len(hand) == 14 and _pairs(counts, g, policy):
            return True
        if "eight_pairs" in policy.shapes and len(hand) == 17 and _pairs(counts, g, policy, True):
            return True
        if "thirteen_orphans" in policy.shapes and len(hand) == 14:
            return next(_orphan_templates(counts, g, policy), None) is not None
    return False


def can_form_melds(hand, melds=(), policy=None, *, meld_count=None):
    policy = policy or DEFAULT_POLICY
    if meld_count is not None:
        policy = replace(policy, meld_count=meld_count)
    prepared = _prepare(hand, melds, policy)
    if prepared is None:
        return False
    hand, external, counts, jokers, _ = prepared
    return (len(hand) == 3 * (policy.meld_count - len(external))
            and _standard(counts, len(jokers), policy, False))


def can_form_pairs(hand, pair_count, policy=None):
    policy = policy or DEFAULT_POLICY
    if type(pair_count) is not int or not 0 <= pair_count <= 8:
        return False
    prepared = _prepare(hand, (), replace(policy, meld_count=5))
    if prepared is None:
        return False
    real, _, counts, jokers, _ = prepared
    return len(real) == pair_count * 2 and _pairs(counts, len(jokers), policy)


@lru_cache(maxsize=16384)
def _waits(hand, melds, policy):
    prepared = _prepare(hand, melds, policy)
    if prepared is None:
        return frozenset()
    real, external, _, _, physical = prepared
    if len(real) != 3 * (policy.meld_count - len(external)) + 1:
        return frozenset()
    return frozenset(t for t in policy.physical_tiles
                     if physical[t] < policy.supply(t) and can_win(real + (t,), melds, policy))


def structural_waits(hand, melds=(), policy=None):
    policy = policy or DEFAULT_POLICY
    try:
        ordered = sorted(hand)
        # Validate before packing: bytes would otherwise coerce int subclasses
        # and bools, bypassing _prepare's exact physical-identity type check.
        if any(type(tile) is not int for tile in ordered):
            return frozenset()
        return _waits(bytes(ordered), tuple(melds), policy)
    except (TypeError, ValueError):
        return frozenset()


def _group_patterns(args, offset, cost, pair):
    resources, transitions = _group_engine(*args)
    current = []

    def walk(rank, prev1, prev2, eye, remaining):
        if rank == len(args[0]):
            if remaining == 0 and prev1 == prev2 == eye == 0:
                yield tuple(current)
            return
        if not ((resources(rank, prev1, prev2, eye) >> remaining) & 1):
            return
        tile = TILES[offset + rank]
        for p, k, s, used in transitions(rank, prev1, prev2, eye):
            if used > remaining:
                continue
            added = ([("pair", (tile, tile))] * p
                     + [("triplet", (tile,) * 3)] * k
                     + [("sequence", (tile, tile + 1, tile + 2))] * s)
            current.extend(added)
            yield from walk(rank + 1, s, prev1, eye - p, remaining - used)
            if added:
                del current[-len(added):]

    yield from walk(0, 0, 0, pair, cost)


def _standard_templates(counts, jokers, policy):
    args = tuple(_group_inputs(counts, jokers, policy))
    profiles = tuple(_group_profile(*a) for a in args)

    # A later group's patterns are reused for every earlier-group template.
    # Cache only for this enumeration, with at most 4*(jokers+1)*2 keys; do not
    # retain large complete decompositions in the global structural cache.
    @lru_cache(maxsize=None)
    def patterns(group, cost, pair):
        return tuple(_group_patterns(args[group], GROUPS[group][0], cost, pair))

    def walk(group, remaining, eye, result):
        if group == 4:
            if remaining == 0 and eye == 0:
                yield result
            return
        for pair in range(eye + 1):
            for cost in range(remaining + 1):
                if not ((profiles[group][pair] >> cost) & 1):
                    continue
                for pattern in patterns(group, cost, pair):
                    yield from walk(group + 1, remaining - cost, eye - pair, result + pattern)

    yield from walk(0, jokers, 1, ())


def _pair_templates(counts, jokers, policy, singleton=False):
    resources, choices = _pairs_engine(counts, jokers, policy, int(singleton))

    def walk(i, single_left, remaining, result):
        if i == 34:
            if single_left == remaining == 0:
                yield result
            return
        if not ((resources(i, single_left) >> remaining) & 1):
            return
        tile = TILES[i]
        for total, odd, cost in choices(i, single_left):
            if cost <= remaining:
                groups = (("pair", (tile, tile)),) * (total // 2)
                if odd:
                    groups += (("singleton", (tile,)),)
                yield from walk(i + 1, single_left - odd, remaining - cost, result + groups)

    yield from walk(0, int(singleton), jokers, ())


def _distribute(total, capacities):
    if not capacities:
        if total == 0:
            yield ()
        return
    for used in range(max(0, total - sum(capacities[1:])), min(total, capacities[0]) + 1):
        for rest in _distribute(total - used, capacities[1:]):
            yield (used,) + rest


def _resolved_shape(kind, template, real, external, policy, winning_index, assigned):
    logical_assignment = [0] * len(real)
    groups = list(external)
    winning_group = -1
    for g, (group_kind, logical) in enumerate(template):
        indices = tuple(assigned[g, j] for j in range(len(logical)))
        for index, tile in zip(indices, logical):
            logical_assignment[index] = tile
        if winning_index in indices:
            winning_group = len(groups)
        groups.append(ResolvedGroup(group_kind, logical, tuple(real[i] for i in indices), indices))
    joker_uses = tuple(JokerUse(i, t, policy.natural(t), logical_assignment[i])
                      for i, t in enumerate(real) if t in policy.joker_tiles)
    external_jokers = tuple(JokerUse(-1, physical, policy.natural(physical), logical)
                            for group in external
                            for physical, logical in zip(group.physical, group.logical)
                            if physical in policy.joker_tiles)
    return WinningShape(kind, tuple(groups), tuple(logical_assignment), joker_uses,
                        winning_group, winning_index, external_jokers)


def _resolve_logical_structure(kind, template, real, external, policy, winning_index):
    """One witness per logical template and feasible winning logical identity.

    Explicit opt-in: this projection intentionally does not preserve alternative
    winning groups or non-winning wildcard allocations. It is valid only for
    scoring rules proven to depend on the documented logical feature key.
    """
    slots = defaultdict(list)
    for group, (_, logical) in enumerate(template):
        for position, tile in enumerate(logical):
            slots[tile].append((group, position))
    assigned = {}
    wild = []
    for index, physical in enumerate(real):
        if physical in policy.joker_tiles:
            wild.append(index)
        else:
            assigned[slots[policy.natural(physical)].pop(0)] = index
    remaining = [(tile, slot) for tile in sorted(slots) for slot in slots[tile]]
    if winning_index not in wild:
        assigned.update((slot, index) for (_, slot), index in zip(remaining, wild))
        yield _resolved_shape(kind, template, real, external, policy, winning_index, assigned)
        return
    other_wild = [index for index in wild if index != winning_index]
    for tile in sorted(slots):
        if not slots[tile]:
            continue
        win_slot = slots[tile][0]
        variant = assigned | {win_slot: winning_index}
        variant.update((slot, index) for (_, slot), index
                       in zip((item for item in remaining if item[1] != win_slot), other_wild))
        yield _resolved_shape(kind, template, real, external, policy, winning_index, variant)


def _resolve(kind, template, real, external, policy, winning_index):
    """Assign actual wildcard instances and distinguish the winning component.

    Identical non-winning natural tiles within a group are interchangeable;
    all joker target/group allocations and all winning-group placements remain.
    """
    normal = defaultdict(list)
    wild = []
    for i, t in enumerate(real):
        if t in policy.joker_tiles:
            wild.append(i)
        else:
            normal[policy.natural(t)].append(i)
    slots = defaultdict(lambda: defaultdict(list))
    for g, (_, faces) in enumerate(template):
        for j, tile in enumerate(faces):
            slots[tile][g].append((g, j))
    faces = sorted(slots)
    allocations = []
    for tile in faces:
        groups = sorted(slots[tile])
        capacities = tuple(len(slots[tile][g]) for g in groups)
        deficit = sum(capacities) - len(normal[tile])
        if deficit < 0:
            return
        choices = []
        for quotas in _distribute(deficit, capacities):
            ghost_slots = []
            natural_slots = []
            for group, count in zip(groups, quotas):
                ghost_slots.extend(slots[tile][group][:count])
                natural_slots.extend(slots[tile][group][count:])
            choices.append((ghost_slots, natural_slots))
        allocations.append(choices)
    if any(t not in slots for t in normal):
        return
    permutations_seen = set()
    wildcard_orders = []
    for order in permutations(wild):
        signature = tuple((real[i], i == winning_index) for i in order)
        if signature not in permutations_seen:
            permutations_seen.add(signature)
            wildcard_orders.append(order)
    for allocation in product(*allocations):
        ghost_slots = [s for ghosts, _ in allocation for s in ghosts]
        if len(ghost_slots) != len(wild):
            continue
        natural_assignments = {}
        for tile, (_, positions) in zip(faces, allocation):
            natural_assignments.update(zip(positions, normal[tile]))
        natural_variants = [natural_assignments]
        if winning_index >= 0 and winning_index not in wild:
            win_tile = policy.natural(real[winning_index])
            original_slot = next((p for p, i in natural_assignments.items() if i == winning_index), None)
            if original_slot is None:
                continue
            by_group = {}
            for slot, i in natural_assignments.items():
                if policy.natural(real[i]) == win_tile:
                    by_group.setdefault(slot[0], slot)
            natural_variants = []
            for target_slot in by_group.values():
                variant = natural_assignments.copy()
                variant[original_slot], variant[target_slot] = variant[target_slot], variant[original_slot]
                natural_variants.append(variant)
        for order in wildcard_orders:
            joker_assignment = dict(zip(ghost_slots, order))
            for natural_assignment in natural_variants:
                assigned = natural_assignment | joker_assignment
                yield _resolved_shape(kind, template, real, external, policy, winning_index, assigned)


def iter_winning_shapes(hand, melds=(), policy=None, winning_tile=None, *, winning_index=None,
                        equivalence="full"):
    if equivalence not in ("full", "logical_structure"):
        raise ValueError("equivalence must be full or logical_structure")
    policy = policy or DEFAULT_POLICY
    prepared = _prepare(hand, melds, policy)
    if prepared is None:
        return
    real, external, counts, jokers, _ = prepared
    if len(real) != 3 * (policy.meld_count - len(external)) + 2:
        return
    if winning_tile is not None and (type(winning_tile) is not int or winning_tile not in real):
        return
    if winning_index is None:
        winning_index = max((i for i, t in enumerate(real) if t == winning_tile), default=-1)
        if winning_tile is not None and winning_index < 0:
            return
    if type(winning_index) is not int or not -1 <= winning_index < len(real):
        return
    if winning_tile is not None and winning_index >= 0 and real[winning_index] != winning_tile:
        return
    g = len(jokers)
    generators = []
    if "standard" in policy.shapes and _standard(counts, g, policy):
        generators.append(("standard", _standard_templates(counts, g, policy)))
    if not external:
        if "seven_pairs" in policy.shapes and len(real) == 14 and _pairs(counts, g, policy):
            generators.append(("seven_pairs", _pair_templates(counts, g, policy)))
        if "eight_pairs" in policy.shapes and len(real) == 17 and _pairs(counts, g, policy, True):
            generators.append(("eight_pairs", _pair_templates(counts, g, policy, True)))
        if "thirteen_orphans" in policy.shapes and len(real) == 14:
            generators.append(("thirteen_orphans", _orphan_templates(counts, g, policy)))
    for kind, templates in generators:
        for template in templates:
            resolve = _resolve if equivalence == "full" else _resolve_logical_structure
            yield from resolve(kind, template, real, external, policy, winning_index)


def clear_caches():
    for function in (_group_domains, _group_profile, _minimum_group_jokers, _standard, _pairs, _waits):
        function.cache_clear()


def cache_info():
    return {name: func.cache_info()._asdict() for name, func in
            (("groups", _group_profile), ("minimum_groups", _minimum_group_jokers),
             ("standard", _standard), ("pairs", _pairs), ("waits", _waits))}
