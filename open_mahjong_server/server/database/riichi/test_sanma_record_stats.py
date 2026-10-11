"""Three-seat records remain usable by the same riichi statistics pipeline."""
import copy
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

from .record_stats import analyze_riichi_record
from .store_riichi import store_riichi_game_stats, store_riichi_fan_stats


def record(ticks):
    return {
        'game_title': {'rule': 'riichi', 'sub_rule': 'riichi/sanma',
                       'starting_scores': [35000, 36000, 37000]},
        'game_round': {'round_index_1': {'seats': [2, 0, 1], 'start_player_index': 2,
                                       'action_ticks': ticks + [['end']]}},
    }


@pytest.mark.parametrize('source', ['T', 'F'])
def test_nuki_replacement_preserves_actor_and_closed_statistics(source):
    data = record([['d', 44], ['nuki', 2, 44, source], ['nd', 31],
                   ['hu_riichi', 2, 'hu_self', 3, 40, ['门前清自摸和', '拔北宝牌'], [-4000, -2000, 6000]]])
    stats = analyze_riichi_record(data, 0, 41000, 1)
    assert stats['total_rounds'] == stats['self_draw_count'] == stats['win_count'] == 1
    assert stats['fulu_round_count'] == stats['riichi_details']['fulu_win_count'] == 0
    assert stats['riichi_details']['damaten_win_count'] == 1
    assert stats['total_round_score'] == 6000
    assert stats['first_place_count'] == 1
    assert analyze_riichi_record(data, 3) is None


def test_draw_after_nuki_rotates_modulo_three_and_ron_uses_three_changes():
    data = record([['d', 44], ['nuki', 2, 44, 'T'], ['nd', 31], ['c', 31],
                   ['d', 21], ['c', 21], ['riichi', 0],
                   ['hu_riichi', 1, 'hu_first', 3, 40, ['立直'], [-8000, 8000, 0]]])
    # Original player 1 now occupies seat 0, the discarder after seat 2.
    stats = analyze_riichi_record(data, 1)
    assert stats['deal_in_count'] == stats['riichi_details']['riichi_deal_in_count'] == 1
    assert stats['riichi_details']['total_deal_in_points'] == 8000
    assert stats['riichi_details']['total_riichi_turn'] == 1
    assert stats['total_round_score'] == -9000


@pytest.mark.parametrize('flags', [[True, False, False], [True, True, False], [False, False, False]])
def test_three_player_draw_and_rank_never_create_fourth_place(flags):
    data = record([['ryuukyoku', flags, [3000, -1500, -1500], 'exhaustive']])
    for original, seat in enumerate([2, 0, 1]):
        stats = analyze_riichi_record(copy.deepcopy(data), original, rank=4)
        assert stats['riichi_details']['draw_round_count'] == 1
        assert stats['riichi_details']['tenpai_draw_count'] == int(flags[seat])
        assert stats['fourth_place_count'] == 0
