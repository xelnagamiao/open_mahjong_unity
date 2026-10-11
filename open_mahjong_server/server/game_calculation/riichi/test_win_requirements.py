"""Yaku-only minimum han and bonus-inclusive non-dealer ron thresholds."""
import pytest
from mahjong.hand_calculating.hand_config import OptionalRules, HandConfig

from .riichi_hepai_check import Riichi_Hepai_Check
from .win_requirements import meets_win_requirements, standard_ron_score


HAND = [22, 23, 24, 33, 34, 35, 36, 37, 38, 28, 28]


def calc(hand=HAND, **context):
    return Riichi_Hepai_Check().hepai_check(hand, ['s13'], [], 38,
        dict(player_wind=1, round_wind=0, has_open_tanyao=True, **context))


def test_dora_can_reach_mangan_but_cannot_supply_missing_yaku_han():
    result = calc(dora_indicators=[27, 27])
    assert (result['yaku_han'], result['han'], result['standard_ron_score']) == (1, 5, 8000)
    assert meets_win_requirements(result, 1, 8000)
    assert not meets_win_requirements(result, 2, 8000)
    assert not meets_win_requirements(result, 1, 12000)


def test_red_and_ura_bonus_do_not_satisfy_yaku_minimum():
    red = calc(hand=[305 if t == 35 else t for t in HAND], dora_indicators=[27])
    assert (red['yaku_han'], red['han']) == (1, 4)
    assert not meets_win_requirements(red, 3)
    closed = Riichi_Hepai_Check().hepai_check([12,13,14] + HAND, [], [], 38,
        dict(player_wind=1, is_riichi=True, ura_dora_indicators=[27]))
    assert (closed['yaku_han'], closed['han']) == (3, 5)
    assert meets_win_requirements(closed, 3, 8000)
    assert not meets_win_requirements(closed, 4)


@pytest.mark.parametrize('han,fu,score', [
    (1, 60, 2000), (2, 30, 2000), (3, 30, 3900), (3, 40, 5200),
    (3, 50, 6400), (4, 30, 7700), (5, 30, 8000), (6, 30, 12000),
    (8, 30, 16000), (11, 30, 24000), (13, 30, 32000),
])
def test_standard_ron_rounding_and_limit_tiers(han, fu, score):
    assert standard_ron_score(han, fu, OptionalRules()) == score


@pytest.mark.parametrize('dealer', [False, True])
@pytest.mark.parametrize('tsumo', [False, True])
def test_dealer_and_payment_method_share_the_same_line(dealer, tsumo):
    result = Riichi_Hepai_Check().hepai_check(HAND, ['s13'], [], 38,
        dict(player_wind=0 if dealer else 1, is_tsumo=tsumo,
             dora_indicators=[27, 27], tsumi_number=10, kyoutaku_number=8))
    assert result['standard_ron_score'] == 8000
    assert meets_win_requirements(result, 1, 8000)
    assert not meets_win_requirements(result, 1, 12000)


def test_sanma_north_bonus_only_affects_win_line():
    result = Riichi_Hepai_Check().hepai_check([22,23,24,33,34,35,36,37,38,28,28], ['k45'], [], 38,
        dict(is_sanma=True, player_wind=1, is_tsumo=True, nuki_count=4))
    assert (result['yaku_han'], result['han'], result['standard_ron_score']) == (1, 5, 8000)
    assert result['score'] == 6000
    assert meets_win_requirements(result, 1, 8000)
    assert not meets_win_requirements(result, 2, 8000)


def test_bonus_only_never_satisfies_either_gate():
    result = calc(hand=[22,23,24,33,34,35,37,38,39,28,28], dora_indicators=[27,27])
    assert result['no_yaku'] and result['yaku_han'] == result['standard_ron_score'] == 0
    assert not meets_win_requirements(result, 0, 0)


def test_kiriage_and_counted_yakuman_rules_apply_to_win_line():
    assert standard_ron_score(4, 30, OptionalRules(kiriage=True)) == 8000
    capped = OptionalRules(kazoe_limit=HandConfig.KAZOE_SANBAIMAN)
    assert standard_ron_score(13, 30, capped) == 24000
    assert standard_ron_score(13, 25, capped, is_yakuman=True) == 32000
    assert standard_ron_score(26, 25, capped, is_yakuman=True) == 64000


def test_true_yakuman_keeps_actual_yakuman_value_and_ignores_dora():
    hand = [11,19,21,29,31,39,41,42,43,44,45,46,47,47]
    result = Riichi_Hepai_Check().hepai_check(hand, [], [], 47,
        dict(player_wind=0, dora_indicators=[46], nuki_count=3, is_sanma=True))
    assert (result['han'], result['yaku_han'], result['standard_ron_score']) == (13, 13, 32000)
    assert meets_win_requirements(result, 3, 32000)


def test_cached_bonus_entries_are_excluded_when_new_result_fields_are_missing():
    result = dict(han=6, yaku=['立直', '宝牌*2', '赤宝牌*1', '里宝牌*1', '拔北宝牌*1'])
    assert meets_win_requirements(result, 1)
    assert not meets_win_requirements(result, 2)
