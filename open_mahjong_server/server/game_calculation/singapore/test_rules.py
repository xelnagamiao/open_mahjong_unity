from collections import Counter
import pytest

from . import rules as r

PLAIN = (11, 12, 13, 21, 22, 23, 34, 35, 36, 16, 17, 18, 25, 25)
PUNGS = (11, 11, 11, 22, 22, 22, 33, 33, 33, 45, 45, 45, 26, 26)


def test_fixed_physical_supply():
    assert len(r.WALL) == 148
    counts = Counter(r.WALL)
    assert all(counts[t] == 4 for t in r.TILES)
    assert all(counts[t] == 1 for t in r.BONUS_TILES)


@pytest.mark.parametrize('flowers,seat,expected', [([],0,0),([51],0,1),([51],1,0),
    ([51,52,53,54],0,2),([55,56,57,58],3,2),([51,55],0,2),([61,62,63],1,3),([61,62,63,64],0,5)])
def test_bonus_exclusions(flowers,seat,expected):
    assert sum(v for _,v in r.bonus_fans(flowers,seat)) == expected


@pytest.mark.parametrize('self_draw', [False, True])
def test_minimum_and_self_draw_does_not_add_fan(self_draw):
    hand = (11,12,13,24,25,26,34,35,36,17,17,17,28,28)
    assert r.score(hand,winning_tile=28,self_draw=self_draw) is None
    assert r.score(hand,flowers=(61,),winning_tile=28,self_draw=self_draw)['tai'] == 1


@pytest.mark.parametrize('hand,melds,tile,self_draw,name', [
    (PUNGS,(),26,True,'四暗刻'),
    ((41,41,41,42,42,42,45,45,45,46,46,46,47,47),(),47,False,'字一色'),
    ((11,11,11,19,19,19,21,21,21,29,29,29,39,39),(),39,False,'清幺九'),
    ((11,11,11,12,13,14,15,16,17,18,19,19,19,15),(),15,False,'九莲宝灯'),
    ((11,19,21,29,31,39,41,42,43,44,45,46,47,47),(),47,False,'十三幺'),
    ((26,26),('G11','g22','g33','g45'),26,True,'四杠'),
    ((45,45,45,46,46,46,47,47,47,11,12,13,25,25),(),25,False,'大三元'),
    ((41,41,41,42,42,42,43,43,43,44,44,44,25,25),(),25,False,'大四喜')])
def test_all_limit_hand_families(hand,melds,tile,self_draw,name):
    value = r.score(hand,melds,winning_tile=tile,self_draw=self_draw)
    assert value and value['tai']==5 and value['limit'] and name in value['fan_names'][0]


def test_corrected_flush_pinghu_is_five_not_eighty_five():
    value=r.score((11,12,13,12,13,14,15,16,17,17,18,19,15,15),winning_tile=15,self_draw=True)
    assert value['tai']==5 and value['raw_tai']==5 and '清一色平胡' in value['fan_names'][0]


def test_pinhu_wait_and_special_payment():
    assert r.score(PLAIN,winning_tile=25) is None  # pair only
    value=r.score(PLAIN,winning_tile=36)
    assert value['tai']==4 and value['pinghu']
    assert r.payments(0,value,payer=1)=={0:40,1:-20,2:-10,3:-10}
    assert r.payments(0,value)=={0:60,1:-20,2:-20,3:-20}
    assert r.score(PLAIN,winning_tile=35) is None  # closed wait
    assert r.score(PLAIN,winning_tile=13) is None  # edge wait
    assert r.score(PLAIN,winning_tile=25,self_draw=True)['tai']==4
    assert r.score(PLAIN,flowers=(52,),winning_tile=36)['tai']==1


@pytest.mark.parametrize('pair',[41,45,46,47])
def test_pinhu_pair_rejects_value_honors(pair):
    assert r.score(PLAIN[:-2]+(pair,pair),winning_tile=36,self_draw=True) is None


def test_ordinary_hand_families():
    value=r.score(PUNGS,winning_tile=26)
    assert value['tai']==3 and dict(value['fan_items'])=={'三元刻':1,'碰碰胡':2}
    half=r.score((11,12,13,11,12,13,14,15,16,45,45,45,18,18),winning_tile=18)
    assert dict(half['fan_items'])=={'三元刻':1,'混一色':2}
    small=r.score((45,45,45,46,46,46,47,47,11,12,13,21,22,23),winning_tile=23)
    assert dict(small['fan_items'])=={'小三元':3}
    green=r.score((32,33,34,32,33,34,36,36,36,47,47,47,38,38),winning_tile=38)
    assert green['tai']==5 and dict(green['fan_items'])=={'三元刻':1,'绿一色':4}
    little=r.score((41,41,41,42,42,42,43,43,43,44,44,11,12,13),winning_tile=13)
    assert dict(little['fan_items'])=={'小四喜':4,'混一色':2} and little['tai']==5
    mixed=r.score((11,11,11,19,19,19,41,41,41,45,45,45,29,29),winning_tile=29,seat=1,round_wind=43)
    assert dict(mixed['fan_items'])=={'三元刻':1,'碰碰胡':2,'混幺九':2}


