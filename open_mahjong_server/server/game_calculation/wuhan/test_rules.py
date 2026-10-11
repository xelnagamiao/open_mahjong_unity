from copy import deepcopy

import pytest

from . import rules as r


@pytest.mark.parametrize('indicator,joker,skins', [(11,12,(19,11,45)),(19,11,(18,19,45)),
    (29,21,(28,29,45)),(41,42,(46,41,45)),(44,47,(43,44,45)),
    (45,47,(43,44,45)),(47,46,(44,47,45)),(46,41,(47,46,45))])
def test_indicator(indicator,joker,skins):
    assert r.special_tiles(indicator) == (joker,skins)


@pytest.mark.parametrize('indicator',[None,True,0,10,20,48,'11'])
def test_invalid_indicator(indicator):
    with pytest.raises(ValueError): r.special_tiles(indicator)


@pytest.mark.parametrize('config',[{},None,{'rule_version':'mil2025'}])
def test_config(config):
    assert r.normalize_config(config) == {'rule_version':'mil2025'}


@pytest.mark.parametrize('config',[[],False,{'rule_version':'2024'},{'base':1}])
def test_invalid_config(config):
    with pytest.raises(ValueError): r.normalize_config(config)


@pytest.mark.parametrize('code', ['s11','s19','k48','p11','','s45','k1',None])
def test_invalid_meld(code):
    assert r.meld_tiles(code) == ()


def test_no_skin_in_hand_meld_or_joker_assignment():
    assert not r.shapes([11,12,14,21,22,23,31,32,33,25,25],['s26'],13)
    assert not r.shapes([11,12,13,21,22,23,31,32,33,25,25],['k38'],38)
    assert not r.shapes([11,12,13,21,22,23,31,32,33,25,25],['k39'],38)


def test_physical_maximum_and_size():
    assert not r.valid_hand([11]*5+[12]*6,['k25'],38)
    assert not r.valid_hand([11]*10,['k25'],38)
    assert not r.valid_hand([11]*10+[True],['k25'],38)


@pytest.mark.parametrize('hand,meld,kind',[
    ([12]*3+[15]*3+[18]*3+[22]*2,'k25','将一色'),
    ([41]*3+[42]*3+[43]*3+[44]*2,'k47','风一色')])
def test_structured_special_hand_also_counts_all_triplets(hand,meld,kind):
    detail=r.hand_detail(hand,[meld],38)
    assert kind in detail['fan_names'] and '碰碰和' in detail['fan_names']
    assert detail['shape_fan']==5 and detail['hard']


def test_invalid_wait_hand_cannot_advertise_a_win():
    assert not r.waits([11]*5+[12]*5,['s26'],38)


def test_small_pair_seed_and_hard():
    hand=[11,12,13,21,22,23,31,32,33,25,25]
    d=r.hand_detail(hand,['s26'],38)
    assert d and not d['big'] and d['hard'] and d['shape_fan']==1
    assert r.hand_detail(hand[:-2]+[24,24],['s26'],38) is None
    assert r.hand_detail(hand[:-1]+[39],['s26'],38)['shape_fan']==0
    assert r.hand_detail(hand[:-2]+[39,39],['s26'],38) is None
    assert r.hand_detail([11,12,13,14,15,16,34,35,36,22,22],['s26'],21,sea=True)['hard']


def test_two_jokers_need_big_even_when_pair_seeded():
    hand=[11,12,39,21,22,39,31,32,33,25,25]
    assert r.hand_detail(hand,['s26'],38) is None
    d=r.hand_detail(hand,['s26'],38,sea=True)
    assert d and d['fan_names']==['海底']
    assert r.hand_detail(hand,['s26'],38,replacement=True)['big']
    assert r.hand_detail(hand,['s26'],38,robbing=True)['big']


@pytest.mark.parametrize('hand,melds,names', [
    ([11]*3+[21]*3+[31]*3+[24]*2,['k26'],{'碰碰和','硬和'}),
    ([11,12,13,14,15,16,17,18,19,11,11],['s12'],{'清一色','硬和'}),
    ([12,15,18,22,25,28,32,35,38,12,15],['k22'],{'将一色','硬和'}),
    ([41,42,43,44,46,47,41,42,43,44,46],['k47'],18,)] )
