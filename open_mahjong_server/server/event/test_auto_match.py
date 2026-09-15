"""Deterministic event matchmaking lifecycle checks; no database or sockets used."""
import asyncio
import copy
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

from server.database.db_manager import DatabaseManager
from server.event.auto_match import EventAutoMatcher
from server.event.event_router import _handle_ready
from server.gamestate.gamestate_manager import GameStateManager
from server.gamestate.gamestate_router import handle_add_spectator
from server.gamestate.public.spectator_manager import SpectatorManager
from server.gamestate.public.spectator_rules import deliver_realtime_spectator_message
from server.friend.friend_manager import FriendManager, PendingRealtimeRequest, RealtimeSpectator
from server.match.match_manager import MatchManager
from server.response import Response
from server.room.room_manager import RoomManager


class MemoryDatabase:
    parse_entry_config = staticmethod(DatabaseManager.parse_entry_config)

    def __init__(self):
        self.events = {"venue": {
            "event_id": "venue", "status": "active", "entry_config": {},
            "room_settings": {
                "manual": {"room_rule": "riichi", "room_config": {"game_round": 2}},
                "auto_match": {"enabled": True, "room_rule": "guobiao", "room_config": {"game_round": 4, "round_timer": 20, "step_timer": 5}, "updated_by": 900},
            },
        }}
        self.ready = []
        self.rejected = set()

    def get_event(self, event_id):
        return copy.deepcopy(self.events.get(event_id))

    def get_event_admin_role(self, event_id, user_id):
        return "owner" if user_id == 900 else None

    def get_event_registration(self, event_id, user_id):
        return {"status": "rejected" if user_id in self.rejected else "approved"}

    def get_user_settings(self, user_id):
        return {"username": f"player{user_id}"}

    def list_event_ready_players(self, event_id):
        return sorted([copy.copy(row) for row in self.ready if row["event_id"] == event_id], key=lambda row: row["ready_at"])

    def set_event_ready(self, event_id, user_id, ready):
        self.ready = [row for row in self.ready if (row["event_id"], row["user_id"]) != (event_id, user_id)]
        if ready:
            self.ready.append({"event_id": event_id, "user_id": user_id, "ready_at": user_id})
        return True

    def claim_event_ready_players(self, event_id, user_ids, automatic=False):
        rows = [row for row in self.ready if row["event_id"] == event_id and row["user_id"] in user_ids]
        if len(rows) != 4:
            return []
        self.ready = [row for row in self.ready if row not in rows]
        return copy.deepcopy(rows)

    def restore_event_ready_players(self, rows):
        for row in rows:
            if not any(existing["event_id"] == row["event_id"] and existing["user_id"] == row["user_id"] for existing in self.ready):
                self.ready.append(copy.copy(row))

    def clear_user_event_ready(self, user_id):
        self.ready = [row for row in self.ready if row["user_id"] != user_id]

    def list_auto_match_event_ids(self):
        return [event_id for event_id, event in self.events.items() if event["status"] == "active" and event["room_settings"]["auto_match"]["enabled"] and len(self.list_event_ready_players(event_id)) >= 4]


