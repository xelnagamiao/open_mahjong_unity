import pytest
from collections import Counter
from . import rules as R

HAND = [21,21,21,22,23,24,25,26,27,28,28,28,29,29]

def test_physical_supply():
    c=Counter(R.tile_pool())
    assert sum(c.values())==84
    assert all(c[t]==4 for t in R.PHYSICAL)
    assert all(c[t]==1 for t in R.BONUS)
    assert not any(t in c for t in (11,31))

@pytest.mark.parametrize('seat',[0,1,2])
def test_bonus_and_honor_table(seat):
    assert sum(i.fan for i in R.bonus_items(R.BONUS,seat))==16
    assert sum(i.fan for i in R.honor_items(R.WINDS+R.DRAGONS,seat))==6
    assert sum(i.fan for i in R.bonus_items([81,81],seat))==2

@pytest.mark.parametrize('bad',[{'multiple_max':1},{'seven_pairs':True},[],False])
def test_strict_options(bad):
    with pytest.raises(ValueError):R.normalize_config(bad)

@pytest.mark.parametrize('t',R.TILES)
def test_fly_pungs(t):
    for n in (0,1,2):
        m=R.Meld('triplet',(t,)*(3-n)+(81,)*n,(t,)*3)
        assert m.code()==f'k{t}'
    with pytest.raises(ValueError):R.Meld('triplet',(81,)*3,(t,)*3)
    with pytest.raises(ValueError):R.Meld('kong',(t,t,t,81),(t,)*4)

@pytest.mark.parametrize('claimed',[21,22,23])
def test_chow_encoding_independent_of_claimed_slot(claimed):
    logical=(claimed,)+tuple(t for t in (21,22,23) if t!=claimed)
    assert R.Meld('sequence',logical,logical).code()=='s22'
    with pytest.raises(ValueError):R.Meld('sequence',(claimed,81,81),logical)

def test_minimum_and_non_exponential_scoring():
    assert R.score_hand(HAND,bonuses=[52]) is None # pure suit 3, wrong flower 0
    q=R.score_hand(HAND,bonuses=[61,62])
    assert q.fan==5 and q.points==200 and not q.maximums
    assert R.score_hand(HAND,bonuses=[61,62,63]).points==220
    assert R.score_hand(HAND,bonuses=[]).points==300

def test_maximum_stacking_opt_in():
    hand=[41]*3+[42]*3+[43]*3+[44]*3+[45]*2
    one=R.score_hand(hand,self_draw=True)
    many=R.score_hand(hand,self_draw=True,multiple_max=True)
    assert one.points==300 and many.points==100+200*len(many.maximums)
    assert {'大四喜','四暗刻','字一色','无花'} <= set(one.maximums)

def test_starting_four_fly_is_only_automatic_exception():
    hand=[81]*4+[21,22,24,25,27,28,41,42,43]
    assert R.score_hand(hand,bonuses=[52],opening_four_fly=True).automatic
    assert R.score_hand(hand,bonuses=[52]) is None

def test_physical_limits_and_wild_waits():
    assert R.score_hand([21]*5+HAND[5:]) is None
    assert R.score_hand(HAND,bonuses=[61,61]) is None
    assert 29 in R.waits(tuple(HAND[:-1])) and 81 in R.waits(tuple(HAND[:-1]))
    h=HAND[:-2]+[81,81]
    assert R.score_hand(h,bonuses=[61,62]) is not None

def test_duplicate_honor_pungs_each_count():
    melds=[R.Meld('triplet',(41,41,81),(41,)*3)]
    assert R.side_fan([41,41,41],melds,[],0)==4

@pytest.mark.parametrize('payer,expected',[(None,[800,-400,-400]),(1,[600,-400,-200]),(2,[600,-200,-400])])
def test_standard_payments(payer,expected):
    q=R.Quote(5,200,(),())
    changes,ledger=R.payments(0,q,[0,0,0],[0,0,0],payer=payer)
    assert changes==expected and sum(changes)==0
    assert sum(x['points'] for x in ledger)==expected[0]

@pytest.mark.parametrize('sides,extra', [([0,4,5],100),([0,5,7],40),([0,4,4],0),([0,5,5],0)])
def test_loser_comparison(sides,extra):
    d,_=R.payments(0,R.Quote(5,200,(),()),sides,[0,0,0],payer=1)
    assert d==[600,-400-extra,-200+extra]
    d,_=R.payments(0,R.Quote(10,300,(),('max',)),sides,[0,0,0],payer=1)
    assert d==[900,-600,-300]

def test_fly_payments_and_liability():
    d,ledger=R.payments(0,R.Quote(5,200,(),()),[0,0,0],[1,0,3],payer=1)
    assert d==[550,-600,50] and sum(d)==0
    d,_=R.payments(0,R.Quote(5,200,(),()),[0,0,0],[0,0,0],payer=1,liable=2)
    assert d==[600,0,-600]

@pytest.mark.parametrize('context,item', [({'self_draw':True,'after_flower':True},'flower_win'),({'self_draw':True,'after_kong':True},'kong_win'),({'rob_kong':True},'rob_kong')])
def test_context_fan(context,item):
    q=R.score_hand(HAND,bonuses=[61,62],**context)
    assert item in {x.id for x in q.items} and q.fan==6

@pytest.mark.parametrize('context,maximum', [({'self_draw':True,'consecutive_kongs':2},'连杠和'),({'first_tile':True},'首张和'),({'last_tile':True},'末张和')])
def test_event_maximum(context,maximum):
    assert maximum in R.score_hand(HAND,bonuses=[52],**context).maximums

def test_shape_maximums_and_special_fan():
    q=R.score_hand([21]*3+list(range(22,29))+[29]*3+[25],bonuses=[52],self_draw=True)
    assert '九莲宝灯' in q.maximums
    assert R.score_hand([21]*3+list(range(22,29))+[29]*3+[25],bonuses=[52]) is None
    assert '十六花' in R.score_hand(HAND,bonuses=R.BONUS).maximums
    assert '四飞' in R.score_hand(HAND[:-4]+[81]*4,bonuses=[52]).maximums
    q=R.score_hand([41,41,42,42,43,43,44,44,45,45,46,46,47,47],bonuses=[52])
    assert 'seven_pairs' in {x.id for x in q.items}
    q=R.score_hand([45]*3+[46]*3+[47]*2+[21,22,23]+[24,25,26],bonuses=[52])
    assert 'small_dragons' in {x.id for x in q.items}
    q=R.score_hand([45]*3+[46]*3+[47]*3+[21,22,23]+[24,24],bonuses=[52])
    assert '大三元' in q.maximums
    q=R.score_hand([41]*3+[42]*3+[43]*3+[44]*2+[21,22,23],bonuses=[52])
    assert '小四喜' in q.maximums
    melds=[R.Meld('kong',(t,)*4,(t,)*4,concealed=True) for t in (21,22,23,24)]
    assert '四杠' in R.score_hand([25,25],melds,bonuses=[52]).maximums

@pytest.mark.parametrize('call', [lambda:R.Meld('pair',(21,21),(21,21)),lambda:R.Meld('sequence',(21,23,25),(21,23,25)),lambda:R.Meld('triplet',(21,21,21),(21,21,22)),lambda:R.Meld('triplet',(21,21,22),(21,21,21)),lambda:R.bonus_items([61,61],0),lambda:R.bonus_items([],3),lambda:R.transfer([0,0,0],0,0,5),lambda:R.payments(3,R.Quote(5,200,(),()),[0]*3,[0]*3)])
def test_invalid_domain_inputs(call):
    with pytest.raises(ValueError):call()
