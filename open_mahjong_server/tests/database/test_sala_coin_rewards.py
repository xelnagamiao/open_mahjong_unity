"""Match rewards use integer hundredths and actual completed table hands."""
from copy import deepcopy

import pytest

from server.database.game_rewards import (REWARD_MINOR_PER_HAND, REWARD_VERSION,
                                          apply_currency_to_match_result,
                                          build_currency_reward)
from server.database.statistics_catalog import RULES
from test_round_progress import record


@pytest.mark.parametrize('count', [3, 4])
def test_all_human_match_sizes_receive_five_hundredths_per_completed_hand(count):
    rule = next(r for r in RULES if r['player_count'] == count and r['rule'] != 'free')
    replay = record(rule['rule'], rule['sub_rule'], hands=16, players=count)
    replay['game_title']['room_type'] = 'match'
    ids = [replay['game_title'][f'p{i}_uid'] for i in range(count)]
    reward = build_currency_reward(replay, ids, ids)
    assert REWARD_VERSION == 3 and REWARD_MINOR_PER_HAND == 5
    assert reward['eligible'] and reward['completed_hands'] == 16
    assert [p['currency_delta_minor'] for p in reward['players']] == [80] * count


def test_repeats_pay_once_per_ended_hand_and_guests_receive_nothing():
    replay = record(hands=11)
    replay['game_title']['room_type'] = 'match'
    replay['game_round']['round_index_12'] = dict(round_index=12, action_ticks=[['d', 11]])
    ids = [replay['game_title'][f'p{i}_uid'] for i in range(4)]
    reward = build_currency_reward(replay, ids, ids[:3])
    assert reward['completed_hands'] == 11
    assert [p['currency_delta_minor'] for p in reward['players']] == [55, 55, 55, 0]


@pytest.mark.parametrize('room_type', ['custom', 'events'])
def test_nonmatch_games_do_not_receive_rewards(room_type):
    replay = record(hands=16)
    replay['game_title']['room_type'] = room_type
    ids = [replay['game_title'][f'p{i}_uid'] for i in range(4)]
    assert all(p['currency_delta_minor'] == 0 for p in build_currency_reward(replay, ids, ids)['players'])


def test_saved_old_reward_remains_authoritative_when_viewing_existing_result():
    replay = {'currency_reward': {'version': 2, 'players': [
        dict(user_id=10000101, currency_delta_minor=160, currency_reward_status='saved')]},
        'match_result': {'players': [dict(user_id=10000101)]}}
    saved = deepcopy(replay['currency_reward'])
    apply_currency_to_match_result(replay)
    assert replay['currency_reward'] == saved
    assert replay['match_result']['players'][0]['currency_delta_minor'] == 160
    assert replay['match_result']['players'][0]['currency_reward_version'] == 2
