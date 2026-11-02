"""Impossible fifth-copy waits shared by the older shape enumerators."""
import pytest

from .game_calculation_service import GameCalculationService


@pytest.mark.parametrize("rule", ["Classical", "Sichuan", "Riichi"])
def test_four_concealed_copies_cannot_wait_for_a_fifth(rule):
    hand = [11, 11, 11, 11, 13, 14, 15, 16, 17, 18, 19, 19, 19]
    waits = getattr(GameCalculationService(), rule + "_tingpai_check")(hand, [])
    assert waits == {12}


@pytest.mark.parametrize("rule", ["Sichuan"])
@pytest.mark.parametrize("hand,melds,expected", [
    ([21, 22, 23, 31, 32, 33, 37, 37, 37, 11], ["k11"], set()),
    ([12, 13, 21, 22, 23, 31, 32, 33, 37, 37], ["g11"], {14}),
    ([12, 13, 21, 22, 23, 31, 32, 33, 37, 37], ["G11"], {14}),
    ([12, 12, 12, 13, 14, 21, 22, 23, 37, 37], ["s12"], {15, 37}),
])
def test_declared_tiles_count_towards_four_copy_limit(rule, hand, melds, expected):
    waits = getattr(GameCalculationService(), rule + "_tingpai_check")(hand, melds)
    assert waits == expected
