from collections import Counter

import pytest

from . import tiles as t
from .solver import Meld, iter_shapes, meld_options, structural_win


def test_catalog_and_physical_supply():
    assert len(t.CATALOG) == len({v['name'] for v in t.CATALOG.values()}) == 40
    pack = t.normal_pack()
    assert len(pack) == 136
    assert Counter(t.face(v) for v in pack) == {v: 4 for v in t.FACES}
    assert sum(t.bonus(v) == 1 for v in pack) == 11
    assert sum(t.bonus(v) == -1 for v in pack) == 6
    assert sum(t.unusual_back(v) for v in pack) == 5
    assert t.valid_physical(pack + list(t.JOKERS[:8]))
    assert not t.valid_physical(pack + list(t.JOKERS[:9]))
    assert all(t.SUPPLY[value] == 1 for value in t.JOKERS)


@pytest.mark.parametrize('tile', t.JOKERS[:30])
def test_regular_joker_domains_are_exact(tile):
    for face in t.FACES:
        assert t.allows(tile, face, 'triplet') == (face in t.CATALOG[tile]['domain'])


@pytest.mark.parametrize('start,position,ryanmen,edge', [
    (11, 0, True, False), (11, 1, False, True), (11, 2, False, True),
    (12, 0, True, False), (12, 1, False, True), (12, 2, True, False),
    (17, 0, False, True), (17, 1, False, True), (17, 2, True, False),
])
def test_structural_jokers_require_correct_slot(start, position, ryanmen, edge):
    assert t.allows(t.RYANMEN, start + position, 'sequence', position, start=start) == ryanmen
    assert t.allows(t.EDGE_CLOSED, start + position, 'sequence', position, start=start) == edge


def test_special_roles_and_no_hidden_call_jokers():
    assert not t.allows(t.EYES, 11, 'seven_pair')
    assert not t.allows(t.EYES, 11, 'orphan')
    assert t.allows(t.EYES, 11, 'pair')
    for joker in (t.RYANMEN, t.EDGE_CLOSED, t.OLD_YOUNG):
        assert not t.allows(joker, 11, 'knitted')
        assert not t.allows(joker, 11, 'unconnected')
    assert not t.allows(t.CHOW, 11, 'sequence', start=11)
    assert t.allows(t.CHOW, 11, 'sequence', start=11, external=True)
    assert not t.allows(t.PUNG, 11, 'kong', external=True)
    assert t.allows(t.PUNG, 11, 'kong', external=True, declared='peng')
    for joker in (t.MANGAN, t.ODD, t.EVEN):
        assert not t.allows(joker, 11, 'triplet', external=True)


def test_meld_may_change_sequence_to_pung_but_call_jokers_preserve_role():
    interpretations = meld_options(Meld((5018, 15, t.EMPEROR), declared='chi'))
    assert {g.kind for g in interpretations} == {'sequence', 'triplet'}
    assert any(g.logical == (15, 15, 15) for g in interpretations)
    assert {g.kind for g in meld_options(Meld((t.CHOW, 15, t.EMPEROR), declared='chi'))} == {'sequence'}
    assert {g.kind for g in meld_options(Meld((t.PUNG, 15, t.EMPEROR), declared='peng'))} == {'triplet'}
    assert not meld_options(Meld((t.CHOW, 15, 15, t.EMPEROR), declared='chi'))


def test_constrained_winning_face_and_physical_bonus_identity():
    hand = (11, 12, 1013, 21, 22, 2023, 31, 32, 33, 41, 41, 41, 46, t.EMPEROR)
    results = list(iter_shapes(hand, variant='riichi'))
    assert results and {s.winning_face for s in results} == {46}
    assert all(s.assignment[2] == 13 and s.assignment[5] == 23 for s in results)
    assert not structural_win(hand[:-1] + (t.CHOW,), (), 'riichi')
    assert not structural_win(hand[:-1] + (5000,), (), 'riichi')


def test_more_than_four_logical_copies_are_legal_but_physical_copies_are_not():
    hand = (11, 11, 11, 11, 5000, 5004, 21, 22, 23, 31, 32, 33, 46, 46)
    assert any(Counter(s.assignment)[11] == 6 for s in iter_shapes(hand, variant='riichi'))
    assert not structural_win((11,) * 5 + hand[5:], (), 'riichi')


def test_more_than_eight_distinct_jokers_cannot_appear_in_one_pack():
    assert t.valid_physical(tuple(t.JOKERS[:8]))
    assert not t.valid_physical(tuple(t.JOKERS[:9]))


def test_seven_pairs_and_orphans_reject_structure_joker():
    hand = (11, 11, 14, 14, 17, 17, 22, 22, 25, 25, 28, 28, 41)
    assert any(s.kind == 'seven_pairs' for s in iter_shapes(hand + (t.EMPEROR,), variant='riichi'))
    assert not any(s.kind == 'seven_pairs' for s in iter_shapes(hand + (t.EYES,), variant='riichi'))
    assert any(s.kind == 'thirteen_orphans' for s in iter_shapes(t.ORPHANS + (t.EMPEROR,), variant='riichi'))
    assert not any(s.kind == 'thirteen_orphans' for s in iter_shapes(t.ORPHANS + (t.EYES,), variant='riichi'))
