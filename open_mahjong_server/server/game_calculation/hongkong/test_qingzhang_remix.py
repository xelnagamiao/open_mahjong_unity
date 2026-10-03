"""Examples from the uploaded book plus independent payment/qualification cases."""
from dataclasses import replace
import pytest

from .models import Fan, HandContext, HongKongRules, QINGZHANG_REMIX, GAMETOWER, LIANHUISE, PROFILES
from .solver import structural_waits, decompositions
from .scoring import score_hand
from .qingzhang_remix import basic_points, round_five, remix_payments, FAN_POINTS
from .test_hongkong_calculation import tiles

RULES = HongKongRules(QINGZHANG_REMIX)


def quote(hand, melds=(), **kwargs):
    hand = tiles(hand)
    return score_hand(HandContext(hand, melds=tuple(melds), winning_tile=kwargs.pop("winning_tile",hand[-1]), **kwargs), RULES)


@pytest.mark.parametrize("hand,melds,options,fan,value", [
    ("23488m 555p 222678s",(),{},"simples",1),
    ("123456m 789p 55566z",(),{},"dragon_45",1),
    ("123456m 789p 11166z",(),{},"seat_wind",1),
    ("123456m 789p 11166z",(),{},"round_wind",1),
    ("123678m 456s 789p 44z",(),{},"all_chows",1),
    ("11123466m 567999s",(),{},"missing_suit",1),
    ("23423488m 123s",("k47",),{},"identical",1),
    ("55z",("s12","k24","g33","k46"),{},"begging",1),
    ("222m 444p 123456s 55z",(),{},"concealed_pungs",1),
    ("123456m 789p 55566z",(),{"flowers":(51,)},"flower_51",1),
    ("123456m 789p 55566z",(),{"self_draw":True},"closed_self",2),
    ("123p 456s 55z",("g11","G29"),{},"kongs",2),
    ("234m 345s 456p 666m 33z",(),{},"mixed_steps",2),
    ("123p 456s 55566z",("g11",),{"self_draw":True,"replacement":"kong"},"kong_draw",2),
    ("123456m 789p 55566z",(),{"last_tile":True},"last_tile",2),
    ("123456m 789p 55566z",(),{"rob_kong":True},"rob_kong",2),
    ("123456m 789p 55566z",(),{"flowers":(51,52,53,54)},"flower_set_51",2),
    ("666888999m 33z",("k31",),{},"all_pungs",3),
    ("111345777999m 33z",(),{},"half_flush",3),
    ("123m 456s 789p 888m 77z",(),{},"mixed_straight",3),
    ("123m 789s 123p 99m 555z",(),{},"mixed_outside",3),
    ("123m 444s 567p 44455z",(),{},"five_categories",3),
    ("2266m 22s 1133p 1133z",(),{},"seven_pairs",3),
    ("111m 444p 666s 123p 55z",(),{},"concealed_pungs",3),
    ("234m 234s 234p 777p 66z",(),{},"same_chows",3),
    ("333m 444s 555p 789s 11p",(),{},"mixed_pung_steps",3),
    ("234m 888s 11133444z",(),{},"little_three_winds",3),
    ("147m 258s 369p 12367z",(),{},"knitted",3),
    ("123m 789s 123999p 99m",(),{},"pure_outside",4),
    ("123456789m 555p 22s",(),{},"straight_1",4),
    ("123456m 789p 55566z",(),{"flowers":tuple(range(51,58))},"flower_seven",4),
    ("111999s 111m 33366z",(),{},"terminals_honors",5),
    ("234345456p 666m 33z",(),{},"pure_steps",5),
    ("234234m 678678s 66z",(),{},"two_identical",5),
    ("12355567899944m",(),{},"full_flush",7),
    ("333444555p 789s 11p",(),{},"pure_pung_steps",7),
    ("123m 678s 55666777z",(),{},"little_dragons",4),
    ("123456m 789p 55566z",(),{"flowers":tuple(range(51,59))},"flower_eight",7),
    ("123456m 789p 55566z",(),{"humanly":True},"humanly",7),
    ("234234m 777s 11p",("s13",),{},"three_identical",9),
    ("123s 55z",("g11","G29","g35"),{},"kongs",9),
    ("222m 222s 222p 567p 33z",(),{},"same_pungs",9),
    ("234345456567m 22s",(),{},"four_steps",9),
    ("123m 11122233355z",(),{},"big_three_winds",9),
    ("11122345678999m",(),{"winning_tile":19},"nine_gates",14),
    ("123m 55s 555666777z",(),{},"big_dragons",14),
    ("111m 222p 333s 55566z",(),{},"four_concealed",14),
    ("19m 19p 19s 12345677z",(),{},"orphans",14),
    ("456m 11122233444z",(),{},"little_winds",16),
    ("11223344556677z",(),{},"all_honors",16),
    ("111999m 111999p 99s",(),{},"all_terminals",16),
    ("333444555666s 11p",(),{},"four_pung_steps",16),
    ("123456m 789p 55566z",(),{"heavenly":True,"self_draw":True},"heavenly",16),
    ("123456m 789p 55566z",(),{"earthly":True,"self_draw":True},"earthly",16),
    ("234234234234m 11s",(),{},"four_identical",32),
    ("11122233344455z",(),{},"big_winds",32),
    ("11123456789995m",(),{"winning_tile":15},"pure_nine_gates",32),
    ("55z",("G11","g22","G33","g46"),{},"four_kongs",32),
])
def test_all_document_patterns(hand,melds,options,fan,value):
    result = quote(hand,melds,**options)
    assert result.is_win, result
    found = {f.id:f.value for f in result.fans}
    assert found.get(fan) == value, found


