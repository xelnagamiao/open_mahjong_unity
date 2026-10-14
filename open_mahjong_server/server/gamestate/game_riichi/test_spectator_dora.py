"""Dora delivery must agree for seated, realtime, delayed and reconnect viewers."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_sanma import sanma
from .test_double_riichi import make_game
from .test_sanma_nuki_honba import connect_players
from .init_tiles import init_riichi_tiles
from .boardcast import broadcast_game_start
from ..public.spectator_manager import SpectatorManager


@pytest.mark.parametrize('rule', ['sanma_majsoul', 'sanma_tenhou', 'four'])
@pytest.mark.parametrize('rotation', [0, 1, 2])
@pytest.mark.parametrize('enabled', [False, True])
def test_dora_all_delivery_paths_and_five_indicator_limit(rule, rotation, enabled):
    async def run():
        g = make_game(0)[0] if rule == 'four' else sanma(rule)
        g.room_id = 825014
        sockets = connect_players(g)
        count = len(g.player_list)
        for i, p in enumerate(g.player_list):
            p.hand_tiles = []
            p.original_player_index = (i + rotation) % count
        g.master_seed = 20261014
        init_riichi_tiles(g)
        g.detailed_config['kan_dora'] = enabled
        g.spectator_manager = SpectatorManager(g, delay=0)
        g.spectator_manager.current_round_index = 1
        spectators = []
        for seat in range(count):
            uid = 500 + seat
            g.game_server.user_id_to_connection[uid] = SimpleNamespace(websocket=AsyncMock())
            spectators.append(g.game_server.user_id_to_connection[uid])
        g.realtime_spectators = [SimpleNamespace(user_id=500+i, host_user_id=p.user_id)
                                 for i, p in enumerate(g.player_list)]
        # Equal tile faces are distinct physical indicators; never deduplicate them.
        g._dora_slots = [21] * 5
        g._ura_slots = [22] * 5
        for slot in range(5):
            g.tiles_list[-6-2*slot] = 21
            g.tiles_list[-5-2*slot] = 22
        g.dora_indicators = [21]
        for reveal in range(5):
            await g._reveal_kan_dora()
            expected = min(reveal+1, 4) if enabled else 0
            assert g.kan_dora_indicators == [21] * expected
            for connection in [*sockets.values(), *spectators]:
                messages = [c.args[0] for c in connection.websocket.send_json.call_args_list
                            if c.args[0]['type'] == 'gamestate/riichi/update_dora']
                assert len(messages) == expected
                if messages:
                    assert messages[-1]['dora_indicators'] == [21]
                    assert messages[-1]['kan_dora_indicators'] == [21] * expected
        expected_ticks = [['dora', 21]] * (4 if enabled else 0)
        assert g.game_record['game_round']['round_index_1']['action_ticks'] == expected_ticks
        assert [t['tick'] for t in g.spectator_manager.round_ticks.get(1, [])] == expected_ticks
        await broadcast_game_start(g)
        for connection in [*sockets.values(), *spectators]:
            messages = [c.args[0]['game_info'] for c in connection.websocket.send_json.call_args_list
                        if c.args[0]['type'] == 'gamestate/riichi/game_start']
            assert len(messages) == 1
            assert messages[0]['dora_indicators'] == [21]
            assert messages[0]['kan_dora_indicators'] == [21] * (4 if enabled else 0)
    asyncio.run(run())
