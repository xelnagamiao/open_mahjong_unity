import pytest

from . import tiles as t
from .wall import TwinDecks, Wall


def wall(riichi=False):
    return Wall(t.normal_pack() + list(t.JOKERS[:8]), opening=1, riichi=riichi)


def test_clockwise_draw_crosses_right_corner_and_takes_top_first():
    w = wall()
    positions = [w.draw()[1] for _ in range(38)]
    assert positions[:4] == [1, 0, 3, 2]
    assert positions[34:] == [35, 34, 109, 108]
    view = w.public()
    assert [(view[p]['side'], view[p]['stack'], view[p]['layer']) for p in positions[34:]] == [
        (0,17,1), (0,17,0), (3,0,1), (3,0,0)]


@pytest.mark.parametrize('variant,dice,expected', [
    ('riichi', [2,3], 11),
    ('riichi', [6,6], 133),
    ('guobiao', [5,6,6,6], 47),
    ('guobiao', [2,3,1,1], 15),
])
def test_opening_uses_first_roll_for_side_and_all_dice_for_stack(variant,dice,expected):
    w = TwinDecks(17).next_wall(variant=variant,dice=dice)
    assert w.opening == expected
    assert w.draw()[1] == expected
    assert w.dice == dice


@pytest.mark.parametrize('variant,dice', [('guobiao',[2,3]),('riichi',[2,3,1,1]),('guobiao',[1,2,3,7])])
def test_wrong_dice_format_is_rejected(variant,dice):
    with pytest.raises(ValueError):
        TwinDecks(17).next_wall(variant=variant,dice=dice)


def test_guobiao_supplements_draw_tail_top_then_bottom_and_keep_front():
    w = wall()
    assert [w.draw(from_tail=True)[1] for _ in range(4)] == [71,70,69,68]
    assert w.draw()[1] == 1
    assert not w.dead and not w.indicators()


def test_riichi_four_fixed_rinshan_and_all_five_indicator_pairs():
    w = wall(True)
    assert [x['position'] for x in w.public() if x['tile'] is not None] == [67]
    expected_rinshan = [71,70,69,68]
    for count, expected in enumerate(expected_rinshan, 1):
        assert w.draw(fourth_kong=True)[1] == expected
        assert len(set(w.dead) - w.removed) == 14
        visible = {x['position'] for x in w.public() if x['tile'] is not None}
        assert visible == set([67,65,63,61,59][:count+1])
    assert {x['position'] for x in w.public(ura=True) if x['tile'] is not None} == set(range(58,68))
    assert not set(expected_rinshan) & {x['position'] for x in w.public(ura=True) if x['tile'] is not None}
    with pytest.raises(ValueError):
        w.draw(fourth_kong=True)
    assert w.draw()[1] == 1
    assert w.dora_count == 5


def test_tail_supplement_when_only_one_live_tile_remains():
    w = wall()
    for _ in range(143):
        w.draw()
    assert w.draw(from_tail=True)[1] == 70
    assert not w.live and len(w.removed) == 144
