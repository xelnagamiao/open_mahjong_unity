from collections import Counter
import random

import pytest

from . import rules as r

STANDARD = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
OPEN_ZERO = [11, 12, 45, 31, 32, 33, 41, 41, 41, 42, 42]


def quote(hand=STANDARD, melds=(), **kwargs):
    return r.score_hand(hand, melds, indicator=46, winning_tile=hand[-1], **kwargs)


@pytest.mark.parametrize('indicator,up,down', [(11,12,19),(19,11,18),(29,21,28),(39,31,38),
    (41,42,44),(42,43,41),(43,44,42),(44,41,43),(45,47,46),(47,46,45),(46,45,47)])
def test_hun_cycles(indicator, up, down):
    assert r.hun_tile(indicator) == up
    assert r.hun_tile(indicator, 'down') == down
    assert r.hun_tile(indicator, 'self') == indicator


@pytest.mark.parametrize('invalid', [{'unknown': True}, {'half_hun': 1}, {'hun_mode': 'next'},
    {'rule_version':'future'}, {'half_hun_threshold': 1}, {'fan_kong': 'true'}, []])
def test_config_rejects_unknown_or_coerced_values(invalid):
    with pytest.raises(ValueError):
        r.normalize_config(invalid)


def test_zero_fan_is_legal_and_half_hun_does_not_increase_it():
    for half in (False, True):
        q = quote(OPEN_ZERO, ('s22',), config={'half_hun':half})
        assert q['fan'] == q['raw_fan'] == 0
        assert q['points'] == 1 and q['substitutions'] == 1
        assert quote(OPEN_ZERO, ('s22',), source='discard') is None


def test_basic_fans_natural_hun_and_open_hand():
    q = quote()
    assert q['fan_values'] == {'门清':1, '素':1} and q['points'] == 4
    natural = STANDARD[:-2] + [45,45]
    assert quote(natural)['fan_values'] == {'门清':1, '素':1}
    assert quote(STANDARD[3:], ('s12',))['fan_values'] == {'素':1}
    assert quote(discarded_hun=True) is None


def test_seven_pairs_and_three_luxury_pairs():
    seven = [11,11,12,12,21,21,22,22,31,31,41,41,42,42]
    assert quote(seven)['fan_values'] == {'门清':1, '素':1, '七对':1}
    q = quote([11]*4+[21]*4+[31]*4+[42]*2)
    assert q['fan'] == 6 and q['fan_values']['豪华七对'] == 3
    assert quote([11]*4+[21]*4+[31]*4+[42]*2, config={'fan_all_pungs':True})['fan'] == 6


def test_pure_straight_and_all_pungs_variants():
    q = quote(list(range(11,20))+[12,12,12,15,15])
    assert q['fan_values']['清一色'] == q['fan_values']['一条龙'] == 1
    q = quote([11]*3+[21]*3+[31]*3+[41]*3+[42]*2, config={'fan_all_pungs':True})
    assert q['fan_values'] == {'门清':1, '素':1, '碰碰和':1}


def test_strict_five_wait_does_not_ignore_exhausted_fifth_copy():
    wait = [14,16,21,22,23,31,32,33,41,41,41,42,42]
    q = quote(wait+[15])
    assert q['fan_values']['捉五魁'] == 1
    phantom = [15,16,17,18,18,18,18,41,41,41,42,42,42]
    assert r.logical_waits(tuple(phantom), (), 45, ron=True) == (15,18)
    assert '捉五魁' not in quote(phantom+[15])['fan_values']


def test_discarded_hun_is_natural_and_excluded_from_three_hun_limit():
    wait = STANDARD[:-1]
    assert quote(wait+[45]) is not None
    assert quote(wait+[45], source='discard') is None
    wait = [11,12,13,21,22,23,31,32,33,41,41,45,45]
    q = quote(wait+[45], source='discard', config={'three_hun_self_draw':True})
    assert q is not None
    assert quote(wait+[42], source='discard', config={'three_hun_self_draw':True}) is not None
    wait[-3:] = [45,45,45]
    assert quote(wait+[42], source='discard', config={'three_hun_self_draw':True}) is None


