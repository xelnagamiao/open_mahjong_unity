"""Independent substitution oracle plus physical/scoring edge cases."""
from collections import Counter
from itertools import combinations_with_replacement
import random
from unittest.mock import patch

import pytest

from . import guobiao_four_jokers as rule
from .guobiao_hepai_check import Chinese_Hepai_Check
from .jokers import JokerPolicy, can_win

BASE = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45, 45]
HONOR_WAIT = [15,16,17,32,33,34,38,39,59,59,45]
SEVEN_FAN_CASES = [
    ([59,14,15,24,24,24,28,28], ['k34','s24'], 14, '嵌张'),
    ([34,59,36,19,19,19,21,21,21,27,27], ['s18'], 27, '单钓将'),
    ([37,38,59,46,46], ['s32','s22','s18'], 37, '边张'),
]


@pytest.mark.parametrize('multi', [False, True])
def test_all_exposed_needs_no_wait_flag_and_no_jokers_scores_like_standard(multi):
    hand, melds, way = [24,24], ['k32','k16','k36','k28'], ['点和']+['花牌']*3
    expected = (35, ['全双刻','全求人','双同刻*1','花牌*3'])
    assert rule.hepai_check(hand,melds,way,24,multi) == expected
    assert Chinese_Hepai_Check().hepai_check(hand,melds,way,24) == expected
    assert '全求人' not in rule.hepai_check(hand,melds,['自摸'],24,multi)[1]
    assert '全求人' not in rule.hepai_check(hand,['G32']+melds[1:],way,24,multi)[1]
    assert '全求人' in rule.hepai_check([59,24],melds,way,24,multi)[1]


def test_honor_single_wait_cannot_become_chicken_hand_by_dropping_wait_fan():
    assert rule.hepai_check(HONOR_WAIT,['s24'],['点和','花牌','花牌'],45) == (3,['单钓将','花牌*2'])
    assert rule.hepai_check(HONOR_WAIT,['s24'],['点和','花牌','花牌'],45,True) == (10,['无番和','花牌*2'])
    assert rule.calculate_hint(HONOR_WAIT,['s24'],['点和'],['自摸'],45,8)['kind']=='wuyi'
    assert rule.calculate_hint(HONOR_WAIT,['s24'],['点和'],['自摸'],45,8,True)['kind']=='dianhe'


@pytest.mark.parametrize('hand,melds,win,fan',SEVEN_FAN_CASES)
def test_resolved_single_wait_supplies_eighth_fan(hand,melds,win,fan):
    way=['点和','场风东','自风南']
    off=rule.hepai_check(hand,melds,way,win)
    on=rule.hepai_check(hand,melds,way,win,True)
    assert off[0]==8 and fan in off[1]
    assert on[0]==7 and fan not in on[1]
    assert rule.hepai_check(hand,melds,way+['和单张'],win,True)==on
    assert off[0]==oracle(hand,melds,way,win)
    assert on[0]==oracle(hand,melds,way,win,True)
    assert rule.calculate_hint(hand,melds,way,['自摸'],win,8)['kind']=='dianhe'
    assert rule.calculate_hint(hand,melds,way,['自摸'],win,8,True)['kind']!='dianhe'


def test_resolved_multiple_waits_do_not_gain_single_wait_from_one_decomposition():
    hand=[11,12,13,21,22,23,31,32,33,36,37,38,59,38]
    score,fans=rule.hepai_check(hand,[],['点和','和单张'],38)
    assert score == oracle(hand,[],['点和'],38)
    assert '单钓将' not in fans and '嵌张' not in fans and '边张' not in fans


def test_first_deal_exclusion_and_winning_joker_identity():
    hand=BASE[:-1]+[59]
    ordinary=rule.hepai_check(hand,[],['自摸'],59)
    excluded=rule.hepai_check(hand,[],['自摸'],59,allow_single_wait=False)
    assert ordinary[0]==excluded[0]+1 and '单钓将' in ordinary[1]
    assert rule.hepai_check(hand,[],['自摸'],59,True)==excluded


def oracle(hand, melds, way, win, joker_multi_wait=False):
    normal = [t for t in hand if t != 59]
    physical = Counter(normal)
    for m in melds:
        t = int(m[1:]); physical.update((t-1,t,t+1) if m[0]=='s' else [t] * (4 if m[0] in 'gG' else 3))
    best = 0
    checker = Chinese_Hepai_Check()
    for targets in combinations_with_replacement(rule.TILES, hand.count(59)):
        counts = physical + Counter(targets)
        if max(counts.values(), default=0) > 4:
            continue
        for winning in set(targets) if win == 59 else (win,):
            logical = normal + list(targets)
            flags = [w for w in way if w != '和单张']
            score, _ = checker.hepai_check(logical, list(melds), flags[:], winning)
            if score and not (joker_multi_wait and 59 in hand):
                before = logical[:]; before.remove(winning)
                # Independent exhaustive draw oracle; production decomposes
                # the wait once and never runs 34 fan evaluations here.
                counts = counts.copy(); counts[winning] -= 1
                waits = {t for t in rule.TILES if counts[t] < 4
                         and checker.hepai_check(before + [t], list(melds), ['点和'], t)[0]}
                if waits == {winning}:
                    flags.append('和单张')
                    score, _ = checker.hepai_check(logical, list(melds), flags, winning)
                counts[winning] += 1
            best = max(best, score)
    return best


