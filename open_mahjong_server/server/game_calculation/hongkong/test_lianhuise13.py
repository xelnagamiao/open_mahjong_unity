"""Golden examples from the uploaded rulebook, including its embedded score table."""
from dataclasses import replace

import pytest

from .models import HandContext, HongKongRules, PROFILES
from .scoring import score_hand
from .solver import decompositions
from .lianhuise13 import lianhuise_payments
from .test_hongkong_calculation import tiles

RULES = HongKongRules(PROFILES[1], new13_version="lianhuise")


def score(hand="123456m 234678p 55z", *, melds=(), winning=None, flowers=(), **kwargs):
    hand = tiles(hand)
    return score_hand(HandContext(hand, melds, winning if winning is not None else hand[-1] if hand else 0, flowers=flowers, **kwargs), RULES)


@pytest.mark.parametrize("hand,melds,kwargs,expected", [
    ("123456m 234678p 55z", (), {"self_draw":True}, {"all_chows":1,"no_flowers":1}),
    ("123456m 234678p 55z", (), {"self_draw":True}, {"self_draw":1}),
    ("123456m 234678p 55z", (), {"flowers":(51,),"self_draw":True}, {"seat_flower_51":1}),
    ("123456m 234678p 55z", (), {"flowers":(51,52,53,54,55)}, {"flower_set_51":2,"seat_flower_55":1}),
    ("123456m 234678p 55z", (), {"rob_kong":True}, {"rob_kong":1}),
    ("123456m 234678p 55z", (), {"last_tile":True}, {"last_tile":1}),
    ("123456m 234678p 55z", (), {"replacement":"kong","self_draw":True}, {"kong_draw":1,"self_draw":1}),
    ("123456m 234678p 55z", (), {"replacement":"kong","consecutive_kongs":2,"self_draw":True}, {"consecutive_kongs":8}),
    ("123m 456p 11177z", ("k45",), {}, {"dragon_45":1,"seat_wind":1,"round_wind":1}),
    ("222m 333p 55566s", ("k11",), {}, {"all_pungs":3}),
    ("123456789m 11155z", (), {}, {"half_flush":3}),
    ("999m 111p 11155z", ("k11",), {}, {"mixed_terminals":4}),
    ("123m 456p 66677z", ("k45",), {}, {"little_dragons":5}),
    ("456m 11122233z", ("k44",), {}, {"little_winds":6}),
    ("12345677m", ("s12","s18"), {}, {"full_flush":7}),
    ("123m 55p 666777z", ("k45",), {}, {"big_dragons":8}),
    ("111444m 222555p 66s", (), {"self_draw":True}, {"four_concealed_pungs":8}),
    ("33344455566z", ("k41",), {}, {"all_honors":10}),
    ("999m 111999p 11s", ("k11",), {}, {"all_terminals":10}),
    ("11123456789995m", (), {}, {"nine_gates":10}),
    ("123456m 234678p 55z", (), {"self_draw":True,"heavenly":True}, {"heavenly":13}),
    ("123456m 234678p 55z", (), {"earthly":True}, {"earthly":13}),
    ("123456m 234678p 55z", (), {"humanly":True,"self_draw":True}, {"humanly":13}),
    ("222333444z 55m", ("k41",), {}, {"big_winds":13}),
    ("19m 19p 19s 11234567z", (), {}, {"orphans":13}),
    ("55z", ("g11","G22","g33","g41"), {}, {"four_kongs":13}),
])
def test_document_fan_values(hand,melds,kwargs,expected):
    result = score(hand,melds=melds,**kwargs)
    found = {f.id:f.value for f in result.fans}
    assert result.is_win, result
    assert expected.items() <= found.items()


@pytest.mark.parametrize("count,total",[(7,3),(8,8)])
def test_seven_and_eight_flower_wins_include_self_draw_once(count,total):
    result = score("",flowers=tuple(range(51,51+count)),winning=0,self_draw=True,flower_win="seven" if count==7 else "eight")
    assert result.fan == total
    assert "self_draw" not in result.fan_ids


@pytest.mark.parametrize("bonus",["heavenly","humanly"])
@pytest.mark.parametrize("count",[7,8])
def test_flower_limit_retains_flower_identity_without_stacking(bonus,count):
    result=score("",flowers=tuple(range(51,51+count)),winning=0,self_draw=True,
                 flower_win="seven" if count==7 else "eight",**{bonus:True})
    assert result.is_win and result.fan==13
    assert result.fan_ids==("flower_"+bonus,)


