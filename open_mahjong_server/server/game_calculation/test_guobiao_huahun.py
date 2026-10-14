from itertools import combinations
from types import SimpleNamespace
import pytest
from . import guobiao_huahun as rule, guobiao_four_jokers as shared
from .test_guobiao_four_jokers import BASE, SEVEN_FAN_CASES, HONOR_WAIT


@pytest.mark.parametrize('count', range(5))
@pytest.mark.parametrize('flowers', range(5))
@pytest.mark.parametrize('progressive', [False, True])
@pytest.mark.parametrize('bonus', ['add8', 'double', 'none'])
def test_settlement_and_gate(count, flowers, progressive, bonus):
    hand = BASE[:14-count] + list(rule.JOKERS[:count])
    way = ['自摸', '场风东', '自风南']
    base, _ = shared.hepai_check(rule.normalize(hand), [], way, rule.normalize([hand[-1]])[0])
    score, fans = rule.hepai_check(hand, [], way + ['花牌'] * flowers, hand[-1],
        no_joker_bonus=bonus, black_flower_progressive=progressive)
    subtotal = base + ((0,4,8,16,32)[flowers] if progressive else flowers)
    expected = subtotal if count or bonus == 'none' else subtotal + 8 if bonus == 'add8' else 2 * subtotal
    assert score == expected
    assert rule.qualifying_fan((score, fans)) == base
    assert rule.minimum(hand) == (8,8,16,32,64)[count]
    if bonus == 'none':
        assert not any(f.startswith('无癞子') for f in fans)


@pytest.mark.parametrize('case', SEVEN_FAN_CASES + [(HONOR_WAIT, ['s24'],45,'单钓将')])
@pytest.mark.parametrize('multi', [False,True])
def test_resolved_waits_reused(case,multi):
    hand,melds,win,_=case
    seasons=iter(rule.JOKERS)
    physical=[next(seasons) if t==59 else t for t in hand]
    a=rule.hepai_check(physical,melds,['点和','场风东','自风南'],win,multi)
    b=shared.hepai_check(hand,melds,['点和','场风东','自风南'],win,multi)
    assert a==b


@pytest.mark.parametrize('seasons', [c for n in range(1,5) for c in combinations(rule.JOKERS,n)])
def test_physical_waits(seasons):
    hand=BASE[:13-len(seasons)]+list(seasons)
    actual=rule.waiting_tiles(hand)
    expected=shared.waiting_tiles(rule.normalize(hand))
    if 59 in expected:
        expected.remove(59); expected.update(set(rule.JOKERS)-set(seasons))
    assert actual==expected
    assert not (actual & set(seasons))


@pytest.mark.parametrize('hand,melds', [([51,51]+BASE[:12],[]),(BASE[:13]+[59],[]),
    (BASE[:10]+[51],['G51']), (BASE[:8],['G51','G51'])])
def test_invalid_supply(hand,melds):
    assert not rule.can_win(hand,melds)
    assert rule.hepai_check(hand,melds,['自摸'],hand[-1])==(0,[])


def test_flower_kong_basic_shape_and_no_bonus():
    hand=[11,12,13,21,22,23,31,32,33,45,45]
    score,fans=rule.hepai_check(hand,['G51'],['自摸','杠上开花','花牌'],45)
    assert '花杠' in fans and score>=100
    assert not any(f.startswith('无癞子') for f in fans)
    assert '暗杠' not in fans and '无番和' not in fans
    assert rule.minimum(hand,['G51'])==64
    assert rule.waiting_tiles(hand[:-1],['G51'])=={45}
    assert not rule.can_win([11,11,22,22,33,33,44,44,45,45,46],['G51'])


def test_flower_bonus_does_not_unlock_seven_fan():
    hand,melds,win,_=SEVEN_FAN_CASES[0]
    hand=[51 if t==59 else t for t in hand]
    hint=rule.calculate_hint(hand,melds,['点和']+['花牌']*4,['自摸']+['花牌']*4,
                             win,joker_multi_wait=True)
    result=rule.hepai_check(hand,melds,['点和']+['花牌']*4,win,True)
    assert rule.qualifying_fan(result)==7 and result[0]==39


