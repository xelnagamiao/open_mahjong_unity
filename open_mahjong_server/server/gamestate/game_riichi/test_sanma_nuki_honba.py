"""拔北首巡/一发与三人荣和本场的宣言、计分、广播、牌谱回归。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_sanma import sanma
from .action_check import check_hepai
from .nuki_actions import begin_nuki
from .kan_actions import rob_pending_kan
from .wait_action import _execute_cut, wait_action


WAITING_HAND = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 29, 29]
PRESETS = ['sanma_majsoul', 'sanma_tenhou']


def connect_players(game):
    sockets = {p.user_id: SimpleNamespace(websocket=AsyncMock()) for p in game.player_list}
    game.game_server.user_id_to_connection = sockets
    return sockets


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('declarer', range(3))
@pytest.mark.parametrize('extractor', range(3))
def test_committed_nuki_before_first_discard_allows_only_normal_riichi(preset, declarer, extractor):
    async def run():
        game = sanma(preset)
        sockets = connect_players(game)
        game.player_list[declarer].hand_tiles = list(WAITING_HAND)
        game.current_player_index = extractor
        extracting = game.player_list[extractor]
        extracting.hand_tiles.append(44)
        extracting.has_draw_slot = True
        await begin_nuki(game)
        assert extracting.huapai_list == [44]
        game.current_player_index = declarer
        player = game.player_list[declarer]
        player.hand_tiles.append(47)
        player.has_draw_slot = True
        assert await _execute_cut(game, declarer, 47, True, None, True)
        assert 'riichi' in player.tag_list and 'ippatsu' in player.tag_list
        assert 'daburu_riichi' not in player.tag_list
        assert player.score == 34000 and game.riichi_sticks == 1
        ticks = game.game_record['game_round']['round_index_1']['action_ticks']
        assert ticks[-1] == ['riichi', declarer, 0]
        for socket in sockets.values():
            declaration = next(c.args[0] for c in socket.websocket.send_json.call_args_list
                               if c.args[0]['type'] == 'gamestate/riichi/declare_riichi')
            tags = declaration['refresh_player_tag_list_info']['player_to_tag_list'][declarer]
            assert 'daburu_riichi' not in tags and 'riichi' in tags
    asyncio.run(run())


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('declarer', range(3))
@pytest.mark.parametrize('extractor', range(3))
def test_nuki_after_accepted_double_riichi_cancels_ippatsu_but_keeps_double(preset, declarer, extractor):
    async def run():
        game = sanma(preset)
        sockets = connect_players(game)
        player = game.player_list[declarer]
        player.hand_tiles = WAITING_HAND + [47]
        player.has_draw_slot = True
        game.current_player_index = declarer
        assert await _execute_cut(game, declarer, 47, True, None, True)
        assert {'riichi', 'daburu_riichi', 'ippatsu'} <= set(player.tag_list)
        for socket in sockets.values():
            socket.websocket.reset_mock()
        game.current_player_index = extractor
        game.game_status = 'waiting_hand_action'
        game.player_list[extractor].hand_tiles.append(44)
        game.player_list[extractor].has_draw_slot = True
        await begin_nuki(game)
        assert 'ippatsu' not in player.tag_list
        assert {'riichi', 'daburu_riichi'} <= set(player.tag_list)
        for socket in sockets.values():
            updates = [c.args[0]['refresh_player_tag_list_info']['player_to_tag_list']
                       for c in socket.websocket.send_json.call_args_list
                       if 'refresh_player_tag_list_info' in c.args[0]]
            assert updates and all('ippatsu' not in tags for tags in updates[-1].values())
        game.current_player_index = (declarer + 1) % 3
        actions = {i: [] for i in range(3)}
        check_hepai(game, actions, 36, declarer, 'ron')
        assert actions[declarer]
        result = game.result_dict[actions[declarer][0]]
        assert '双立直' in result['yaku'] and '一发' not in result['yaku']
    asyncio.run(run())


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('outcome', ['ron', 'pass', 'timeout'])
def test_nuki_interrupts_only_after_robbing_window_commits(preset, outcome):
    async def run():
        game = sanma(preset)
        connect_players(game)
        # Both supported robbery policies allow kokushi. Keep ippatsu until a
        # successful extraction; a robbed north never becomes a bonus tile.
        game.player_list[1].hand_tiles = [11, 19, 21, 29, 31, 39, 41, 42, 43, 45, 46, 47, 47]
        game.player_list[1].tag_list = ['riichi', 'ippatsu']
        game.player_list[0].hand_tiles.append(44)
        game.player_list[0].has_draw_slot = True
        await begin_nuki(game)
        assert game.game_status == 'waiting_action_qianggang'
        assert 'ippatsu' in game.player_list[1].tag_list
        assert not game.player_list[0].huapai_list and game._first_round_valid
        if outcome == 'ron':
            assert await rob_pending_kan(game)
            assert not game.player_list[0].huapai_list
            assert 'ippatsu' in game.player_list[1].tag_list
        elif outcome == 'timeout':
            game.step_time = 0
            for player in game.player_list:
                player.remaining_time = 0
            await wait_action(game)
        else:
            waiter = asyncio.create_task(wait_action(game))
            await asyncio.sleep(0)
            await game.action_queues[1].put({'action_type': 'pass'})
            game.action_events[1].set()
            await asyncio.wait_for(waiter, timeout=2)
        if outcome != 'ron':
            assert game.player_list[0].huapai_list == [44]
            assert 'ippatsu' not in game.player_list[1].tag_list
            assert not game._first_round_valid
    asyncio.run(run())


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('winner,loser', [(w, l) for w in range(3) for l in range(3) if w != l])
@pytest.mark.parametrize('honba', [0, 1, 2, 5])
def test_sanma_ron_pays_exactly_200_per_honba_in_scores_record_and_broadcast(preset, winner, loser, honba):
    game = sanma(preset)
    sockets = connect_players(game)
    game.current_player_index = loser
    game.ron_player_index = winner
    game.hu_class = 'hu_first'
    game.honba = honba
    base = 12000 if winner == 0 else 8000
    game.result_dict = {'hu_first': dict(han=5, fu=30, yaku=['满贯'], cost=dict(main=base), score=base)}
    asyncio.run(game._settle_hu())
    payment = base + 200 * honba
    expected = [payment if i == winner else -payment if i == loser else 0 for i in range(3)]
    assert [p.score for p in game.player_list] == [35000 + delta for delta in expected]
    assert sum(p.score for p in game.player_list) == 105000
    tick = game.game_record['game_round']['round_index_1']['action_ticks'][-1]
    assert tick[0] == 'hu_riichi' and tick[6] == expected
    for socket in sockets.values():
        payload = next(c.args[0]['show_result_info'] for c in socket.websocket.send_json.call_args_list
                       if c.args[0]['type'] == 'gamestate/riichi/show_result')
        assert {int(i): delta for i, delta in payload['score_changes'].items()} == dict(enumerate(expected))


@pytest.mark.parametrize('preset', PRESETS)
@pytest.mark.parametrize('loser', range(3))
def test_double_ron_awards_honba_only_to_nearest_winner(preset, loser):
    game = sanma(preset)
    game.current_player_index = loser
    game.honba = 2
    queue = [((loser + 1) % 3, 'hu_first'), ((loser + 2) % 3, 'hu_second')]
    game.result_dict = {action: dict(han=5, fu=30, yaku=['满贯'], cost=dict(main=12000 if seat == 0 else 8000),
                                    score=12000 if seat == 0 else 8000) for seat, action in queue}
    with patch('server.gamestate.game_riichi.RiichiGameState.run_synced_hu_ready_phase', new=AsyncMock()), \
         patch('server.gamestate.game_riichi.RiichiGameState.asyncio.sleep', new=AsyncMock()):
        asyncio.run(game._settle_multi_ron_sequence(queue))
    ticks = [t for t in game.game_record['game_round']['round_index_1']['action_ticks'] if t[0] == 'hu_riichi']
    for index, (winner, action) in enumerate(queue):
        payment = game.result_dict[action]['score'] + (400 if index == 0 else 0)
        assert ticks[index][6] == [payment if seat == winner else -payment if seat == loser else 0 for seat in range(3)]
    assert sum(p.score for p in game.player_list) == 105000


def test_robbing_north_can_score_ippatsu_before_extraction_is_committed():
    game = sanma('sanma_tenhou')
    game.detailed_config['nuki_ron'] = 'all'
    game.player_list[1].hand_tiles = [21,21,22,22,24,24,26,26,31,31,35,35,44]
    game.player_list[1].tag_list = ['riichi', 'ippatsu']
    game.player_list[0].hand_tiles.append(44)
    game.player_list[0].has_draw_slot = True
    asyncio.run(begin_nuki(game))
    hu = next(action for action in game.action_dict[1] if action.startswith('hu_'))
    assert '一发' in game.result_dict[hu]['yaku']
    assert not game.player_list[0].huapai_list