@pytest.mark.parametrize("hand,melds,kwargs,absent", [
    ("123m 456p 66677z", ("k45",), {}, {"dragon_45","dragon_46"}),
    ("123m 55p 666777z", ("k45",), {}, {"dragon_45","dragon_46","dragon_47"}),
    ("999m 111p 11155z", ("k11",), {}, {"all_pungs"}),
    ("456m 11122233z", ("k44",), {}, {"half_flush"}),
    ("111444m 222555p 66s", (), {"self_draw":True}, {"all_pungs","concealed","self_draw"}),
    ("123456m 234678p 55z", (), {"flowers":(51,52,53,54)}, {"seat_flower_51"}),
    ("123456m 234678p 55z", (), {"last_tile":True,"self_draw":True,"replacement":"kong"}, {"last_tile"}),
    ("123456m 234678p 55z", (), {"self_draw":True,"replacement":"flower","consecutive_kongs":2}, {"kong_draw","consecutive_kongs"}),
    ("123456m 234678p 55z", (), {"self_draw":True,"replacement":"kong","consecutive_kongs":2}, {"kong_draw","self_draw"}),
    ("123m 456p 55577z", ("G11",), {}, {"concealed","four_concealed_pungs"}),
])
def test_non_cumulative_patterns(hand,melds,kwargs,absent):
    assert not absent.intersection(score(hand,melds=melds,**kwargs).fan_ids)


def test_limits_replace_all_other_fans_and_highest_limit_wins():
    result = score("11122233344455z",self_draw=True,heavenly=True,flowers=(51,55))
    assert result.fan == 13 and len(result.fans)==1
    assert result.fan_ids == ("heavenly",)
    assert score("11123456789995m",self_draw=True).raw_fan==10


def test_no_flower_variant_and_minimum_three_and_no_seven_pairs():
    ctx = HandContext(tiles("123456m 234678p 55z"),winning_tile=45)
    no_flowers = replace(RULES,flowers=False)
    assert not score_hand(ctx,RULES).is_win
    assert "concealed" not in score_hand(ctx,RULES).fan_ids
    assert score_hand(replace(ctx,self_draw=True),RULES).fan==3
    assert not score_hand(ctx,no_flowers).is_win
    assert "concealed" in score_hand(ctx,no_flowers).fan_ids
    assert score_hand(replace(ctx,self_draw=True),no_flowers).fan==3
    assert not score_hand(replace(ctx,flowers=(51,)),no_flowers).is_win
    hand=tiles("1122m 3344p 5566s 77z")
    assert not decompositions(hand,(),RULES)
    assert decompositions(hand,(),HongKongRules(PROFILES[1]))


@pytest.mark.parametrize("fan,ron,each",[(3,8,4),(4,16,8),(5,24,12),(6,32,16),(7,48,24),(8,64,32),(9,96,48),(10,128,64),(11,192,96),(12,256,128),(13,384,192),(30,384,192)])
def test_embedded_image_payment_table(fan,ron,each):
    assert lianhuise_payments(fan,1,discarder=0)==[-ron,ron,0,0]
    assert lianhuise_payments(fan,1)==[-each,3*each,-each,-each]
    assert lianhuise_payments(fan,1,liability=2)==[0,3*each,-3*each,0]


@pytest.mark.parametrize("fan,winner,options",[(2,0,{}),(3,4,{}),(True,0,{}),(3,0,{"discarder":0}),(3,0,{"liability":0})])
def test_invalid_payment_inputs_are_rejected(fan,winner,options):
    with pytest.raises(ValueError): lianhuise_payments(fan,winner,**options)


@pytest.mark.parametrize("ctx",[
    HandContext(tiles("123456m 234678p 55z"),winning_tile=99),
    HandContext(tiles("123456m 234678p 55z"),winning_tile=45,flowers=(51,51)),
    HandContext(tiles("123456m 234678p 55z"),winning_tile=45,flowers=(59,)),
    HandContext(tiles("123456m 234678p 55z"),("invalid",),winning_tile=45),
    HandContext((),flowers=tuple(range(51,58)),self_draw=False,flower_win="seven"),
    HandContext((),flowers=tuple(range(51,58)),self_draw=True,flower_win="eight"),
])
def test_invalid_hand_and_flower_requests_are_rejected(ctx):
    assert not score_hand(ctx,RULES).is_win
