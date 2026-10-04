"""A confirmed ron must identify its real source, including fanless rob-kong."""
import asyncio
import importlib
from unittest.mock import AsyncMock

import pytest

from .test_lanshi_v4_integration import state
from ...game_calculation.test_lanshi_v4 import tiles


@pytest.mark.parametrize('source', range(4))
@pytest.mark.parametrize('robbed', [False, True])
def test_settlement_preserves_source_after_pending_kong_is_cleared(monkeypatch, source, robbed):
    module = importlib.import_module('server.gamestate.game_guobiao.GuobiaoGameState')
    gs = state()
    gs.current_round = gs.round_index = gs.max_round = 1
    gs.room_random_seed = 20261003
    gs.claim_protection = gs.tactical_call = False
    winner = (source + 1) % 4
    captured = []

    def deal(s):
        s.round_random_seed = 20261003
        s.tiles_list = [22] * 4
        for player in s.player_list:
            player.hand_tiles = tiles('123456789m123p1s')
            player.combination_tiles = []; player.combination_mask = []
            player.discard_tiles = []; player.huapai_list = []
            player.waiting_tiles = set(); player.has_draw_slot = False
            player.last_drawn_tile = None
        s.player_list[0].hand_tiles.append(31)

    async def confirmed_ron():
        # Isolate the transition after adjudication. Full physical-wall/action
        # validation covers scoring separately; this checks wire presentation.
        gs.current_player_index = source
        gs.player_list[winner].hand_tiles = tiles('19m19p19s1123467z')
        gs.player_list[source].discard_tiles = [22 if robbed else 45]
        gs.jiagang_tile = 45 if robbed else None
        gs.hu_class = 'hu_first'
        gs.result_dict = {'hu_first': (50, ['十三幺', '和绝张'])}
        gs.game_status = 'check_hepai'

    class Captured(Exception):
        pass

    async def result(s, **payload):
        captured.append(payload)
        raise Captured

    monkeypatch.setattr(module, 'init_guobiao_tiles', deal)
    monkeypatch.setattr(module, 'broadcast_result', result)
    gs.broadcast_game_start = AsyncMock()
    gs.broadcast_ask_hand_action = AsyncMock()
    gs.wait_action = confirmed_ron
    with pytest.raises(Captured):
        asyncio.run(asyncio.wait_for(gs.game_loop_chinese(), 3))
    payload, = captured
    assert gs.jiagang_tile is None
    assert payload['is_qianggang'] is robbed
    assert payload['ron_discarder_index'] == source
    assert payload['hepai_player_index'] == winner
    assert payload['hepai_tile'] == 45
    assert payload['hepai_player_hand'][-1] == 45
