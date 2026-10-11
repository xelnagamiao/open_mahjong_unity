import pytest

from . import rules as b

READY = [11,12,13,21,22,23,31,32,33,45,45,45,19]


def score(hand=None, codes=(), win=19, **context):
    return b.score(hand or READY+[win], codes, winning_tile=win, **context)


@pytest.mark.parametrize('hand,win,allowed', [
    (READY+[19],19,True),
    ([12,13,14,22,23,24,32,33,34,15,15,15,26,26],26,False),
    ([11,12,13,22,23,24,31,32,33,35,36,37,29,29],29,False),
    ([11,12,13,21,22,23,24,25,26,45,45,45,19,19],19,False),
    ([11,12,13,13,14,15,16,17,18,19,19,19,12,12],12,True),
    ([11,12,13,21,22,23,31,32,33,35,36,37,45,45],45,True),
    ([12,12,13,13,14,14,22,22,24,24,26,26,28,28],28,True),
    ([12,12,12,12,14,14,22,22,24,24,26,26,28,28],28,True),
])
def test_win_gates_and_seven_pairs_exemption(hand, win, allowed):
    assert bool(score(hand, win=win)) is allowed


@pytest.mark.parametrize('hand,codes', [([11]*14,()), (READY,()), (READY+[19],('bad',)),
    ([45,45],('Ydragon',)*4), ([11,11],('k11', 'k21', 'k31','k45'))])
def test_invalid_shapes_and_fifth_copy(hand,codes):
    assert b.score(hand,codes,winning_tile=hand[-1]) is None


def test_dragon_group_only_self_draw_and_two_tiles_wait():
    hand=[11,12,13,21,22,23,31,32,33,19,19,45,46]
    assert b.waits(hand)=={47}
    assert score(hand+[47],win=47)['shape']=='dragon'
    assert '和中/发/白' in score(hand+[47],win=47)['fan_names']
    assert score(hand+[47],win=47,self_draw=False) is None
    detail=score([11,11,11,21,21,21,31,31,31,19,19,45,46,47],win=47)
    assert '飘和' in detail['fan_names'] and '和中/发/白' in detail['fan_names']


@pytest.mark.parametrize('field,name', [('after_kong','杠上开花'),('last_four','海底'),('long_hair','长毛'),('declared_ready','听牌')])
def test_context_fans(field,name):
    assert name in score(**{field:True})['fan_names']


@pytest.mark.parametrize('context,yes,no', [
    ({'self_draw':False},'点炮','抢杠和'),
    ({'self_draw':False,'robbing_kong':True},'抢杠和','点炮'),
    ({'self_draw':False,'kong_discard':True},'杠流','点炮'),
])
def test_ron_context_exclusions(context,yes,no):
    result=score(**context)
    assert yes in result['fan_names'] and no not in result['fan_names']


@pytest.mark.parametrize('meld,name,excluded', [
    ('k45','中/发/白刻','明杠'),('g45','中/发/白明杠','中/发/白刻'),
    ('G45','中/发/白暗杠','暗杠'),('g18','明杠','暗杠'),('G18','暗杠','明杠'),
    ('Ydragon','喜杠','中/发/白刻'),('Ywind','旋风杠','明杠'),
])
def test_meld_fans_and_exclusions(meld,name,excluded):
    detail=score([11,12,13,21,22,23,31,32,33,19,19],(meld,))
    assert name in detail['fan_names'] and excluded not in detail['fan_names']


def test_borrowed_kong_changes_scoring_without_changing_tile_count():
    hand=[11,12,13,21,22,23,31,32,33,19,19]
    detail=score(hand,('k45',),borrowed=(45,46,47))
    assert '中/发/白明杠' in detail['fan_names'] and '中/发/白刻' not in detail['fan_names']
    assert b.valid_hand(hand,('k45',),complete=True) is not None
    detail=score(hand,('k41',),borrowed=(41,42,43,44))
    assert '明杠' in detail['fan_names']


