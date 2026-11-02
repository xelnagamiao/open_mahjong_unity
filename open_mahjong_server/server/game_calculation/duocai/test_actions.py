import pytest

from . import tiles as t
from .actions import call_choices, resolve_response, turn_choices
from .solver import Meld


def test_chow_direction_and_call_joker_specific_actions():
    assert not any(v.kind == 'chi' for v in call_choices((11, 12, 15), 13))
    assert any(v.kind == 'chi' and v.take == (11, 12) for v in call_choices((11, 12, 15), 13, next_player=True))
    assert {v.kind for v in call_choices((11, 12, 15), t.CHOW, next_player=True)} == {'chi'}
    assert {v.kind for v in call_choices((11, 11, 11, 12), t.PUNG, next_player=True)} == {'peng'}


@pytest.mark.parametrize('joker', [t.MANGAN, t.ODD, t.EVEN])
def test_conditional_jokers_cannot_be_called_or_used_to_call(joker):
    assert not call_choices((11, 11, 11, 12), joker, next_player=True)
    assert not call_choices((joker,), 11, next_player=True)
    assert not turn_choices((11, 11, 11, joker))


@pytest.mark.parametrize('joker', [t.CHOW, t.PUNG, t.RYANMEN, t.EDGE_CLOSED, t.OLD_YOUNG])
def test_new_kongs_reject_structural_and_call_only_jokers(joker):
    assert not turn_choices((11, 11, 11, joker))
    assert not any(v.kind == 'minggang' for v in call_choices((11, 11, joker), 11))


def test_existing_call_pung_can_extend_but_cannot_receive_call_pung():
    original = Meld((11, 11, t.PUNG), declared='peng', source=2)
    choices = turn_choices((11,), (original,))
    assert len(choices) == 1 and choices[0].take == (11,) and choices[0].meld_index == 0
    normal = Meld((11, 11, 11), declared='peng')
    assert not turn_choices((t.PUNG,), (normal,))
    chow = Meld((11, 12, t.CHOW), declared='chi')
    assert not turn_choices((t.EMPEROR,), (chow,))


def test_ambiguous_chow_can_become_kong_and_high_kong_grows_one_at_a_time():
    original = Meld((5018, 15, t.EMPEROR), declared='chi')
    choices = turn_choices((15, 15), (original,))
    assert len(choices) == 1 and choices[0].faces == ((15, 15, 15, 15),)
    extended = Meld((*original.physical, 15), declared=original.declared)
    choices = turn_choices((15,), (extended,))
    assert len(choices) == 1 and choices[0].faces == ((15,) * 5,)
    assert not turn_choices((15,), (extended,), can_extend=False)
    assert turn_choices((15,), (extended,), can_fourth=False)


def test_physical_color_choice_is_preserved_and_deduplicated():
    choices = call_choices((13, 13, 1013, 2013), t.EMPEROR)
    pungs = [v for v in choices if v.kind == 'peng']
    assert {v.take for v in pungs} == {(13, 13), (13, 1013), (13, 2013), (1013, 2013)}
    assert len({v.key for v in choices}) == len(choices)
    assert not call_choices((13, 13, 13), 13)
    assert all(v.public()['take'] == list(v.take) for v in pungs)


@pytest.mark.parametrize('discarder', range(4))
def test_response_priority_and_nearest_head_bump(discarder):
    next_seat, opposite, previous = ((discarder + i) % 4 for i in (1, 2, 3))
    assert resolve_response(discarder, {next_seat: 'chi', opposite: 'peng', previous: 'hu'}) == (previous, 'hu')
    assert resolve_response(discarder, {next_seat: 'peng', opposite: 'minggang'}) == (next_seat, 'peng')
    assert resolve_response(discarder, {next_seat: 'hu', previous: 'hu'}) == (next_seat, 'hu')
    assert resolve_response(discarder, {next_seat: 'pass', opposite: 'pass'}) is None
