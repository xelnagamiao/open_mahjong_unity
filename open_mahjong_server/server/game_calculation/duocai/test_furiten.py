import pytest

from . import tiles as t
from .furiten import discard_furiten, legal_ron, logical_waits
from .scoring import Context


HAND = (11, 12, 13, 22, 23, 24, 34, 35, 36, 36, 37, 28, 28)


def test_discard_any_wait_disables_all_ron_but_meld_only_jokers_do_not():
    assert logical_waits(HAND) == {35, 38}
    assert discard_furiten(HAND, (), [35], 38)
    assert discard_furiten(HAND, (), [t.EMPEROR], 38)
    assert not discard_furiten(HAND, (), [t.CHOW, t.PUNG], 38)
    assert not legal_ron(HAND, (), [35], 38)


def test_conditional_discard_checks_current_effective_interpretations():
    assert not discard_furiten(HAND, (), [t.EVEN], 38)
    assert discard_furiten(HAND, (), [t.ODD], 38)
    assert discard_furiten(HAND, (), [t.EVEN], 38, Context(riichi=1))
    assert not discard_furiten(HAND, (), [t.MANGAN], 38)


def test_temporary_and_permanent_furiten_block_only_ron_path():
    assert legal_ron(HAND, (), [], 38)
    assert not legal_ron(HAND, (), [], 38, temporary=True)
    assert not legal_ron(HAND, (), [], 38, permanent=True)


def test_bonus_discards_use_normal_face_and_nonwinning_special_does_not_pass():
    assert discard_furiten(HAND, (), [2037], 38) is False
    assert not legal_ron(HAND, (), [], t.EVEN)
    assert legal_ron(HAND, (), [], 38)


def test_joker_can_complete_a_fifth_logical_copy_without_fifth_physical_tile():
    from .solver import waits, structural_win
    hand = (11, 11, 11, 11, 22, 22, 22, 33, 1033, 2033, 44, 44, 44)
    assert not structural_win(hand + (11,), (), 'riichi')
    assert t.EMPEROR in waits(hand, (), 'riichi')
    assert logical_waits(hand) == {11}
    assert discard_furiten(hand, (), [5000], t.EMPEROR, Context(riichi=1))
    assert not legal_ron(hand, (), [5000], t.EMPEROR, Context(riichi=1))


def test_joker_only_wait_is_available_for_riichi_discard_selection():
    from ...gamestate.game_duocai.actions import riichi_cuts
    hand = (11, 11, 11, 11, 22, 22, 22, 33, 1033, 2033, 44, 44, 44)
    assert 47 in riichi_cuts(hand + (47,), ())


@pytest.mark.parametrize('hand', [
    HAND,
    (5039, *HAND[1:]),
    (5037, 5001, *HAND[2:]),
    (5038, 5001, 5002, *HAND[3:]),
    (11, 11, 11, 11, 22, 22, 22, 33, 1033, 2033, 44, 44, 44),
])
def test_optimized_logical_waits_match_independent_full_physical_shape_projection(hand):
    from .solver import iter_shapes
    expected = {shape.winning_face for tile in t.PHYSICAL
                for shape in iter_shapes(hand + (tile,), (), 'riichi')}
    assert logical_waits(hand) == expected


def test_exhausted_normal_faces_include_concealed_meld_tiles():
    from .solver import Meld, iter_shapes
    hand = (11, 31, 32, 33)
    melds = (Meld((11, 11, 11, 5000), True, 'angang'),
             Meld((22, 22, 22, 22), True, 'angang'),
             Meld((45, 45, 45, 45), True, 'angang'))
    expected = {shape.winning_face for tile in t.PHYSICAL
                for shape in iter_shapes(hand + (tile,), melds, 'riichi')}
    assert expected == {11} and logical_waits(hand, melds) == expected
