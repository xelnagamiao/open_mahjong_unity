from dataclasses import dataclass
from functools import lru_cache
from itertools import combinations, permutations, product

from ..jokers.models import INDEX, JokerPolicy
from ..jokers.solver import _pair_templates, _standard_templates, _standard, _pairs, _orphan_templates
from . import tiles as t


@dataclass(frozen=True)
class Meld:
    physical: tuple
    concealed: bool = False
    declared: str = 'peng'
    source: int = -1

    @classmethod
    def read(cls, value):
        return value if isinstance(value, cls) else cls(tuple(value['physical']), value.get('concealed', False), value.get('declared', 'peng'), value.get('source', -1))


@dataclass(frozen=True)
class Group:
    kind: str
    logical: tuple
    physical: tuple
    indices: tuple = ()
    external: bool = False
    concealed: bool = True

    @property
    def code(self):
        letter = {'sequence': 'S', 'triplet': 'K', 'pair': 'q', 'seven_pair': 'q', 'kong': 'G'}[self.kind]
        if self.external and not self.concealed:
            letter = letter.lower()
        return letter + str(self.logical[1] if self.kind == 'sequence' else self.logical[0])


@dataclass(frozen=True)
class Shape:
    kind: str
    groups: tuple
    winning_group: int
    winning_face: int
    assignment: tuple
    physical: tuple


def _matches(hand, mask, logical, kind, *, anchor=-1, external=False, declared=''):
    used = [i for i in range(len(hand)) if mask >> i & 1]
    candidates = [tuple(i for i in used if t.allows(hand[i], face, kind, slot,
                        start=logical[0], external=external, declared=declared))
                  for slot, face in enumerate(logical)]
    if any(not values for values in candidates):
        return
    order = sorted(range(len(logical)), key=lambda slot: len(candidates[slot]))
    assigned = [-1] * len(logical)
    seen = set()

    def fill(depth, remaining):
        if depth == len(order):
            if anchor >= 0 and anchor not in assigned:
                return
            key = tuple(sorted(assigned)) if len(set(logical)) == 1 else tuple(assigned)
            if key not in seen:
                seen.add(key)
                yield tuple(assigned)
            return
        slot = order[depth]
        previous_same = next((j for j in order[:depth] if logical[j] == logical[slot]), None)
        for i in candidates[slot]:
            if not remaining >> i & 1 or previous_same is not None and i <= assigned[previous_same]:
                continue
            assigned[slot] = i
            yield from fill(depth + 1, remaining ^ (1 << i))

    yield from fill(0, mask)


@lru_cache(maxsize=8192)
def meld_options(meld):
    physical = meld.physical
    if len(physical) < 3 or not t.valid_physical(physical):
        return ()
    kinds = ('sequence', 'triplet') if len(physical) == 3 else ('kong',)
    result = []
    for kind in kinds:
        for value in t.FACES:
            if kind == 'sequence' and (value >= 40 or value % 10 > 7):
                continue
            logical = (value, value + 1, value + 2) if kind == 'sequence' else (value,) * len(physical)
            if next(_matches(physical, (1 << len(physical)) - 1, logical, kind, external=True, declared=meld.declared), None) is not None:
                result.append(Group(kind, logical, physical, external=True, concealed=meld.concealed))
    return tuple(result)


@lru_cache(maxsize=64)
def _group_options(hand, seven):
    by_anchor = [[] for _ in hand]
    full_mask = (1 << len(hand)) - 1
    for kind in (('seven_pair',) if seven else ('pair', 'triplet', 'sequence')):
        for value in t.FACES:
            if kind == 'sequence' and (value >= 40 or value % 10 > 7):
                continue
            logical = (value, value + 1, value + 2) if kind == 'sequence' else (value,) * (2 if kind in ('pair', 'seven_pair') else 3)
            seen = set()
            for indices in _matches(hand, full_mask, logical, kind):
                bits = sum(1 << i for i in indices)
                winner = logical[indices.index(len(hand) - 1)] if len(hand) - 1 in indices else 0
                key = bits, winner
                if key in seen:
                    continue
                seen.add(key)
                group = Group(kind, logical, tuple(hand[i] for i in indices), indices)
                for i in indices:
                    by_anchor[i].append((bits, group))
    return tuple(tuple(values) for values in by_anchor)