@pytest.mark.parametrize("number,expected",[(32,30),(33,35),(37,35),(38,40),(28,30),(27,25)])
def test_document_rounding(number,expected):
    assert round_five(number)==expected


def test_limits_and_additional_point_policy():
    assert basic_points([Fan("a","",1)],[Fan("x","",12)])==25
    assert basic_points([Fan("a","",1)],[Fan("x","",13)])==30
    assert basic_points([Fan("a","",14),Fan("b","",1)],[Fan("x","",70)])==540
    assert basic_points([Fan("a","",14),Fan("b","",14),Fan("c","",9)],[Fan("x","",24)])==840
    assert basic_points([Fan("a","",16),Fan("b","",14)],[Fan("x","",24)])==960
    assert basic_points([Fan("a","",32),Fan("b","",16)],[])==1620


def test_flower_bonuses_cannot_satisfy_the_minimum_or_win_without_shape():
    # Open pinfu has only 1 fan / 15 points. Flowers and even a complete set
    # cannot turn it into a legal win; this is not seven-flower auto-win.
    for flowers in ((),(51,),(52,53,54),tuple(range(51,58)),tuple(range(51,59))):
        result=quote("456m 789p 123s 44z",("s12",),flowers=flowers,winning_tile=23)
        assert not result.is_win
    assert not quote("111222m 123s 56p 123z",flowers=tuple(range(51,59))).is_win
    assert not quote("123456m 789p 55566z",flowers=tuple(range(51,59)),flower_win="eight",self_draw=True).is_win


def test_strict_four_concealed_and_concealed_kongs_keep_closed_status():
    hand="111m 222p 333s 55566z"
    assert "four_concealed" not in quote(hand,winning_tile=45).fan_ids
    assert "four_concealed" in quote(hand,winning_tile=46).fan_ids
    assert "four_concealed" in quote("222p 333s 55566z",("G11",)).fan_ids


def test_duplicate_pairs_and_both_knitted_forms_and_invalid_near_misses():
    for text in ("1111m 2222p 33s 5566z", "147m 258s 369p 12367z", "28m 147s 36p 1234567z"):
        hand=tiles(text)
        assert decompositions(hand,(),RULES)
        before=list(hand);before.pop()
        assert hand[-1] in structural_waits(before,(),RULES)
    for text in ("147m 258s 369p 11267z", "147m 147s 369p 12367z", "147m 24s 36p 1234567z", "147m 258s 36p 123456z"):
        assert not decompositions(tiles(text),(),RULES)


def test_non_cumulative_fans_and_flower_extras():
    r=quote("123456m 789p 55566z",flowers=(51,52,53,54,55))
    assert {f.id for f in r.fans if f.id.startswith("flower_")}=={"flower_set_51","flower_55"}
    r=quote("123456m 789p 55566z",flowers=tuple(range(51,58)))
    assert {f.id for f in r.fans if f.id.startswith("flower_")}=={"flower_seven","flower_55","flower_56","flower_57"}
    r=quote("123456m 789p 55566z",flowers=tuple(range(51,59)))
    assert {f.id for f in r.fans if f.id.startswith("flower_")}=={"flower_eight"}
    assert "closed_self" not in quote("2266m 22s 1133p 1133z",self_draw=True).fan_ids
    assert "self_draw" in quote("2266m 22s 1133p 1133z",self_draw=True).fan_ids
    assert "single_wait" not in quote("2266m 22s 1133p 1133z").fan_ids
    assert "full_flush" not in quote("11122345678999m",winning_tile=19).fan_ids


