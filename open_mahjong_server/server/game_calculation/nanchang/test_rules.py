from itertools import combinations
import pytest
from . import rules as r

NORMAL = [11,12,13,14,15,16,22,23,24,31,32,33,41,41]


@pytest.mark.parametrize("tile,next_tile", [(19,11),(29,21),(39,31),(41,42),(42,43),(43,44),(44,41),(45,47),(47,46),(46,45),(11,12)])
def test_jing_cycles(tile,next_tile):
    assert r.secondary_jing(tile) == next_tile


@pytest.mark.parametrize("group",list(combinations(range(41,45),3))+[(45,46,47)])
def test_all_honor_chows(group):
    hand = NORMAL[:9]+list(group)+[41,41]
    assert r.is_complete(hand,indicator=39)


def test_jing_can_represent_fifth_logical_copy_and_ron_jing_is_literal():
    hand = [11,11,11,11,45,12,13,21,22,23,31,32,33,45]
    assert r.is_complete(hand,indicator=45)
    hand = NORMAL.copy()
    hand[11] = 47
    assert r.is_complete(hand,indicator=47)
    assert not r.is_complete(hand,indicator=47,self_draw=False,winning_tile=47)


def test_pdf_thirteen_lan_impossible_wildcard_example():
    hand = list(r.HONORS)+[11,13,33,37,23,27,17]
    assert not r.is_complete(hand,indicator=11)


def test_thirteen_lan_and_seven_stars():
    hand = list(r.HONORS)+[11,14,17,21,24,27,31]
    score = r.score_hand(hand,indicator=39,winning_tile=31)
    assert "七星十三烂" in score["fan_names"]
    assert "十三烂" not in score["fan_names"]
    assert not r.is_complete([41]*7+[11,14,17,21,24,27,31],indicator=39)


def test_seven_pairs_four_identical_count_as_two_pairs():
    hand = [11]*4+[12]*2+[21]*2+[22]*2+[31]*2+[32]*2
    score = r.score_hand(hand,indicator=45)
    assert "德国七对" in score["fan_names"]
    assert "小七对" not in score["fan_names"]


def test_jingdiao_standard_and_six_pairs():
    assert r.is_jingdiao(NORMAL[:12]+[45],indicator=45)
    assert r.is_jingdiao([11,11,12,12,21,21,22,22,31,31,32,32,45],indicator=45)
    assert not r.is_jingdiao(NORMAL[:-1],indicator=45)
    assert r.is_jingdiao([45],['k11','k21','k31','k41'],indicator=45)


def test_german_discard_and_zimo_payments():
    zimo = r.score_hand(NORMAL,indicator=45,winner=1,winning_tile=41)
    assert zimo["changes"] == [-13,31,-9,-9]
    ron = r.score_hand(NORMAL,indicator=45,winner=1,payer=2,self_draw=False,winning_tile=41)
    assert ron["changes"] == [-4,15,-9,-2]
    de = r.score_hand(NORMAL,indicator=45,winner=1,other_has_jing=False)
    assert de["changes"] == [-26,62,-18,-18]
    assert "德国" not in de["fan_names"]


def test_minimum_excludes_dealer_discard_and_self_draw():
    hand = NORMAL.copy()
    hand[11] = 47
    score = r.score_hand(hand,indicator=47,winner=0)
    assert score["qualifying_fan"] == 0
    assert r.score_hand(hand,indicator=47,after_kong=True)["qualifying_fan"] == 1
    assert r.score_hand(hand,indicator=47,discarded=[47,46])["qualifying_fan"] == 3


def test_heaven_and_earth_fixed_scores_ignore_all_other_fans():
    for flag in ("heavenly","earthly"):
        score = r.score_hand(NORMAL,indicator=45,discarded=[45]*3,**{flag:True})
        assert score["fixed"] == 20
        assert len(score["fan_names"]) == 1
        assert score["points"] == 60
    hand = NORMAL[:12]+[45,41]
    assert r.score_hand(hand,indicator=45,heavenly=True)["fixed"] == 40
    assert r.score_hand(hand,indicator=45,earthly=True,winning_tile=41)["fixed"] == 40


@pytest.mark.parametrize("fan",[1,2,5,6,10,20])
@pytest.mark.parametrize("winner,payer",[(0,None),(1,None),(1,0),(2,1),(0,1)])
def test_cap_and_zero_sum(fan,winner,payer):
    changes,rows = r.win_payments(winner,fan,payer=payer,german=5,jingdiao=payer is None)
    assert sum(changes) == 0
    assert all(row["fan"] <= 6 and row["points"] <= 74 for row in rows)


def test_pdf_chongguan_liability_examples():
    first = r.jing_payments([[11,11,12],[],[12],[]],indicator=11,
                            transfers=[dict(payer=1,receiver=0,tile=12)])
    assert first["charges"][0] == [0,20,5,5]
    second = r.jing_payments([[11,11,11,12],[],[12],[]],indicator=11,
                             transfers=[dict(payer=1,receiver=0,tile=11)])
    assert second["charges"][0] == [0,60,12,12]
    assert sum(first["changes"]) == sum(second["changes"]) == 0


def test_bawang_jing_and_disabled_do_not_create_false_bawang():
    assert r.jing_payments([[11],[],[],[]],indicator=11)["charges"][0] == [0,4,4,4]
    result = r.jing_payments([[11],[12],[],[]],indicator=11,disabled=[1])
    assert result["sole"] is None
    assert result["charges"][0] == [0,2,2,2]
    assert result["charges"][1] == [0]*4


@pytest.mark.parametrize("kind,unit",[("angang",2),("jiagang",1),("gang",1)])
def test_kongs_and_jing_kongs(kind,unit):
    assert r.kong_payments(1,kind,21,indicator=11) == [-unit,unit*3,-unit,-unit]
    assert r.kong_payments(1,kind,12,indicator=11) == [-10,30,-10,-10]


def test_structural_supply_and_extended_meld_codec():
    assert r.meld_tiles('s414244') == (41,42,44)
    assert r.valid_hand(NORMAL[:9]+[41,41],['s424344'])
    assert not r.valid_hand([11]*5+NORMAL[5:])
    assert not r.valid_hand(NORMAL+[51])
    assert r.normalize_config(None) == r.CONFIG
    with pytest.raises(ValueError):
        r.normalize_config({'double':True})