def _closed_shapes(hand, mask, groups_left, pair_left, *, seven=False, distinct_pairs=False):
    if mask == (1 << len(hand)) - 1:
        return _template_groups(hand, seven, distinct_pairs)
    by_anchor = _group_options(hand, seven)
    dead = set()

    @lru_cache(maxsize=2048)
    def options(remaining, pair):
        ids = [i for i in range(len(hand)) if remaining >> i & 1]
        anchor = min(ids, key=lambda i: (t.joker(hand[i]), len(t.DOMAIN[hand[i]]), hand[i], i))
        return tuple((bits, group) for bits, group in by_anchor[anchor]
                     if bits & remaining == bits and (pair or group.kind != 'pair'))

    def visit(remaining, groups, pair, pair_faces):
        if not remaining:
            if groups == 0 and not pair:
                yield ()
            return
        if remaining.bit_count() != groups * (2 if seven else 3) + 2 * bool(pair):
            return
        key = remaining, groups, pair, pair_faces
        if key in dead:
            return
        found = False
        for bits, group in options(remaining, pair):
            value, kind = group.logical[0], group.kind
            if kind != 'pair' and groups == 0 or seven and distinct_pairs and value in pair_faces:
                continue
            new_faces = tuple(sorted((*pair_faces, value))) if seven and distinct_pairs else pair_faces
            for tail in visit(remaining ^ bits, groups - int(kind != 'pair'), pair and kind != 'pair', new_faces):
                found = True
                yield (group,) + tail
        if not found:
            dead.add(key)

    return visit(mask, groups_left, pair_left, ())


def _template_groups(hand, seven, distinct_pairs):
    counts = [0] * 34
    for value in hand:
        if not t.joker(value):
            counts[INDEX[t.face(value)]] += 1
    jokers = sum(t.joker(v) for v in hand)
    policy = JokerPolicy(closed_cap=2 if distinct_pairs else None)
    templates = (_pair_templates if seven else _standard_templates)(bytes(counts), jokers, policy)
    allowed_cache = {}

    def candidates(kind, logical, position):
        key = kind, logical, position
        if key not in allowed_cache:
            allowed_cache[key] = tuple(i for i, value in enumerate(hand)
                                       if t.allows(value, logical[position], kind, position, start=logical[0]))
        return allowed_cache[key]

    for template in templates:
        if seven:
            template = tuple(('seven_pair', logical) for _, logical in template)
        slots = [(gi, kind, logical, pos) for gi, (kind, logical) in enumerate(template) for pos in range(len(logical))]
        choices = [candidates(kind, logical, pos) for _, kind, logical, pos in slots]
        if any(not values for values in choices):
            continue
        order = sorted(range(len(slots)), key=lambda i: len(choices[i]))
        seen_winners = set()
        for winning_slot, (gi, kind, logical, pos) in enumerate(slots):
            key = kind, logical, logical[pos]
            if len(hand) - 1 not in choices[winning_slot] or key in seen_winners:
                continue
            seen_winners.add(key)
            owners = [-1] * len(hand)
            owners[-1] = winning_slot

            def assign(slot, visited):
                for index in choices[slot]:
                    if index == len(hand) - 1 or visited[index]:
                        continue
                    visited[index] = True
                    if owners[index] < 0 or assign(owners[index], visited):
                        owners[index] = slot
                        return True
                return False

            if not all(slot == winning_slot or assign(slot, [False] * len(hand)) for slot in order):
                continue
            indices = [[-1] * len(logical) for _, logical in template]
            for index, slot in enumerate(owners):
                group_index, _, _, position = slots[slot]
                indices[group_index][position] = index
            yield tuple(Group(kind, logical, tuple(hand[i] for i in ids), tuple(ids))
                        for (kind, logical), ids in zip(template, indices))


def _special_assignments(hand, logical, kind):
    candidates = [tuple(i for i, physical in enumerate(hand) if t.allows(physical, value, kind)) for value in logical]
    order = sorted(range(len(logical)), key=lambda i: len(candidates[i]))
    assignment = [0] * len(hand)
    seen = set()

    def visit(depth, remaining):
        if depth == len(order):
            key = tuple(assignment)
            if key not in seen:
                seen.add(key)
                yield key
            return
        slot = order[depth]
        duplicates = set()
        for i in candidates[slot]:
            identity = (hand[i], i == len(hand) - 1)
            if not remaining >> i & 1 or identity in duplicates:
                continue
            duplicates.add(identity)
            assignment[i] = logical[slot]
            yield from visit(depth + 1, remaining ^ (1 << i))

    yield from visit(0, (1 << len(hand)) - 1)


