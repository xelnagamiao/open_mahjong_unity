"""Bot rooms use ordinary cadence and never claim to hide other humans' responses."""
import asyncio
import importlib
from itertools import combinations
import time
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .ai import pacing
from .ai.auto_cut_ai import auto_cut_action
from .claim_protection import begin_claim_protection_interval, end_claim_protection_interval, init_claim_protection_state
from .outbound_pipe import close_outbound_pipes, drain_viewer
from .test_claim_protection_pacing import ADAPTERS


@pytest.mark.parametrize("rule,module", ADAPTERS)
@pytest.mark.parametrize("humans", list(combinations(range(4), 2)))
@pytest.mark.parametrize("tactical", [False, True])
@pytest.mark.parametrize("enabled", [False, True])
def test_two_humans_receive_bot_cut_together_before_any_response(monkeypatch, rule, module, humans, tactical, enabled):
    async def run():
        b = importlib.import_module(f"server.gamestate.{module}.boardcast")
        bot_seats = [i for i in range(4) if i not in humans]
        actor, claimant, observer = bot_seats[0], humans[0], humans[1]
        wire = {i: [] for i in range(4)}
        players, connections = [], {}
        for seat in range(4):
            uid = 100 + seat if seat in humans else (0 if seat == actor else 2)
            players.append(NS(user_id=uid, username=str(uid), player_index=seat, tag_list=[],
                              hand_tiles=[11, 12], has_draw_slot=True, discard_tiles=[12]))
            async def send(payload, index=seat):
                wire[index].append((time.monotonic(), payload))
            connections[uid] = NS(websocket=NS(send_json=send))
        state = NS(room_rule=rule, bot_speed="fast", claim_protection=enabled, tactical_call=tactical,
                   player_list=players, current_player_index=actor, server_action_tick=1,
                   game_status="waiting_hand_action", waiting_players_list=[actor], lifecycle_state="running",
                   game_server=NS(user_id_to_connection=connections), send_to_realtime_spectators=AsyncMock())
        init_claim_protection_state(state)
        delay = .04
        monkeypatch.setitem(pacing.BOT_SPEEDS, "fast", delay)
        async def submitted(gs, seat, action, *args, **kwargs):
            assert action == "cut"
            begin_claim_protection_interval(gs, {claimant: ["peng", "pass"]}, seat)
            await b.broadcast_do_action(gs, ["cut"], seat, cut_tile=12)
        monkeypatch.setattr("server.gamestate.public.ai.auto_cut_ai.get_ai_action", submitted)
        started = time.monotonic()
        try:
            await auto_cut_action(state, actor, ["cut"], "waiting_hand_action")
            assert not state._cp_active
            assert not state._cp_pending_cut
            assert state._cp_timer_task is None
            assert state._cp_discard_delivery is None
            for viewer in humans:
                assert len(wire[viewer]) == 1  # No human has replied, yet both already see the cut.
                assert wire[viewer][0][1]["do_action_info"]["cut_tile"] == 12
            tolerance = max(.01, time.get_clock_info("monotonic").resolution)
            assert wire[claimant][0][0] - started >= delay - tolerance
            assert abs(wire[claimant][0][0] - wire[observer][0][0]) < .025
            await b.broadcast_do_action(state, ["peng"], claimant, cut_tile=12)
            await asyncio.gather(*(drain_viewer(state, i) for i in humans))
            for viewer in humans:
                assert [p["do_action_info"]["action_list"] for _, p in wire[viewer]] == [["cut"], ["peng"]]
        finally:
            close_outbound_pipes(state)
            end_claim_protection_interval(state)
    asyncio.run(run())
