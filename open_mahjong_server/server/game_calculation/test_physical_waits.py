"""Four-copy limits, including Sichuan's concealed-hand-only wait policy."""
import pytest

from .game_calculation_service import GameCalculationService


@pytest.mark.parametrize("rule", ["Classical", "Sichuan", "Riichi"])
def test_four_concealed_copies_cannot_wait_for_a_fifth(rule):
    hand = [11, 11, 11, 11, 13, 14, 15, 16, 17, 18, 19, 19, 19]
    waits = getattr(GameCalculationService(), rule + "_tingpai_check")(hand, [])
    assert waits == {12}


@pytest.mark.parametrize("rule", ["Sichuan"])
@pytest.mark.parametrize("hand,melds,expected", [
    ([21, 22, 23, 31, 32, 33, 37, 37, 37, 11], ["k11"], {11}),
    ([12, 13, 21, 22, 23, 31, 32, 33, 37, 37], ["g11"], {11, 14}),
    ([12, 13, 21, 22, 23, 31, 32, 33, 37, 37], ["G11"], {11, 14}),
    ([12, 12, 12, 13, 14, 21, 22, 23, 37, 37], ["s12"], {12, 15, 37}),
    ([11, 12, 13, 17, 18, 19, 23], ["k23", "k14"], {23}),
    ([11] * 4 + [13, 14, 15] + [17] * 3 + [19] * 3, [], set()),
])
def test_sichuan_only_concealed_copies_exclude_waits(rule, hand, melds, expected):
    waits = getattr(GameCalculationService(), rule + "_tingpai_check")(hand, melds)
    assert waits == expected
