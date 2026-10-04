from collections import Counter

import pytest

from .models import HandContext, HongKongRules, PROFILES
from .solver import decompositions, structural_waits
from .old_style import score_old, old_payments
from .ledger import PullLedger
from .patterns import best_local_patterns
from .models import Fan
from .scoring import score_hand
from .modern13 import new13_payments
from .modern16 import _local_meld_patterns
from dataclasses import replace
import random

OLD = HongKongRules()
NEW13 = HongKongRules(PROFILES[1])
NEW16 = HongKongRules(PROFILES[2])


def tiles(notation):
    out = []
    for group in notation.split():
        suit = {"m": 1, "p": 2, "s": 3, "z": 4}[group[-1]]
        out.extend(suit * 10 + int(n) for n in group[:-1])
    return tuple(out)


def test_profile_shapes_and_physical_copy_limit():
    seven = tiles("1122m 3344p 5566s 77z")
    assert not decompositions(seven, (), OLD)
    assert any(s.kind == "seven_pairs" for s in decompositions(seven, (), NEW13))
    sixteen = tiles("123456m 123p 789s 11122z")
    assert decompositions(sixteen, (), NEW16)
    assert not decompositions(sixteen, (), OLD)
    assert not decompositions(tiles("11123456789m"), ("G11",), OLD)
    assert not structural_waits(tiles("1112345678m"), ("G11",), OLD)
    assert not decompositions(tiles("123456789m 11122z"), ("s19",), OLD)


def test_special_sixteen_shapes():
    for hand, expected in [
        ("11223344556677788m", "likugu"),
        ("147m 258p 369s 11234567z", "sixteen_unconnected"),
        ("19m 19p 19s 11234567z 234m", "orphans"),
    ]:
        assert expected in {s.kind for s in decompositions(tiles(hand), (), NEW16)}
    assert "sixteen_unconnected" not in {s.kind for s in decompositions(tiles("124m 258p 369s 11234567z"), (), NEW16)}


def test_old_small_dragons_adds_dragon_pungs_and_caps():
    ctx = HandContext(tiles("123456m 55566677z"), winning_tile=47)
    result = score_old(ctx, OLD)
    assert result.is_win
    assert {"little_dragons", "dragon_45", "dragon_46", "half_flush", "concealed"} <= set(result.fan_ids)
    assert result.fan == 9
    big = score_old(HandContext(tiles("123m 55566677711z"), winning_tile=41), OLD)
    assert big.fan == 10 and big.raw_fan > 10


def test_old_chows_allow_honor_pair_and_single_wait():
    ctx = HandContext(tiles("123456m 234678p 55z"), winning_tile=45)
    assert "all_chows" in score_old(ctx, OLD).fan_ids
    assert not score_old(ctx, OLD).is_win  # Only 平糊 + 门清: two fan.
    assert score_old(HandContext(ctx.hand, winning_tile=45, self_draw=True), OLD).is_win


def test_old_no_flower_mode_differs_from_zero_flowers():
    ctx = HandContext(tiles("123456789m 234p 55z"), winning_tile=45, self_draw=True)
    a, b = score_old(ctx, OLD), score_old(ctx, HongKongRules(flowers=True))
    assert "concealed" in a.fan_ids and "no_flowers" not in a.fan_ids
    assert "no_flowers" in b.fan_ids and "concealed" not in b.fan_ids
    with_flowers = score_old(HandContext(ctx.hand, winning_tile=45, self_draw=True, flowers=(51, 52, 53, 54)), HongKongRules(flowers=True))
    assert "flower_set_51" in with_flowers.fan_ids and "seat_flower_51" in with_flowers.fan_ids


def test_nine_gates_ron_requires_nine_wait_but_self_draw_does_not():
    hand = tiles("11123455678999m")
    assert "nine_gates" in score_old(HandContext(hand, winning_tile=15), OLD).fan_ids
    assert "nine_gates" not in score_old(HandContext(hand, winning_tile=11), OLD).fan_ids
    assert "nine_gates" in score_old(HandContext(hand, winning_tile=11, self_draw=True), OLD).fan_ids


def test_old_four_concealed_pungs_and_no_kong():
    hand = tiles("111333m 555p 77788s")
    assert "four_concealed_pungs" not in score_old(HandContext(hand, winning_tile=11), OLD).fan_ids
    assert "four_concealed_pungs" in score_old(HandContext(hand, winning_tile=38), OLD).fan_ids
    assert "four_concealed_pungs" in score_old(HandContext(hand, winning_tile=11, self_draw=True), OLD).fan_ids


