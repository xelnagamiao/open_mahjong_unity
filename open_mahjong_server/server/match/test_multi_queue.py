import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

from .match_manager import MatchManager, ALL_QUEUE_TYPES
from .match_router import handle_match_message


class MultiQueueTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.server = SimpleNamespace(
            players={str(uid): SimpleNamespace(user_id=uid, is_tourist=False, current_room_id=None) for uid in range(1, 10)},
            user_id_to_connection={uid: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock())) for uid in range(1, 10)},
            gamestate_manager=SimpleNamespace(is_user_in_active_game=Mock(return_value=False), remove_spectator_from_all_games=AsyncMock()),
            room_manager=SimpleNamespace(rooms={}, event_seating_users=set()),
            db_manager=SimpleNamespace(get_rank_data=Mock(return_value={"guobiao_rank": "四段"}), get_user_sponsor_mcrpl=Mock(return_value={"is_beginner_qualified": True,"is_intermediate_qualified":True,"is_advanced_qualified":True,"is_mcrpl_qualified":True})),
        )
        self.manager = self.server.match_manager = MatchManager(self.server)
        self.manager._start_game = AsyncMock()
        self.a, self.b, self.c = ALL_QUEUE_TYPES[:3]

    async def join(self, uid, queue):
        return await self.manager.join_queue(str(uid), queue)

    async def test_additive_idempotent_and_limit(self):
        for q in ALL_QUEUE_TYPES[:4]:
            self.assertTrue((await self.join(1, q)).success)
        revision = self.manager.snapshot(1)["match_revision"]
        self.assertTrue((await self.join(1, self.a)).success)
        self.assertEqual(self.manager.snapshot(1)["match_revision"], revision)
        denied = await self.join(1, ALL_QUEUE_TYPES[4])
        self.assertFalse(denied.success)
        self.assertEqual(denied.my_queues, ALL_QUEUE_TYPES[:4])
        self.assertEqual(self.manager.queues[self.a].count(1), 1)

    async def test_remove_one_keeps_other_and_cancel_all_is_idempotent(self):
        await self.join(1, self.a); await self.join(1, self.b)
        result = await self.manager.leave_queue("1", self.a)
        self.assertEqual(result.my_queues, [self.b])
        self.assertEqual(result.my_queue, self.b)
        self.assertTrue(self.manager.is_user_in_queue(1))
        result = await self.manager.leave_queue("1")
        self.assertEqual(result.my_queues, [])
        self.assertTrue((await self.manager.leave_queue("1")).success)
        self.assertFalse(self.manager.is_user_in_queue(1))

    async def test_winner_removes_all_other_queues_before_yield(self):
        for uid in [1, 2, 3]:
            await self.join(uid, self.a); await self.join(uid, self.b)
        await self.join(4, self.b)
        await asyncio.sleep(0)
        self.assertEqual(self.manager.committed_users, {1, 2, 3, 4})
        self.assertEqual(self.manager.queues[self.a], [])
        self.manager._start_game.assert_awaited_once_with(self.b, [1,2,3,4])
        for uid in [1,2,3,4]:
            payload = self.server.user_id_to_connection[uid].websocket.send_json.call_args.args[0]
            self.assertEqual(payload["match_queue_type"], self.b)
            self.assertEqual(payload["my_queues"], [])
            self.assertTrue(payload["match_committed"])
        self.assertFalse((await self.join(1, self.c)).success)
        self.assertFalse((await self.manager.leave_queue("1")).success)

    async def test_competing_queues_never_match_shared_players_twice(self):
        for uid in [1,2,3]:
            await self.join(uid, self.a); await self.join(uid, self.b)
        original = self.server.gamestate_manager.remove_spectator_from_all_games
        async def yielding_cleanup(uid):
            await asyncio.sleep(0)
        original.side_effect = yielding_cleanup
        await asyncio.gather(self.join(4, self.a), self.join(5, self.b))
        await asyncio.sleep(0)
        self.assertEqual(self.manager._start_game.await_count, 1)
        self.assertEqual(len(self.manager.committed_users), 4)
        self.assertEqual(sum(len(q) for q in self.manager.queues.values()), 1)

    async def test_cancel_all_waits_for_earlier_join(self):
        started, resume = asyncio.Event(), asyncio.Event()
        async def slow_cleanup(uid):
            started.set(); await resume.wait()
        self.server.gamestate_manager.remove_spectator_from_all_games.side_effect = slow_cleanup
        joining = asyncio.create_task(self.join(1, self.a))
        await started.wait()
        leaving = asyncio.create_task(self.manager.leave_queue("1"))
        resume.set()
        await asyncio.gather(joining, leaving)
        self.assertEqual(self.manager.snapshot(1)["my_queues"], [])

    async def test_disconnect_during_join_cannot_reinsert_player(self):
        async def disconnect(uid):
            self.manager.player_disconnect(uid)
        self.server.gamestate_manager.remove_spectator_from_all_games.side_effect = disconnect
        self.assertFalse((await self.join(1, self.a)).success)
        self.assertFalse(self.manager.is_user_in_queue(1))

    async def test_disconnect_removes_every_queue_but_keeps_commit_lock(self):
        await self.join(1,self.a); await self.join(1,self.b)
        self.manager.player_disconnect(1)
        self.assertFalse(self.manager.is_user_in_queue(1))
        self.assertFalse(any(1 in q for q in self.manager.queues.values()))
        self.manager.committed_users.add(1);self.manager.player_disconnect(1)
        self.assertTrue(self.manager.is_user_committed(1))

    async def test_simultaneous_append_still_limits_to_four(self):
        async def cleanup(uid): await asyncio.sleep(0)
        self.server.gamestate_manager.remove_spectator_from_all_games.side_effect = cleanup
        results = await asyncio.gather(*(self.join(1,q) for q in ALL_QUEUE_TYPES))
        self.assertEqual(sum(r.success for r in results),4)
        self.assertEqual(len(self.manager.snapshot(1)["my_queues"]),4)

    async def test_eligibility_checked_for_each_append(self):
        await self.join(1, self.a)
        with patch("server.match.match_manager.can_play_tier", return_value=False):
            result = await self.join(1, self.b)
        self.assertFalse(result.success)
        self.assertEqual(result.my_queues,[self.a])

    async def test_event_reservation_is_rechecked_after_yield(self):
        async def reserve(uid): self.server.room_manager.event_seating_users.add(uid)
        self.server.gamestate_manager.remove_spectator_from_all_games.side_effect=reserve
        self.assertFalse((await self.join(1,self.a)).success)

    async def test_invalid_requests_have_operation_response_and_snapshot(self):
        for q in ["invalid", [], None]:
            result=await self.join(1,q)
            self.assertEqual(result.type,"match/join_queue_done")
            self.assertFalse(result.success)
            self.assertEqual(result.my_queues,[])

    async def test_router_echoes_request_id_and_supports_targeted_leave(self):
        ws=SimpleNamespace(send_json=AsyncMock())
        await self.join(1,self.a);await self.join(1,self.b)
        await handle_match_message(self.server,"1",{"type":"match/leave_queue","queue_type":self.a,"match_request_id":"cancel-a"},ws)
        payload=ws.send_json.call_args.args[0]
        self.assertEqual(payload["match_request_id"],"cancel-a")
        self.assertEqual(payload["my_queues"],[self.b])
        await handle_match_message(self.server,"1",{"type":"match/get_queue_status"},ws)
        payload=ws.send_json.call_args.args[0]
        self.assertEqual(payload["my_queues"],[self.b])
        self.assertEqual(payload["queue_status"][self.b]["waiting"],1)

    async def test_menu_count_counts_people_not_queue_entries(self):
        await self.join(1,self.a);await self.join(1,self.b);await self.join(2,self.b)
        ws=SimpleNamespace(send_json=AsyncMock())
        await handle_match_message(self.server,"1",{"type":"match/get_queue_status"},ws)
        self.assertEqual(ws.send_json.call_args.args[0]["match_player_count"],2)

    async def test_rule_counts_deduplicate_waiting_players_and_add_playing(self):
        await self.join(1, self.a); await self.join(1, self.b); await self.join(2, self.b)
        await self.join(1, "riichi_beginner_dongfeng")
        self.manager.playing_counts[self.a] = 4
        self.manager.playing_counts[self.c] = 8
        self.manager.playing_counts["riichi_beginner_dongfeng"] = 4
        ws = SimpleNamespace(send_json=AsyncMock())
        await handle_match_message(self.server, "1", {"type": "match/get_queue_status"}, ws)
        self.assertEqual(ws.send_json.call_args.args[0]["match_rule_player_counts"],
                         {"guobiao": 14, "riichi": 5, "qingque": 0, "sichuan": 0, "riichi_sanma": 0, "sichuan_xueliu_exchange": 0})
        self.assertEqual(self.manager.get_queue_status()[self.b]["waiting"], 2)
        self.manager.player_disconnect(1)
        self.assertEqual(self.manager.get_rule_player_counts()["guobiao"], 13)
        self.assertEqual(self.manager.get_rule_player_counts()["riichi"], 4)

    async def test_join_exception_returns_failure_without_losing_existing_queue(self):
        await self.join(1,self.a)
        self.server.gamestate_manager.remove_spectator_from_all_games.side_effect=RuntimeError("test cleanup failure")
        with self.assertLogs("server.match.match_manager",level="ERROR"):
            result=await self.join(1,self.b)
        self.assertFalse(result.success)
        self.assertEqual(result.type,"match/join_queue_done")
        self.assertEqual(result.my_queues,[self.a])

    async def test_custom_room_tourist_and_active_game_still_block(self):
        self.server.players["1"].is_tourist=True
        self.assertFalse((await self.join(1,self.a)).success)
        self.server.players["1"].is_tourist=False
        self.server.room_manager.rooms["room"]={"player_list":[1]}
        self.assertFalse((await self.join(1,self.a)).success)
        self.server.room_manager.rooms.clear()
        self.server.gamestate_manager.is_user_in_active_game.return_value=True
        self.assertFalse((await self.join(1,self.a)).success)


if __name__ == "__main__":
    unittest.main()
