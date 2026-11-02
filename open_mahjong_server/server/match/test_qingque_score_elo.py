import itertools
import random

import pytest

from .qingque_score_elo import ALGORITHM, score_elo_details


def players(scores=(800, 200, -200, -800)):
    return [dict(user_id=11000001+i, net_points=score, place=i+1) for i, score in enumerate(scores)]


def changes(ratings=(1500,)*4, scores=(800,200,-200,-800), n=16):
    return [p['elo_delta'] for p in score_elo_details(ratings, players(scores), n)]


def test_calibrated_scale_and_no_score_cap():
    assert changes() == [16,4,-4,-16]
    assert changes(scores=(1600,400,-400,-1600)) == [32,8,-8,-32]
    assert changes(scores=(16000,4000,-4000,-16000)) == [320,80,-80,-320]
    assert changes(scores=(300,0,-300,0), n=1) == [6.06,0,-6.06,0]
    assert changes(scores=(0,0,0,0)) == [0]*4


def test_opponent_expectation_and_legacy_protection_scale():
    assert changes(ratings=(2100,1500,1500,1500)) == [11.5,5.5,-2.5,-14.5]
    assert changes(ratings=(1000,)*4) == [19.77,7.77,-.23,-12.23]
    details = score_elo_details([1500]*4, players(), 16)
    assert details[0]['rating_k'] == 32
    assert details[0]['rating_average_points'] == 50
    assert details[0]['rating_algorithm'] == ALGORITHM


def test_score_evidence_is_not_multiplied_by_short_sessions_or_zero_draws():
    assert changes(scores=(800,0,-800,0), n=1)[0] < 16.16
    assert changes(scores=(800,0,-800,0), n=32)[0] < changes(scores=(800,0,-800,0), n=16)[0]
    assert changes(scores=(1600,0,-1600,0), n=32)[0] > changes(scores=(800,0,-800,0), n=16)[0]


def test_user_identity_permutation_zero_sum_and_place_independence():
    rng = random.Random(20261027)
    for _ in range(80):
        r = [rng.randint(1000,3000) for _ in range(4)]
        s = [rng.randint(-50000,50000) for _ in range(3)]
        p = players((*s,-sum(s)))
        n = rng.randint(1,3000)
        expected = score_elo_details(r,p,n)
        assert sum(d['rating_base_delta'] for d in expected) == pytest.approx(0)
        for order in itertools.permutations(range(4)):
            actual = score_elo_details([r[i] for i in order],[p[i] for i in order],n)
            assert actual == [expected[i] for i in order]
        for value in p:
            value['place'] = 4
        assert score_elo_details(r,p,n) == expected


@pytest.mark.parametrize('n', [0,-1,True,1.5,None])
def test_invalid_completed_hand_count(n):
    with pytest.raises(ValueError): score_elo_details([1500]*4,players(),n)


@pytest.mark.parametrize('scores', [(1,0,0,0),(float('nan'),0,0,0),(True,0,-1,0)])
def test_invalid_score_evidence(scores):
    with pytest.raises(ValueError): score_elo_details([1500]*4,players(scores),16)