@pytest.mark.parametrize("points",[30,35,125,540,840,1080,1620])
@pytest.mark.parametrize("winner",range(4))
def test_payment_paths(points,winner):
    other=[i for i in range(4) if i!=winner]
    ron,liable,third=other
    for discarder in (None,ron):
        result=remix_payments(points,winner,discarder=discarder)
        assert sum(result)==0 and result[winner]==3*points
        assert result[ron]==(-points if discarder is None else -(3*points-60))
        assert result[third]==(-points if discarder is None else -30)
        result=remix_payments(points,winner,discarder=discarder,liability=liable,liability_kind="twelve")
        assert result[liable]==-3*points and result[ron]==0
        result=remix_payments(points,winner,discarder=discarder,liability=liable,liability_kind="limit")
        assert result[liable]==(-3*points if discarder is None else -2*points)
        assert sum(result)==0 and result[winner]==3*points
    assert remix_payments(points,winner,discarder=ron,liability=ron,liability_kind="limit")[ron]==-3*points


def test_explicit_versions_preserve_legacy_behavior():
    for new,version in ((GAMETOWER,"gametower"),(LIANHUISE,"lianhuise")):
        old=HongKongRules(PROFILES[1],new13_version=version)
        fresh=HongKongRules(new)
        assert fresh.version==old.version
        ctx=HandContext(tiles("123456m 789p 55566z"),winning_tile=46,self_draw=True)
        assert score_hand(ctx,old)==score_hand(ctx,fresh)
    assert RULES.flowers and not RULES.has_ready and RULES.minimum_fan==1


@pytest.mark.parametrize("points,winner,kwargs",[(25,0,{}),(True,0,{}),(31,0,{}),(30,4,{}),(30,0,{"discarder":0}),(30,0,{"liability":0})])
def test_invalid_payments(points,winner,kwargs):
    with pytest.raises(ValueError): remix_payments(points,winner,**kwargs)


@pytest.mark.parametrize("fan,points",enumerate((15,30,50,75,100,125,150,180,210,240,280,320,360,420,480,540),1))
def test_each_document_fan_to_point_value(fan,points):
    assert FAN_POINTS[fan]==points
    # Sum one-fan awards to avoid mistaking a 14+ total for a limit pattern.
    assert basic_points([Fan(str(n),"",1) for n in range(fan)],[])==points


@pytest.mark.parametrize("code,name,value",[
    ("k11","幺九明刻",0),("k22","中张明刻",0),
    ("G11","幺九暗杠",16),("g11","幺九明杠",8),
    ("G22","中张暗杠",8),("g22","中张明杠",4),
])
def test_meld_additional_points_use_actual_visibility_and_terminal_class(code,name,value):
    result=quote("456m 789s 55566z",(code,),last_tile=True)
    assert result.is_win
    awards=[f for f in result.fans if f.name==name and f.unit=="分"]
    assert [f.value for f in awards]==([value] if value else [])


def test_pair_points_closed_ron_pung_points_and_four_return():
    result=quote("123456m 789p 55511z")
    awards={f.id:(f.name,f.value) for f in result.fans}
    assert awards["value_pair"]==("连风将牌",3)
    assert awards["closed_ron"]==("门前清食和",10)
    assert awards["single_wait"]==("听一扉",2)
    assert any(f.name=="幺九暗刻" and f.value==4 for f in result.fans)
    result=quote("122223m 456p 55566z")
    assert not any(f.name=="中张暗刻" for f in result.fans)
    assert any(f.id=="four_return_12" and f.value==3 for f in result.fans)
    assert any(f.id=="value_pair" and f.value==2 for f in result.fans)
    assert not any(f.id=="four_return_11" for f in quote("456m 789s 55566z",("G11",)).fans)
    assert "value_pair" not in quote("123m 678s 55666777z").fan_ids  # Forced by 小三元.


