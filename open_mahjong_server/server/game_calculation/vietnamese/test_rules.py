from collections import Counter
import pytest
from .rules import *

NORMAL=(11,12,13,21,22,23,31,32,33,17,18,19,25,25)


def test_deck_and_scoped_jokers():
    assert len(PHYSICAL)==160 and len(FLOWERS)==16 and len(JOKERS)==7
    for joker,valid in ((ANY,TILES),(DOT,range(21,30)),(BAMBOO,range(31,40)),(CHARACTER,range(11,20)),(WIND,range(41,45)),(DRAGON,range(45,48)),(NUMBER,tuple(t for t in TILES if t<40))):
        assert {t for t in TILES if represents(joker,t)}==set(valid)
    assert not any(represents(BIG_FLOWER,t) for t in TILES)
    assert represents(BIG_FLOWER,45,discard_bonus=True)


def test_multiple_jokers_in_group_and_pair():
    assert shape((ANY,NUMBER,CHARACTER,21,22,23,31,32,33,41,41,41,DOT,25))
    assert not shape((WIND,NUMBER,CHARACTER,21,22,23,31,32,33,41,41,41,DOT,25))
    assert shape(NORMAL[:-1]+(DOT,))
    assert not shape(NORMAL[:-1]+(WIND,))


def test_bonus_can_only_substitute_as_winning_discard():
    assert score(NORMAL[:-1]+(BIG_FLOWER,),winning_tile=BIG_FLOWER) is not None
    assert score(NORMAL[:-1]+(BIG_FLOWER,),winning_tile=BIG_FLOWER,self_draw=True) is None


def test_physical_invalid_hand_rejected():
    assert score((11,)*14,winning_tile=11) is None
    assert score(NORMAL,flowers=(51,51)) is None


@pytest.mark.parametrize('phan',range(109))
def test_payment_table_and_zero_sum(phan):
    quote=Score(phan,())
    mun,extra=divmod(phan,6)
    expected_single=(mun*32+extra*6) if mun else max(1,2**phan//2)
    expected_double=(mun*64+extra*6) if mun else 2**phan
    assert payment_value(phan,False)==expected_single
    assert payment_value(phan,True)==expected_double
    for winner in range(4):
        for payer in range(4):
            if payer==winner:continue
            changes=payments(winner,quote,payer=payer)
            assert sum(changes)==0 and changes[payer]==-expected_double
            assert changes[winner]==expected_double+2*expected_single
        assert payments(winner,quote,self_draw=True)[winner]==expected_double*3


def test_rob_kong_increment_is_paid_only_by_robbed_player():
    quote=Score(6,(('rob_kong',1),))
    assert payments(0,quote,payer=1)==[128,-96,-16,-16]


@pytest.mark.parametrize('patterns,total',[
    ({'all_sets':3,'half_flush':3,'all_called':3},12),
    ({'full_flush':6,'all_sets':3,'all_called':3},18),
    ({'big_three_dragons':9,'all_sets':3},12),
    ({'big_three_dragons':9,'all_sets':3,'half_flush':3},18),
    ({'little_three_dragons':6,'all_sets':3},9),
    ({'little_three_dragons':6,'all_sets':3,'half_flush':3,'all_called':3},18),
    ({'full_flush':6,'quads':3},9),
])
def test_progressive_counting_book_examples(patterns,total):
    assert sum(progressive(patterns).values())==total


def test_classic_replacement_pair_and_sets():
    batch=[dict(tile=t,batch=1,opening=True) for t in (ANY,NUMBER)]
    assert bonus_score((),batch,(),1,Rules())=={'matching_any_number':9}
    batch[1]['batch']=2
    assert sum(bonus_score((),batch,(),1,Rules()).values())==6
    batch=[dict(tile=t,batch=1,opening=True) for t in (ANY,DOT,BAMBOO,CHARACTER)]
    assert bonus_score((),batch,(),0,Rules())=={'blue_joker_set':12}


def test_flowers_do_not_double_count_seat_bonus_in_bouquet():
    assert bonus_score((51,52,53,54),(),(),0,Rules())=={'bouquet_51':6}
    assert bonus_score((51,55,59,63),(),(),0,Rules(same_number_bouquet=True))=={'number_bouquet_0':12}


def test_large_hand_exclusions_and_no_joker_bonus():
    q=score((11,11,11,19,19,19,21,21,21,29,29,29,39,39),winning_tile=39,self_draw=True)
    names=dict(q.patterns)
    assert names['all_terminals']==24 and names['four_closed_sets']==18 and 'all_sets' not in names
    assert names['no_jokers_bonus']==6
    assert names['wait_closed_bonus']==6


def test_seven_pairs_is_optional_self_draw_only():
    hand=(11,11,14,14,17,17,22,22,25,25,28,28,31,31)
    assert score(hand,winning_tile=31) is None
    assert score(hand,winning_tile=31,self_draw=True) is None
    assert score(hand,winning_tile=31,self_draw=True,rules=Rules(seven_pairs=True)) is not None


@pytest.mark.parametrize('data',[{'seven_pairs':1},{'orphans_phan':6},{'additional_phan':4},{'unknown':True},{'rule_version':'old'}])
def test_invalid_room_rules(data):
    with pytest.raises(ValueError):Rules.from_dict(data)


def test_seven_pairs_composition_and_closed_pair_wait_stack_per_written_definitions():
    hand=tuple(t for t in range(41,48) for _ in range(2))
    q=score(hand,winning_tile=47,self_draw=True,rules=Rules(seven_pairs=True))
    names=dict(q.patterns)
    assert names['seven_pairs']==6 and names['all_honors']==24
    assert names['no_jokers_bonus']==6 and names['wait_closed_bonus']==6


def test_orphans_composition_and_closed_self_draw_follow_nonexclusive_book_patterns():
    q=score(ORPHANS+(11,),winning_tile=11,self_draw=True)
    names=dict(q.patterns)
    assert names['thirteen_orphans']==13 and names['terminals_honors']==6
    assert names['fully_closed']==1 and names['no_jokers_bonus']==6


@pytest.mark.parametrize('hand,melds,required,excluded',[
    ((11,12,13,21,22,23,45,45,45,46,46,46,47,47),(),{'little_three_dragons':6},()),
    ((11,12,13,45,45,45,46,46,46,47,47,47,25,25),(),{'big_three_dragons':9},('dragon_sets',)),
    ((11,11,11,12,13,14,15,16,17,18,19,19,19,15),(),{'nine_gates':18},('full_flush',)),
    ((45,45),('G41','G42','G43','G44'),{'big_four_winds':48,'four_closed_quads':30},('all_sets','quads','four_closed_sets','seat_wind','round_wind')),
])
def test_book_large_pattern_values_and_explicit_exclusions(hand,melds,required,excluded):
    q=score(hand,melds,winning_tile=hand[-1],self_draw=True)
    names=dict(q.patterns)
    for key,value in required.items():assert names[key]==value
    assert not set(excluded)&set(names)