@pytest.mark.parametrize('ghosts', range(5))
def test_zero_to_four_jokers_are_real_inventory(ghosts):
    hand = BASE[:-ghosts] + [59] * ghosts if ghosts else BASE[:]
    assert rule.can_win(hand)
    score, fans = rule.hepai_check(hand, [], ['自摸'], hand[-1])
    assert score > 0 and fans
    assert '花牌' not in fans
    assert hand[-1] in rule.waiting_tiles(hand[:-1])


@pytest.mark.parametrize('hand,melds', [
    (BASE[:9]+[41,41,59,45,45], []),
    ([11,11,12,12,13,13,14,14,15,15,16,16,59,59], []),
    ([11,19,21,29,31,39,41,42,43,44,45,46,59,59], []),
    ([11,14,17,22,25,28,33,36,39,41,42,43,59,59], []),
    ([11,14,17,22,25,28,33,36,39,41,41,41,59,59], []),
    ([11,14,17,22,25,28,33,36,59,45,59], ['k41']),
    ([11,11,11,12,12,12,13,13,59,59,45], ['G45']),
])
@pytest.mark.parametrize('draw', [False, True])
def test_exact_maximum_matches_independent_substitution(hand, melds, draw):
    # Ron a natural tile, retaining the same physical hand.
    win = hand[-1] if draw else next(t for t in hand if t != 59)
    way = ['自摸' if draw else '点和','场风东','自风南']
    expected = oracle(hand, melds, way, win)
    result = rule.hepai_check(hand, melds, way, win)
    assert result[0] == expected
    assert rule.can_win(hand, melds) == bool(expected)


def test_red_discard_cannot_win_and_red_draw_has_no_last_copy_bonus():
    hand = BASE[:-1] + [59]
    assert rule.hepai_check(hand, [], ['点和'], 59) == (0, [])
    assert rule.hepai_check(hand, [], ['自摸','和绝张'], 59) == rule.hepai_check(hand, [], ['自摸'], 59)


def test_waits_and_score_cache_are_owned_copies_and_preserve_inputs():
    hand = BASE[:-1]; melds = []; way = ['自摸']
    before = hand[:], melds[:], way[:]
    waiting = rule.waiting_tiles(hand, melds)
    assert waiting == {45, 59}
    waiting.clear()
    assert rule.waiting_tiles(hand, melds) == {45, 59}
    result = rule.hepai_check(hand+[59], melds, way, 59)
    result[1].append('污染')
    assert '污染' not in rule.hepai_check(hand+[59], melds, way, 59)[1]
    assert before == (hand, melds, way)


@pytest.mark.parametrize('hand,melds', [
    ([59]*5 + BASE[:9], []), (BASE[:13]+[51], []),
    ([11]*5 + BASE[5:], []), ([11,12,13,21,22,23,31,32,33,59,59], ['G59']),
    ([41,41,59,21,22,23,31,32,33,45,45], ['k41']),
])
def test_invalid_physical_or_logical_inventory(hand, melds):
    assert not rule.can_win(hand, melds)
    assert rule.hepai_check(hand, melds, ['自摸'], hand[-1]) == (0, [])


def test_exposed_four_copies_cannot_be_used_as_joker_pair():
    assert 41 not in rule.waiting_tiles([11,12,13,21,22,23,31,32,33,59], ['G41'])
    assert rule.policy_for(('G41',)).closed_limits == ((41, 0),)


def test_four_reds_are_not_an_automatic_win():
    assert not rule.can_win([11,14,18,22,26,29,33,37,41,45,59,59,59,59])


def test_random_one_joker_against_oracle():
    rng = random.Random(20261012)
    wall = list(rule.TILES) * 4
    for _ in range(60):
        hand = rng.sample(wall, 13) + [59]
        expected = oracle(hand, [], ['自摸'], 59)
        assert rule.can_win(hand) == bool(expected)
        assert rule.hepai_check(hand, [], ['自摸'], 59)[0] == expected


def test_shared_per_face_cap_and_red_domain_are_opt_in():
    hand = [11,12,13,21,22,23,31,32,33,41,41,41,45,59]
    assert not can_win(hand)
    assert can_win(hand, policy=rule.POLICY)
    assert not can_win(hand, policy=JokerPolicy(joker_tiles=(59,), physical_tiles=rule.PHYSICAL_TILES,
        physical_limits=((59,4),), closed_limits=((45,1),)))


def test_structural_hot_path_does_not_score():
    with patch.object(Chinese_Hepai_Check, 'hepai_check', side_effect=AssertionError('scoring on hot path')):
        assert rule.waiting_tiles([11,11,11,12,12,12,13,13,13,59,59,59,59])
