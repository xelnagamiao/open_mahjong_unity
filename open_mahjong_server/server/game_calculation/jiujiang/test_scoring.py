import itertools
import pytest

from .scoring import score, flower_details, exposed_flower_points, payments
from .patterns import structural_waits

PLAIN = [11,12,13,21,22,23,31,32,33,17,18,19,25,25]


@pytest.mark.parametrize('self_draw,single,big',itertools.product((False,True),repeat=3))
def test_menqing_tsumo_single_wait_each_double_only_for_big_hu(self_draw,single,big):
    hand = [11,12,13,14,15,16,17,18,19,12,13,14,25,25] if big else PLAIN
    result = score(hand,winning_tile=25 if single else 19,self_draw=self_draw)
    values = {f['name']:f['points'] for f in result['fan_details']}
    unit = 2 if big else 1
    assert values['门清'] == unit
    assert values.get('自摸',0) == (unit if self_draw else 0)
    assert values.get('独听',0) == (unit if single else 0)


def test_flower_alone_meets_minimum_and_unscored_structure_is_still_ready():
    hand=PLAIN[3:]; melds=['s12']
    assert score(hand,melds,winning_tile=19) is None
    assert structural_waits(tuple(sorted(t for i,t in enumerate(hand) if i!=8)),tuple(melds))
    result=score(hand,melds,[47],winning_tile=19)
    assert result['base_score']==1 and result['fan_names']==['发财（1分）']
    assert score(hand,melds,winning_tile=19,self_draw=True)['base_score']==1


@pytest.mark.parametrize('flags,expected',[
    ({'self_draw':True,'after_kong':True},'杠上开花'),
    ({'after_kong_discard':True},'杠上炮'),
    ({'rob_kong':True},'抢杠胡'),({'last_tile':True},'海底'),
    ({'heaven_earth':True},'天地胡'),({'declared_ready':True},'报听')])
def test_special_points(flags,expected):
    result=score(PLAIN,winning_tile=19,**flags)
    names={f['name']:f['points'] for f in result['fan_details']}
    assert names[expected] == (10 if expected in ('天地胡','报听') else 1)


def test_no_false_kong_bonus_and_specials_stack():
    assert '杠上开花（1分）' not in score(PLAIN,winning_tile=19,after_kong=True)['fan_names']
    result=score(PLAIN,winning_tile=19,self_draw=True,after_kong_discard=True,rob_kong=False)
    assert not any('杠' in x for x in result['fan_names'])
    result=score(PLAIN,winning_tile=19,after_kong_discard=True,rob_kong=True,last_tile=True,declared_ready=True)
    names={f['name'] for f in result['fan_details']}
    assert {'抢杠胡','海底','报听'}<=names and '杠上炮' not in names


@pytest.mark.parametrize('tile,kind,expected',[(45,'k',1),(46,'k',1),(45,'g',2),(45,'G',4),(11,'g',1),(11,'G',2)])
def test_arrow_and_kong_flowers_stack(tile,kind,expected):
    assert sum(v for _,v in flower_details([f'{kind}{tile}']))==expected


@pytest.mark.parametrize('self_draw,winning_tile,expected',[(False,45,1),(False,25,2),(True,45,2)])
def test_ron_arrow_is_exposed_only_when_completed_by_winning_tile(self_draw,winning_tile,expected):
    assert flower_details(sets=['k45'],self_draw=self_draw,winning_tile=winning_tile)==[('暗箭刻' if expected==2 else '明箭刻',expected)]


@pytest.mark.parametrize('count,expected',[(0,0),(1,1),(2,1),(3,1),(4,4)])
def test_replaced_fa_points(count,expected):
    assert exposed_flower_points(flowers=[47]*count)==expected


def test_rulebook_arrow_example_and_only_public_flowers_compensated():
    assert exposed_flower_points(['g45','k46'])==5
    assert exposed_flower_points(['G45','k46'])==1
    assert exposed_flower_points(['G45','G46'],[47]*4)==4
    assert sum(v for _,v in flower_details(['G45','G46'],flowers=[47]*4))==14


@pytest.mark.parametrize('draw,concealed',itertools.product((False,True),repeat=2))
def test_full_begging_includes_selfdraw_and_kongs(draw,concealed):
    melds=['G11','G22','G33','G45'] if concealed else ['s12','s22','s32','k45']
    result=score([25,25],melds,winning_tile=25,self_draw=draw)
    assert {'name':'全求人','points':1} in result['fan_details']


@pytest.mark.parametrize('flowers',[[47]*5,[51],[45]])
def test_invalid_flower_snapshot(flowers):
    assert score(PLAIN,flowers=flowers) is None


def test_invalid_hand_never_gets_special_win():
    assert score(PLAIN[:-1]+[47],heaven_earth=True) is None


def test_missing_winning_tile_does_not_invent_a_single_wait_bonus():
    result=score(PLAIN)
    assert result['base_score']==1 and not result['single_wait']
    assert not any(fan['name']=='独听' for fan in result['fan_details'])


@pytest.mark.parametrize('winner,dealer,self_draw,is_ready,is_declared',itertools.product(range(4),range(4),(False,True),(False,True),(False,True)))
def test_each_payment_and_dealer_doubling(winner,dealer,self_draw,is_ready,is_declared):
    payer=(winner+1)%4
    actual,rows=payments(winner,7,discarder=None if self_draw else payer,dealer=dealer,exposed_flowers=5,
        ready=range(4) if is_ready else (),declared=range(4) if is_declared else ())
    assert sum(actual.values())==0 and len(rows)==(3 if self_draw else 1)
    for row in rows:
        expected=(7+(0 if is_ready else 5)+(10 if is_declared else 0))*(2 if dealer in (winner,row['payer']) else 1)
        assert row['points']==expected and actual[row['payer']]==-expected
    assert actual[winner]==sum(r['points'] for r in rows)


def test_selfdraw_payers_have_independent_surcharges():
    changes,rows=payments(1,7,exposed_flowers=5,ready={0,2},declared={2,3})
    assert changes=={0:-14,1:53,2:-17,3:-22}