@pytest.mark.parametrize('flag,name', [('heavenly','天胡'),('earthly','地胡')])
def test_first_turn_limits_require_valid_complete_hand(flag,name):
    assert r.score(PLAIN,winning_tile=25,**{flag:True})['fan_names']==[f'{name}（5台）']
    assert r.score(PLAIN[:-1],**{flag:True}) is None


def test_replacement_chain_and_last_tile_add_once():
    hand=(11,12,13,24,25,26,34,35,36,17,17,17,28,28)
    assert r.score(hand,winning_tile=28,self_draw=True,replacement=True)['tai']==1
    value=r.score(hand,winning_tile=28,self_draw=True,replacement=True,kong_chain=2,last_tile=True)
    assert dict(value['fan_items'])=={'杠杠和':3,'海底':1}


@pytest.mark.parametrize('tai',range(1,6))
@pytest.mark.parametrize('winner',range(4))
def test_payment_matrix_and_liability_zero_sum(tai,winner):
    value=r.detail([('台',tai)])
    for payer in [None]+[i for i in range(4) if i!=winner]:
        result=r.payments(winner,value,payer=payer)
        assert sum(result.values())==0
        assert result[winner]==2**tai*(3 if payer is None else 2)
        liable=(winner+1)%4
        bao=r.payments(winner,value,payer=payer,liable=liable)
        assert bao[winner]==result[winner] and bao[liable]==-result[winner]
        assert sum(v!=0 for v in bao.values())==2
    orphans=r.detail([('十三幺',5)],orphans=True)
    assert r.payments(winner,orphans,payer=(winner+1)%4)[winner]==96


@pytest.mark.parametrize('hand,melds,flowers',[(PLAIN+(25,),(),()),(PLAIN[:-1]+(61,),(),()),
    (PLAIN,(),(61,61)),(PLAIN,(),(59,)),((11,)*14,(),()),((11,11),('s11','s22','s32','s15'),())])
def test_invalid_physical_tiles_cannot_score(hand,melds,flowers):
    assert r.score(hand,melds,flowers) is None


def test_seven_pairs_is_not_an_invented_singapore_hand():
    assert r.score((11,11,19,19,21,21,29,29,31,31,39,39,45,45)) is None
    assert 47 in r.structural_waits((11,19,21,29,31,39,41,42,43,44,45,46,47))


@pytest.mark.parametrize('melds,tile,flowers,seat,wind',[
    (('s12','k16','s18'),15,(),0,42),(('k11','k19','k29'),39,(),0,42),
    (('k41','k42','k43'),44,(),0,42),(('k41','k45','k46'),47,(),0,42),
    (('k45','k46'),47,(),0,42),((),41,(61,62,63),0,41),((),45,(61,62,63,64),0,42)])
def test_all_seven_bao_triggers(melds,tile,flowers,seat,wind):
    assert r.liability_trigger(melds,flowers,tile,seat=seat,round_wind=wind)


def test_concealed_sets_do_not_trigger_public_bao_and_automatic_limits():
    assert not r.liability_trigger(('G41','G42','G43'),(),44,seat=1,round_wind=42)
    assert r.automatic_meld_win(('k45','k46','g47'))['automatic']
    assert r.automatic_meld_win(('k41','k42','k43','G44'))['tai']==5
    assert r.automatic_meld_win(('k45','k46')) is None


@pytest.mark.parametrize('context',[{'seat':-1},{'seat':4},{'round_wind':45}])
def test_scoring_rejects_invalid_seat_context(context):
    assert r.score(PLAIN,**context) is None


def test_pinghu_cannot_assume_missing_ron_tile():
    value=r.score(PLAIN,flowers=[61],winning_tile=None)
    assert value is not None and not value['pinghu'] and not any('平胡' in f for f in value['fan_names'])


@pytest.mark.parametrize('winner,payer,liable,tai',[(4,None,None,1),(0,0,None,1),(0,None,0,1),(0,None,None,0),(0,None,None,6)])
def test_payment_rejects_invalid_winner_or_cap(winner,payer,liable,tai):
    with pytest.raises(ValueError):r.payments(winner,{'tai':tai},payer=payer,liable=liable)
