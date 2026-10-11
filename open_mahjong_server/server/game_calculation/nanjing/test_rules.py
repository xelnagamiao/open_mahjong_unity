import pytest

from .rules import FLOWERS, PHYSICAL, TILES, payments, score_hand, waiting_tiles

PLAIN = [11,12,13,24,25,26,37,38,39,41,41,41,22,22]
OPEN = [11,12,13,24,25,26,37,38,39,22,22]
LAST_COPY_WAIT = [11,12,13,21,22,23,37,38,39,41,41,41,25,25]


def quote(hand=PLAIN, melds=(), flowers=(51,), **kw):
    return score_hand(hand,melds,flowers,winning_tile=kw.pop("winning_tile",hand[-1]),**kw)


def test_physical_deck_and_mandatory_flowers():
    assert len(PHYSICAL)==144 and len(TILES)==31 and len(FLOWERS)==11
    assert quote(PLAIN[:-1]+[45]) is None
    assert quote(OPEN,["s14"]) is None
    assert quote([11]*14) is None
    assert quote(flowers=[51,51]) is None


def test_closed_and_direct_kong_keep_menqing_added_pung_does_not():
    assert "门清" in quote().names
    assert "门清" in quote(OPEN,["g41"]).names
    assert quote(OPEN,["g41"],pung_claimed=True) is None
    assert "门清" not in quote(OPEN,["g41"],flowers=[51,52,53,54],pung_claimed=True).names


def test_four_real_flowers_gate_and_accidentals_do_not_qualify():
    for flowers in ([],[51],[51,52,53]):
        assert quote(OPEN,["k41"],flowers,self_draw=True,after_flower=True) is None
    q=quote(OPEN,["k41"],[51,52,53,54])
    assert q.points==16 and q.qualified
    q=quote(OPEN,["k41"],[],ignore_minimum=True)
    assert q and not q.qualified and "无花果" in q.names


@pytest.mark.parametrize("name,hand,melds,options",[
    ("混一色",[11,12,13,14,15,16,17,18,19,41,41,41,12,12],[],{}),
    ("清一色",[11,12,13,14,15,16,17,18,19,11,11,11,12,12],[],{}),
    ("碰碰和",[11,11,11,22,22,22,33,33,33,41,41,41,12,12],[],{}),
    ("七对",[11,11,12,12,23,23,24,24,35,35,36,36,41,41],[],{}),
    ("龙七对",[11,11,11,11,23,23,24,24,35,35,36,36,41,41],[],{"winning_tile":11}),
    ("全求独钓",[22,22],["k11","k24","k37","G41"],{}),
    ("花开",PLAIN,[],{"self_draw":True,"after_flower":True}),
    ("杠开",PLAIN,[],{"self_draw":True,"after_kong":True,"after_flower":True}),
    ("抢杠",PLAIN,[],{"rob_kong":True}),
    ("压绝",LAST_COPY_WAIT,[],{"public_pungs":[22],"winning_tile":22}),
    ("无花果",PLAIN,[],{"flowers":[]}),
])
def test_pattern_definitions(name,hand,melds,options):
    assert name in quote(hand,melds,**options).names


def test_implied_patterns_are_excluded_and_120_cap():
    q=quote([11,11,11,11,12,12,13,13,14,14,15,15,16,16],flowers=[],winning_tile=11)
    assert q.names=={"龙七对","清一色","无花果"} and q.points==120
    assert "碰碰和" not in quote([22,22],["k11","k24","k37","G41"]).names
    assert "花开" not in quote(self_draw=True,after_flower=True,after_kong=True).names
    assert not any(x.name=="压档" for x in quote(LAST_COPY_WAIT,public_pungs=[22],winning_tile=22).flowers)


def test_dragon_seven_pairs_requires_winning_fourth_copy():
    hand=[11,11,11,11,23,23,24,24,35,35,36,36,41,41]
    assert "七对" in quote(hand,winning_tile=41).names
    assert "龙七对" not in quote(hand,winning_tile=41).names


def test_wait_shapes_and_narrow_wait():
    assert waiting_tiles(PLAIN[:-1])=={22}
    q=quote()
    assert any(x.name=="压档" for x in q.flowers)
    openwait=[11,12,13,24,25,26,37,38,41,41,41,22,22]
    assert {36,39}.issubset(waiting_tiles(openwait))
    assert not any(x.name=="压档" for x in quote(openwait+[39]).flowers)


def test_flower_values():
    q=quote([22,22],["k11","g24","G37","G41"],flowers=[45,46,47,51])
    assert {x.name:x.points for x in q.flowers}=={"花牌":4,"压档":1,"风牌刻":1,"明杠":1,"暗杠":4}


def test_payments_and_multiple_liability_split():
    assert payments(0,25)==[75,-25,-25,-25]
    assert payments(1,25,payer=0)==[-25,25,0,0]
    assert payments(1,25,payer=0,multiplier=3)==[-75,75,0,0]
    assert payments(0,25,responsible=[2])==[75,0,-75,0]
    assert payments(0,25,responsible=[1,2,3])==[75,-25,-25,-25]
    with pytest.raises(ValueError): payments(0,25,responsible=[0])


@pytest.mark.parametrize('hand,melds,pattern,flower',[
    ([18,19],['k11','k12','k14','k16'],'清一色',None),
    ([41,42],['k11','k12','k14','k16'],'混一色',None),
    ([28,29],['k11','k12','k14','k16'],None,'缺一门'),
])
def test_forced_contract_win_keeps_applicable_suit_and_flower_points(hand,melds,pattern,flower):
    q=score_hand(hand,melds,[51],forced_patterns=('全求独钓',),pung_claimed=True)
    assert '全求独钓' in q.names and '碰碰和' not in q.names
    if pattern:assert pattern in q.names
    if flower:assert flower in {x.name for x in q.flowers}


def test_forced_pure_suit_does_not_repeat_implied_suit_points():
    for hand in ([41,42],[28,29]):
        q=score_hand(hand,['k11','k12','k14','k16'],[51],forced_patterns=('全求独钓','清一色'))
        assert q.names=={'全求独钓','清一色'}
        assert {x.name for x in q.flowers}=={'花牌'}


def test_liability_remainders_follow_approved_payer_and_original_seat_policy():
    assert payments(0,86,payer=2,responsible=[1,2,3])==[86,-28,-30,-28]
    assert payments(0,67,responsible=[1,2],seat_order=[2,3,0,1])==[201,-100,-101,0]
    assert payments(0,85,payer=3,responsible=[1,2])==[85,-42,-42,-1]
