"""Exercise settlement -> authenticated ready -> next round in the real loop."""
import asyncio
import importlib
from contextlib import suppress
from unittest.mock import AsyncMock

import pytest

from .RiichiGameState import RiichiGameState
from ..gamestate_router import handle_gamestate_message
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.settlement_sync import restore_result
from ..public.test_ready_phase import until
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ...game_calculation.riichi.rule_config import preset_room_config


@pytest.mark.parametrize("preset", ["sanma_majsoul", "sanma_tenhou", "majsoul", "tenhou"])
@pytest.mark.parametrize("outcome", ["dealer_tsumo", "child_tsumo", "ron", "draw", "abort"])
def test_result_identity_and_seats_survive_until_every_human_readies(monkeypatch, preset, outcome):
    async def run():
        db = build_db_manager()
        messages = []
        server = build_game_server(db, messages=messages)
        room = build_room_data(seed=20261115, room_id="825015", game_round=2, tips=False)
        room.update(preset_room_config(preset), room_rule="riichi")
        count = 3 if preset.startswith("sanma") else 4
        room["player_list"] = room["player_list"][:count]
        state = RiichiGameState(server, room, server.calculation_service, db, "ready-lifecycle")
        server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
        module = importlib.import_module("server.gamestate.game_riichi.RiichiGameState")
        # Only the play preceding settlement is scripted; settlement, delivery,
        # identity validation, ready collection and round advancement are real.
        monkeypatch.setattr(module, "check_action_hand_action", lambda s, i, **kw: {j: [] for j in range(count)})
        state.refresh_waiting_tiles = lambda *_: None
        state.broadcast_ask_hand_action = AsyncMock()
        state.broadcast_do_action = AsyncMock()
        original_wait = state.wait_action
        original_start = state.broadcast_game_start
        initial_order = []
        next_round = asyncio.Event()

        async def start():
            if initial_order:
                next_round.set()
                await asyncio.Event().wait()
            initial_order.extend(p.user_id for p in state.player_list)
            await original_start()

        async def finish_hand():
            if state.game_status == "waiting_ready":
                return await original_wait()
            state.game_status = "END"
            state.current_player_index = 0 if outcome == "dealer_tsumo" else 1
            for p in state.player_list:
                p.waiting_tiles = set()
            if outcome in ("draw", "abort"):
                state.hu_class = "ryuukyoku" if outcome == "draw" else "jiuzhongjiupai"
            else:
                state.hu_class = "hu_first" if outcome == "ron" else "hu_self"
                state.ron_player_index = 2 if outcome == "ron" else None
                state.result_dict = {state.hu_class: dict(han=1, fu=30, yaku=["断幺九"],
                    score=1500 if outcome == "dealer_tsumo" else 1000,
                    cost={"main": 500 if outcome == "dealer_tsumo" else 1000, "additional": 500})}

        state.wait_action = finish_hand
        state.broadcast_game_start = start
        task = asyncio.create_task(state.game_loop_riichi())
        try:
            await until(lambda: state.game_status == "waiting_ready", task)
            result = restore_result(state, 0)
            assert result is not None, "ready must retain the just-presented result"
            assert state.round_index == state.current_round == 1
            assert [p.user_id for p in state.player_list] == initial_order
            result_id = result["settlement_sync"]["result_id"]
            assert result_id
            for seat, uid in enumerate(initial_order):
                restored = restore_result(state, seat)
                assert restored["settlement_sync"]["result_id"] == result_id
                socket = server.user_id_to_connection[uid].websocket
                packet = dict(type="gamestate/GB/send_action", gamestate_id=state.gamestate_id,
                    action="ready", settlement_id="stale-result", action_tick=-1, targetTile=0)
                await handle_gamestate_message(server, f"conn-{uid}", packet, socket)
                assert state.action_dict[seat] == ["ready"]
                packet["settlement_id"] = result_id
                await handle_gamestate_message(server, f"conn-{uid}", packet, socket)
                await until(lambda: not state.action_dict[seat], task)
                if seat < count - 1:
                    assert not next_round.is_set()
                    assert state.round_index == 1
                    assert [p.user_id for p in state.player_list] == initial_order
                    status = restore_result(state, seat)["settlement_sync"]["player_to_ready"]
                    assert status == {str(i): i <= seat for i in range(count)}
            await asyncio.wait_for(next_round.wait(), .5)
            assert state.round_index == 2
            renchan = outcome in ("dealer_tsumo", "abort")
            assert state.current_round == (1 if renchan else 2)
            assert [p.user_id for p in state.player_list] == (
                initial_order if renchan else initial_order[1:] + initial_order[:1])
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
