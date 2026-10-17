"""Official-preset boundaries: score, wire payload and replay must agree."""
import asyncio
from itertools import product
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .action_check import check_action_hand_action, check_hepai, refresh_waiting_tiles
from .test_rule_branches import game
from .test_sanma import sanma
from .wait_action import _execute_cut, _apply_passed_ron_furiten
from ...game_calculation.riichi.rule_config import catalog, preset_room_config


PRESETS = [p['id'] for p in catalog()['presets']]
HAND = [12, 13, 14, 22, 23, 24, 33, 34, 35, 36, 37, 38, 28, 28]
SANMA_HAND = [22, 23, 24, 22, 23, 24, 33, 34, 35, 36, 37, 38, 28, 28]


def connected(preset):
    g = sanma(preset) if preset.startswith('sanma_') else game(preset)
    for key, value in preset_room_config(preset).items():
        if key != 'game_round':
            setattr(g, key, value)
    sockets = {p.user_id: SimpleNamespace(websocket=AsyncMock(), user_id=p.user_id) for p in g.player_list}
    g.game_server.user_id_to_connection = sockets
    g.game_server.players = {str(uid): c for uid, c in sockets.items()}
    g.send_to_realtime_spectators = AsyncMock()
    g.current_player_index = 0
    g.current_round = 2
    g.tiles_list = [11] * 50
    for p in g.player_list:
        p.discard_origin_tiles = [47]
        p.discard_tiles = [47]
    return g, sockets


def win(g, *, riichi=True, tsumo=False, chombo=False):
    winner = g.player_list[1]
    winner.tag_list = ['riichi', 'ippatsu'] if riichi else []
    winner.hand_tiles = list(SANMA_HAND if g.sub_rule == 'riichi/sanma' else HAND)
    if not tsumo:
        winner.hand_tiles.remove(38)
    winner.waiting_tiles = {38}
    if tsumo:
        g.current_player_index = 1
    actions = {i: [] for i in range(len(g.player_list))}
    check_hepai(g, actions, 38, 1, 'tsumo' if tsumo else 'ron')
    g.hu_class = 'hu_self' if tsumo else 'hu_first'
    g.ron_player_index = 1
    result = g.result_dict[g.hu_class]
    if not tsumo:
        winner.hand_tiles.append(38)
    asyncio.run(g._settle_cuohe() if chombo else g._settle_hu())
    return result


def assert_indicators(g, sockets, dora, ura):
    tick = g.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[0] == 'hu_riichi'
    assert tick[7:9] == [dora, ura]
    for conn in sockets.values():
        payload = next(c.args[0]['show_result_info'] for c in conn.websocket.send_json.call_args_list
                       if c.args[0]['type'] == 'gamestate/riichi/show_result')
        assert payload['dora_indicators'] == dora
        assert payload['ura_dora_indicators'] == ura


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('tsumo,chombo', [(False, False), (True, False), (False, True)])
def test_preset_bonus_scoring_and_revealed_indicators_agree(preset, tsumo, chombo):
    g, sockets = connected(preset)
    g.dora_indicators = [21]
    g.kan_dora_indicators = [33] if preset != 'jpml_a' else []
    g.ura_dora_indicators = [27]
    g.ura_kan_dora_indicators = [33] if preset != 'jpml_a' else []
    result = win(g, tsumo=tsumo, chombo=chombo)
    assert ('一发' in result['yaku']) is (preset != 'jpml_a')
    assert any(y.startswith('里宝牌') for y in result['yaku']) is (preset != 'jpml_a')
    assert_indicators(g, sockets, [21] if preset == 'jpml_a' else [21, 33],
                      [] if preset == 'jpml_a' else [27, 33])


@pytest.mark.parametrize('ura,kan,kan_ura,riichi', product([False, True], repeat=4))
def test_independent_bonus_switches_are_respected_in_settlement(ura, kan, kan_ura, riichi):
    g, sockets = connected('tenhou')
    g.detailed_config.update(ura_dora=ura, kan_dora=kan, kan_ura_dora=kan_ura)
    g.dora_indicators, g.kan_dora_indicators = [11], [21]
    g.ura_dora_indicators, g.ura_kan_dora_indicators = [27], [33]
    result = win(g, riichi=riichi)
    expected_ura = ([27] if ura else []) + ([33] if kan and kan_ura else []) if riichi else []
    expected_han = (2 * ura + int(kan and kan_ura)) if riichi else 0
    assert next((int(y.split('*')[1]) for y in result['yaku'] if y.startswith('里宝牌*')), 0) == expected_han
    assert_indicators(g, sockets, [11] + ([21] if kan else []), expected_ura)


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('live', [0, 1, 2, 3, 4])
def test_last_riichi_requires_a_remaining_turn_for_the_actual_player_count(preset, live):
    g, _ = connected(preset)
    g.player_list[0].hand_tiles = list(SANMA_HAND if preset.startswith('sanma_') else HAND)
    g.tiles_list = [11] * (g.dead_wall_count + live)
    threshold = 1 if preset in ('jpml_a', 'mleague') else len(g.player_list)
    assert ('riichi_cut' in check_action_hand_action(g, 0)[0]) is (live >= threshold)


@pytest.mark.parametrize('preset', PRESETS)
def test_fifth_copy_in_concealed_hand_is_not_tenpai_or_riichi(preset):
    g, _ = connected(preset)
    p = g.player_list[0]
    p.hand_tiles = [41] * 4 + [22, 23, 24, 32, 33, 34, 45, 45, 45]
    refresh_waiting_tiles(g, 0)
    assert not p.waiting_tiles
    assert not g._is_ryuukyoku_tenpai(p)
    p.hand_tiles.append(47)
    check_action_hand_action(g, 0)
    assert 47 not in p.riichi_candidate_cuts


@pytest.mark.parametrize('preset,tenpai', [('tenhou', True), ('majsoul', True), ('mleague', False)])
def test_formal_tenpai_accounts_for_own_melds_only_when_rules_require(preset, tenpai):
    g, _ = connected(preset)
    p = g.player_list[0]
    p.hand_tiles = [11, 22, 23, 24, 32, 33, 34, 45, 45, 45]
    p.combination_tiles = ['k11']
    refresh_waiting_tiles(g, 0)
    assert bool(p.waiting_tiles) is tenpai
    assert g._is_ryuukyoku_tenpai(p) is tenpai


@pytest.mark.parametrize('preset', ['tenhou', 'jpml_a', 'mleague', 'sanma_tenhou'])
@pytest.mark.parametrize('drew', [False, True])
def test_call_discard_does_not_clear_furiten_before_a_real_draw(preset, drew):
    g, _ = connected(preset)
    p = g.player_list[0]
    p.hand_tiles = [22, 23, 24, 32, 33, 34, 45, 45, 45, 11]
    p.combination_tiles = ['k41']
    p.has_draw_slot = False
    _apply_passed_ron_furiten(g, [0])
    if drew:
        p.hand_tiles.pop()
        p.get_tile([11])
    assert asyncio.run(_execute_cut(g, 0, 11, drew, None, False))
    assert p.temp_furiten is (not drew)
