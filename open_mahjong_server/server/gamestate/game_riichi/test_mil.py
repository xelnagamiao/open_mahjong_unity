"""MIL/RCR 2023 first-page rules, with authoritative payments and replay evidence."""
import asyncio
from itertools import combinations

import pytest

from .test_preset_audit import connected, HAND
from .rule_logic import finalize_scores, next_honba
from .action_check import check_action_hand_action, check_action_ankan
from ...game_calculation.riichi.rule_config import normalize_riichi_config, preset_room_config
from ...game_calculation.riichi.riichi_hepai_check import Riichi_Hepai_Check


def test_legacy_defaults_and_mil_only_four_player_penalty():
    assert normalize_riichi_config()['honba_enabled'] is True
    assert normalize_riichi_config()['noten_penalty'] == 3000
    assert normalize_riichi_config({}, 'riichi/sanma')['noten_penalty'] == 2000
    assert normalize_riichi_config({'noten_penalty': 'fixed_gap_2000'}, 'riichi/standard')['noten_penalty'] == 'fixed_gap_2000'
    with pytest.raises(ValueError, match='固定分差'):
        normalize_riichi_config({'noten_penalty': 'fixed_gap_2000'}, 'riichi/sanma')
    with pytest.raises(ValueError, match='2000'):
        normalize_riichi_config({'noten_penalty': 2000}, 'riichi/standard')
    for invalid in (0, 1, 'false', None):
        with pytest.raises(ValueError):
            normalize_riichi_config({'honba_enabled': invalid})


DRAW_CASES = [list(c) for n in range(5) for c in combinations(range(4), n)]


@pytest.mark.parametrize('tenpai', DRAW_CASES)
def test_fixed_difference_payments_reach_scores_messages_and_replay(tenpai):
    g, sockets = connected('mil')
    for p in g.player_list:
        p.score = 30000
        p.hand_tiles = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 29, 29]
        p.ryuukyoku_declared_tenpai = p.player_index in tenpai
    g.hu_class = 'ryuukyoku'
    asyncio.run(g._settle_round())
    gain, loss = {0: (0, 0), 1: (1500, -500), 2: (1000, -1000), 3: (500, -1500), 4: (0, 0)}[len(tenpai)]
    expected = [gain if i in tenpai else loss for i in range(4)]
    assert [p.score for p in g.player_list] == [30000 + d for d in expected]
    assert sum(expected) == 0
    tick = g.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[:3] == ['ryuukyoku', [int(i in tenpai) for i in range(4)], expected]
    for socket in sockets.values():
        payload = next(c.args[0]['show_result_info'] for c in socket.websocket.send_json.call_args_list
                       if c.args[0]['type'] == 'gamestate/riichi/show_result')
        assert {int(i): v for i, v in payload['score_changes'].items()} == dict(enumerate(expected))


@pytest.mark.parametrize('winner', range(4))
@pytest.mark.parametrize('tsumo', [False, True])
@pytest.mark.parametrize('pao', [False, True])
def test_no_honba_on_wins_and_pao_but_deposits_still_paid(winner, tsumo, pao):
    g, sockets = connected('mil')
    before = [p.score for p in g.player_list]
    loser, liable = (winner + 1) % 4, (winner + 2) % 4
    g.current_player_index = winner if tsumo else loser
    g.ron_player_index, g.hu_class = winner, 'hu_self' if tsumo else 'hu_first'
    # A stale nonzero count must not leak into payment or the stored result.
    g.honba, g.riichi_sticks = 5, 2
    base = (48000 if winner == 0 else 32000) if pao else (12000 if winner == 0 else 8000)
    main = (16000 if pao else 4000) if tsumo else base
    additional = main if winner == 0 else main // 2
    result = dict(han=13 if pao else 5, fu=30, yaku=['大三元' if pao else '满贯'],
                  cost=dict(main=main, additional=additional), score=base)
    if pao:
        g.player_list[winner].pao_liability = {'大三元': liable}
        result.update(yakuman_multiplier=1, yakuman_components={'大三元': 1})
    g.result_dict = {g.hu_class: result}
    expected = [0] * 4
    if pao:
        expected[liable] = -base if tsumo else -base // 2
        if not tsumo:
            expected[loser] = -base // 2
    elif tsumo:
        for i in range(4):
            if i != winner:
                expected[i] = -main if winner == 0 or i == 0 else -additional
    else:
        expected[loser] = -base
    expected[winner] = base + 2000
    asyncio.run(g._settle_hu())
    assert [p.score for p in g.player_list] == [s + d for s, d in zip(before, expected)]
    assert sum(expected) == 2000 and g.riichi_sticks == 0 and g.honba == 0
    tick = g.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[0] == 'hu_riichi' and tick[6] == expected
    for socket in sockets.values():
        payload = next(c.args[0]['show_result_info'] for c in socket.websocket.send_json.call_args_list
                       if c.args[0]['type'] == 'gamestate/riichi/show_result')
        assert payload['honba'] == 0 and payload['riichi_sticks_collected'] == 2


@pytest.mark.parametrize('kind,renchan', [('hu_self', True), ('ryuukyoku', True), ('ryuukyoku', False), ('hu_first', False)])
def test_renchan_and_draw_counters_can_be_disabled_without_changing_turns(kind, renchan):
    g, _ = connected('mil')
    g.hu_class, g.honba = kind, 3
    assert next_honba(g, renchan) == 0
    g.detailed_config['honba_enabled'] = True
    assert next_honba(g, renchan) == (4 if renchan or kind == 'ryuukyoku' else 0)


@pytest.mark.parametrize('hand,win,extra,expected', [
    (HAND, 38, {'dora_indicators': [37]}, 7700),
    ([11, 12, 13] * 2 + [14, 15, 16] * 2 + [18, 18], 16, {'dora_indicators': [17]}, 24000),
    ([41] * 3 + [42] * 3 + [43] * 3 + [45] * 3 + [47] * 2, 47, {}, 32000),
    ([11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47, 47], 47, {}, 32000),
])
def test_mil_hand_scores(hand, win, extra, expected):
    config = preset_room_config('mil')
    ctx = {**config['detailed_config'], 'red_dora': False, 'is_riichi': True,
           'player_wind': 1, 'round_wind': 0, 'tsumi_number': 5, **extra}
    result = Riichi_Hepai_Check().hepai_check(hand, [], [], win, ctx)
    assert result['is_valid'] and result['score'] == expected
    assert result['cost']['main_bonus'] == 0


def test_end_conditions_negative_riichi_and_ankan_robbery():
    g, _ = connected('mil')
    g.player_list[0].score = -1000
    assert g._can_declare_riichi_by_score(-1000)
    assert 'riichi_cut' in check_action_hand_action(g, 0)[0]
    assert all(not actions for actions in check_action_ankan(g, 47).values())
    g.current_round, g.hu_class = 8, 'hu_self'
    g.player_list[0].score = 60000
    assert not g._riichi_match_should_end(True, 8)
    g.hu_class = 'ryuukyoku'
    assert not g._riichi_match_should_end(True, 8)
    assert g._riichi_match_should_end(False, 9)


def test_final_ties_share_uma_without_awarding_leftover_deposits():
    g, _ = connected('mil')
    for p, score in zip(g.player_list, [35000, 35000, 24000, 24000]):
        p.score = score
    g.riichi_sticks = 2
    finalize_scores(g)
    assert [p.riichi_points for p in g.player_list] == [15, 15, -16, -16]
    assert [p.score for p in g.player_list] == [35000, 35000, 24000, 24000]
    assert g.riichi_sticks == 2
