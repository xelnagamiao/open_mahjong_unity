import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .ZhongyongGameState import ZhongyongGameState
from ..game_beijing.BeijingGameState import BeijingGameState
from ..game_duocai.DuocaiGameState import DuocaiGameState
from ..public.outbound_pipe import close_outbound_pipes


@pytest.mark.parametrize('engine', [ZhongyongGameState, BeijingGameState, DuocaiGameState])
def test_slow_viewer_does_not_delay_other_viewers_but_flush_preserves_backpressure(engine):
    async def run():
        state = engine()
        state.claim_protection = False
        release, blocked, healthy_ask = asyncio.Event(), asyncio.Event(), asyncio.Event()
        received = {i: [] for i in range(4)}
        connections = {}
        for index, player in enumerate(state.player_list):
            async def send(packet, viewer=index):
                if viewer == 0 and packet['sequence'] == 0:
                    blocked.set()
                    await release.wait()
                received[viewer].append(packet['sequence'])
                if viewer == 3 and packet['sequence'] == 2:
                    healthy_ask.set()
            connections[player.user_id] = SimpleNamespace(websocket=SimpleNamespace(send_json=send))
        state.game_server = SimpleNamespace(user_id_to_connection=connections)
        state.send_to_realtime_spectators = AsyncMock()
        state.outbound_payloads = []
        for sequence, action in enumerate(('deal_tile', 'cut', None)):
            for viewer in range(4):
                packet = dict(player_index=viewer, sequence=sequence, action_tick=state.server_action_tick)
                if action:
                    packet['do_action_info'] = dict(action_list=[action], action_player=0)
                else:
                    packet['ask_other_action_info'] = dict(action_list=['peng', 'pass'])
                state.outbound_payloads.append(packet)
        flush = asyncio.create_task(state.flush_outbound_payloads())
        try:
            await asyncio.wait_for(blocked.wait(), 1)
            await asyncio.wait_for(healthy_ask.wait(), 1)
            assert received[0] == []
            assert received[3] == [0, 1, 2]
            assert not flush.done()
            release.set()
            await asyncio.wait_for(flush, 1)
            assert all(messages == [0, 1, 2] for messages in received.values())
        finally:
            release.set()
            await asyncio.wait_for(flush, 1)
            close_outbound_pipes(state)
    asyncio.run(run())
