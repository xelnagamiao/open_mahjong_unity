"""Exercise the public factory and socket router with the actual Wenzhou core.

Only the room transport/database shell and background game-loop scheduling are
substituted. State construction, action authorization/queues, transitions, and
cleanup all use production code. This is intentionally separate from scorer
tests so a missing public registration cannot be hidden by direct construction.
"""
import asyncio
from types import SimpleNamespace
import unittest
from unittest.mock import AsyncMock, Mock, patch

from server.gamestate import gamestate_manager as factory
from server.gamestate.gamestate_router import handle_gamestate_message
from server.gamestate.game_wenzhou.WenzhouGameState import WenzhouGameState
from server.gamestate.game_wenzhou.state_machine import Phase
from server.gamestate.game_wenzhou.tests.helpers import SocketRecorder, assert_conserved


class WenzhouFactoryRouterTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        room = WenzhouGameState._default_room_data()
        room.update(room_id="factory-wenzhou", host_user_id=101, allow_spectator=False,
                    round_timer=20, step_timer=5, instance_id="factory-room-instance",
                    detailed_config={"edition": "mil-wenzhou-2024-om1"})
        self.room = room
        self.connections = {
            str(uid): SimpleNamespace(user_id=uid, websocket=SocketRecorder())
            for uid in room["player_list"] + [999]
        }
        self.server = SimpleNamespace(
            players=self.connections,
            user_id_to_connection={int(uid): conn for uid, conn in self.connections.items()},
            room_manager=SimpleNamespace(rooms={room["room_id"]: room},
                active_game_room_ids={}, _sync_room_host=Mock(), all_players_ready=Mock(return_value=True),
                finish_custom_game_room=AsyncMock()),
            db_manager=SimpleNamespace(get_user_settings=Mock(return_value={})),
            calculation_service=object(),
        )
        self.manager = factory.GameStateManager(self.server)
        self.server.gamestate_manager = self.manager
        async def hold_game_loop(state, loop):
            await asyncio.Event().wait()
        # Delay the real loop so each test can deterministically drive its
        # production action window. The state itself is never mocked.
        with patch.object(factory, "attach_game_frames"), patch.object(self.manager, "run_game", hold_game_loop):
            result = await self.manager.start_game("101", room["room_id"])
        self.assertIsNone(result, getattr(result, "message", "factory rejected Wenzhou"))
        self.state = self.manager.get_game_state_by_room_id(room["room_id"])
        self.assertIsInstance(self.state, WenzhouGameState)

    async def asyncTearDown(self):
        await self.manager.cleanup_game_state_complete(room_id=self.room["room_id"])

    async def route(self, uid, suffix, **fields):
        await handle_gamestate_message(self.server, str(uid), dict(
            type="gamestate/wenzhou/" + suffix, gamestate_id=self.state.gamestate_id,
            action_tick=self.state.server_action_tick, **fields), self.connections[str(uid)].websocket)

    def open_turn(self):
        self.state.initialize_round()
        self.state.open_action_window(self.state.opening_window())

    async def test_factory_indexes_duplicate_guard_and_real_cleanup(self):
        state = self.state
        self.assertEqual(state.rule_version, "mil-wenzhou-2024-om1")
        self.assertEqual(state.sub_rule, "wenzhou/mil2024")
        self.assertIs(self.manager.room_id_to_WenzhouGameState[state.room_id], state)
        self.assertIs(self.manager.get_game_state_by_gamestate_id(state.gamestate_id), state)
        for uid in self.room["player_list"]:
            self.assertIs(self.manager.get_game_state_by_user_id(uid), state)
        self.assertEqual(self.server.room_manager.active_game_room_ids[state.gamestate_id], state.room_id)
        self.assertEqual(self.room["active_gamestate_id"], state.gamestate_id)
        result = await self.manager.start_game("101", state.room_id)
        self.assertFalse(result.success)
        await self.manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
        self.assertTrue(state.game_task.cancelled())
        self.assertEqual(state.lifecycle_state, "closed")
        self.assertIsNone(self.manager.get_game_state_by_room_id(state.room_id))
        self.assertTrue(all(state.room_id not in index for index in self.manager._room_indexes()))
        self.assertEqual(self.manager.user_id_to_game_state, {})
        self.assertEqual(self.manager.gamestate_id_to_game_state, {})
        self.assertEqual(self.server.room_manager.active_game_room_ids, {})
        self.server.room_manager.finish_custom_game_room.assert_awaited_once_with(
            state.room_id, expected_gamestate_id=state.gamestate_id,
            expected_instance_id="factory-room-instance")

    async def test_cut_route_commits_real_physical_transition(self):
        self.open_turn()
        tile = self.state.player_list[0].hand_tiles[-1]
        window = self.state.live_pending_window
        await self.route(101, "cut_tile", TileId=tile, cutIndex=16, cutClass=True)
        self.assertEqual(self.state.action_queues[0].qsize(), 1)
        response = self.state.action_queues[0].get_nowait()
        self.assertEqual(response["action_type"], "cut")
        self.assertEqual(response["player_index"], 0)
        self.state.apply_action_results(window, {0: response})
        self.assertEqual(self.state.player_list[0].discard_tiles, [tile])
        self.assertEqual(len(self.state.player_list[0].hand_tiles), 16)
        assert_conserved(self.state)

    async def test_send_action_ready_uses_real_queue_and_window(self):
        self.open_turn()
        self.state.end_draw()
        ready_task = asyncio.create_task(self.state.run_round_ready_phase(timeout=2))
        try:
            for _ in range(100):
                if self.state.machine.phase == Phase.READY:
                    break
                await asyncio.sleep(0)
            self.assertEqual(self.state.machine.phase, Phase.READY)
            for uid in self.room["player_list"]:
                await self.route(uid, "send_action", action="ready")
            await asyncio.wait_for(ready_task, timeout=2)
            self.assertEqual(self.state.waiting_players_list, [])
            self.assertTrue(all(queue.empty() for queue in self.state.action_queues.values()))
        finally:
            if not ready_task.done():
                ready_task.cancel()
                await asyncio.gather(ready_task, return_exceptions=True)

    async def test_stale_tick_spectator_wrong_seat_and_duplicate_are_rejected(self):
        self.open_turn()
        tile = self.state.player_list[0].hand_tiles[-1]
        message = dict(type="gamestate/wenzhou/cut_tile", gamestate_id=self.state.gamestate_id,
                       action_tick=self.state.server_action_tick - 1, TileId=tile, cutClass=True, cutIndex=16)
        await handle_gamestate_message(self.server, "101", message, self.connections["101"].websocket)
        self.assertTrue(self.state.action_queues[0].empty())
        self.assertTrue(self.connections["101"].websocket.messages)
        for uid in (102, 999):
            await self.route(uid, "cut_tile", TileId=tile, cutIndex=16, cutClass=True)
        self.assertTrue(all(queue.empty() for queue in self.state.action_queues.values()))
        await self.route(101, "cut_tile", TileId=tile, cutIndex=16, cutClass=True)
        await self.route(101, "cut_tile", TileId=tile, cutIndex=16, cutClass=True)
        self.assertEqual(self.state.action_queues[0].qsize(), 1)
        self.assertEqual(self.connections["101"].websocket.messages[-1]["type"], "tips")

    async def test_missing_closed_and_abandoned_game_do_not_dispatch(self):
        self.open_turn()
        socket = self.connections["101"].websocket
        await handle_gamestate_message(self.server, "101", dict(type="gamestate/wenzhou/send_action",
            gamestate_id="missing", action="ready"), socket)
        self.assertEqual(socket.messages[-1]["type"], "gamestate/closed")
        self.state.abandoned_users.add(101)
        await self.route(101, "send_action", action="ready")
        self.assertEqual(socket.messages[-1]["type"], "tips")
        self.assertTrue(all(queue.empty() for queue in self.state.action_queues.values()))


if __name__ == "__main__":
    unittest.main()
