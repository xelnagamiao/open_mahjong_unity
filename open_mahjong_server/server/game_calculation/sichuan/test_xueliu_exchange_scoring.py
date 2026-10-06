"""换三张与弃三张独立番表、根倍率及特殊和型。"""
import pytest

from .xueliu_hepai_check import Xueliu_Hepai_Check, Xueliu_Tingpai_Check

CHECK = Xueliu_Hepai_Check()


@pytest.mark.parametrize('hand,melds,expected,main', [
    ([11,12,13,14,15,16,21,22,23,24,25,26,28,28], [], 3, '基本胡'),
    ([12,13,14,15,16,17,22,23,24,25,26,27,28,28], [], 4, '基本胡'),
    ([11,11,13,13,15,15,17,17,21,21,23,23,25,25], [], 6, '七对'),
    ([12,12,12,12,14,14,16,16,22,22,24,24,26,26], [], 14, '七对'),
    ([11,11,13,13,15,15,17,17,19,19,12,12,14,14], [], 12, '清七对'),
    ([11,11,11,13,13,13,15,15,15,17,17,17,19,19], [], 11, '清碰'),
    ([11,11,11,13,13,13,21,21,21,23,23,23,29,29], [], 5, '碰碰胡'),
    ([11,12,13,14,15,16,17,18,19,11,12,13,15,15], [], 7, '清一色'),
    ([11,11,11,11,13,13,13,13,21,21,23,23,25,25], [], 18, '七对'),
    ([11,11,11,11,13,13,13,13,21,21,21,21,25,25], [], 24, '七对'),
    ([29,29], ['k11','k13','k21','k23'], 8, '金钩钓'),
    ([19,19], ['k11','k13','k15','k17'], 16, '清金钩钓'),
])
def test_exchange_shape_values(hand, melds, expected, main):
    fan, names = CHECK.hepai_check(hand, melds, [], hand[-1], meld_count=4)
    assert fan == expected
    assert main in names
    if main in ('七对', '清七对'):
        assert '门清' not in names


def test_kong_and_situational_roots_multiply_instead_of_adding_old_kong_fan():
    hand = [12,13,14,21,22,23,24,25,26,28,28]
    fan, names = CHECK.hepai_check(hand, ['G11'], ['自摸','杠上花'], 28, meld_count=4)
    assert fan == 9  # (基本胡2 + 门清1) × (1 + 杠根1 + 杠上花1)
    assert names == ['基本胡','门清','根','杠上花']
    assert CHECK.hepai_check(hand, ['G11'], [], 28, dingque_suit=1, meld_count=4) == (0, [])


def test_seven_pairs_wait_is_recognized_only_by_exchange():
    hand = [11,11,13,13,15,15,17,17,21,21,23,23,25]
    assert Xueliu_Tingpai_Check().tingpai_check(hand, [], 4) == {25}
    assert Xueliu_Tingpai_Check().tingpai_check(hand, [], 3) == set()
    fan, names = CHECK.hepai_check([11,12,13,14,15,16,21,22,23,25,25], [], ['自摸'], 25, meld_count=3)
    assert (fan, names) == (2, ['基本胡','门清自摸'])


@pytest.mark.parametrize('context', ['杠上花', '杠上炮', '抢杠', '海底'])
def test_each_special_context_adds_one_root_multiplier(context):
    hand = [11,12,13,14,15,16,21,22,23,24,25,26,28,28]
    fan, names = CHECK.hepai_check(hand, [], [context], 28, meld_count=4)
    assert fan == 6 and names == ['基本胡', '门清', context]


@pytest.mark.parametrize('hand,melds', [
    ([11]*5+[12,13,14,21,22,23,24,25,26], []),
    ([11,12,13,21,22,23,24,25,26,28,28], ['g11']),
    ([11,12,13,21,22,23,24,25,26,28,28], ['s15']),
    ([11,12,13,21,22,23,24,25,26,28,28], ['gXX']),
    ([11,12,13,21,22,23,24,25,26,28,28], [None]),
    ([], []),
])
def test_exchange_rejects_invalid_physical_tiles_and_melds(hand, melds):
    assert CHECK.hepai_check(hand, melds, [], meld_count=4) == (0, [])
