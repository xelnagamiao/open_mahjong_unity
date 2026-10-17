"""Fractional Qingque fans must agree in live, spectator and recorded settlements."""
import asyncio
import importlib
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

import pytest

from .QingqueGameState import QingqueGameState
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer
from ...response import Show_result_info


@pytest.mark.parametrize("fan,base", [(1, 1), (3.60616, 13), (8.01904, 64), (8.93625, 80)])
@pytest.mark.parametrize("hu_class,winner", [("hu_self", 0), ("hu_first", 1), ("hu_second", 2), ("hu_third", 3)])
def test_live_realtime_delayed_and_record_keep_the_same_fan(monkeypatch, fan, base, hu_class, winner):
    async def run():
        module = importlib.import_module(QingqueGameState.__module__)
        room = dict(player_list=[100, 101, 102, 103], room_id=1, room_rule="qingque",
                    room_type="events", sub_rule="qingque/standard", round_timer=20,
                    step_timer=5, game_round=1, tips=False, allow_spectator=True)
        connections = {uid: NS(user_id=uid, websocket=NS(send_json=AsyncMock())) for uid in room["player_list"]}
        server = NS(user_id_to_connection=connections, players={str(uid): c for uid, c in connections.items()},
                    gamestate_manager=NS(cleanup_game_state_complete=AsyncMock()))
        calculation = Mock()
        calculation.Qingque_tingpai_check.return_value = []
        calculation.GetBasePoint.return_value = base
        database = Mock()
        database.store_qingque_game_record.return_value = None
        state = QingqueGameState(server, room, calculation, database, "fan-presentation-test")
        state.Debug = True
        state.current_round = 4  # Final hand skips ready timers and any next-hand setup.
        state.send_to_realtime_spectators = AsyncMock()
        state.broadcast_game_start = AsyncMock()
        state.broadcast_do_action = AsyncMock()
        state.broadcast_game_end = AsyncMock()
        state.spectator_manager.send_final_record_and_close = AsyncMock()
        fans = ["门前清", "一般高"]

        async def win_after_initial_deal():
            state.hu_class = hu_class
            state.result_dict = {hu_class: (fan, fans)}
            state.game_status = "END"

        state.broadcast_ask_hand_action = win_after_initial_deal
        monkeypatch.setattr(module, "check_action_hand_action", lambda *a, **kw: {})
        try:
            await state.game_loop_qingque()
            await asyncio.gather(*(drain_viewer(state, i) for i in range(4)))
            player_results = [call.args[0]["show_result_info"]
                              for conn in connections.values() for call in conn.websocket.send_json.call_args_list
                              if call.args[0]["type"] == "gamestate/qingque/show_result"]
            assert len(player_results) == 4
            expected_changes = ({i: base * (3 if i == winner else -1) for i in range(4)}
                                if hu_class == "hu_self" else {i: base * (3 if i == winner else -3 if i == 0 else 0) for i in range(4)})
            for result in player_results:
                assert result["hu_score"] == fan
                assert result["hu_fan"] == fans
                assert result["score_changes"] == expected_changes
                assert result["hepai_player_index"] == winner
            live_spectator_results = [call.args[1].show_result_info
                                      for call in state.send_to_realtime_spectators.call_args_list]
            assert len(live_spectator_results) == 4
            assert all(result.hu_score == fan for result in live_spectator_results)
            ticks = [tick for rd in state.game_record["game_round"].values()
                     for tick in rd["action_ticks"] if tick[0] == hu_class]
            assert len(ticks) == 1
            assert ticks[0][1:5] == [winner, fan, fans, [expected_changes[i] for i in range(4)]]
            delayed = [entry["tick"] for rd in state.spectator_manager.round_ticks.values()
                       for entry in rd if entry["tick"][0] == hu_class]
            assert delayed == ticks
            assert sum(expected_changes.values()) == 0
            calculation.GetBasePoint.assert_called_once_with(fan)
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("value", [0, 1, 3.60616, 8.01904, 8.93625, 88, 32000])
def test_shared_result_schema_preserves_fractional_and_integer_units(value):
    original = Show_result_info(hu_score=value, hu_class="hu_self", action_tick=1)
    decoded = Show_result_info.parse_raw(original.json())
    assert decoded.hu_score == value
