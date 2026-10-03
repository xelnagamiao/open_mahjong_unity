"""Independent book examples and boundary cases for the user-supplied rules."""

import pytest

from .rules import (FLOWERS, TILES, normalize_config, parse_meld, payments,
                    score_hand, waiting_tiles)


OPEN_PLAIN = [14, 15, 16, 21, 22, 23, 34, 35, 36, 28, 28]
CLOSED_PLAIN = [11, 12, 13] + OPEN_PLAIN


@pytest.mark.parametrize("draw,flowers,expected", [
    (True, [], None), (True, [51], None), (True, [51, 52], 3),
    (False, [51, 52], None), (False, [51, 52, 53], 4),
    (True, list(FLOWERS), 9),
])
def test_small_three_and_four_flowers(draw, flowers, expected):
    result = score_hand(OPEN_PLAIN, ["s12"], flowers, self_draw=draw)
    assert (result.points if result else None) == expected


@pytest.mark.parametrize("pure,closed,pungs,expected", [
    (False, False, False, 6), (False, True, False, 9),
    (False, False, True, 11), (False, True, True, 14),
    (True, False, False, 8), (True, True, False, 11),
    (True, False, True, 13), (True, True, True, 16),
])
def test_all_six_published_stacking_examples(pure, closed, pungs, expected):
    pair = [19, 19] if pure else [41, 41]
    groups = [11]*3 + [13]*3 + [15]*3 + [18]*3 if pungs else [11,12,13,14,15,16,17,18,19,11,12,13]
    hand = groups + pair if closed else groups[3:] + pair
    melds = [] if closed else ["k11" if pungs else "s12"]
    result = score_hand(hand, melds, self_draw=True)
    assert result.base_flowers == expected
    assert result.qualifying_flowers == expected - 1
    assert result.points == expected
    assert not result.extras


def test_closed_pattern_already_includes_win_base_and_honor_pair_is_not_pung():
    result = score_hand(CLOSED_PLAIN)
    assert result.base_flowers == 4
    assert result.fan_names == ["门清（4花）"]
    honor_pair = CLOSED_PLAIN[:-2] + [45,45]
    result = score_hand(honor_pair, winning_tile=45)
    assert result.base_flowers == 4 and not result.extras


@pytest.mark.parametrize("tile,seat,open_pung,closed_pung,open_kong,closed_kong", [
    (41,41,2,3,4,5), (41,42,1,2,3,4),
    (45,42,2,3,4,5), (46,43,2,3,4,5), (47,44,2,3,4,5),
])
def test_every_honor_flower_column_and_ron_deduction(tile, seat, open_pung, closed_pung, open_kong, closed_kong):
    tail = [21,22,23,34,35,36,28,28]
    for code, expected in [(f"k{tile}",open_pung), (f"g{tile}",open_kong), (f"G{tile}",closed_kong)]:
        result = score_hand(tail, ["s12",code], [51,52], seat_wind=seat, self_draw=True)
        assert sum(x.flowers for x in result.extras) == expected + 2
    hand = tail + [tile]*3
    draw = score_hand(hand,["s12"],[51,52],seat_wind=seat,self_draw=True)
    ron = score_hand(hand,["s12"],[51,52],seat_wind=seat,winning_tile=tile)
    pair_ron = score_hand(hand,["s12"],[51,52],seat_wind=seat,winning_tile=28)
    assert draw.base_flowers == closed_pung + 3
    assert ron.base_flowers == open_pung + 3
    assert pair_ron.base_flowers == draw.base_flowers


@pytest.mark.parametrize("code,extra", [("g11",1),("G11",2)])
def test_number_kongs_and_concealed_kong_preserves_menqing(code, extra):
    result = score_hand(OPEN_PLAIN, [code], [51,52], self_draw=True)
    assert result.base_flowers == (4 if code[0]=="G" else 1) + extra + 2


@pytest.mark.parametrize("draw", [False, True])
def test_one_flower_big_single_and_no_flower_rejected(draw):
    melds = ["s12","s15","s22","s35"]
    assert score_hand([28,28],melds,self_draw=draw) is None
    result = score_hand([28,28],melds,[51],self_draw=draw)
    assert (result.base_flowers,result.multiplier,result.points)==(2,2,4)
    assert result.fan_names == ["胡牌底花（1花）","花牌（1花）","大吊车（×2）"]


