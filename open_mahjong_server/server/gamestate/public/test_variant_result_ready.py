"""Non-four-player tables acknowledge results through the production router."""
import asyncio
from contextlib import suppress
import importlib

import pytest

from . import settlement_sync
from .lifecycle import cancel_auxiliary_tasks
from .test_ready_phase import until
from ..gamestate_router import handle_gamestate_message
from ..verifier.host import build_db_manager, build_game_server, build_room_data


@pytest.mark.parametrize("rule,count", [("guobiao", 3), ("malaysia", 3),
    ("beijing", 4), ("shangrao", 4), ("duocai", 4)])
def test_ready_router_preserves_other_seats_and_restoration(rule, count):
    async def run():
        db = build_db_manager()
        server = build_game_server(db, messages=[])
        if rule == "guobiao":
            from ..game_guobiao.GuobiaoGameState import GuobiaoGameState
            from ..game_guobiao.broadcast import broadcast_result
            room = build_room_data(seed=20261115, room_id="825016", tips=False)
            room.update(sub_rule="guobiao/sanma", player_list=room["player_list"][:3])
            state = GuobiaoGameState(server, room, server.calculation_service, db, "variant-ready")
            for seat, p in enumerate(state.player_list):
                p.player_index = p.original_player_index = seat
            state.game_status = "END"
            await broadcast_result(state, hu_class="hu_self", hepai_player_index=0,
                player_to_score={p.player_index: p.score for p in state.player_list},
                hu_score=8, hu_fan=[], hepai_player_hand=[11] * 14,
                next_status="round_end_by_ready")
            operation = state.run_hu_result_ready_phase(0)
        else:
            name = rule.capitalize() + "GameState"
            cls = getattr(importlib.import_module(f"server.gamestate.game_{rule}.{name}"), name)
            state = cls(calculation_service=object())
            state.initialize_round()
            state.machine.phase = type(state.machine.phase)("END")
            state.game_server = server
            for player, uid in zip(state.player_list, server.user_id_to_connection):
                player.user_id = uid
            for seat in range(count):
                await state.send_payload_to_player(seat, {
                    "type": f"gamestate/{rule}/show_result",
                    "show_result_info": {"hu_class": "hu_self", "hepai_player_index": 0,
                        "next_status": "round_end_by_ready",
                        "player_to_score": {i: p.score for i, p in enumerate(state.player_list)}}})
            operation = state.run_round_ready_phase(timeout=8)
        server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
        assert len(state.player_list) == count
        original_seats = [p.user_id for p in state.player_list]
        task = asyncio.create_task(operation)
        try:
            await until(lambda: state.game_status == "waiting_ready", task)
            result_id = settlement_sync.snapshot(state)["result_id"]
            assert result_id
            for seat, player in enumerate(state.player_list):
                restored = settlement_sync.restore_result(state, seat)
                assert restored["settlement_sync"]["result_id"] == result_id
                channel = "GB" if rule == "guobiao" else rule
                tick = -1 if rule == "guobiao" else state.build_ready_status_payload(seat)["ready_status_info"]["action_tick"]
                await handle_gamestate_message(server, f"conn-{player.user_id}", {
                    "type": f"gamestate/{channel}/send_action", "gamestate_id": state.gamestate_id,
                    "action": "ready", "settlement_id": result_id, "action_tick": tick,
                }, server.user_id_to_connection[player.user_id].websocket)
                await until(lambda: not state.action_dict[seat], task)
                if seat < count - 1:
                    assert not task.done()
                    assert settlement_sync.snapshot(state)["player_to_ready"] == {
                        str(i): i <= seat for i in range(count)}
            await asyncio.wait_for(task, .5)
            assert not any(state.action_dict.values())
            assert [p.user_id for p in state.player_list] == original_seats
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