def test_inherent_self_draw_and_closed_patterns_do_not_duplicate_their_points():
    ordinary=quote("123456m 789p 55566z",self_draw=True)
    assert "closed_self" in ordinary.fan_ids and "self_draw" not in ordinary.fan_ids
    for hand in ("2266m 22s 1133p 1133z","147m 258s 369p 12367z","123456789m 555p 22s","234234m 678678s 66z"):
        assert "closed_ron" not in quote(hand).fan_ids
        drawn=quote(hand,self_draw=True)
        assert "closed_self" not in drawn.fan_ids and "self_draw" in drawn.fan_ids
    kong=quote("456m 789s 55566z",("g11",),self_draw=True,replacement="kong")
    assert "kong_draw" in kong.fan_ids and "self_draw" not in kong.fan_ids
    flower=quote("456m 789s 55566z",("g11",),self_draw=True,replacement="flower",last_tile=True)
    assert "kong_draw" not in flower.fan_ids and "self_draw" in flower.fan_ids


@pytest.mark.parametrize("seat",range(41,45))
def test_own_flower_and_other_flower_points_follow_seat_wind(seat):
    result=quote("123456m 789p 55566z",flowers=(51,55),seat_wind=seat)
    awards=[f for f in result.fans if f.id.startswith("flower_")]
    assert {(f.value,f.unit) for f in awards}==({(1,"番")} if seat==41 else {(4,"分")})


@pytest.mark.parametrize("flowers,win",[((51,51),46),((59,),46),((),49)])
def test_invalid_flower_or_missing_winning_tile_fails_closed(flowers,win):
    assert not quote("123456m 789p 55566z",flowers=flowers,winning_tile=win).is_win


def test_each_repeat_adds_two_basic_points_to_dealer_and_non_dealer():
    for seat in (41,42,43,44):
        for count in (1,2,4):
            result=quote("123456m 789p 55566z",seat_wind=seat,dealer_streak=count)
            assert any(f.id=="dealer_streak" and f.value==2*count and f.unit=="分" for f in result.fans)


def test_updated_little_dragons_is_four_plus_two_dragon_fans():
    result=quote("123m 678s 55z",("k46","k47"))
    assert result.is_win and result.raw_fan==6 and result.fan==125
    assert {f.id:f.value for f in result.fans if f.unit=="番"}=={
        "little_dragons":4,"dragon_46":1,"dragon_47":1,
    }
    assert "value_pair" not in result.fan_ids


def test_removed_middle_pung_points_can_make_a_hand_fall_below_minimum():
    # Missing suit: 15; closed ron: 10; one wait: 2 = 27, rounded to 25.
    # The old extra 3 for the concealed middle pung used to make this 30.
    result=quote("22245655m 123789p",winning_tile=15)
    assert not result.is_win and result.fan==25 and result.raw_fan==1
    assert not any(f.name=="中张暗刻" for f in result.fans)
    with_repeat=quote("22245655m 123789p",winning_tile=15,dealer_streak=1)
    assert with_repeat.is_win and with_repeat.fan==30


def test_document_head_bump_payment_examples():
    assert remix_payments(60,1,discarder=0,head_bumped=(2,))==[-150,180,0,-30]
    assert remix_payments(60,1,discarder=0,head_bumped=(2,3))==[-180,180,0,0]
    assert remix_payments(60,1,discarder=0)==[-120,180,-30,-30]


def test_multiple_ron_adds_independent_payments_including_payments_between_winners():
    first=remix_payments(60,1,discarder=0)
    second=remix_payments(100,2,discarder=0)
    third=remix_payments(150,3,discarder=0)
    assert [a+b for a,b in zip(first,second)]==[-360,150,270,-60]
    assert [a+b+c for a,b,c in zip(first,second,third)]==[-750,120,240,390]


@pytest.mark.parametrize("kind",["twelve","limit"])
@pytest.mark.parametrize("liable",[0,2,3])
def test_head_bump_exemption_only_changes_ordinary_bystander_payments(kind,liable):
    normal=remix_payments(60,1,discarder=0,liability=liable,liability_kind=kind)
    assert remix_payments(60,1,discarder=0,liability=liable,liability_kind=kind,head_bumped=(2,))==normal


@pytest.mark.parametrize("bumped,discarder",[
    ((2,),None),((0,),0),((1,),0),((4,),0),((-1,),0),((2,2),0),((True,),0),(("2",),0),
])
def test_invalid_head_bumped_payment_participants_are_rejected(bumped,discarder):
    with pytest.raises(ValueError,match="head-bumped"):
        remix_payments(60,1,discarder=discarder,head_bumped=bumped)
