"""Explicit sanma/yonma payment tables, including wire payloads and record ticks."""
import asyncio
from itertools import combinations
from unittest.mock import AsyncMock

import pytest

from .test_sanma import sanma
from .test_double_riichi import make_game
from .test_sanma_nuki_honba import connect_players
from ...game_calculation.riichi.rule_config import normalize_riichi_config


RULES = ['sanma_majsoul', 'sanma_tenhou', 'four']


@pytest.mark.parametrize('sub_rule,total', [('riichi/sanma',2000),('riichi/standard',3000)])
def test_missing_config_uses_subrule_penalty_and_explicit_custom_values_survive(sub_rule,total):
    assert normalize_riichi_config({}, sub_rule)['noten_penalty'] == total
    for custom in ([0,2000,3000] if sub_rule == 'riichi/sanma' else [0,3000]):
        assert normalize_riichi_config({'noten_penalty':custom}, sub_rule)['noten_penalty'] == custom


@pytest.mark.parametrize('sub_rule', ['riichi/standard','riichi/langyong','riichi/washizu','riichi/washizu_realtime'])
def test_four_player_rooms_reject_sanma_penalty_that_cannot_split_evenly(sub_rule):
    from ...room.room_validators import RiichiRoomValidator
    with pytest.raises(ValueError, match='2000'):
        RiichiRoomValidator(room_name='noten-boundary', game_round=2, round_timer=20,
                            step_timer=5, sub_rule=sub_rule, detailed_config={'noten_penalty':2000})


def table(rule):
    game = make_game(0)[0] if rule == 'four' else sanma(rule)
    for player in game.player_list:
        player.score = 35000 if rule != 'four' else 25000
        player.discard_tiles = player.discard_origin_tiles = []
        player.hand_tiles = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 29, 29]
    game.send_to_realtime_spectators = AsyncMock()
    return game


CASES = [(r, w, l) for r in RULES for w in range(4 if r == 'four' else 3)
         for l in range(4 if r == 'four' else 3) if w != l]


def assert_settlement(game, sockets, before, expected):
    assert [p.score for p in game.player_list] == [s + d for s, d in zip(before, expected)]
    assert sum(expected) == 0
    tick = game.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[0] == 'hu_riichi' and tick[6] == expected
    for socket in sockets.values():
        payload = next(c.args[0]['show_result_info'] for c in socket.websocket.send_json.call_args_list
                       if c.args[0]['type'] == 'gamestate/riichi/show_result')
        assert {int(i): v for i, v in payload['score_changes'].items()} == dict(enumerate(expected))


@pytest.mark.parametrize('rule,winner,loser', CASES)
@pytest.mark.parametrize('honba', [0, 1, 2, 5])
def test_ron_honba_is_200_sanma_300_four(rule, winner, loser, honba):
    g = table(rule)
    sockets = connect_players(g)
    before = [p.score for p in g.player_list]
    g.current_player_index, g.ron_player_index, g.honba, g.hu_class = loser, winner, honba, 'hu_first'
    base = 12000 if winner == 0 else 8000
    g.result_dict = {'hu_first': dict(han=5, fu=30, yaku=['满贯'], cost=dict(main=base), score=base)}
    asyncio.run(g._settle_hu())
    payment = base + honba * (300 if rule == 'four' else 200)
    expected = [payment if i == winner else -payment if i == loser else 0 for i in range(len(g.player_list))]
    assert_settlement(g, sockets, before, expected)


@pytest.mark.parametrize('rule,winner', [(r, w) for r in RULES for w in range(4 if r == 'four' else 3)])
@pytest.mark.parametrize('honba', [0, 1, 2, 5])
@pytest.mark.parametrize('mode', ['loss', 'split'])
def test_tsumo_honba_is_100_per_real_payer(rule, winner, honba, mode):
    g = table(rule)
    sockets = connect_players(g)
    before = [p.score for p in g.player_list]
    g.current_player_index, g.honba, g.hu_class = winner, honba, 'hu_self'
    g.detailed_config['sanma_tsumo'] = mode
    cost = dict(main=4000, additional=4000 if winner == 0 else 2000)
    g.result_dict = {'hu_self': dict(han=5, fu=30, yaku=['满贯'], cost=cost)}
    expected = [0] * len(g.player_list)
    for payer in range(len(expected)):
        if payer == winner:
            continue
        base = 4000 if winner == 0 or payer == 0 else 2000
        if rule != 'four' and mode == 'split':
            base += 2000 if winner == 0 else 1000
        expected[payer] = -base - honba * 100
    expected[winner] = -sum(expected)
    asyncio.run(g._settle_hu())
    assert_settlement(g, sockets, before, expected)


DRAW_CASES = [(r, list(c)) for r in RULES for n in range((4 if r == 'four' else 3) + 1)
              for c in combinations(range(4 if r == 'four' else 3), n)]


@pytest.mark.parametrize('rule,tenpai', DRAW_CASES)
def test_default_draw_penalty_table_and_record(rule, tenpai):
    g = table(rule)
    connect_players(g)
    count = len(g.player_list)
    expected_total = 3000 if rule == 'four' else 2000
    assert g.detailed_config['noten_penalty'] == expected_total
    before = [p.score for p in g.player_list]
    for p in g.player_list:
        p.ryuukyoku_declared_tenpai = p.player_index in tenpai
    g.hu_class = 'ryuukyoku'
    asyncio.run(g._settle_round())
    expected = [0] * count
    if 0 < len(tenpai) < count:
        expected = [expected_total // len(tenpai) if i in tenpai else -expected_total // (count - len(tenpai))
                    for i in range(count)]
    assert [p.score for p in g.player_list] == [s + d for s, d in zip(before, expected)]
    tick = g.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[:3] == ['ryuukyoku', [int(i in tenpai) for i in range(count)], expected]
    assert sum(expected) == 0


@pytest.mark.parametrize('rule,winner,loser', CASES)
@pytest.mark.parametrize('honba_payer', ['liable', 'discarder'])
def test_pao_transfers_correct_ron_honba(rule, winner, loser, honba_payer):
    g = table(rule)
    sockets = connect_players(g)
    before = [p.score for p in g.player_list]
    liable = next(i for i in range(len(g.player_list)) if i not in (winner, loser))
    g.current_player_index, g.ron_player_index, g.honba, g.hu_class = loser, winner, 2, 'hu_first'
    g.detailed_config.update(pao=True, pao_honba=honba_payer)
    g.player_list[winner].pao_liability = {'大三元': liable}
    base = 48000 if winner == 0 else 32000
    g.result_dict = {'hu_first': dict(han=13, fu=40, yaku=['大三元'], cost=dict(main=base), score=base,
                                      yakuman_multiplier=1, yakuman_components={'大三元': 1})}
    expected = [0] * len(g.player_list)
    bonus = 600 if rule == 'four' else 400
    expected[winner] = base + bonus
    expected[loser] = expected[liable] = -base // 2
    expected[liable if honba_payer == 'liable' else loser] -= bonus
    asyncio.run(g._settle_hu())
    assert_settlement(g, sockets, before, expected)
