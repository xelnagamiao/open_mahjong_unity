import asyncio
import time
import unittest
from unittest.mock import patch

from server.gamestate.game_changsha.test_exchange_six import make_state
from server.gamestate.public.ai.get_action import get_action


SMALL_WAIT = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]


class ExchangeSixTacticalCallTests(unittest.IsolatedAsyncioTestCase):
    async def wait_phase(self, state, phase):
        for _ in range(200):
            if state.exchange_six.phase == phase and state.waiting_players_list:
                return
            await asyncio.sleep(.001)
        self.fail(f"未进入阶段 {phase}")


    async def choose(self, state, seat, action, target=0, tick=None):
        await get_action(state, f"conn-{seat}", action, False, 0, 0, target,
                         action_tick=state.server_action_tick if tick is None else tick)


    def state(self):
        state = make_state()
        state.exchange_six.actual_draw_counts = {seat: 2 for seat in range(4)}
        return state


    async def test_passed_pong_can_interrupt_chow(self):
        state = self.state()
        state.player_list[0].discard_tiles = [23]
        state.player_list[1].hand_tiles = [21, 22, 11, 14, 16, 18, 25, 27, 29, 31, 34, 36, 38]
        state.player_list[2].hand_tiles = [23, 23, 11, 14, 16, 18, 25, 27, 29, 31, 34, 36, 38]
        state.player_list[3].tag_list.append("peida")
        task = asyncio.create_task(state.exchange_six.claims([23]))
        await self.wait_phase(state, "claims")
        await self.choose(state, 2, "pass")
        await self.choose(state, 1, "chi_left", 23)
        await self.wait_phase(state, "claims_recheck")
        self.assertIn("peng", state.action_dict[2])
        self.assertEqual(state.player_list[1].combination_tiles, [])
        await self.choose(state, 2, "peng", 23)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[2].combination_tiles, ["k23"])
        self.assertEqual(state.player_list[1].combination_tiles, [])
        self.assertEqual(state.player_list[0].discard_tiles, [])


    async def test_passed_win_can_join_other_win_without_reviving_old_pass(self):
        for prior_pass in (False, True):
            with self.subTest(prior_pass=prior_pass):
                state = self.state()
                flow = state.exchange_six
                for seat in (1, 2):
                    state.player_list[seat].hand_tiles = list(SMALL_WAIT)
                state.player_list[3].tag_list.append("peida")
                if prior_pass:
                    state.player_passed_hu_base[1] = 2
                task = asyncio.create_task(flow.claims([33], allow_meld=False))
                await self.wait_phase(state, "claims")
                old_tick = state.server_action_tick
                if not prior_pass:
                    await self.choose(state, 1, "pass")
                await self.choose(state, 2, "hu_second")
                if not prior_pass:
                    await self.wait_phase(state, "claims_recheck")
                    packet = flow.packet(1).state
                    self.assertEqual(packet.step_remaining, 0)
                    self.assertGreater(packet.remaining_time, 0)
                    self.assertFalse(await flow.submit(1, "hu_first", action_tick=old_tick))
                    await self.choose(state, 1, "hu_first")
                await asyncio.wait_for(task, 1)
                self.assertEqual(set(flow.wins), {2} if prior_pass else {1, 2})
                if not prior_pass:
                    with patch.object(state, "_draw_changsha_birds", return_value=[]) as birds:
                        await flow.settle()
                    birds.assert_called_once_with(2)
                    self.assertEqual([player.score for player in state.player_list], [-4, 2, 2, 0])
                    self.assertEqual(flow.next_dealer, 0)
                    messages = state.game_server.user_id_to_connection[100].websocket.messages
                    claims = [message["do_action_info"] for message in messages
                              if message.get("do_action_info", {}).get("is_claim")]
                    self.assertEqual(sorted(claim["action_player"] for claim in claims), [1, 2])
                    result = [message["show_result_info"] for message in messages if message.get("show_result_info")][-1]
                    self.assertTrue(result["silent"])


    async def test_straight_kong_can_be_robbed_by_small_hu_forbidden_player(self):
        state = self.state()
        flow = state.exchange_six
        flow.small_hu_forbidden_seat = 2
        state.player_list[1].hand_tiles = [12] * 3 + [13, 14, 15, 16, 17, 18, 21, 22, 23, 25]
        state.player_list[2].hand_tiles = [21, 22, 23, 24, 25, 26, 31, 32, 33, 25, 25, 13, 14]
        for seat in (0, 3):
            state.player_list[seat].tag_list.append("peida")
        state.player_list[0].discard_tiles = [12]
        wall = list(state.tiles_list)
        task = asyncio.create_task(flow.claims([12]))
        await self.wait_phase(state, "claims")
        opening_tick = state.server_action_tick
        self.assertIn("gang", state.action_dict[1])
        await self.choose(state, 1, "gang", 12)
        for _ in range(200):
            if state.server_action_tick != opening_tick and 2 in state.waiting_players_list:
                break
            await asyncio.sleep(.001)
        self.assertIn("hu_first", state.action_dict[2])
        await self.choose(state, 2, "hu_first")
        await asyncio.wait_for(task, 1)
        self.assertEqual(flow.payer, 1)
        self.assertIn("抢杠胡", flow.wins[2][0]["fans"])
        self.assertEqual(state.player_list[1].combination_tiles, [])
        self.assertEqual(state.tiles_list, wall)


    async def test_recheck_receipt_deadline_reconnect_and_cancellation(self):
        for outcome in ("timely", "expired", "cancelled"):
            with self.subTest(outcome=outcome):
                state = self.state()
                flow = state.exchange_six
                for seat in (1, 2):
                    state.player_list[seat].hand_tiles = list(SMALL_WAIT)
                state.player_list[3].tag_list.append("peida")
                task = asyncio.create_task(flow.claims([33], allow_meld=False))
                await self.wait_phase(state, "claims")
                await self.choose(state, 1, "pass")
                await self.choose(state, 2, "hu_second")
                await self.wait_phase(state, "claims_recheck")
                tick = state.server_action_tick
                deadline = flow.recheck_deadline
                await flow.send_snapshot(101, 1)
                restored = state.game_server.user_id_to_connection[101].websocket.messages[-1]["state"]
                self.assertEqual(restored["private_state"]["action_list"], ["hu_first", "pass"])
                self.assertEqual(flow.recheck_deadline, deadline)
                if outcome == "cancelled":
                    task.cancel()
                    await asyncio.gather(task, return_exceptions=True)
                else:
                    if outcome == "timely":
                        self.assertTrue(await flow.submit(1, "hu_first", action_tick=tick))
                    flow.recheck_deadline = time.time() - 1
                    if outcome == "expired":
                        self.assertFalse(await flow.submit(1, "hu_first", action_tick=tick))
                    state.action_events[1].set()
                    await asyncio.wait_for(task, 2)
                    self.assertEqual(set(flow.wins), {1, 2} if outcome == "timely" else {2})
                self.assertIsNone(flow.recheck_deadline)
                self.assertFalse(state.waiting_players_list)
                self.assertTrue(all(queue.empty() for queue in state.action_queues.values()))


    async def test_bot_can_reselect_pong_after_chow(self):
        state = make_state(bot_seats=(2,))
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].discard_tiles = [23]
        state.player_list[1].hand_tiles = [21, 22, 11, 14, 16, 18, 25, 27, 29, 31, 34, 36, 38]
        state.player_list[2].hand_tiles = [23, 23, 11, 14, 16, 18, 25, 27, 29, 31, 34, 36, 38]
        state.player_list[3].tag_list.append("peida")
        async def bot(seat, default):
            return {"action_type": "pass"} if flow.phase == "claims" else {"action_type": "peng", "target_tile": 23}
        with patch.object(flow, "bot_action", side_effect=bot):
            task = asyncio.create_task(flow.claims([23]))
            await self.wait_phase(state, "claims")
            await self.choose(state, 1, "chi_left", 23)
            await asyncio.wait_for(task, 2)
        self.assertEqual(state.player_list[2].combination_tiles, ["k23"])


    async def test_real_bot_recheck_uses_only_reopened_targets(self):
        state = make_state(bot_seats=(1,))
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[0].discard_tiles = [23]
        state.player_list[1].hand_tiles = [23, 23, 21, 22, 11, 14, 16, 18, 25, 27, 29, 31, 34]
        state.player_list[2].hand_tiles = [23, 23, 11, 14, 16, 18, 25, 27, 29, 31, 34, 36, 38]
        state.player_list[3].tag_list.append("peida")
        original_bot_action = flow.bot_action

        async def bot(seat, default):
            # 首轮主动过，战术重开由真实选择器决定，不替它挑碰牌。
            return ({"action_type": "pass"} if flow.phase == "claims"
                    else await original_bot_action(seat, default))

        async def calculate_inline(state, selector, *args):
            return selector(*args)

        with patch.object(flow, "bot_action", side_effect=bot), patch(
                "server.gamestate.public.ai.bot_executor.run_room_bot_cpu", side_effect=calculate_inline):
            task = asyncio.create_task(flow.claims([23]))
            await self.wait_phase(state, "claims")
            await self.choose(state, 2, "peng", 23)
            await asyncio.wait_for(task, 2)
        self.assertEqual(state.player_list[1].combination_tiles, ["k23"])
        self.assertEqual(state.player_list[2].combination_tiles, [])
