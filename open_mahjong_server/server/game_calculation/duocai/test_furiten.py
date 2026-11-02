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
