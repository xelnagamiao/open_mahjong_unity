"""Qingque unlimited games finish through normal settlement, never mid-hand."""
import asyncio
import importlib
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

import pytest

from .QingqueGameState import QingqueGameState
from .boardcast import _build_game_start_payload_for_viewer
from .unlimited_session import UnlimitedSession
from ..gamestate_router import handle_unlimited_end, handle_vote_start
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer
from ...response import GameInfo
from ...room.room_router import handle_create_Qingque_room
from ...room.test_guobiao_flowers import room_manager


def make_state(enabled=True):
    room = dict(player_list=[100, 101, 102, 103], room_id=1, room_rule="qingque",
                room_type="custom", sub_rule="qingque/standard", round_timer=20,
                step_timer=5, game_round=4, tips=False, allow_spectator=True,
                unlimited_rounds=enabled)
    connections = {uid: NS(user_id=uid, websocket=NS(send_json=AsyncMock())) for uid in room["player_list"]}
    server = NS(user_id_to_connection=connections, players={str(uid): c for uid, c in connections.items()},
                gamestate_manager=NS(cleanup_game_state_complete=AsyncMock()))
    calculation, database = Mock(), Mock()
    calculation.Qingque_tingpai_check.return_value = []
    calculation.GetBasePoint.return_value = 16
    database.store_qingque_game_record.return_value = None
    state = QingqueGameState(server, room, calculation, database, "unlimited-test")
    state.game_status = "waiting_hand_action"
    state.send_to_realtime_spectators = AsyncMock()
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
    server.gamestate_manager.get_game_state_by_gamestate_id = lambda gid: state if gid == state.gamestate_id else None
    return state


@pytest.mark.parametrize("enabled", [False, True])
def test_room_creation_keeps_flag_and_action_timers(enabled):
    async def run():
        manager = room_manager()
        response = await manager.create_Qingque_room("connection", "青雀", 2, "", 20, 5, False, unlimited_rounds=enabled)
        assert response.success, response.message
        assert response.room_info["unlimited_rounds"] is enabled
        assert response.room_info["game_round"] == (4 if enabled else 2)
        assert response.room_info["round_timer"] == 20 and response.room_info["step_timer"] == 5
        assert manager.get_room_list().room_list[0]["unlimited_rounds"] is enabled
    asyncio.run(run())


@pytest.mark.parametrize("value", [None, 0, 1, "false", "true", []])
def test_room_rejects_invalid_flags(value):
    manager = room_manager()
    result = asyncio.run(manager.create_Qingque_room("connection", "青雀", 4, "", 20, 5, False, unlimited_rounds=value))
    assert not result.success and not manager.rooms


@pytest.mark.parametrize("enabled", [None, False, True])
def test_creation_router_preserves_legacy_default(enabled):
    async def run():
        server = NS(players={}, create_Qingque_room=AsyncMock(return_value=NS(dict=lambda **kw: {})))
        payload = dict(roomname="青雀", gameround=4, password="", roundTimerValue=20, stepTimerValue=5, tips=False)
        if enabled is not None:
            payload["unlimited_rounds"] = enabled
        await handle_create_Qingque_room(server, "host", payload, NS(send_json=AsyncMock()))
        assert server.create_Qingque_room.await_args.kwargs["unlimited_rounds"] is (enabled is True)
    asyncio.run(run())


def test_multiple_players_can_withdraw_and_return_independently(monkeypatch):
    async def run():
        state = make_state()
        session = state.unlimited_session
        clock = [100.0]
        monkeypatch.setattr("server.gamestate.game_mmcr.unlimited_session.monotonic", lambda: clock[0])
        assert (await session.set_withdrawn(100, True))[0]
        assert (await session.set_withdrawn(101, True))[0]
        assert session.should_end_after_round()
        assert state.game_status == "waiting_hand_action"
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_not_awaited()
        assert (await session.set_withdrawn(100, False))[0]
        snapshot = session.snapshot()
        assert [p["user_id"] for p in snapshot["withdrawn_players"]] == [101]
        assert snapshot["returned_player"]["user_id"] == 100 and snapshot["countdown"] == 5
        assert session.should_end_after_round()
        clock[0] += 2
        await session.set_withdrawn(100, False)  # Retry must not restart the five seconds.
        assert session.snapshot()["countdown"] == 3
        clock[0] += 3
        assert session.snapshot()["returned_player"] is None
        assert session.should_end_after_round()
        await session.set_withdrawn(101, False)
        assert not session.should_end_after_round()
        for conn in state.game_server.user_id_to_connection.values():
            payload = conn.websocket.send_json.call_args.args[0]
            assert payload["unlimited_info"]["withdrawn_players"] == []
        assert state.send_to_realtime_spectators.await_count == 20
    asyncio.run(run())