def test_big_shapes(hand,melds,names):
    if names==18:
        d=r.hand_detail(hand,melds,18)
        assert d['gold'] and '风一色' in d['fan_names']
    else:
        d=r.hand_detail(hand,melds,46)
        assert set(d['fan_names'])==names and d['big']


def table(melds=('s12',), specials=()):
    return [dict(melds=list(melds),specials=list(specials),added=[]) for _ in range(4)]


def test_table_fan_and_units_explicit_entries_only():
    assert r.table_fan(['k11','s23','g31','G41'],[45,39,37,38],39)==10
    assert r.table_fan(['g31'],[],39,[31])==2
    assert r.kong_units(['k11','s23','g31','G41'],[45,39,37,38],39)==8


@pytest.mark.parametrize('fan,points', [(0,0),(5,0),(6,10),(7,20),(8,40),(9,60),(10,80),(11,80)])
def test_points(fan,points):
    assert r.point_value(fan)==points


def detail(fan=3,big=True,gold=False):
    return dict(shape_fan=fan,big=big,gold=gold,fan_names=['清一色'] if big else ['硬和'])


def test_minimum_is_each_opponent_and_ron_vs_tsumo():
    t=table()
    assert r.settlement(detail(),0,None,t,38)['changes']=={0:30,1:-10,2:-10,3:-10}
    assert r.settlement(detail(),0,1,t,38) is None
    t[2]['specials']=[45];t[3]['specials']=[45]
    d=r.settlement(detail(),0,1,t,38)
    assert d['fan_by_player']=={1:6,2:6,3:6} and d['total']==30


def test_dealer_counts_only_small():
    t=table(specials=[45])
    assert r.settlement(detail(1,False),0,None,t,38)['fan_by_player']=={1:7,2:7,3:7}
    assert r.settlement(detail(1,False),1,None,t,38)['fan_by_player']=={0:7,2:6,3:6}
    assert r.settlement(detail(1,True),1,None,t,38)['fan_by_player']=={0:6,2:6,3:6}


def test_gold_all_three_must_cap_and_no_multiplier_stacking():
    t=table(specials=[45]*3)
    d=r.settlement(detail(1),0,None,t,38)
    assert d['gold'] and d['payments']=={1:100,2:100,3:100}
    t[1]['specials'].pop()
    d=r.settlement(detail(1),0,None,t,38)
    assert not d['gold'] and d['payments']=={1:60,2:80,3:80}
    for indicator in [11,41]:
        t=table(specials=[45]*3)
        assert r.settlement(detail(1),0,None,t,indicator,previous_gold=0)['total']==600


def test_continuous_and_reverse_gold():
    t=table(specials=[45]*3)
    assert r.settlement(detail(1),0,None,t,38,previous_gold=0)['total']==600
    assert r.settlement(detail(1),0,None,t,38,previous_gold=2)['payments']=={1:100,2:200,3:100}


def test_unopened_penalty_and_three_suns():
    t=table(melds=(),specials=())
    t[0]['melds']=['g11','g21','g31']
    d=r.settlement(detail(4),0,None,t,38)
    assert d['total']==1200
    t[1]['specials']=[37]
    assert r.settlement(detail(4),0,None,t,38)['total']==500
    t[2]['melds']=['G41']
    assert r.settlement(detail(4),0,None,t,38)['total']==400


@pytest.mark.parametrize('liable',[(1,),(1,2),(1,2,3),(0,1,1)])
def test_liability_zero_sum(liable):
    d=r.settlement(detail(),0,None,table(),38,liabilities=liable)
    assert sum(d['changes'].values())==0 and d['changes'][0]==30
    assert set(d['payments'])==set(liable)-{0}


def test_context_fans_stack_first_three_then_one():
    d=r.hand_detail([11]*3+[12]*3+[15]*3+[18]*2,['k19'],38,replacement=True,sea=True,robbing=True)
    assert set(d['fan_names'])=={'碰碰和','清一色','杠上花','海底','抢杠','硬和'}
    assert d['shape_fan']==8


def test_waits_are_structural_including_unmet_pair_and_copy_safe():
    hand=[11,12,13,21,22,23,31,32,33,24]
    assert {24,39}<=r.waits(hand,['s26'],38)
    d=r.shapes(hand+[24],['s26'],38)
    d[0]['fan_names'].append('bad')
    assert all('bad' not in s['fan_names'] for s in r.shapes(hand+[24],['s26'],38))
