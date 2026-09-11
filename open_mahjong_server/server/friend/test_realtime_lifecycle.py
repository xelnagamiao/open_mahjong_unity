"""实时观战结束必须同步清理被观战者面板和未响应的申请。"""

import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock

from server.friend.friend_manager import (
    FriendManager,
    PendingRealtimeRequest,
    RealtimeSpectator,
)


class RealtimeLifecycleTest(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.websockets = {
            user_id: SimpleNamespace(send_json=AsyncMock())
            for user_id in (101, 102, 201, 202, 203)
        }
        self.game = SimpleNamespace(
            gamestate_id="ending-game",
            player_list=[
                SimpleNamespace(user_id=101),
                SimpleNamespace(user_id=102),
                SimpleNamespace(user_id=1),
            ],
            realtime_spectators=[RealtimeSpectator(201, "spectator", 101)],
        )
        game_server = SimpleNamespace(
            user_id_to_connection={
                user_id: SimpleNamespace(websocket=websocket)
                for user_id, websocket in self.websockets.items()
            },
            gamestate_manager=SimpleNamespace(
                gamestate_id_to_game_state={self.game.gamestate_id: self.game},
                get_game_state_by_user_id=lambda user_id: self.game,
            ),
        )
        self.manager = FriendManager(game_server)

    def messages(self, user_id, message_type):
        return [
            call.args[0]
            for call in self.websockets[user_id].send_json.await_args_list
            if call.args[0]["type"] == message_type
        ]

    def assert_host_list_empty(self, user_id=101):
        messages = self.messages(user_id, "friend/realtime_spectators_changed")
        self.assertTrue(messages, "被观战者必须收到空列表，才能关闭实时观战面板")
        self.assertEqual(messages[-1]["realtime_spectators"], [])
        self.assertEqual(messages[-1]["realtime_gamestate_id"], self.game.gamestate_id)

    def add_request(self, request_id="pending", gamestate_id="ending-game", from_user_id=202):
        request = PendingRealtimeRequest(
            request_id=request_id,
            from_user_id=from_user_id,
            from_username="applicant",
            to_user_id=101,
            to_username="host",
            gamestate_id=gamestate_id,
            player_index=0,
            created_at=0,
            timer_task=asyncio.create_task(asyncio.sleep(60)),
        )
        self.manager.pending_requests[request_id] = request
        self.manager.outgoing_by_from[from_user_id] = request_id
        return request

    async def test_game_end_clears_hosts_and_notifies_spectator(self):
        await self.manager.on_game_end(self.game)

        self.assertEqual(self.game.realtime_spectators, [])
        self.assert_host_list_empty(101)
        self.assert_host_list_empty(102)
        ended = self.messages(201, "friend/realtime_ended")
        self.assertEqual(len(ended), 1)
        self.assertEqual(ended[0]["realtime_gamestate_id"], self.game.gamestate_id)

    async def test_game_end_syncs_empty_list_even_without_spectators(self):
        self.game.realtime_spectators.clear()

        await self.manager.on_game_end(self.game)

        self.assert_host_list_empty()

    async def test_repeated_game_end_does_not_repeat_spectator_notification(self):
        await self.manager.on_game_end(self.game)
        await self.manager.on_game_end(self.game)

        self.assertEqual(len(self.messages(201, "friend/realtime_ended")), 1)
        self.assert_host_list_empty()

    async def test_disconnected_spectator_cannot_prevent_host_cleanup(self):
        self.websockets[201].send_json.side_effect = OSError("socket closed")

        await self.manager.on_game_end(self.game)

        self.assert_host_list_empty(101)
        self.assert_host_list_empty(102)

    async def test_game_end_revokes_pending_request_without_active_spectators(self):
        self.game.realtime_spectators.clear()
        request = self.add_request()

        await self.manager.on_game_end(self.game)
        await asyncio.sleep(0)

        self.assertNotIn(request.request_id, self.manager.pending_requests)
        self.assertNotIn(request.from_user_id, self.manager.outgoing_by_from)
        self.assertTrue(request.timer_task.cancelled())
        revoked = self.messages(101, "friend/realtime_request_revoked")
        declined = self.messages(202, "friend/realtime_request_declined")
        self.assertEqual(len(revoked), 1)
        self.assertEqual(len(declined), 1)
        self.assertEqual(revoked[0]["realtime_request_id"], request.request_id)
        self.assertEqual(declined[0]["realtime_request_id"], request.request_id)
        self.assertEqual(revoked[0]["realtime_gamestate_id"], self.game.gamestate_id)
        self.assert_host_list_empty()

    async def test_game_end_preserves_requests_for_other_games(self):
        ending = self.add_request()
        other = self.add_request("other", "other-game", 203)

        await self.manager.on_game_end(self.game)

        self.assertNotIn(ending.request_id, self.manager.pending_requests)
        self.assertIs(self.manager.pending_requests[other.request_id], other)
        self.assertEqual(self.manager.outgoing_by_from[203], other.request_id)
        self.assertFalse(other.timer_task.cancelling())
        self.assertEqual(self.messages(203, "friend/realtime_request_declined"), [])

    async def test_repeated_game_end_does_not_repeat_request_revocation(self):
        self.add_request()

        await self.manager.on_game_end(self.game)
        await self.manager.on_game_end(self.game)

        self.assertEqual(len(self.messages(101, "friend/realtime_request_revoked")), 1)
        self.assertEqual(len(self.messages(202, "friend/realtime_request_declined")), 1)

    async def test_explicit_spectator_exit_clears_host_list(self):
        response = await self.manager.exit_realtime(201)

        self.assertTrue(response.success)
        self.assert_host_list_empty()

    async def test_host_kick_clears_host_list(self):
        response = await self.manager.kick_realtime(101, 201)

        self.assertTrue(response.success)
        self.assert_host_list_empty()
        self.assertEqual(len(self.messages(201, "friend/realtime_kicked")), 1)

    async def test_spectator_disconnect_clears_host_list(self):
        await self.manager.on_player_disconnect(201)

        self.assert_host_list_empty()


if __name__ == "__main__":
    unittest.main()
