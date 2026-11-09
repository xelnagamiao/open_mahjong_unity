"""Scheduled progress and actual completed deals must never be interchangeable."""
from copy import deepcopy

import pytest

from server.database.round_progress import (apply_round_progress, completed_hand_count,
                                            reward_human_ids, round_progress)
from server.database.statistics_catalog import RULES


def record(rule='riichi', sub_rule='riichi/standard', *, hands=11, limit=2, players=4):
    return {'game_title': dict(rule=rule, sub_rule=sub_rule, max_round=limit, room_type='custom',
                              **{f'p{i}_uid': 10000101+i for i in range(players)}),
            'game_round': {f'round_index_{i}': dict(round_index=i, current_round=min(i, 8),
                                                   action_ticks=[['liuju'], ['end']])
                           for i in range(1, hands+1)}}


def test_dealer_repeats_count_actual_ended_deals_and_exclude_current_unfinished_hand():
    replay = record()
    replay['game_round']['round_index_12'] = dict(round_index=12, current_round=8, action_ticks=[['d', 11]])
    before = deepcopy(replay)
    progress = round_progress(replay)
    assert progress == dict(version=1, player_count=4, limit_unit='circles', planned_hands=8,
                            completed_hands=11, started_hands=12, last_round_index=11, progress_round=8)
    assert replay == before


@pytest.mark.parametrize('definition', [r for r in RULES if r['rule'] != 'free'], ids=lambda r:r['sub_rule'])
def test_every_rule_uses_the_same_completed_hand_contract(definition):
    replay = record(definition['rule'], definition['sub_rule'], hands=7, players=definition['player_count'])
    # Several settlements in a blood-flow hand still constitute one deal.
    replay['game_round']['round_index_1']['action_ticks'] = [['hu_self',0],['hu_self',1],['end'],['end']]
    assert completed_hand_count(replay) == 7
    assert round_progress(replay)['completed_hands'] == 7


def test_hongque_normalizes_legacy_circle_metadata_without_changing_action_history():
    replay = record('hongque', 'hongque/v1.6', hands=16, limit=4)
    before = deepcopy(replay['game_round'])
    assert apply_round_progress(replay, '4/4_rank', 'match') == '16/4_rank'
    assert replay['game_title']['max_round'] == 16
    assert replay['game_title']['round_progress']['planned_hands'] == 16
    assert replay['game_title']['round_progress']['completed_hands'] == 16
    assert replay['game_round'] == before
    assert apply_round_progress(replay, '16/4_rank', 'match') == '16/4_rank'


def test_partial_hongque_keeps_its_scheduled_limit_and_duplicate_keeps_board_count():
    replay = record('hongque', 'hongque/v1.6', hands=5, limit=16)
    apply_round_progress(replay, '16/4')
    assert replay['game_title']['round_progress']['completed_hands'] == 5
    assert replay['game_title']['round_progress']['planned_hands'] == 16
    replay = record('sichuan', 'sichuan/doubles_duplicate', hands=1, limit=1)
    replay['game_title']['duplicate_round_count'] = 1
    assert round_progress(replay)['planned_hands'] == 1


@pytest.mark.parametrize('version,expected', [('fuzhou-user-20261014-v1',12),
                                             ('fuzhou-user-20261015-v2',8)])
def test_fuzhou_schedule_uses_the_saved_rule_version_but_rewards_actual_hands(version,expected):
    replay = record('fuzhou','fuzhou/standard',hands=15,limit=2)
    replay['game_title']['rule_version'] = version
    progress = round_progress(replay)
    assert progress['planned_hands'] == expected
    assert progress['completed_hands'] == 15


def test_guests_are_human_and_three_player_tables_qualify_but_bots_do_not():
    replay = record(hands=1)
    replay['game_title']['p3_uid'] = 9000001
    ids = [10000101,10000102,10000103,9000001]
    assert reward_human_ids(replay, ids) == sorted(ids)
    replay['game_title']['p3_uid'] = 1
    assert reward_human_ids(replay, ids[:3]) == []
    replay = record(sub_rule='riichi/sanma', players=3)
    assert reward_human_ids(replay, [10000101,10000102,10000103]) == [10000101,10000102,10000103]


def test_conflicting_actual_round_index_is_rejected_instead_of_overpaying():
    replay = record(hands=1)
    replay['game_round']['round_index_1']['round_index'] = 4
    with pytest.raises(ValueError, match='索引冲突'):
        completed_hand_count(replay)
