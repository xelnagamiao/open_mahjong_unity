import pytest

from .guobiao_hepai_check import Chinese_Hepai_Check
from .guobiao_kshen_hepai_check import Kshen_Hepai_Check
from .guobiao_xiaolin_hepai_check import Xiaolin_Hepai_Check


RULES = [
    (Chinese_Hepai_Check, (25, 26, 27)),
    (Xiaolin_Hepai_Check, (32, 33, 34)),
    (Kshen_Hepai_Check, (34, 34, 38)),
]


@pytest.mark.parametrize("checker_class,expected", RULES)
@pytest.mark.parametrize("suit", (1, 2, 3))
@pytest.mark.parametrize("wait", (0, 1, 2))
def test_exposed_pong_does_not_gain_concealed_hand(checker_class, expected, suit, wait):
    # 1234456777 + 1/4/7，副露 999；4 的两种拆分不得共享“暗转明”。
    tile = suit * 10 + (1, 4, 7)[wait]
    hand = [suit * 10 + rank for rank in (1, 2, 3, 4, 4, 5, 6, 7, 7, 7)] + [tile]
    melds = [f"k{suit * 10 + 9}"]
    way = ["点和", "场风南", "自风西"]
    before = (hand[:], melds[:], way[:])
    checker = checker_class(debug=False)
    for _ in range(2):
        score, fans = checker.hepai_check(hand, melds, way, tile)
        assert score == expected[wait]
        assert "门前清" not in fans
        assert (hand, melds, way) == before


@pytest.mark.parametrize("checker_class,_expected", RULES)
def test_closed_ron_keeps_concealed_hand_and_wind_fans(checker_class, _expected):
    hand = [11, 11, 11, 22, 23, 24, 35, 36, 37, 41, 41, 41, 29, 29]
    way = ["点和", "场风东", "自风东"]
    before = way[:]
    _score, fans = checker_class(debug=False).hepai_check(hand, [], way, 11)
    assert "门前清" in fans
    if checker_class is Kshen_Hepai_Check:
        assert "役·门风刻" in fans
    else:
        assert {"圈风刻", "门风刻"}.issubset(fans)
    assert way == before


def test_standard_self_draw_of_four_is_27():
    hand = [31, 32, 33, 34, 34, 35, 36, 37, 37, 37, 34]
    score, fans = Chinese_Hepai_Check().hepai_check(hand, ["k39"], ["自摸", "场风南", "自风西"], 34)
    assert score == 27
    assert "自摸" in fans
    assert "门前清" not in fans
