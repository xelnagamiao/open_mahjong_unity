import random
from collections import Counter
from dataclasses import replace

import pytest

from ..guobiao_hepai_check import Chinese_Hepai_Check
from ..riichi.riichi_hepai_check import Riichi_Hepai_Check
from . import tiles as t
from .scoring import Context, conditions, guobiao_shape, riichi_base_points, riichi_shape
from .solver import Meld, iter_shapes


def physical(faces):
    pack = t.normal_pack()
    result = []
    for face in faces:
        value = next(v for v in pack if t.face(v) == face)
        pack.remove(value)
        result.append(value)
    return tuple(result)


def scores(hand, melds=(), variant='riichi', ctx=Context()):
    calculator = riichi_shape if variant == 'riichi' else guobiao_shape
    return [calculator(shape, ctx) for shape in iter_shapes(hand, melds, variant)]


def best(hand, melds=(), ctx=Context()):
    return max(scores(hand, melds, ctx=ctx), key=lambda s: (riichi_base_points(s.yaku_fan, s.fu, s.yakuman), s.yaku_fan, s.fu))


PINFU = (12, 13, 14, 22, 23, 24, 33, 34, 35, 36, 37, 28, 28, 38)


def test_pinfu_kiriage_and_visible_ura_separation():
    score = best(PINFU, ctx=Context(riichi=1, indicators=(37,), ura_indicators=(27,)))
    assert (score.yaku_fan, score.fu, score.visible, score.total) == (3, 30, 4, 6)
    assert riichi_base_points(score.visible, score.fu) == 2000
    assert conditions((t.EVEN, t.MANGAN), score.visible, score.fu, 'riichi')
    assert not conditions((t.ODD,), score.visible, score.fu, 'riichi')
    assert scores(PINFU, ctx=Context(tsumo=True))[0].fu == 20


@pytest.mark.parametrize('han,fu,expected', [(0, 40, 0), (-2, 50, 0), (1, 30, 240), (3, 60, 2000), (4, 30, 2000), (5, 20, 2000), (6, 30, 3000), (8, 30, 4000), (11, 30, 6000), (13, 30, 8000), (26, 30, 8000)])
def test_riichi_limit_points(han, fu, expected):
    assert riichi_base_points(han, fu) == expected


def test_double_wind_pair_four_fu_and_winning_group():
    hand = physical((11, 11, 11, 22, 23, 24, 34, 35, 36, 36, 37, 41, 41, 38))
    result = best(hand, ctx=Context(riichi=1, seat_wind=41, round_wind=41))
    assert result.fu == 50
    hand = physical((11, 11, 11, 21, 21, 21, 31, 31, 31, 44, 44, 45, 45, 44))
    ron = best(hand)
    tsumo = best(hand, ctx=Context(tsumo=True))
    assert ('三暗刻', 2) in ron.entries and ron.yakuman == 0
    assert tsumo.yakuman == 1 and ('四暗刻', 13) in tsumo.entries


def test_all_four_double_yakuman_exceptions_are_single():
    cases = [
        (t.ORPHANS + (47,), '国士无双十三面'),
        ((11, 11, 11, 21, 21, 21, 31, 31, 31, 45, 45, 45, 44, 44), '四暗刻单骑'),
        ((41, 41, 41, 42, 42, 42, 43, 43, 43, 44, 44, 44, 45, 45), '大四喜'),
        ((11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19, 15), '纯正九莲宝灯'),
    ]
    for hand, name in cases:
        result = best(physical(hand), ctx=Context(indicators=(44, 44, 44), ura_indicators=(44,), riichi=1))
        assert (name, 13) in result.entries
        assert result.yakuman == len(result.entries)
        assert result.visible == result.total == result.yakuman * 13
        assert result.dora == result.ura == result.physical_bonus == 0


def test_physical_bonus_and_joker_indicator_count_actual_entities():
    hand = (11, 12, 1013, 21, 22, 2023, 31, 32, 33, 45, 45, 45, 3046, t.EMPEROR)
    result = best(hand, ctx=Context(indicators=(47, 5000), ura_indicators=(5001,), riichi=1))
    assert result.physical_bonus == 1
    assert result.dora == 3
    assert result.ura == 1
    assert result.visible + 1 == result.total


def test_silver_can_reduce_valid_yaku_to_zero_and_bonuses_never_create_yaku():
    hand = (11, 12, 2013, 22, 23, 24, 34, 35, 36, 36, 37, 41, 41, 38)
    result = best(hand, ctx=Context(riichi=1))
    assert result.yaku_fan == 1 and result.total == 0
    assert riichi_base_points(result.total, result.fu) == 0
    opened = Meld((11, 12, 1013), declared='chi')
    result = best((22, 23, 24, 34, 35, 36, 36, 37, 41, 41, 38), (opened,), Context(indicators=(37,)))
    assert result.yaku_fan == 0 and result.total == 2


@pytest.mark.parametrize('size,concealed,expected', [(4, False, 40), (5, False, 60), (6, False, 90), (4, True, 70), (5, True, 100), (6, True, 160)])
def test_high_kong_fu_before_rounding(size, concealed, expected):
    kong = Meld(tuple([11] * 4 + [5000, 5004][:size - 4]), concealed=concealed, declared='angang' if concealed else 'gang')
    result = best((22, 23, 24, 32, 33, 34, 36, 37, 45, 45, 38), (kong,), Context(riichi=int(concealed)))
    assert result.fu == expected


