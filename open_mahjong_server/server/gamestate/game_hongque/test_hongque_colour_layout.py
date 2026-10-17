"""同花比较花色及重数，与牌组类型、数字方向、颜色环起点无关。"""
from copy import deepcopy

import pytest

from .rules import classify_meld
from .scoring import _same_colour_layout_fans, best_win_result
from .tile import HongqueTile


FLAGS = dict(self_draw=True, before_first_discard=False, wall_empty=False)


def flower_fans(groups):
    shapes = [classify_meld(group) for group in groups]
    assert all(shape is not None for shape in shapes)
    return _same_colour_layout_fans(shapes)


def codes(colours, numbers):
    return [HongqueTile(colour, number).code for colour, number in zip(colours, numbers)]


def test_reported_cross_boundary_sequence_and_triplet_scores_six_points():
    hand = 'AY1 AY9 GY2 GY9 FY3 FY9'.split()
    melds = [{'tiles': 'CY1 DX1 DY1'.split()}, {'tiles': 'DY8 EY8 FY8'.split()}]
    original = deepcopy((hand, melds))
    result = best_win_result(hand, melds, **FLAGS)
    assert result is not None
    assert result['fans'] == [{'name': '双同花', 'value': 2, 'count': 1, 'total': 2}]
    assert (result['base'], result['fan_total'], result['points']) == (3, 2, 6)
    assert (hand, melds) == original


@pytest.mark.parametrize('start', range(14))
@pytest.mark.parametrize('step', (1, 2))
@pytest.mark.parametrize('reverse', (False, True))
@pytest.mark.parametrize('count,name,value', [(2, '双同花', 2), (3, '三同花', 6), (4, '四同花', 12)])
def test_mixed_group_types_at_every_colour_boundary(start, step, reverse, count, name, value):
    colours = [(start + step * i) % 14 for i in range(3)]
    numbers = [[1, 2, 3], [4, 5, 6], [3, 4, 5]]
    groups = [codes(colours, [9, 9, 9])]
    groups.extend(codes(colours, ns[::-1] if reverse else ns) for ns in numbers[:count - 1])
    assert len({tile for group in groups for tile in group}) == 3 * count
    expected = [{'name': name, 'value': value, 'count': 1, 'total': value}]
    assert flower_fans(groups) == expected
    assert flower_fans([group[::-1] for group in groups[::-1]]) == expected
    result = best_win_result([], [{'tiles': group} for group in groups], **FLAGS)
    assert [fan for fan in result['fans'] if fan['name'].endswith('同花')] == expected


@pytest.mark.parametrize('start', range(14))
@pytest.mark.parametrize('shift', range(1, 7))
def test_complete_colour_cycles_ignore_their_starting_point(start, shift):
    colours = [(start + 2 * i) % 14 for i in range(7)]
    rotated = colours[shift:] + colours[:shift]
    groups = [codes(colours, range(1, 8)), codes(rotated, range(1, 8))]
    assert len(set(groups[0] + groups[1])) == 14
    assert flower_fans(groups) == [{'name': '双同花', 'value': 2, 'count': 1, 'total': 2}]


def test_colour_multiplicity_and_length_still_have_to_match():
    # Eight-tile sequences cover the same seven levels but repeat different levels.
    first = codes([i * 2 % 14 for i in range(8)], range(1, 9))
    second = codes([(2 + i * 2) % 14 for i in range(8)], range(1, 9))
    assert flower_fans([first, second]) == []
    assert flower_fans(['AX1 AX2 AX3'.split(), 'AX4 AX5 AX6 AX7'.split()]) == []
    assert flower_fans(['AX1 AY2 BX3'.split(), 'BX4 BY5 CX6'.split()]) == []


def test_independent_cross_boundary_pairs_can_repeat():
    groups = ['FY1 GY2 AY3'.split(), 'AY9 FY9 GY9'.split(),
              'GX2 GY3 AX4'.split(), 'AX8 GX8 GY8'.split()]
    # No tile is shared between groups.
    assert len({tile for group in groups for tile in group}) == 12
    assert flower_fans(groups) == [{'name': '双同花', 'value': 2, 'count': 2, 'total': 4}]