@pytest.mark.parametrize('bonus', ['add8', 'double', 'none'])
def test_room_and_hint_requests_preserve_reward_mode(bonus):
    from ..room.room_validators import GBRoomValidator
    from ..webapi.calc import FourJokersHintRequest
    room = GBRoomValidator(room_name='花混验证', game_round=1, round_timer=20,
        step_timer=5, sub_rule=rule.SUB_RULE, no_joker_bonus=bonus)
    hint = FourJokersHintRequest(hand_tiles=BASE, get_tile=45,
        sub_rule=rule.SUB_RULE, no_joker_bonus=bonus)
    assert room.no_joker_bonus == hint.no_joker_bonus == bonus


@pytest.mark.parametrize('bonus', ['add8', 'double', 'none'])
def test_reward_mode_does_not_change_no_joker_qualifying_fan(bonus):
    # An ordinary low-fan hand must stay below the eight-fan gate even when
    # settlement rewards and black flowers would bring its total above it.
    hand = [12,13,14,24,24,24,28,28]
    result = rule.hepai_check(hand, ['k34','s24'], ['点和','花牌'], 14,
        no_joker_bonus=bonus)
    base = shared.hepai_check(hand, ['k34','s24'], ['点和'], 14)[0]
    assert 0 < base < 8
    assert rule.qualifying_fan(result) == base
    hint = rule.calculate_hint(hand, ['k34','s24'], ['点和','花牌'], [], 14,
        no_joker_bonus=bonus)
    assert (hint['kind'] != 'wuyi') == (base >= 8)


@pytest.mark.parametrize('joker', rule.JOKERS)
@pytest.mark.parametrize('multi', [False, True])
@pytest.mark.parametrize('way', [['自摸'], ['自摸', '杠上开花'], ['自摸', 'last_deal']])
def test_winning_season_never_scores_last_copy(joker, multi, way):
    # The season completes 123s, but is not the physical fourth 3s.
    hand, melds = [21,22,45,45,joker], ['s12','s25','s32']
    base = rule.hepai_check(hand, melds, way, joker, multi)
    flagged = way + ['和绝张']
    assert base[0] > 0 and '和绝张' not in base[1]
    assert rule.hepai_check(hand, melds, flagged, joker, multi) == base
    assert flagged == way + ['和绝张']
    assert rule.calculate_hint(hand, melds, ['点和'], flagged, joker,
                               joker_multi_wait=multi) == rule.calculate_hint(
        hand, melds, ['点和'], way, joker, joker_multi_wait=multi)


@pytest.mark.parametrize('held_joker', [False, True])
@pytest.mark.parametrize('way', [['自摸'], ['点和']])
@pytest.mark.parametrize('bonus', ['add8', 'double', 'none'])
def test_natural_winning_tile_keeps_last_copy_with_retained_jokers(held_joker, way, bonus):
    hand, melds = [51 if held_joker else 21,22,45,45,23], ['s12','s25','s32']
    base = rule.hepai_check(hand, melds, way, 23, no_joker_bonus=bonus)
    result = rule.hepai_check(hand, melds, way + ['和绝张'], 23, no_joker_bonus=bonus)
    assert '和绝张' in result[1]
    assert rule.qualifying_fan(result) == rule.qualifying_fan(base) + 4
    assert result[0] == base[0] + (8 if not held_joker and bonus == 'double' else 4)


def test_ignored_season_last_copy_flag_reuses_base_score_cache():
    rule._base_score.cache_clear()
    hand, melds = [21,22,45,45,51], ['s12','s25','s32']
    base = rule.hepai_check(hand, melds, ['自摸'], 51)
    before = rule._base_score.cache_info()
    assert rule.hepai_check(hand, melds, ['自摸','和绝张'], 51) == base
    after = rule._base_score.cache_info()
    assert after.misses == before.misses
    assert after.hits == before.hits + 1


@pytest.mark.parametrize('slot', [False,True])
@pytest.mark.parametrize('tile', [11,51,52])
@pytest.mark.parametrize('claim', [False,True,None])
def test_draw_only_discard(slot,tile,claim):
    p=SimpleNamespace(hand_tiles=[11,51,52],has_draw_slot=slot,last_drawn_tile=52 if slot else None)
    assert rule.legal_cut(p,tile,claim)==(tile==11 or slot and tile==52 and claim is True)
    cut,kind=rule.timeout_cut(p,tile,claim)
    assert rule.legal_cut(p,cut,kind)