def test_pure_joker_kongs_are_separate_from_yaku_requirement():
    meld = Meld((5000, 5008, 5020, 5039), declared='gang')
    hand = (22, 23, 24, 32, 33, 34, 36, 37, 41, 41, 38)
    result = best(hand, (meld,))
    assert result.pure_open == 1 and result.pure_closed == 0
    assert result.yaku_fan == 0
    concealed = best(hand, (replace(meld, concealed=True, declared='angang'),), Context(riichi=1))
    assert concealed.pure_closed == 1 and concealed.pure_open == 0


def test_mcr_conditional_threshold_and_bonus_do_not_change_minimum():
    hand = (11, 12, 1013, 15, 15, 34, 35, 36, 37, 38, 39)
    meld = Meld((17, 1017, 19), declared='chi')
    assert not scores(hand, (meld,), 'guobiao')
    meld = Meld((17, 18, 19), declared='chi')
    result = max(scores(hand, (meld,), 'guobiao'), key=lambda s: s.yaku_fan)
    assert result.physical_bonus == 1 and result.visible == result.yaku_fan + 1
    assert not conditions((t.MANGAN,), 23, 0, 'guobiao')
    assert conditions((t.MANGAN,), 24, 0, 'guobiao')
    assert not conditions((t.ODD, t.EVEN), 25, 0, 'guobiao')


def test_special_shapes_preserve_physical_bonus():
    hand = tuple(3041 if v == 41 else v for v in t.ORPHANS) + (t.EMPEROR,)
    result = next(guobiao_shape(s) for s in iter_shapes(hand) if s.kind == 'thirteen_orphans')
    assert result.physical_bonus == 1
    hand = (11, 11, 14, 14, 17, 1017, 22, 22, 25, 25, 28, 28, 41, t.EMPEROR)
    result = next(guobiao_shape(s) for s in iter_shapes(hand) if s.kind == 'seven_pairs')
    assert result.physical_bonus == 1 and '七对' in result.entries


def test_rulebook_parity_example_uses_one_complete_interpretation():
    hand = (t.EVEN, 12, 12, 33, 1033, 2033, 34, 35, 36, 22, 23, 26, 27, 28)
    interpretations = [(shape, riichi_shape(shape, Context(riichi=1, ippatsu=True))) for shape in iter_shapes(hand, variant='riichi')]
    valid = [(shape, result) for shape, result in interpretations if conditions(hand, result.visible, result.fu, 'riichi')]
    assert valid and all(shape.assignment[0] == 21 and result.visible == 2 for shape, result in valid)
    assert any(shape.assignment[0] == 24 and result.visible == 3 for shape, result in interpretations)


def test_mcr_knitted_and_unconnected_are_scored_without_redecomposition():
    for hand, fan_name in [
        ((11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45), '全不靠'),
        ((11, 14, 17, 22, 25, 28, 33, 41, 42, 43, 44, 45, 46, 47), '七星不靠'),
        ((11, 14, 17, 22, 25, 28, 33, 36, 39, 45, 45, 45, 46, 46), '组合龙'),
    ]:
        results = scores(hand, variant='guobiao')
        assert any(fan_name in result.entries for result in results)


def test_mcr_best_score_matches_shared_calculator_for_normal_physical_hands():
    cases = [
        ((11, 12, 13, 17, 18, 19, 35, 35, 21, 22, 23, 27, 28, 29), 22),
        ((41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47), 42),
        ((11, 12, 13, 13, 16, 19, 32, 35, 38, 21, 24, 27, 43, 43), 13),
    ]
    from ..gb_tingpai_check import Chinese_Tingpai_Check
    for hand, win in cases:
        before = list(hand)
        before.remove(win)
        for tsumo in (False, True):
            way = ['自摸' if tsumo else '点和', '自风南', '场风东']
            if Chinese_Tingpai_Check().tingpai_check(before, []) == {win}:
                way.append('和单张')
            expected, _ = Chinese_Hepai_Check().hepai_check(list(hand), [], way, win)
            result = max(scores(physical(before + [win]), variant='guobiao', ctx=Context(tsumo=tsumo)), key=lambda s: s.yaku_fan)
            assert result.yaku_fan == expected


def test_riichi_shape_scoring_matches_existing_calculator_for_random_natural_hands():
    rng = random.Random(20261021)
    checker = Riichi_Hepai_Check()
    groups = [(v, v, v) for v in t.FACES] + [(v, v + 1, v + 2) for v in t.NUMBERS if v % 10 <= 7]
    checked = 0
    while checked < 100:
        hand = [v for _ in range(4) for v in rng.choice(groups)] + [rng.choice(t.FACES)] * 2
        if max(Counter(hand).values()) > 4:
            continue
        rng.shuffle(hand)
        for tsumo in (False, True):
            result = best(physical(hand), ctx=Context(riichi=1, tsumo=tsumo))
            reference = checker.hepai_check(hand, [], [], hand[-1], dict(player_wind=1, round_wind=0, is_riichi=True, is_tsumo=tsumo, double_yakuman=False, multiple_yakuman=True, kiriage_mangan=True))
            assert reference['is_valid'], reference
            assert (result.yaku_fan, result.fu, result.yakuman) == (reference['yaku_han'], reference['fu'], reference['yakuman_multiplier']), (hand, tsumo, result, reference)
        checked += 1
