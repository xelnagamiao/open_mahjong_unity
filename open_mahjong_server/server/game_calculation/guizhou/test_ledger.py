from itertools import product

import pytest

from .ledger import Chicken, Kong, settle


def players():
    return [dict(hand_tiles=[],combination_tiles=[],discard_tiles=[]) for _ in range(4)]


def category(result, name):
    totals = [0]*4
    for t in result.transfers:
        if t.category == name:
            totals[t.payer] -= t.points
            totals[t.payee] += t.points
    return totals


def win(ps, **kwargs):
    return settle(ps,winners=[0],source="self_draw",scores={0:3},ready_values=[3]*4,**kwargs)


def test_rulebook_responsibility_chicken_pung_example():
    ps = players()
    ps[0]["combination_tiles"] = ["k31"]
    result = win(ps,chickens=[Chicken(31,1,0)])
    assert category(result,"chicken") == [11,-5,-3,-3]


@pytest.mark.parametrize("kind,code,supplier,expected", [
    ("concealed","G31",None,[9,-3,-3,-3]),
    ("added","g31",None,[9,-3,-3,-3]),
    ("direct","g31",1,[3,-3,0,0]),
])
def test_rulebook_kong_examples(kind,code,supplier,expected):
    ps = players()
    ps[0]["combination_tiles"] = [code]
    chickens = [] if kind == "concealed" else [Chicken(31,1,0)]
    result = win(ps,chickens=chickens,kongs=[Kong(0,31,kind,supplier)])
    assert category(result,"kong") == expected
    assert category(result,"chicken") == ([12,-4,-4,-4] if kind == "concealed" else [14,-6,-4,-4])


def test_golden_chicken_doubles_that_type_and_not_other_normal_type():
    ps = players()
    ps[0]["combination_tiles"] = ["k31"]
    ps[0]["hand_tiles"] = [28]
    result = win(ps,indicator=39,chickens=[Chicken(31,1,0)])
    assert result.chicken_tile == 31
    assert category(result,"chicken") == [25,-11,-7,-7]


@pytest.mark.parametrize("indicator,expected", [(11,12),(19,11),(29,21),(39,31),(None,None)])
def test_indicator_next_rank_wrap(indicator,expected):
    assert win(players(),indicator=indicator).chicken_tile == expected


def test_charge_chicken_three_points_and_first_ron_exception():
    ps = players()
    ps[1]["discard_tiles"] = [31]
    assert category(win(ps,chickens=[Chicken(31,1)]),"chicken") == [-3,9,-3,-3]
    ps[1]["discard_tiles"] = []
    ps[0]["won_tiles"] = [31]
    result = settle(ps,winners=[0],source="discard",payer=1,scores={0:3},ready_values=[3]*4,chickens=[Chicken(31,1,won=True)])
    assert category(result,"chicken") == [3,-1,-1,-1]


def test_not_ready_pays_public_chickens_and_kongs_but_not_concealed_chickens():
    ps = players()
    ps[1].update(hand_tiles=[31,28],discard_tiles=[31],combination_tiles=["G22"])
    result = settle(ps,winners=[0],source="discard",payer=2,scores={0:8},ready_values=[0,0,3,0],kongs=[Kong(1,22,"concealed")])
    assert category(result,"chicken") == [1,-2,1,0]
    assert category(result,"kong") == [3,-6,3,0]


@pytest.mark.parametrize("ready", [False,True])
@pytest.mark.parametrize("source,hot", [("rob_kong",False),("discard",True)])
def test_robbed_or_hot_payer_cannot_collect_but_unready_still_reverses(ready,source,hot):
    ps=players()
    ps[1].update(hand_tiles=[28],discard_tiles=[31])
    result=settle(ps,winners=[0],source=source,payer=1,scores={0:3},ready_values=[3,3 if ready else 0,3,3],
                  kongs=[Kong(1,22,"concealed")],hot=hot)
    assert all(t.payee != 1 for t in result.transfers)
    assert category(result,"chicken") == ([0,0,0,0] if ready else [1,-3,1,1])
    assert category(result,"kong") == ([0,0,0,0] if ready else [3,-9,3,3])
    assert category(result,"hand") == ([9,-9,0,0] if source=="rob_kong" else [3,-3,0,0])


def test_hanbao_has_no_kong_points():
    assert category(win(players(),kongs=[Kong(1,22,"concealed",hanbao=True)]),"kong") == [0]*4


def test_unready_direct_kong_pays_each_ready_opponent_under_ix2():
    # IX.2 says "向和牌者及听牌者支付相同的分数". The supplier-only
    # restriction describes positive direct-kong income, not the not-ready penalty.
    result = settle(players(), winners=[0], source="discard", payer=3, scores={0:8},
                    ready_values=[0,0,3,0], kongs=[Kong(1,22,"direct",3)])
    assert category(result,"kong") == [3,-6,3,0]


def test_multi_ron_payment_and_single_physical_winning_chicken():
    ps=players()
    ps[2]["won_tiles"]=[31]
    result=settle(ps,winners=[1,2],source="discard",payer=0,scores={1:8,2:13},ready_values=[0]*4)
    assert category(result,"hand") == [-21,8,13,0]
    assert category(result,"chicken") == [-1,-1,3,-1]
    assert sum(result.changes)==0


@pytest.mark.parametrize("flags", list(product((0,8),repeat=4)))
def test_all_draw_readiness_partitions_ignore_chicken_and_kong(flags):
    ps=players()
    ps[1]["hand_tiles"]=[31]
    result=settle(ps,ready_values=flags,kongs=[Kong(1,22,"concealed")],chickens=[Chicken(31,0)],indicator=39)
    assert not any(t.category != "ready" for t in result.transfers)
    assert sum(result.changes)==0
    for i, value in enumerate(flags):
        assert result.changes[i] == (8*flags.count(0) if value else -sum(flags))


def test_invalid_winner_and_score_rejected():
    with pytest.raises(ValueError):
        settle(players(),winners=[0],source="discard",payer=0,scores={0:3})
    with pytest.raises(ValueError):
        settle(players(),winners=[0],source="self_draw",scores={0:40})
