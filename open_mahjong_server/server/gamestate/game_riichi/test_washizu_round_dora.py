"""Transparent riichi shares dealer continuance and kan notification contracts."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_protocol import connected
from .test_washizu_doubles import captain_game
from .test_kan_dora_flow import run_kan_case, AuditFinished
from .init_tiles import init_riichi_tiles
from .wait_action import _execute_cut
from .boardcast import broadcast_game_start
from .action_check import refresh_waiting_tiles
from .kan_actions import begin_kan
from .wait_action import _broadcast_hu_and_end
from ..public.spectator_manager import SpectatorManager
from ...game_calculation.riichi.washizu_tiles import SUB_RULES


def transparent_game(rule):
    if rule == 'riichi/washizu_doubles':
        return captain_game()
    g, sockets = connected('majsoul')
    g.sub_rule = rule
    g.red_dora = False
    g.open_xiru = False
    for p in g.player_list:
        p.hand_tiles = []
        p.has_draw_slot = False
    init_riichi_tiles(g)
    return g, sockets


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('kind', ['hu_self', 'hu_first', 'hu_second', 'hu_third'])
@pytest.mark.parametrize('winner', range(4))
def test_only_the_dealer_winning_repeats_the_hand(rule, kind, winner):
    g, _ = transparent_game(rule)
    g.hu_class = kind
    g.current_player_index = winner if kind == 'hu_self' else (winner + 1) % 4
    g.ron_player_index = None if kind == 'hu_self' else winner
    assert g._compute_last_renchan() is (winner == 0)


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('tenpai', [False, True])
@pytest.mark.parametrize('declared', [False, True])
def test_exhaustive_draw_repeats_only_for_declared_dealer_tenpai(rule, tenpai, declared):
    g, _ = transparent_game(rule)
    g.hu_class = 'ryuukyoku'
    p = g.player_list[0]
    p.waiting_tiles = {2011} if tenpai else set()
    p.ryuukyoku_declared_tenpai = declared
    assert g._compute_last_renchan() is (tenpai and declared)


@pytest.mark.parametrize('rule', SUB_RULES[:2])
def test_optional_tenpai_continuance_can_still_be_disabled(rule):
    g, _ = transparent_game(rule)
    g.hu_class = 'ryuukyoku'
    g.player_list[0].waiting_tiles = {11}
    g.detailed_config['tenpai_renchan'] = False
    assert not g._compute_last_renchan()


async def run_round_transition(rule, outcome, last_hand=False):
    """Drive the real settlement and next-deal loop from a controlled outcome."""
    g, sockets = transparent_game(rule)
    g.current_round = g.max_round * 4 if last_hand else 1
    g.honba = 2
    g.room_random_seed = 20261018
    starts = []
    async def start():
        await broadcast_game_start(g)
        starts.append(dict(round=g.current_round, hand=g.round_index, honba=g.honba,
                           seats=[p.user_id for p in g.player_list]))
        if len(starts) == 2:
            raise AuditFinished()
    async def choose_outcome():
        for player in g.player_list:
            player.waiting_tiles = set()
        if outcome in ('dealer_win', 'child_win'):
            g.current_player_index = 0 if outcome == 'dealer_win' else 1
            g.hu_class = 'hu_self'
            g.result_dict = {'hu_self': dict(han=1, fu=30, yaku=['门前清自摸和'], no_yaku=False,
                                            cost={'main': 500, 'additional': 500}, score=1500)}
        else:
            g.hu_class = 'four_wind_abort' if outcome == 'abort' else 'ryuukyoku'
            if outcome == 'dealer_tenpai':
                g.player_list[0].hand_tiles = [2012,2013,2014,2022,2023,2024,
                                              2033,2034,2035,2036,2037,2038,2028]
        g.game_status = 'END'
    g.broadcast_game_start = start
    g.broadcast_ask_hand_action = AsyncMock()
    g.wait_action = choose_outcome
    # An ordinary last-dealer win can end by agari-yame. Disable it explicitly
    # so this test proves the scheduled-hand boundary does not force a rotation.
    g.detailed_config.update(agari_yame=False, tenpai_yame=False)
    with patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase', new=AsyncMock()), \
         patch('server.gamestate.game_riichi.RiichiGameState.run_result_ready_phase', new=AsyncMock()), \
         patch('server.gamestate.game_riichi.doubles.shuffle_players', lambda *_: None):
        with pytest.raises(AuditFinished):
            await g.game_loop_riichi()
    repeat = outcome in ('dealer_win', 'dealer_tenpai', 'abort')
    a, b = starts
    assert b['round'] == a['round'] + (not repeat)
    assert b['hand'] == a['hand'] + 1
    assert b['honba'] == (0 if outcome == 'child_win' else a['honba'] + 1)
    assert b['seats'] == (a['seats'] if repeat else a['seats'][1:] + a['seats'][:1])
    first = g.game_record['game_round']['round_index_1']
    assert first['action_ticks'][-1] == ['end']
    for connection in sockets.values():
        states = [c.args[0]['game_info'] for c in connection.websocket.send_json.call_args_list
                  if c.args[0]['type'].endswith('/game_start')]
        assert states[-1]['current_round'] == b['round']
        assert states[-1]['honba'] == b['honba']
    return dict(rule=rule, outcome=outcome, starts=starts, record=g.game_record,
                messages=[c.args[0] for c in sockets[100].websocket.send_json.call_args_list])


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('outcome', ['dealer_win', 'dealer_tenpai', 'dealer_noten', 'child_win', 'abort'])
def test_next_deal_preserves_or_rotates_seats_and_honba(rule, outcome):
    asyncio.run(run_round_transition(rule, outcome))


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('outcome', ['dealer_win', 'dealer_tenpai', 'abort'])
def test_last_dealer_can_repeat_without_forced_match_end(rule, outcome):
    asyncio.run(run_round_transition(rule, outcome, last_hand=True))


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('kind', ['jiuzhongjiupai', 'four_wind_abort', 'four_kan_abort',
                                 'four_riichi_abort', 'three_ron_abort'])
def test_abortive_draw_repeats_even_the_last_scheduled_hand(rule, kind):
    async def run():
        g, _ = transparent_game(rule)
        g.current_round = g.max_round * 4
        g.hu_class = kind
        await g._settle_round()
        assert g._compute_last_renchan()
        assert g.next_status == 'round_end_by_ready'
    asyncio.run(run())


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('kind', ['angang', 'gang', 'jiagang'])
@pytest.mark.parametrize('timing', ['after_discard', 'immediate'])
@pytest.mark.parametrize('enabled', [False, True])
def test_kan_dora_reaches_all_players_at_the_correct_time(rule, kind, timing, enabled):
    data = asyncio.run(run_kan_case(rule, kind, timing, enabled))
    for seat in range(4):
        live = [m['payload'] for m in data['messages'] if m['seat'] == seat and m['phase'] == 'live']
        reveals = [p for p in live if p['type'].endswith('/update_dora')]
        assert len(reveals) == int(enabled)
        if enabled:
            assert reveals[0]['kan_dora_indicators'] == data['indicators']
            assert reveals[0]['dora_info']['new_indicator'] == data['indicators'][0]
            actions = [p.get('do_action_info', {}).get('action_list') for p in live]
            reveal_index = live.index(reveals[0])
            replacement_index = actions.index(['deal_gang_tile'])
            discard_index = max(i for i, a in enumerate(actions) if a == ['cut'])
            assert replacement_index < reveal_index
            assert (reveal_index < discard_index) is (kind == 'angang' or timing == 'immediate')
        restored = [m['payload']['game_info'] for m in data['messages']
                    if m['seat'] == seat and m['phase'] == 'reconnect'
                    and m['payload']['type'].endswith('/game_start')]
        assert restored[-1]['kan_dora_indicators'] == data['indicators']


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('tile', [35, 2035])
def test_robbed_added_kan_does_not_draw_or_broadcast_a_new_indicator(rule, tile):
    async def run():
        g, sockets = transparent_game(rule)
        g.current_player_index = 0
        caller, winner = g.player_list[:2]
        caller.hand_tiles = [2011,2012,2013,2021,2022,2023,2031,2032,2033,2045,tile]
        caller.combination_tiles = ['k35']
        caller.combination_mask = [[0, 2035, 1, 2035, 0, 2035 if tile == 35 else 35]]
        caller.has_draw_slot = True
        winner.hand_tiles = [2012,2013,2014,2022,2023,2024,2033,2034,2036,2037,2038,2028,2028]
        winner.tag_list = ['riichi', 'ippatsu']
        refresh_waiting_tiles(g, 1)
        before = list(g.tiles_list)
        await begin_kan(g, 'jiagang', tile)
        assert g.game_status == 'waiting_action_qianggang'
        await _broadcast_hu_and_end(g, 1, g.action_dict[1][0], tile)
        assert caller.combination_tiles == ['k35']
        assert winner.hand_tiles[-1] == tile
        assert g.total_kans == 0 and not g.kan_dora_indicators
        assert g.tiles_list == before
        for connection in sockets.values():
            assert not any(c.args[0]['type'].endswith('/update_dora')
                           for c in connection.websocket.send_json.call_args_list)
    asyncio.run(run())


@pytest.mark.parametrize('rule', SUB_RULES)
@pytest.mark.parametrize('indicator', [21, 2021])
def test_deferred_dora_matches_live_spectators_reconnect_and_record(rule, indicator):
    async def run():
        g, sockets = transparent_game(rule)
        # Exercise the actual spectator forwarding, including all four host views.
        del g.send_to_realtime_spectators
        viewers = []
        for seat, player in enumerate(g.player_list):
            conn = SimpleNamespace(websocket=AsyncMock())
            uid = 500 + seat
            g.game_server.user_id_to_connection[uid] = conn
            viewers.append(conn)
        g.realtime_spectators = [SimpleNamespace(user_id=500+i, host_user_id=p.user_id)
                                 for i, p in enumerate(g.player_list)]
        g.spectator_manager = SpectatorManager(g, delay=0)
        g.spectator_manager.current_round_index = 1
        # Equal indicators must remain distinct; transparency IDs travel unchanged.
        g.tiles_list[-4:] = [indicator] * 4
        budget = len(g.tiles_list) - g.dead_wall_count
        p = g.player_list[0]
        for expected_count in range(1, 5):
            p.hand_tiles.append(47)
            p.has_draw_slot = True
            g._pending_kan_dora_count = 1
            assert await _execute_cut(g, 0, 47, True, None, False)
            assert g.kan_dora_indicators == [indicator] * expected_count
            for connection in [*sockets.values(), *viewers]:
                messages = [c.args[0] for c in connection.websocket.send_json.call_args_list]
                reveals = [m for m in messages if m['type'].endswith('/update_dora')]
                assert len(reveals) == expected_count
                assert reveals[-1]['kan_dora_indicators'] == g.kan_dora_indicators
                cut = max(i for i, m in enumerate(messages)
                          if m.get('do_action_info', {}).get('action_list') == ['cut'])
                assert messages.index(reveals[-1]) > cut
            assert len(g.tiles_list) - g.dead_wall_count == budget
        assert await g._reveal_kan_dora() is None
        ticks = g.game_record['game_round']['round_index_1']['action_ticks']
        assert [t for t in ticks if t[0] == 'dora'] == [['dora', indicator]] * 4
        assert [t['tick'] for t in g.spectator_manager.round_ticks[1] if t['tick'][0] == 'dora'] == [['dora', indicator]] * 4
        await broadcast_game_start(g)
        for connection in [*sockets.values(), *viewers]:
            start = connection.websocket.send_json.call_args_list[-1].args[0]['game_info']
            assert start['kan_dora_indicators'] == [indicator] * 4
    asyncio.run(run())
