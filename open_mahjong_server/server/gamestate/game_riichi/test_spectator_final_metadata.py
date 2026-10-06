"""延时观战终局使用正式牌谱的最终供托、实点和顺位点。"""
import asyncio
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_double_riichi import make_game
from .test_sanma import sanma
from ..public.spectator_manager import SpectatorManager


@pytest.mark.parametrize('preset', ['sanma_majsoul', 'sanma_tenhou', 'standard'])
@pytest.mark.parametrize('finalized', [False, True])
def test_spectator_final_title_matches_saved_record_without_live_leak(preset, finalized):
    game = make_game(0)[0] if preset == 'standard' else sanma(preset)
    count = len(game.player_list)
    initial = dict(rule='riichi', sub_rule=game.sub_rule, player_count=count)
    game.game_record['game_title'] = dict(initial, start_time='2026-10-05T00:00:00')
    if finalized:
        game.game_record['game_title'].update(
            riichi_final_scores=[35000] * count,
            riichi_final_sticks=0,
            riichi_points={str(i): float((count - 1 - 2 * i) * 10) for i in range(count)},
        )
    manager = SpectatorManager(game)
    manager.game_title = dict(initial)
    manager.round_headers = {1: dict(timestamp=0, data=dict(seats=list(range(count))))}
    manager.round_ticks = {1: [dict(timestamp=0, tick=['end'])]}

    live = manager._build_spectator_record(1)
    assert 'riichi_final_scores' not in live['game_title']
    assert 'riichi_points' not in live['game_title']
    connection = SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
    manager.spectator_connections[999] = connection
    asyncio.run(manager.send_final_record_and_close())
    message = connection.websocket.send_json.call_args.args[0]
    complete = json.loads(message['message_info']['content'])
    assert message['type'] == 'spectator/record_complete'
    assert complete['game_title']['player_count'] == count
    assert complete['game_title']['start_time'] == game.game_record['game_title']['start_time']
    assert complete['game_round']['round_index_1']['seats'] == list(range(count))
    for key in ('riichi_final_scores', 'riichi_final_sticks', 'riichi_points'):
        assert complete['game_title'].get(key) == game.game_record['game_title'].get(key)
    if finalized:
        snapshot = manager._build_full_record()
        game.game_record['game_title']['riichi_final_scores'][0] += 1000
        game.game_record['game_title']['riichi_points']['0'] += 1
        assert snapshot['game_title']['riichi_final_scores'] == complete['game_title']['riichi_final_scores']
        assert snapshot['game_title']['riichi_points'] == complete['game_title']['riichi_points']
