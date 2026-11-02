from dataclasses import replace

import pytest

from . import tiles as t
from .evaluation import evaluate
from .payments import exhaustive_payments, win_payments
from .scoring import Context
from .solver import Meld


def test_pure_joker_reward_uses_yakuman_limit_not_separate_point_addition():
    meld = Meld((5000, 5008, 5020, 5039), declared='gang')
    hand = (45, 45, 45, 22, 23, 24, 36, 37, 41, 41, 38)
    ordinary = evaluate(hand, (Meld((11, 11, 11, 11), declared='gang'),), 'riichi')
    assert ordinary.valid and ordinary.yaku_fan == 1 and ordinary.fan == 1
    result = evaluate(hand, (meld,), 'riichi')
    assert result.valid and result.fan == 13 and result.yakuman == 1
    assert win_payments(result, 1, discarder=2) == [0, 32000, -32000, 0]
    assert all('役牌' not in entry for entry in result.entries)
    result = evaluate(hand, (replace(meld, concealed=True, declared='angang'),), 'riichi')
    assert result.valid and result.fan == 26 and result.yakuman == 2
    assert win_payments(result, 1, discarder=2) == [0, 64000, -64000, 0]


def test_pure_reward_and_dora_do_not_supply_the_required_yaku():
    meld = Meld((5000, 5008, 5020, 5039), declared='gang')
    result = evaluate((22, 23, 24, 32, 33, 34, 36, 37, 41, 41, 38), (meld,), 'riichi', Context(indicators=(37,)))
    assert result.fan == 13 and not result.meets_minimum and not result.valid
    with pytest.raises(ValueError):
        win_payments(result, 1, discarder=2)


def test_valid_zero_point_win_collects_only_riichi_pool():
    hand = (11, 12, 2013, 22, 23, 24, 34, 35, 36, 36, 37, 41, 41, 38)
    result = evaluate(hand, (), 'riichi', Context(riichi=1))
    assert result.valid and result.fan == 0
    assert win_payments(result, 1, discarder=2, honba=3, riichi_sticks=2) == [0, 2000, 0, 0]


def test_same_explanation_must_satisfy_parity_and_scoring():
    hand = (t.EVEN, 12, 12, 33, 1033, 2033, 34, 35, 36, 22, 23, 26, 27, 28)
    result = evaluate(hand, (), 'riichi', Context(riichi=1, ippatsu=True))
    assert result.valid and result.fan == result.visible == 2 and result.shape.assignment[0] == 21


def test_mcr_bonus_included_after_minimum_and_standard_base_formula():
    hand = (11, 12, 13, 17, 18, 19, 35, 35, 21, 22, 23, 27, 28, 29)
    result = evaluate(hand)
    result = replace(result, fan=8, visible=8, meets_minimum=True, meets_conditions=True)
    assert win_payments(result, 0) == [324, -108, -108, -108]
    assert win_payments(result, 0, discarder=1) == [308, -108, -100, -100]
    assert win_payments(replace(result, fan=0), 0, discarder=1) == [0, 0, 0, 0]


@pytest.mark.parametrize('winner', range(4))
@pytest.mark.parametrize('source', [None, 0, 1, 2, 3])
def test_payments_are_conserved_for_all_seats_and_sources(winner, source):
    if source == winner:
        return
    hand = (12, 13, 14, 22, 23, 24, 33, 34, 35, 36, 37, 28, 28, 38)
    result = evaluate(hand, (), 'riichi', Context(riichi=1, tsumo=source is None))
    changes = win_payments(result, winner, discarder=source, honba=2, riichi_sticks=3)
    assert sum(changes) == 3000 and changes[winner] > 3000
    assert all(value <= 0 for seat, value in enumerate(changes) if seat != winner)


def test_responsibility_payment_applies_to_covered_yakuman_component_only():
    hand = (11, 11, 11, 21, 21, 21, 31, 31, 31, 45, 45, 45, 44, 44)
    result = evaluate(hand, (), 'riichi')
    result = replace(result, fan=26, visible=26, yakuman=2, entries=('大三元（13番）', '四暗刻单骑（13番）'))
    assert win_payments(result, 1, liabilities={'大三元': 2}) == [-16000, 64000, -40000, -8000]
    assert win_payments(result, 1, discarder=3, liabilities={'大三元': 2}) == [0, 64000, -16000, -48000]
    assert win_payments(result, 1, rinshan_payer=3, honba=1) == [0, 64300, 0, -64300]
    assert win_payments(result, 1, honba=2, liabilities={'大三元': 2}) == [-16000, 64600, -40600, -8000]
    assert win_payments(result, 1, discarder=2, honba=2, liabilities={'大三元': 2}) == [0, 64600, -64600, 0]
    assert win_payments(result, 1, discarder=3, honba=2, liabilities={'大三元': 2}) == [0, 64600, -16600, -48000]
    single = replace(result, fan=13, visible=13, yakuman=1)
    assert win_payments(single, 1, honba=2, liabilities={'大三元': 2}) == [0, 32600, -32600, 0]


@pytest.mark.parametrize('ready,expected', [([], [0, 0, 0, 0]), ([0], [3000, -1000, -1000, -1000]), ([0, 2], [1500, -1500, 1500, -1500]), ([1, 2, 3], [-3000, 1000, 1000, 1000]), ([0, 1, 2, 3], [0, 0, 0, 0])])
def test_exhaustive_draw_standard_noten_payment(ready, expected):
    assert exhaustive_payments(ready) == expected