def test_dragon_concealed_kong_subsumes_all_lower_kong_and_pung_fans():
    detail=score([11,12,13,21,22,23,31,32,33,19,19],('G45',))
    assert '中/发/白暗杠' in detail['fan_names']
    assert not {'暗杠','明杠','中/发/白刻','中/发/白明杠'} & set(detail['fan_names'])


def test_pure_suit_still_needs_terminal_and_triplet_and_adds_four_fan():
    result=score([11,12,13,13,14,15,16,17,18,19,19,19,12,12],win=12)
    assert '清一色' in result['fan_names']
    assert not score([12,13,14,13,14,15,16,17,18,18,18,18,12,12],win=12)


def test_seven_pairs_excludes_single_and_keeps_four_identical():
    result=score([12]*4+[14]*2+[22]*2+[24]*2+[26]*2+[28]*2,win=28)
    assert result['fan_names'].count('四归一')==1
    assert '单吊/边张/夹和' not in result['fan_names']


def test_four_triplets_and_hand_one():
    d=score([19,19],('k11','k21','k31','k45'))
    assert '手把一' in d['fan_names'] and '飘和' in d['fan_names']
    assert '单吊/边张/夹和' not in d['fan_names']


def test_pdf_single_wait_examples():
    assert '单吊/边张/夹和' not in score([21,22,23,24,25,16,16,23],('k15','k39'),win=23)['fan_names']
    assert b.waits([22,22,22,24],('k15','k39','k16'))=={23,24}
    d=score([22,23,15,15,15,16,16,16,33,34,35,38,38,21],win=21)
    assert '单吊/边张/夹和' in d['fan_names']
    d=score([21,22,23,23,24,25,15,15,15,16,33,34,35,16],win=16)
    assert '单吊/边张/夹和' not in d['fan_names']


@pytest.mark.parametrize('winner',range(4))
@pytest.mark.parametrize('discarder', [None,0,1,2,3])
def test_payer_caps_and_conservation(winner,discarder):
    if winner==discarder:
        with pytest.raises(ValueError):b.payments(winner,score(),discarder=discarder)
        return
    changes,rows=b.payments(winner,score(long_hair=True,declared_ready=True),discarder=discarder,closed=(True,)*4)
    assert sum(changes.values())==0 and all(r['score']<=64 and r['fan']<=6 for r in rows)
    assert len(rows)==(3 if discarder is None else 1)
    if discarder is not None:assert all(v==0 for k,v in changes.items() if k not in (winner,discarder))


@pytest.mark.parametrize('closed,expected,excluded', [
    ((True,True,True,True),'闭大四','闭门'),
    ((False,True,True,True),'三家闭','闭门'),
    ((True,True,False,False),'闭门','闭大四')])
def test_closed_table_subsumption(closed,expected,excluded):
    _,rows=b.payments(0,score(),discarder=1,closed=closed)
    assert expected in rows[0]['fan_names'] and excluded not in rows[0]['fan_names']


@pytest.mark.parametrize('tile',b.TILES)
def test_egg_values(tile):
    assert b.egg_points(tile)==(10 if tile==31 else 5 if tile//10==2 else 0)


def test_uncapped_payments_differ_by_dealer_and_each_payers_closed_state():
    detail=dict(fan_names=[],shape='standard')
    changes,rows=b.payments(1,detail,closed=(False,False,False,False))
    assert changes=={0:-2,1:4,2:-1,3:-1}
    changes,rows=b.payments(1,detail,closed=(False,True,True,False))
    assert changes=={0:-4,1:10,2:-4,3:-2}
    assert next(row for row in rows if row['payer']==2)['fan_names']==['闭门','闭门']
    detail['shape']='seven_pairs'
    changes,_=b.payments(1,detail,closed=(False,True,True,False))
    assert changes=={0:-2,1:5,2:-2,3:-1}


@pytest.mark.parametrize('invalid',[True,51,'11',None])
def test_invalid_tile_types_never_become_wins_or_waits(invalid):
    assert not b.waits(READY[:-1]+[invalid])
    assert b.score(READY+[19],winning_tile=invalid) is None
