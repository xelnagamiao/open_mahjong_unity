from .xueliu_hepai_check import Xueliu_Hepai_Check, Xueliu_Tingpai_Check


def test_xueliu_uses_three_melds_plus_pair_after_throw_three():
    checker = Xueliu_Hepai_Check()
    fan, names = checker.hepai_check(
        [11, 12, 13, 21, 22, 23, 21, 22, 23, 15, 15],
        [],
        ["自摸"],
        15,
    )
    assert fan == 2
    assert names == ["基本胡", "门清自摸"]


def test_xueliu_pengpeng_and_qingpeng_values():
    checker = Xueliu_Hepai_Check()
    fan, names = checker.hepai_check(
        [11, 11, 11, 21, 21, 21, 22, 22, 22, 15, 15],
        [],
        [],
        15,
    )
    assert fan == 4
    assert names == ["碰碰胡"]

    fan, names = checker.hepai_check(
        [11, 11, 11, 12, 12, 12, 13, 13, 13, 15, 15],
        [],
        [],
        15,
    )
    assert fan == 12
    assert names == ["清碰"]


def test_xueliu_waits_use_ten_tile_shape():
    checker = Xueliu_Tingpai_Check()
    waits = checker.tingpai_check(
        [11, 12, 13, 21, 22, 23, 21, 22, 23, 15], [],
    )
    assert waits == {15}
