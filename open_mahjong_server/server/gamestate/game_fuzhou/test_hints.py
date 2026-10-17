"""Waiting scores use the settlement calculator, with no speculative event bonus."""
import copy
import json

import pytest

from tests.local_hint_oracles.fuzhou import build_hints
from .test_state import WIN, state_with


@pytest.mark.parametrize('base', [10, 15, 20])
@pytest.mark.parametrize('flowers,flower_score', [([], 30), ([51], 16), ([41, 42], 2), ([41]*4, 6), ([51,52,53,54], 6)])
def test_wait_points_include_base_flowers_and_dealer_streak(base, flowers, flower_score):
    state = state_with(WIN[:-1])
    state.detailed_config['base_score'] = base
    state.dealer_streak = 2
    state.player_list[0].huapai_list = flowers
    details = build_hints(state, 0)['waiting_details']
    assert details[29] == dict(kind='ron', points=base + flower_score + 6)
    assert details[39] == dict(kind='self_only', points=base + flower_score + 6 + 1)


@pytest.mark.parametrize('hand,melds,jin,points', [
    ([11,12,13,14,15,16,17,18,19,11,11,11,14,14,14,19,19], [], 29, 100),
    ([11,12,13,14,15,16,17,18,19,11,11,11,14,14,14,19,29], [], 29, 71),
    (WIN[:-2]+[39,39], [], 39, 102),
    (WIN[:12]+[39,39,39,29,29], [], 39, 163),
    ([11]*3+[21]*3+[31]*3+[37]*3+[25]*3+[29,29], [], 39, 55),
    ([29,29], ['k11','k21','k31','k37','k25'], 39, 115),
    (WIN[6:], ['g12','G15'], 39, 13),
])
def test_wait_scores_include_best_pattern_and_melds(hand, melds, jin, points):
    # Only the tested player's concealed tiles/melds matter to a private hint.
    state = state_with(WIN[:-1])
    player = state.player_list[0]
    state.jin = jin
    player.hand_tiles = hand[:-1]
    player.combination_tiles = melds
    detail = build_hints(state, 0)['waiting_details'][hand[-1]]
    assert detail['points'] == points
    source = 'self_draw' if detail['kind'] == 'self_only' else 'discard'
    scored_hand = hand if source == 'self_draw' else hand[:-1]
    assert state.score_win(0, source, hand[-1], hand=scored_hand)['points'] == points


@pytest.mark.parametrize('restriction', ['discarded_gold', 'golden_middle', 'youjin1', 'youjin2', 'other_youjin', 'last_four'])
def test_self_draw_restrictions_keep_numeric_points(restriction):
    state = state_with(WIN[:-1])
    player = state.player_list[0]
    expected = 40
    if restriction == 'discarded_gold': player.discarded_gold = True
    if restriction == 'golden_middle': player.golden_middle = True; expected += 40
    if restriction.startswith('youjin'):
        player.youjin = int(restriction[-1]); state.youjin_owner = 0
        expected += 60 * player.youjin
    if restriction == 'other_youjin': state.youjin_owner = 1
    if restriction == 'last_four': state.in_last_four = True
    detail = build_hints(state, 0)['waiting_details'][29]
    assert detail == dict(kind='self_only', points=expected)


def test_cache_refreshes_when_only_scoring_inputs_change():
    state = state_with(WIN[:-1])
    assert build_hints(state, 0)['waiting_details'][29]['points'] == 40
    state.player_list[0].huapai_list = [51]
    assert build_hints(state, 0)['waiting_details'][29]['points'] == 26
    state.player_list[0].huapai_list = [41,41,41,42]
    assert build_hints(state, 0)['waiting_details'][29]['points'] == 14
    state.player_list[0].huapai_list[-1] = 41  # Same count; now a complete set.
    assert build_hints(state, 0)['waiting_details'][29]['points'] == 16
    state.dealer_streak = 3
    assert build_hints(state, 0)['waiting_details'][29]['points'] == 25
    state.detailed_config['base_score'] = 20
    assert build_hints(state, 0)['waiting_details'][29]['points'] == 35


def test_discard_previews_score_the_remaining_hand_without_mutating_state():
    state = state_with(WIN[:-1]+[39])
    player = state.player_list[0]
    before = copy.deepcopy({k:v for k,v in player.__dict__.items() if k != 'record_counter'})
    details = build_hints(state, 0)['waiting_details_by_discard']
    assert details[39][29] == dict(kind='self_only', points=40)
    assert details[39][39] == dict(kind='self_only', points=41)
    assert details[29][39] == dict(kind='self_only', points=102)
    assert {k:v for k,v in player.__dict__.items() if k != 'record_counter'} == before


def test_locked_waits_and_pre_gold_never_show_a_legal_score():
    state = state_with(WIN[:-1])
    state.player_list[0].golden_middle = True
    state.player_list[0].locked_waits = {39}
    assert build_hints(state, 0)['waiting_details'][29] == dict(kind='unmet', points=0)
    assert build_hints(state, 0)['waiting_details'][39]['points'] == 81
    state.jin = None
    assert build_hints(state, 0)['waiting_details'] == {}


def test_future_discard_preview_does_not_retain_kong_draw_bonus():
    state = state_with(WIN[:-1]+[28])
    state.player_list[0].after_ready_kong = True
    state.player_list[0].discarded_gold = True
    assert build_hints(state, 0)['waiting_details_by_discard'][28][29] == dict(kind='self_only', points=40)


def test_live_and_record_hints_serialize_the_same_local_inputs(monkeypatch):
    state = state_with(WIN[:-1])
    state.start_round_recording()
    def forbid(*args, **kwargs):
        pytest.fail('private and replay hints must not search or score on the server')
    monkeypatch.setattr('server.game_calculation.fuzhou.rules.waiting_tiles', forbid)
    monkeypatch.setattr('server.game_calculation.fuzhou.rules.score_hand', forbid)
    private = state.build_game_start_payload(0)['game_info']['fuzhou_hints']
    assert 'waiting_details' not in private
    assert private['local_request']['rule'] == 'fuzhou'
    assert private['local_request']['hand'] == WIN[:-1]
    # Numeric scoring is covered above; live/replay transports inputs only.
    assert private['local_request']['context']['jin'] == state.jin
    state.record_hints()
    ticks = state.game_record['game_round']['round_index_1']['action_ticks']
    hints_tick = next(t for t in reversed(ticks) if t[:2] == ['fuzhou', 'hints'])
    recorded = hints_tick[2]
    if isinstance(recorded, str): recorded = json.loads(recorded)
    assert json.loads(json.dumps(recorded[0])) == json.loads(json.dumps(private))