@pytest.mark.parametrize("who,value,enabled,status", [
    (999, True, True, "waiting_hand_action"), (0, True, True, "waiting_hand_action"),
    (100, True, False, "waiting_hand_action"), (100, "true", True, "waiting_hand_action"),
    (100, None, True, "waiting_hand_action"), (100, True, True, "END"),
    (100, True, True, "waiting_ready"), (100, False, True, "finished"),
])
def test_invalid_or_late_requests_do_not_change_game(who, value, enabled, status):
    state = make_state(enabled)
    state.game_status = status
    ok, message = asyncio.run(state.unlimited_session.set_withdrawn(who, value))
    assert not ok and message and not state.unlimited_session.withdrawn


def test_router_uses_authenticated_player_and_blocks_legacy_immediate_end():
    async def run():
        state = make_state()
        socket = NS(send_json=AsyncMock())
        await handle_unlimited_end(state.game_server, "100", dict(gamestate_id=state.gamestate_id, user_id=101, withdrawn=True), socket)
        assert set(state.unlimited_session.withdrawn) == {100}
        await handle_vote_start(state.game_server, "100", dict(gamestate_id=state.gamestate_id, vote_type="end"), socket)
        assert socket.send_json.call_args.args[0]["success"] is False
        assert not hasattr(state, "vote_manager")
    asyncio.run(run())


def test_game_start_and_reconnect_preserve_request_after_seat_change(monkeypatch):
    async def run():
        state = make_state()
        await state.unlimited_session.set_withdrawn(100, True)
        state.player_list[0].player_index, state.player_list[1].player_index = 1, 0
        state.player_list.sort(key=lambda p: p.player_index)
        payload = GameInfo(**_build_game_start_payload_for_viewer(state, 1)).dict()
        assert payload["unlimited_rounds"] is True
        assert payload["unlimited_info"]["withdrawn_players"][0]["user_id"] == 100
        module = importlib.import_module(QingqueGameState.__module__)
        monkeypatch.setattr(module, "reconnected_send_pending_ask", AsyncMock())
        await state.player_reconnect(100)
        payload = state.game_server.user_id_to_connection[100].websocket.send_json.call_args.args[0]
        assert payload["game_info"]["unlimited_info"]["withdrawn_players"][0]["user_id"] == 100
    asyncio.run(run())


@pytest.mark.parametrize("hu_class", ["hu_self", "liuju"])
@pytest.mark.parametrize("continue_once", [False, True])
def test_real_game_loop_settles_requested_hand_and_can_continue_past_sixteen(monkeypatch, hu_class, continue_once):
    async def run():
        module = importlib.import_module(QingqueGameState.__module__)
        state = make_state()
        state.Debug = True
        state.current_round = 16 if continue_once else 2
        state.round_index = state.current_round
        state.broadcast_game_start = AsyncMock()
        state.broadcast_do_action = AsyncMock()
        state.broadcast_game_end = AsyncMock()
        state.spectator_manager.send_final_record_and_close = AsyncMock()
        started = []

        async def finish_hand():
            started.append(state.current_round)
            assert (await state.unlimited_session.set_withdrawn(100, True))[0]
            if continue_once and len(started) == 1:
                assert (await state.unlimited_session.set_withdrawn(100, False))[0]
            state.hu_class = hu_class
            state.result_dict = {hu_class: (4, ["门前清"])} if hu_class != "liuju" else {}
            state.game_status = "END"

        state.broadcast_ask_hand_action = finish_hand
        state.wait_action = AsyncMock()
        monkeypatch.setattr(module, "check_action_hand_action", lambda *a, **kw: {})
        monkeypatch.setattr(module, "run_result_ready_phase", AsyncMock())
        monkeypatch.setattr(module, "liuju_ready_wait_seconds", lambda: 0)
        try:
            await state.game_loop_qingque()
            await asyncio.gather(*(drain_viewer(state, i) for i in range(4)))
            assert started == ([16, 17] if continue_once else [2])
            state.broadcast_game_end.assert_awaited_once()
            state.db_manager.store_qingque_game_record.assert_called_once()
            state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
            assert state.game_record["game_title"]["unlimited_rounds"] is True
            assert len(state.game_record["game_round"]) == len(started)
            result = [c.args[0]["show_result_info"]
                      for c in state.game_server.user_id_to_connection[100].websocket.send_json.call_args_list
                      if c.args[0]["type"] == "gamestate/qingque/show_result"]
            assert result[-1]["next_status"] == "match_end"
            if continue_once:
                assert result[0]["next_status"] == "round_end_by_ready"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