def test_concealed_kong_retains_ron_and_open_kong_does_not():
    hand = [21,22,23,31,32,33,41,41,41,42,42]
    q = quote(hand, ('G11',), source='discard', config={'fan_kong':True})
    assert q['fan_values'] == {'门清':1, '素':1, '杠':1}
    assert quote(hand, ('g11',), source='discard') is None
    assert quote(hand, ('G45',)) is None


def test_half_hun_minimum_one_and_best_assignment():
    hand = [11,12,45,21,22,45,31,32,45,41,41,41,42,42]
    normal = quote(hand)
    half = quote(hand, config={'half_hun':True})
    assert normal['raw_fan'] >= 1
    assert half['fan'] == 1
    assert quote(STANDARD[:-2]+[45,45], config={'half_hun':True})['fan'] == 2


def test_state_fans_and_kong_count():
    q = quote(source='rob_kong', last_tile=True, earthly=True, tian_ting=True,
              dealer=True, repeat=2, config={'fan_dealer':True,'fan_repeat':True,'fan_zimo':True})
    assert q['fan_values'] == {'门清':1,'素':1,'海底捞月':1,'抢杠和':1,'地和':1,'天听':1,'庄':1,'连庄':2}
    q = quote(heavenly=True, after_kong=True, config={'fan_zimo':True})
    assert q['fan_values']['天和'] == 2 and q['fan_values']['杠上开花'] == 1 and q['fan_values']['自摸'] == 1


def test_all_waits_and_four_hun_shortcut():
    wait = [11,12,13,21,22,23,31,32,33,41,41,41,45]
    assert len(r.logical_waits(tuple(wait), (), 45)) == 34
    assert quote(wait+[42], config={'fan_all_waits':True})['fan_values']['混儿悠'] == 1
    hand = [45]*4+[11,14,17,21,24,27,31,34,37,41]
    assert quote(hand, config={'four_hun_win':True}, four_hun=True)['fan_values'] == {'混儿杠':2}
    assert quote(hand, four_hun=True) is None
    assert quote(hand, config={'four_hun_win':True}, four_hun=True, source='discard') is None


@pytest.mark.parametrize('winner', range(4))
def test_payment_conservation_and_all_pay(winner):
    payer = (winner+1)%4
    assert sum(r.payments(winner, 8)) == 0
    assert r.payments(winner, 8)[winner] == 24
    assert r.payments(winner, 8, payer=payer)[winner] == 8
    assert r.payments(winner, 8, payer=payer, all_pay=True)[payer] == -24
    assert r.payments(winner, 8, liability=payer)[payer] == -24


def test_physical_supply_hun_indicator_and_invalid_forms():
    assert not r.valid_hand([11]*5+STANDARD[5:], indicator=46)
    assert not r.valid_hand([46]*4+STANDARD[4:], indicator=46)
    assert quote([11,19,21,29,31,39,41,42,43,44,45,46,47,47]) is None
    assert quote(STANDARD, ('bogus',)) is None
    assert quote(STANDARD[:-1]+[True]) is None
    assert r.match_results([10,10,-5,-15]) == [dict(rank=1,score=10),dict(rank=1,score=10),dict(rank=3,score=-5),dict(rank=4,score=-15)]


def test_generated_shapes_survive_permutation_and_wild_substitution():
    rng = random.Random(20261022)
    groups = [*r.SEQUENCES, *((t,t,t) for t in r.TILES)]
    checked = 0
    for _ in range(200):
        hand = [t for group in rng.choices(groups,k=4) for t in group]+[rng.choice(r.TILES)]*2
        if max(Counter(hand).values())>4 or Counter(hand)[46]>3:
            continue
        if hand.count(45)<4:
            hand[rng.randrange(14)]=45
        if max(Counter(hand).values())>4:
            continue
        win = hand[-1]
        first = r.score_hand(hand, indicator=46, winning_tile=win)
        assert first is not None
        rng.shuffle(hand)
        second = r.score_hand(hand, indicator=46, winning_tile=win)
        assert first == second
        checked += 1
    assert checked > 100