@pytest.mark.parametrize("draw,kong,sea,rob,factor", [
    (True,False,False,False,1), (True,True,False,False,2),
    (True,False,True,False,2), (True,True,True,False,4),
    (False,False,False,True,3), (False,False,True,False,3),
    (False,True,False,False,1),
])
def test_special_factors_and_single_payer_are_applied_once(draw,kong,sea,rob,factor):
    result=score_hand(CLOSED_PLAIN,self_draw=draw,after_kong=kong,sea_bottom=sea,rob_kong=rob)
    assert result.points==4*factor
    delta=payments(0,result.points,payer=None if draw else 2)
    assert delta==([12*factor,-4*factor,-4*factor,-4*factor] if draw else [4*factor,0,-4*factor,0])
    assert sum(delta)==0


def test_all_factors_multiply_after_flower_total_and_do_not_create_eligibility():
    melds=["s12","s15","s22","g35"]
    result=score_hand([28,28],melds,[51],self_draw=True,after_kong=True,sea_bottom=True)
    assert (result.base_flowers,result.multiplier,result.points)==(3,8,24)
    assert score_hand(OPEN_PLAIN,["s12"],[51],self_draw=True,after_kong=True,sea_bottom=True) is None


def test_all_honors_counts_fixed_patterns_and_separate_pung_flowers():
    hand=[41]*3+[42]*3+[45]*3+[46]*3+[47]*2
    result=score_hand(hand,self_draw=True,seat_wind=41)
    assert [x.name for x in result.patterns]==["全风板","碰碰胡","门清"]
    assert result.base_flowers == 13+6+4-2 + 3+2+3+3


def test_seven_pairs_is_off_by_default_and_uses_shared_bottom():
    hand=[11,11,14,14,17,17,22,22,25,25,28,28,33,33]
    assert score_hand(hand) is None
    result=score_hand(hand,seven_pairs=True)
    assert result.shape=="seven_pairs" and result.points==13
    assert waiting_tiles(hand[:-1])==set()
    assert waiting_tiles(hand[:-1],seven_pairs=True)=={33}
    quad=[11]*4+[14]*2+[22]*2+[25]*2+[33]*2+[37]*2
    assert score_hand(quad,seven_pairs=True).points==13


def test_waits_reject_fifth_copy_including_declared_kongs_and_flower_tiles():
    assert waiting_tiles([11], ["G11","s22","s25","s35"]) == set()
    assert waiting_tiles([28], ["G11","s22","s25","s35"]) == {28}
    assert waiting_tiles([51], ["s12","s22","s25","s35"]) == set()
    assert waiting_tiles([True]*13) == set()
    assert waiting_tiles([11]*13) == set()
    assert waiting_tiles([11]*12) == set()


@pytest.mark.parametrize("hand,melds,kwargs", [
    ([11]*14,[],{}), (CLOSED_PLAIN,["s12"],{}), (CLOSED_PLAIN,[],{"winning_tile":51}),
    (CLOSED_PLAIN,[],{"flowers":[51,51]}), (CLOSED_PLAIN,[],{"flowers":[59]}),
    (CLOSED_PLAIN,[],{"seat_wind":45}), (CLOSED_PLAIN,[],{"self_draw":True,"rob_kong":True}),
    (OPEN_PLAIN,["s11"],{}), (OPEN_PLAIN,["s19"],{}), (OPEN_PLAIN,["k51"],{}),
    (OPEN_PLAIN,["x11"],{}), (OPEN_PLAIN,[None],{}),
    ([True]+CLOSED_PLAIN[1:],[],{}),
])
def test_invalid_hand_never_wins(hand,melds,kwargs):
    assert score_hand(hand,melds,**kwargs) is None


def test_external_chow_uses_middle_not_start():
    assert parse_meld("s12").tiles==(11,12,13)
    assert parse_meld("s38").tiles==(37,38,39)
    with pytest.raises(ValueError):
        parse_meld("s39")


@pytest.mark.parametrize("value", [{"seven_pairs":1},{"seven_pairs":"false"},{"extra":True},[],False])
def test_strict_room_config(value):
    with pytest.raises(ValueError):
        normalize_config(value)


def test_config_default_and_enabled():
    assert normalize_config()=={"seven_pairs":False}
    assert normalize_config({"seven_pairs":True})=={"seven_pairs":True}


@pytest.mark.parametrize("winner,points,payer", [(True,1,None),(4,1,None),(0,0,None),(0,1,0),(0,1,4),(0,1,True)])
def test_invalid_payments(winner,points,payer):
    with pytest.raises(ValueError):
        payments(winner,points,payer=payer)