@pytest.mark.parametrize("fan,ron", [(3,32),(4,64),(5,96),(6,128),(7,192),(8,256),(9,384),(10,512),(14,512)])
def test_old_payment_table_and_responsibilities(fan, ron):
    assert old_payments(fan, 0, discarder=1, liability=2) == [ron, -ron, 0, 0]
    assert old_payments(fan, 0) == [3*ron//2, -ron//2, -ron//2, -ron//2]
    assert old_payments(fan, 0, liability=2) == [3*ron//2, 0, -3*ron//2, 0]
    assert old_payments(fan, 0, liability=2, kong_draw=True) == old_payments(fan, 0)
    assert old_payments(fan, 0, discarder=1, liability=2, rob_kong=True) == [3*ron//2, -3*ron//2, 0, 0]


def test_pull_carry_grow_rounding_and_kick():
    ledger = PullLedger()
    assert ledger.apply_win({(0,1): 11}, winners=[0]) == [0]*4
    assert ledger.apply_win({(0,2): 7}, winners=[0]) == [0]*4
    assert ledger.debts[(0,1)].points == 11
    ledger.apply_win({(0,1): 5}, winners=[0])
    assert ledger.debts[(0,1)].points == 22  # ceil(16.5) + 5
    assert ledger.apply_win({(1,0): 9}, winners=[1]) == [18,-11,-7,0]
    assert ledger.snapshot() == [dict(creditor=1, debtor=0, points=9, mouths=1)]
    assert ledger.collect() == [-9,9,0,0]
    assert ledger.collect() == [0]*4


def test_three_mouths_cut_and_outside_win():
    ledger = PullLedger()
    ledger.apply_win({(0,1): 10}, winners=[0])
    with pytest.raises(ValueError):
        ledger.cut(1, 0)
    ledger.apply_win({(0,1): 10}, winners=[0])
    ledger.apply_win({(0,1): 10}, winners=[0])
    assert ledger.cut(1,0) == [48,-48,0,0]
    ledger.apply_win({(0,1): 13}, winners=[0])
    assert ledger.apply_win({(3,2): 8}, winners=[3]) == [13,-13,0,0]


def test_single_settlement_global_maximum_not_greedy():
    # One 10-point combination conflicts with two mutually compatible sixes.
    a, b, c = Fan("a", "A", 10, 0b0011), Fan("b", "B", 6, 0b0101), Fan("c", "C", 6, 0b1010)
    assert set(best_local_patterns([a,b,c])) == {b,c}
    # Two dragons may share one of their three groups (<50%).
    d, e = Fan("dragon", "清龙", 15, 0b00111), Fan("dragon", "清龙", 15, 0b11100)
    assert len(best_local_patterns([d,e])) == 2


@pytest.mark.parametrize("notation,winning,key,value",[
    ("11123456789995m",15,"nine_gates",40),
    ("19m 19p 19s 11234567z",41,"orphans",40),
    ("1122m 3344p 5566s 77z",47,"seven_pairs",7),
    ("111222333m 777p 88s",38,"linked_1",2),
    ("111222333444m 88s",38,"linked_1",40),
    ("222m 222p 222s 111z 88s",38,"same_pungs_2",2),
    ("123123m 789789p 88s",38,"double_identical",10),
    ("234234m 456p 789s 88p",28,"identical_chows",1),
    ("123456789m 777p 88s",38,"straight_1",3),
    ("234m 234p 234s 111z 88s",38,"three_meet_3",3),
    ("222555m 888p 222s 55s",35,"pungs_258",20),
    ("234s 666888s 666z 22s",32,"green",40),
])
def test_new13_source_fan_examples(notation,winning,key,value):
    score = score_hand(HandContext(tiles(notation),winning_tile=winning),NEW13)
    assert score.is_win
    assert {f.id:f.value for f in score.fans}[key] == value
    if key == "nine_gates":
        assert "full_flush" not in score.fan_ids
    if key == "pungs_258":
        assert "three_numbers" not in score.fan_ids


def test_new13_publisher_pung_first_example():
    # The publisher explicitly gives this ambiguous 4m ron example. It could
    # instead assign 4m to 345m and claim three concealed pungs, but must not.
    hand = tiles("344445m 55p 222555s")
    score = score_hand(HandContext(hand,winning_tile=14),NEW13)
    assert score.is_win and "concealed_pungs" not in score.fan_ids
    drawn = score_hand(HandContext(hand,winning_tile=14,self_draw=True),NEW13)
    assert "concealed_pungs" in drawn.fan_ids


def test_new13_payment_ratios_and_zero_fan_hand():
    assert new13_payments(0,0,discarder=1) == [4,-4,0,0]
    assert new13_payments(2,0,discarder=1,full_shoot=False) == [12,-6,-3,-3]
    assert new13_payments(2,0,discarder=1,full_shoot=False,nine_exposed=True) == [12,-12,0,0]
    assert new13_payments(2,0) == [18,-6,-6,-6]
    assert new13_payments(2,0,liability=3) == [18,0,0,-18]


@pytest.mark.parametrize("notation,winning,key,value",[
    ("111222333444555m 66p",26,"five_pure_pungs",240),
    ("111222333444555m 66p",26,"linked_pungs_5",240),
    ("11122233344455566m",16,"little_linked_pungs_6",480),
    ("123456789m 123p 789s 44z",44,"pure_straight",30),
    ("123m 456p 789s 234m 678p 44z",44,"mixed_straight",20),
    ("123123123123m 789s 44z",44,"identical_chows_4",360),
    ("123234345456567m 88p",28,"pure_steps_5",400),
    ("123345567789m 234p 44z",44,"pure_jumps_4",100),
    ("123m 234p 345s 456m 567p 44z",44,"mixed_steps_5",100),
    ("11122233344455z 666s",45,"big_winds",160),
    ("11122233355566677z",47,"all_honors",120),
    ("19m 19p 19s 11234567z 234m",41,"orphans",100),
    ("147m 258p 369s 11234567z",41,"unconnected",50),
    ("147m 147p 147s 11234567z",41,"unconnected_same",20),
    ("1122m 3344p 556677788s",37,"likugu",40),
])
def test_modern16_detailed_table_values(notation,winning,key,value):
    score = score_hand(HandContext(tiles(notation),winning_tile=winning,self_draw=True),NEW16)
    assert score.is_win,notation
    assert any(f.id==key and f.value==value for f in score.fans),[(f.id,f.value) for f in score.fans]


def test_modern16_closed_ready_and_self_draw_compound_exactly():
    hand = tiles("234567m 345p 678s 22244z")
    ordinary = score_hand(HandContext(hand,winning_tile=44,self_draw=True),NEW16)
    ready = score_hand(HandContext(hand,winning_tile=44,self_draw=True,ready="ordinary"),NEW16)
    assert ready.fan - ordinary.fan == 10  # 门清5 -> 门叮15; 门摸5 stays.
    assert {"closed_ready","concealed_self_draw"} <= set(ready.fan_ids)
    assert "concealed" not in ready.fan_ids and "self_draw" not in ready.fan_ids


def test_modern16_flower_sets_do_not_count_individual_flowers_again():
    ctx = HandContext(tiles("234567m 345p 678s 22244z"),winning_tile=44,flowers=(51,52,53,54,55))
    score = score_hand(ctx,NEW16)
    ids = set(score.fan_ids)
    assert {"flower_set_51","flower_55","seat_flower_55"} <= ids
    assert not ids & {"flower_51","seat_flower_51","flower_52","flower_53","flower_54"}


def test_modern16_likugu_double_counts_shared_awards_but_one_base():
    hand = tiles("11223344556677788m")
    result = score_hand(HandContext(hand,winning_tile=17,self_draw=True),NEW16)
    assert result.shape == "likugu_double"
    assert sum(f.id=="base" for f in result.fans) == 1
    assert any(f.id=="likugu_likugu" for f in result.fans)
    assert "likugu_concealed_self_draw" in result.fan_ids
    assert result.fan == sum(f.value for f in result.fans)


def test_flower_wins_do_not_require_a_normal_hand():
    old = score_hand(HandContext((),flowers=tuple(range(51,58)),flower_win="seven",self_draw=True),HongKongRules(flowers=True))
    assert old.is_win and old.fan==3 and "self_draw" not in old.fan_ids
    eight = score_hand(HandContext((),flowers=tuple(range(51,59)),flower_win="eight",self_draw=True),NEW16)
    assert eight.is_win and eight.fan==40
    assert not score_hand(HandContext((),flowers=(51,52),flower_win="eight"),NEW16).is_win


def test_modern16_big_chicken_and_duck_use_detailed_table():
    ctx = HandContext(tiles("345s 44z"), ("k17","s13","s26","s38"),35,flowers=(52,))
    chicken = score_hand(ctx,NEW16)
    duck = score_hand(replace(ctx,self_draw=True),NEW16)
    assert chicken.fan == 46, chicken.fans  # 5 base + 1 flower + 40 chicken.
    assert duck.fan == 27, duck.fans  # 5 base + 1 flower + 1 self draw + 20 duck.
    assert "big_chicken" not in score_hand(replace(ctx,rob_kong=True),NEW16).fan_ids


def test_modern16_open_straight_and_kong_only_concealed_definition():
    hand = tiles("456789m 234p 678s 55z")
    score = score_hand(HandContext(hand,("s12",),45,self_draw=True),NEW16)
    assert any(f.id=="pure_straight" and f.value==15 for f in score.fans)
    kongs = ("G11","g22","G33","G44","G47")
    result = score_hand(HandContext((45,45),kongs,45,self_draw=True),NEW16)
    assert result.is_win and "concealed" in result.fan_ids
    assert any(f.id=="kongs" and f.value==10 for f in result.fans)


def test_modern16_orphan_extra_meld_scores_actual_honor_or_kong_only():
    honor=score_hand(HandContext(tiles("19m 19p 19s 11234567z 555z"),winning_tile=41),NEW16)
    assert {'orphans','honor_45','dragon_45'} <= set(honor.fan_ids)
    assert 'five_categories' not in honor.fan_ids
    assert not any(f.id=='honor_41' for f in honor.fans)
    kong=score_hand(HandContext(tiles("19m 19p 19s 11234567z"),("G25",),41),NEW16)
    assert kong.is_win and any(f.id=='kongs' and f.value==2 for f in kong.fans)


def test_modern16_all_honors_does_not_stack_implied_whole_hand_patterns():
    result=score_hand(HandContext(tiles("11122233355566677z"),winning_tile=47),NEW16)
    assert result.is_win and 'all_honors' in result.fan_ids
    assert not {'mixed_terminals','mixed_rank','mixed_outside'} & set(result.fan_ids)
    big=score_hand(HandContext(tiles("11122233344455z 666s"),winning_tile=45),NEW16)
    assert 'big_winds' in big.fan_ids
    assert not {'seat_wind','round_wind'} & set(big.fan_ids)


@pytest.mark.parametrize("rules",[OLD,NEW13,NEW16])
def test_invalid_tiles_melds_and_flower_data_are_not_wins(rules):
    hand = tiles("123456789m 11122z" if not rules.is_sixteen else "123456789m 123p 11122z")
    assert not score_hand(HandContext(hand,winning_tile=0),rules).is_win
    assert not score_hand(HandContext(hand+(11,),winning_tile=11),rules).is_win
    assert not score_hand(HandContext(hand,winning_tile=41,flowers=(51,51)),rules).is_win
    assert not decompositions(hand,("x44",),rules)
    assert not decompositions(hand,("sXX",),rules)
    assert not decompositions(hand,("g99",),rules)
    assert not structural_waits(hand[:-1],("G11",)*6,rules)
    assert not structural_waits(hand[:-2]+(50,),(),rules)


def test_profile_and_payment_validation():
    with pytest.raises(ValueError):
        HongKongRules("hongkong/unknown")
    with pytest.raises(ValueError):
        HongKongRules(PROFILES[1],flowers=True)
    assert HongKongRules.from_room({"sub_rule":PROFILES[2]}).flowers
    with pytest.raises(ValueError):
        old_payments(2,0)
    with pytest.raises(ValueError):
        old_payments(3,0,rob_kong=True)
    with pytest.raises(ValueError):
        old_payments(3,0,discarder=0)
    with pytest.raises(ValueError):
        new13_payments(-1,0)
    with pytest.raises(ValueError):
        new13_payments(1,0,discarder=0)
    ledger = PullLedger()
    with pytest.raises(ValueError):
        ledger.apply_win({},winners=[])
    with pytest.raises(ValueError):
        ledger.apply_win({(0,0):10},winners=[0])
    assert ledger.debts == {}


@pytest.mark.parametrize("rules",[OLD,NEW13,NEW16])
def test_generated_complete_hands_are_order_independent_and_waits_agree(rules):
    rng = random.Random(48016)
    seen = 0
    # Generated from the Mahjong definition, independently of the solver.
    while seen < 120:
        hand = [rng.choice(tuple(range(11,20))+tuple(range(21,30))+tuple(range(31,40))+tuple(range(41,48)))]*2
        for _ in range(rules.structure.meld_count):
            if rng.randrange(2):
                low = rng.choice((1,2,3))*10+rng.randrange(1,8)
                hand.extend([low,low+1,low+2])
            else:
                hand.extend([rng.choice(tuple(range(11,20))+tuple(range(21,30))+tuple(range(31,40))+tuple(range(41,48)))]*3)
        if max(Counter(hand).values()) > 4:
            continue
        rng.shuffle(hand)
        tile = hand[-1]
        assert decompositions(hand,(),rules,tile)
        assert tile in structural_waits(hand[:-1],(),rules)
        context = HandContext(hand,winning_tile=tile,self_draw=True)
        first = score_hand(context,rules)
        assert first.shape
        assert first.fan >= 0
        assert first.raw_fan == sum(f.value for f in first.fans)
        rng.shuffle(hand)
        assert score_hand(replace(context,hand=tuple(hand)),rules) == first
        seen += 1