class EventAutoMatchTest(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.db = MemoryDatabase()
        self.server = SimpleNamespace(db_manager=self.db, user_id_to_connection={}, players={})
        self.active = {}
        self.queued = set()
        self.server.match_manager = SimpleNamespace(is_user_in_queue=lambda uid: uid in self.queued, is_user_committed=lambda uid: False)
        self.started = []
        self.server.gamestate_manager = SimpleNamespace(
            is_user_in_active_game=lambda uid: uid in self.active,
            user_id_to_game_state=self.active,
            get_game_state_by_room_id=lambda room_id: None,
            start_game=self.start_game,
            remove_spectator_from_all_games=AsyncMock(),
        )
        self.server.friend_manager = SimpleNamespace(leave_spectating_for_event=AsyncMock())
        self.manager = RoomManager(self.server)
        self.server.room_manager = self.manager
        self.matcher = EventAutoMatcher(self.server, poll_seconds=0.01)
        self.server.event_auto_matcher = self.matcher

    async def start_game(self, connect_id, room_id, *, event_auto_start=False):
        room = self.manager.rooms[room_id]
        self.assertTrue(event_auto_start)
        self.assertEqual(room["ready_list"], room["player_list"])
        for uid in room["player_list"]:
            sent = self.server.user_id_to_connection[uid].websocket.send_json.await_args_list
            self.assertTrue(any(call.args[0]["type"] == "event/seated" for call in sent), "room entry must precede game start")
        room["is_game_running"] = True
        state = SimpleNamespace(room_id=room_id)
        for uid in room["player_list"]:
            self.active[uid] = state
        self.started.append(copy.deepcopy(room))
        return None

    def add_players(self, count, start=101, event_id="venue"):
        ids = list(range(start, start + count))
        for uid in ids:
            conn = SimpleNamespace(user_id=uid, Connect_id=f"conn-{uid}", username=f"player{uid}", current_room_id=None, is_tourist=False, websocket=SimpleNamespace(send_json=AsyncMock()))
            self.server.players[conn.Connect_id] = conn
            self.server.user_id_to_connection[uid] = conn
            self.db.set_event_ready(event_id, uid, True)
        return ids

    def queued_ids(self):
        return [row["user_id"] for row in self.db.list_event_ready_players("venue")]

    def assert_no_orphan_room(self):
        self.assertFalse(self.manager.rooms)
        self.assertFalse(self.manager.event_seating_users)
        self.assertTrue(all(not conn.current_room_id for conn in self.server.user_id_to_connection.values()))

    async def test_four_players_start_with_independent_saved_settings(self):
        self.add_players(3)
        await self.matcher.run_once()
        self.assertFalse(self.started)
        self.add_players(1, start=104)
        await self.matcher.run_once()
        self.assertEqual(len(self.started), 1)
        self.assertEqual(self.started[0]["room_rule"], "guobiao")
        self.assertEqual(self.started[0]["game_round"], 4)
        self.assertFalse(self.queued_ids())
        self.assertFalse(self.manager.event_seating_users)

    async def test_nine_players_create_two_fifo_tables(self):
        self.add_players(9)
        await self.matcher.run_once()
        self.assertEqual([room["player_list"] for room in self.started], [[101, 102, 103, 104], [105, 106, 107, 108]])
        self.assertEqual(self.queued_ids(), [109])

    async def test_offline_busy_queued_and_unapproved_players_are_skipped(self):
        self.add_players(9)
        self.server.user_id_to_connection.pop(101)
        self.active[102] = SimpleNamespace(room_id="elsewhere")
        self.queued.add(103)
        self.db.rejected.add(104)
        self.manager.rooms["other"] = {"player_list": [105]}
        await self.matcher.run_once()
        self.assertEqual(self.started[0]["player_list"], [106, 107, 108, 109])
        self.assertEqual(self.queued_ids(), [101, 102, 103, 104, 105])

    async def test_closed_or_disabled_venue_does_not_start(self):
        self.add_players(4)
        for closed, enabled in [(True, True), (False, False)]:
            self.db.events["venue"]["status"] = "closed" if closed else "active"
            self.db.events["venue"]["room_settings"]["auto_match"]["enabled"] = enabled
            await self.matcher.run_once()
        self.assertEqual(len(self.queued_ids()), 4)
        self.assert_no_orphan_room()

    async def test_concurrent_manual_and_auto_cannot_seat_twice(self):
        ids = self.add_players(4)
        results = await asyncio.gather(
            self.manager.match_event_ready_players("venue"),
            self.manager.seat_event_table(900, "venue", ids),
            self.manager.match_event_ready_players("venue"),
        )
        self.assertEqual(len(self.manager.rooms), 1)
        self.assertEqual(len(self.started), 1)
        self.assertEqual(sum(bool(result and result.success) for result in results), 1)

    async def test_same_players_waiting_in_two_venues_start_only_once(self):
        ids = self.add_players(4)
        self.db.events["second"] = copy.deepcopy(self.db.events["venue"])
        self.db.events["second"]["event_id"] = "second"
        for uid in ids:
            self.db.set_event_ready("second", uid, True)
        await asyncio.gather(self.manager.match_event_ready_players("venue"), self.manager.match_event_ready_players("second"))
        self.assertEqual(len(self.started), 1)
        self.assertEqual(len(self.manager.rooms), 1)
        self.assertFalse(self.db.ready)

    async def test_automatic_seating_uses_real_game_start_dispatch(self):
        self.add_players(4)
        self.server.calculation_service = object()
        self.server.gamestate_manager = GameStateManager(self.server)
        def game_state(*args):
            return SimpleNamespace(gamestate_id=args[-1], room_id=args[1]["room_id"], run_game_loop=AsyncMock())
        with patch("server.gamestate.gamestate_manager.GuobiaoGameState", side_effect=game_state):
            response = await self.manager.match_event_ready_players("venue")
        self.assertTrue(response.success)
        self.assertTrue(response.room_info["is_game_running"])
        self.assertEqual(set(self.server.gamestate_manager.user_id_to_game_state), {101, 102, 103, 104})
        state = self.server.gamestate_manager.user_id_to_game_state[101]
        await state.game_task
        state.run_game_loop.assert_awaited_once()

    async def test_invalid_config_keeps_queue(self):
        self.add_players(4)
        self.db.events["venue"]["room_settings"]["auto_match"]["room_config"]["round_timer"] = -20
        await self.matcher.run_once()
        self.assertEqual(len(self.queued_ids()), 4)
        self.assert_no_orphan_room()
        self.assertTrue(self.matcher.status("venue")["last_error"])

    async def test_failed_claim_does_not_leave_empty_room(self):
        self.add_players(4)
        self.db.claim_event_ready_players = Mock(return_value=[])
        await self.matcher.run_once()
        self.assertEqual(len(self.queued_ids()), 4)
        self.assert_no_orphan_room()

    async def test_start_failure_restores_original_queue_positions(self):
        self.add_players(5)
        original = copy.deepcopy(self.db.ready)
        self.server.gamestate_manager.start_game = AsyncMock(return_value=Response(type="error_message", success=False, message="test startup failed"))
        await self.matcher.run_once()
        self.assertEqual(self.db.list_event_ready_players("venue"), original)
        self.assert_no_orphan_room()

    async def test_disable_during_seating_aborts_before_start(self):
        self.add_players(4)
        async def disable(message):
            if message["type"] == "event/seated":
                self.db.events["venue"]["room_settings"]["auto_match"]["enabled"] = False
        self.server.user_id_to_connection[101].websocket.send_json.side_effect = disable
        await self.matcher.run_once()
        self.assertFalse(self.started)
        self.assertEqual(len(self.queued_ids()), 4)
        self.assert_no_orphan_room()

    async def test_disconnect_during_seating_restores_only_connected_players(self):
        self.add_players(4)
        async def disconnect(message):
            if message["type"] == "event/seated":
                self.server.user_id_to_connection[104].event_disconnecting = True
        self.server.user_id_to_connection[101].websocket.send_json.side_effect = disconnect
        await self.matcher.run_once()
        self.assertEqual(self.queued_ids(), [101, 102, 103])
        self.assert_no_orphan_room()

    async def test_player_withdrawing_from_pending_room_is_not_requeued(self):
        self.add_players(4)
        async def withdraw(message):
            if message["type"] == "event/seated":
                conn = self.server.user_id_to_connection[104]
                await self.manager.leave_room(conn.Connect_id, conn.current_room_id)
        self.server.user_id_to_connection[101].websocket.send_json.side_effect = withdraw
        await self.matcher.run_once()
        self.assertEqual(self.queued_ids(), [101, 102, 103])
        self.assert_no_orphan_room()

    async def test_registration_revoked_during_seating_aborts(self):
        self.add_players(4)
        async def revoke(message):
            if message["type"] == "event/seated":
                self.db.rejected.add(104)
        self.server.user_id_to_connection[101].websocket.send_json.side_effect = revoke
        await self.matcher.run_once()
        self.assertFalse(self.started)
        self.assert_no_orphan_room()

    async def test_manual_seating_preserves_manual_start_behavior(self):
        ids = self.add_players(4)
        result = await self.manager.seat_event_table(900, "venue", ids, "riichi", {"game_round": 2})
        self.assertTrue(result.success)
        self.assertEqual(result.room_info["room_rule"], "riichi")
        self.assertEqual(result.room_info["game_round"], 2)
        self.assertFalse(result.room_info["is_game_running"])
        self.assertFalse(self.started)

    async def test_background_worker_runs_without_webpage(self):
        self.add_players(4)
        self.matcher.start()
        try:
            for _ in range(50):
                if self.started:
                    break
                await asyncio.sleep(0.01)
            self.assertEqual(len(self.started), 1)
        finally:
            await self.matcher.stop()
        self.assertFalse(self.matcher.status("venue")["worker_running"])

    async def test_cancellation_rolls_back_reserved_table(self):
        self.add_players(4)
        entered = asyncio.Event()
        async def pause(message):
            if message["type"] == "event/seated":
                entered.set()
                await asyncio.Event().wait()
        self.server.user_id_to_connection[101].websocket.send_json.side_effect = pause
        task = asyncio.create_task(self.manager.match_event_ready_players("venue"))
        await asyncio.wait_for(entered.wait(), timeout=1)
        task.cancel()
        with self.assertRaises(asyncio.CancelledError):
            await task
        self.assertEqual(len(self.queued_ids()), 4)
        self.assert_no_orphan_room()

    async def test_ready_handler_wakes_worker_and_refuses_room_players(self):
        self.add_players(1)
        conn = self.server.user_id_to_connection[101]
        conn.current_room_id = "other-room"
        await _handle_ready(self.server, 101, conn, {"event_id": "venue"}, conn.websocket, True)
        self.assertFalse(conn.websocket.send_json.await_args.args[0]["success"])
        conn.current_room_id = None
        await _handle_ready(self.server, 101, conn, {"event_id": "venue"}, conn.websocket, True)
        self.assertTrue(self.matcher._wake.is_set())

    async def test_host_cannot_start_before_other_players_receive_room_entry(self):
        self.manager.rooms["pending"] = {"event_seating_pending": True}
        game_manager = GameStateManager(self.server)
        response = await game_manager.start_game("unknown", "pending")
        self.assertFalse(response.success)

    async def test_ranked_queue_rechecks_event_reservation_after_await(self):
        self.add_players(1)
        ranked = MatchManager(self.server)
        self.server.match_manager = ranked
        async def reserve(_uid):
            self.manager.event_seating_users.add(101)
        self.server.gamestate_manager.remove_spectator_from_all_games = reserve
        result = await ranked.join_queue("conn-101", "beginner_dongfeng")
        self.assertFalse(result.success)
        self.assertFalse(ranked.is_user_in_queue(101))

    async def test_cleanup_exception_does_not_skip_queue_restoration(self):
        self.add_players(4)
        self.server.gamestate_manager.start_game = AsyncMock(return_value=Response(type="error_message", success=False, message="test startup failed"))
        self.server.gamestate_manager.get_game_state_by_room_id = lambda room_id: SimpleNamespace(gamestate_id="partial")
        self.server.gamestate_manager.cleanup_game_state_complete = AsyncMock(side_effect=RuntimeError("test cleanup failed"))
        await self.matcher.run_once()
        self.assertEqual(self.queued_ids(), [101, 102, 103, 104])
        self.assert_no_orphan_room()

    async def test_removed_room_does_not_skip_queue_restoration(self):
        self.add_players(4)
        async def remove_room(message):
            if message["type"] == "event/seated":
                self.manager.rooms.clear()
                self.db.events["venue"]["status"] = "closed"
        self.server.user_id_to_connection[101].websocket.send_json.side_effect = remove_room
        await self.matcher.run_once()
        self.assertEqual(self.queued_ids(), [101, 102, 103, 104])
        self.assert_no_orphan_room()

    async def test_auto_match_clears_actual_delayed_realtime_and_pending_spectating(self):
        self.add_players(4)
        old_game = SimpleNamespace(
            gamestate_id="old-game", game_server=self.server,
            player_list=[SimpleNamespace(user_id=901 + idx, player_index=idx) for idx in range(4)],
            realtime_spectators=[RealtimeSpectator(user_id=102, username="player102", host_user_id=901)],
        )
        delayed = SpectatorManager(old_game)
        delayed.spectator_connections[103] = self.server.user_id_to_connection[103]
        delayed.spectator_progress[103] = {"round_index": 0}
        delivery = asyncio.create_task(asyncio.Event().wait())
        delayed.spectator_send_tasks[103] = delivery
        old_game.remove_spectator = delayed.remove_spectator
        game_manager = self.server.gamestate_manager
        game_manager.gamestate_id_to_game_state = {"old-game": old_game}
        async def remove_delayed(uid):
            await GameStateManager.remove_spectator_from_all_games(game_manager, uid)
        game_manager.remove_spectator_from_all_games = remove_delayed
        friends = FriendManager(self.server)
        self.server.friend_manager = friends
        timer = asyncio.create_task(asyncio.Event().wait())
        pending = PendingRealtimeRequest(request_id="old-request", from_user_id=101, from_username="player101", to_user_id=901, to_username="host", gamestate_id="old-game", player_index=0, created_at=0, timer_task=timer)
        friends.pending_requests[pending.request_id] = pending
        friends.outgoing_by_from[101] = pending.request_id
        async def assert_clean_then_start(*args, **kwargs):
            self.assertFalse(delayed.spectator_connections)
            self.assertFalse(delayed.spectator_progress)
            self.assertFalse(delayed.spectator_send_tasks)
            self.assertTrue(delivery.cancelled())
            self.assertFalse(old_game.realtime_spectators)
            self.assertFalse(friends.pending_requests)
            self.assertFalse(friends.outgoing_by_from)
            return await self.start_game(*args, **kwargs)
        game_manager.start_game = assert_clean_then_start
        await self.matcher.run_once()
        self.assertEqual(len(self.started), 1)
        with self.assertRaises(asyncio.CancelledError):
            await timer
        for conn in self.server.user_id_to_connection.values():
            self.assertFalse(any(call.args[0]["type"] == "friend/realtime_exit_result" for call in conn.websocket.send_json.await_args_list))

    async def test_disconnect_during_spectator_cleanup_does_not_seat(self):
        self.add_players(4)
        async def disconnect(_uid):
            self.server.user_id_to_connection[104].event_disconnecting = True
        self.server.gamestate_manager.remove_spectator_from_all_games = disconnect
        await self.matcher.run_once()
        self.assertFalse(self.started)
        self.assertEqual(self.queued_ids(), [101, 102, 103])
        self.assert_no_orphan_room()
        self.assertFalse(any(call.args[0]["type"] == "event/seated" for call in self.server.user_id_to_connection[101].websocket.send_json.await_args_list))

    async def test_spectator_initializations_wait_for_event_transition(self):
        self.add_players(1)
        friends = FriendManager(self.server)
        friends._respond_request_locked = AsyncMock(return_value=Response(type="friend/realtime_request_respond_result", success=False, message="test"))
        callback = AsyncMock()
        with patch("server.gamestate.gamestate_router._handle_add_spectator_locked", callback):
            async with self.manager.event_seating_lock:
                realtime = asyncio.create_task(friends.respond_request(901, "pending", True))
                delayed = asyncio.create_task(handle_add_spectator(self.server, "conn-101", {}, self.server.user_id_to_connection[101].websocket))
                await asyncio.sleep(0)
                friends._respond_request_locked.assert_not_awaited()
                callback.assert_not_awaited()
            await asyncio.gather(realtime, delayed)
        friends._respond_request_locked.assert_awaited_once()
        callback.assert_awaited_once()

    async def test_stale_realtime_broadcast_cannot_reach_reserved_or_playing_user(self):
        self.add_players(1)
        self.server.match_manager = MatchManager(self.server)
        old_game = SimpleNamespace(game_server=self.server, player_list=[SimpleNamespace(user_id=901, player_index=0)], realtime_spectators=[RealtimeSpectator(user_id=101, username="player101", host_user_id=901)])
        self.manager.event_seating_users.add(101)
        await deliver_realtime_spectator_message(old_game, 0, {"type": "old-game-data"})
        self.manager.event_seating_users.clear()
        self.active[101] = SimpleNamespace(room_id="new-game")
        await deliver_realtime_spectator_message(old_game, 0, {"type": "old-game-data"})
        self.server.user_id_to_connection[101].websocket.send_json.assert_not_awaited()


class ReadyClaimTransactionTest(unittest.TestCase):
    def run_claim(self, count):
        db = DatabaseManager.__new__(DatabaseManager)
        cursor = Mock()
        cursor.__enter__ = Mock(return_value=cursor)
        cursor.__exit__ = Mock(return_value=False)
        cursor.fetchone.return_value = {"status": "active", "room_settings": {"auto_match": {"enabled": True}}}
        cursor.fetchall.return_value = [{"user_id": uid} for uid in range(count)]
        connection = Mock()
        connection.cursor.return_value = cursor
        db._get_connection = Mock(return_value=connection)
        db._put_connection = Mock()
        result = db.claim_event_ready_players("venue", [101, 102, 103, 104], automatic=True)
        return result, connection

    def test_partial_delete_rolls_back(self):
        result, connection = self.run_claim(3)
        self.assertEqual(result, [])
        connection.rollback.assert_called_once()
        connection.commit.assert_not_called()

    def test_four_rows_commit(self):
        result, connection = self.run_claim(4)
        self.assertEqual(len(result), 4)
        connection.commit.assert_called_once()
        connection.rollback.assert_not_called()


if __name__ == "__main__":
    unittest.main()
