from dataclasses import dataclass
from itertools import combinations

from . import tiles as t
from .solver import Meld, meld_options


@dataclass(frozen=True)
class Choice:
    kind: str
    take: tuple
    faces: tuple
    meld_index: int = -1

    @property
    def key(self):
        return self.kind + ':' + str(self.meld_index) + ':' + ','.join(map(str, self.take))

    def public(self):
        return dict(id=self.key, kind=self.kind, take=list(self.take), faces=[list(v) for v in self.faces], meld_index=self.meld_index)


def _subsets(hand, count):
    return sorted(set(tuple(sorted(values)) for values in combinations(hand, count)))


def kong_faces(physical, declared='gang'):
    if not t.valid_physical(physical):
        return ()
    domain = set(t.FACES)
    for value in physical:
        domain.intersection_update(v for v in t.DOMAIN[value] if t.allows(value, v, 'kong', external=True, declared=declared))
        if not domain:
            return ()
    return tuple((v,) * len(physical) for v in sorted(domain))


def call_choices(hand, called, *, next_player=False, can_kong=True):
    if not t.valid_physical((*hand, called)):
        return ()
    result = []
    for take in _subsets(hand, 2):
        options = meld_options(Meld((*take, called)))
        triplets = tuple(g.logical for g in options if g.kind == 'triplet')
        sequences = tuple(g.logical for g in options if g.kind == 'sequence')
        if triplets:
            result.append(Choice('peng', take, triplets))
        if next_player and sequences:
            result.append(Choice('chi', take, sequences))
    if can_kong and called not in (t.CHOW, t.PUNG):
        for take in _subsets(hand, 3):
            if t.CHOW in take or t.PUNG in take:
                continue
            faces = kong_faces((*take, called))
            if faces:
                result.append(Choice('minggang', take, faces))
    return tuple(result)


def turn_choices(hand, melds=(), *, can_fourth=True, can_extend=True):
    melds = tuple(Meld.read(value) for value in melds)
    if not t.valid_physical((*hand, *(v for m in melds for v in m.physical))):
        return ()
    result = []
    if can_fourth and len(melds) < 4:
        for take in _subsets(hand, 4):
            if t.CHOW in take or t.PUNG in take:
                continue
            faces = kong_faces(take, 'angang')
            if faces:
                result.append(Choice('angang', take, faces))
    for index, meld in enumerate(melds):
        if len(meld.physical) == 3 and not can_fourth or len(meld.physical) >= 4 and not can_extend:
            continue
        for value in sorted(set(hand)):
            if value in (t.CHOW, t.PUNG):
                continue
            faces = kong_faces((*meld.physical, value), meld.declared)
            if faces:
                result.append(Choice('jiagang', (value,), faces, index))
    return tuple(result)


def resolve_response(discarder, responses):
    priority = {'hu': 0, 'peng': 1, 'minggang': 1, 'chi': 2}
    legal = [(seat, action) for seat, action in responses.items() if action in priority and seat != discarder]
    return min(legal, key=lambda item: (priority[item[1]], (item[0] - discarder) % 4), default=None)