def iter_shapes(hand, melds=(), variant='guobiao'):
    hand, melds = tuple(hand), tuple(Meld.read(m) for m in melds)
    if len(hand) + 3 * len(melds) != 14 or len(melds) > 4 or not t.valid_physical((*hand, *(v for m in melds for v in m.physical))):
        return
    external_options = [meld_options(m) for m in melds]
    if any(not options for options in external_options):
        return
    seen = set()

    def shape(kind, closed, external=(), assignment=None):
        groups = tuple(external) + tuple(closed)
        values = [0] * len(hand) if assignment is None else list(assignment)
        winner = -1
        for gi, group in enumerate(groups):
            for i, logical in zip(group.indices, group.logical):
                values[i] = logical
                if i == len(hand) - 1:
                    winner = gi
        key = (kind, tuple(sorted((g.kind, g.logical, g.external, g.concealed, gi == winner) for gi, g in enumerate(groups))), tuple(sorted(values)), values[-1])
        if key not in seen:
            seen.add(key)
            return Shape(kind, groups, winner, values[-1], tuple(values), hand)

    closed_options = _closed_shapes(hand, (1 << len(hand)) - 1, 4 - len(melds), True)
    for closed in closed_options:
        for external in product(*external_options):
            result = shape('standard', closed, external)
            if result:
                yield result
    if not melds:
        for closed in _closed_shapes(hand, (1 << len(hand)) - 1, 7, False, seven=True, distinct_pairs=variant == 'riichi'):
            result = shape('seven_pairs', closed)
            if result:
                yield result
        for pair in t.ORPHANS:
            logical = t.ORPHANS + (pair,)
            for assignment in _special_assignments(hand, logical, 'orphan'):
                result = shape('thirteen_orphans', (), assignment=assignment)
                if result:
                    yield result
    if variant != 'guobiao':
        return
    patterns = tuple(tuple(s * 10 + r for s, start in zip((1, 2, 3), order) for r in range(start, 10, 3)) for order in permutations((1, 2, 3)))
    if not melds:
        natural = [t.face(v) for v in hand if not t.joker(v)]
        if len(natural) == len(set(natural)):
            for pattern in patterns:
                domain = sorted((*pattern, *t.HONORS))
                if not set(natural) <= set(domain):
                    continue
                for extra in combinations([v for v in domain if v not in natural], 14 - len(natural)):
                    for assignment in _special_assignments(hand, tuple(sorted(natural + list(extra))), 'unconnected'):
                        result = shape('unconnected', (), assignment=assignment)
                        if result:
                            yield result
    if len(melds) <= 1:
        for pattern in patterns:
            for indices in _matches(hand, (1 << len(hand)) - 1, pattern, 'knitted'):
                remaining = (1 << len(hand)) - 1
                for i in indices:
                    remaining ^= 1 << i
                knitted = Group('knitted', pattern, tuple(hand[i] for i in indices), indices)
                for closed in _closed_shapes(hand, remaining, 1 - len(melds), True):
                    for external in product(*external_options):
                        result = shape('knitted', (knitted,) + closed, external)
                        if result:
                            yield result


@lru_cache(maxsize=4096)
def structural_win(hand, melds=(), variant='guobiao'):
    if variant == 'riichi':
        if not t.valid_physical((*hand, *(v for m in melds for v in m.physical))):
            return False
        counts = [0] * 34
        for value in hand:
            if not t.joker(value):
                counts[INDEX[t.face(value)]] += 1
        counts = bytes(counts)
        jokers = sum(t.joker(v) for v in hand)
        policy = JokerPolicy(closed_cap=None)
        if not _standard(counts, jokers, policy) and (melds or not (
                _pairs(counts, jokers, policy) or next(_orphan_templates(counts, jokers, policy), None))):
            return False
    return next(iter_shapes(hand, melds, variant), None) is not None


def waits(hand, melds=(), variant='guobiao', candidates=t.PHYSICAL):
    hand, melds = tuple(hand), tuple(Meld.read(m) for m in melds)
    return [value for value in candidates if structural_win(hand + (value,), melds, variant)]
