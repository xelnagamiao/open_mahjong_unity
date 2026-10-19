"""全桌换牌决定通过真实询问阶段和get_action验证。"""
import asyncio
import time
from collections import Counter
import unittest
from unittest.mock import AsyncMock, patch

from .exchange_six import ExchangeSixFlow, EXCHANGE_ACTION, NO_EXCHANGE_ACTION, REPEAT_EXCHANGE_ACTION
from .test_exchange_six import make_state
from ..public.ai.get_action import get_action
from .boardcast import reconnected_send_pending_ask


def live_state(bot_seats=()):
    state = make_state(bot_seats)
    del state.exchange_six.choose_exchange
    del state.exchange_six.wait_for_receipt
    return state


class ExchangeChoiceTests(unittest.IsolatedAsyncioTestCase):
    async def test_initial_choice_waits_for_all_clients_to_finish_deal_animation(self):
        state = live_state()
        flow = state.exchange_six
        banks = [player.remaining_time for player in state.player_list]
        with patch.object(flow, "four_joy", new=AsyncMock()):
            task = asyncio.create_task(flow.opening())
            try:
                await self.wait_phase(state, "deal_dice_received")
                receipt_tick = state.server_action_tick
                for seat in range(4):
                    packet = flow.packet(seat).state
                    self.assertEqual((packet.remaining_time, packet.step_remaining), (0, 0))
                for seat in range(3):
                    await self.action(state, seat, "changsha_exchange_seen")
                await self.action(state, 3, "changsha_exchange_seen", tick=receipt_tick - 1)
                for _ in range(100):
                    if state.waiting_players_list == [3]:
                        break
                    await asyncio.sleep(.001)
                self.assertEqual(flow.phase, "deal_dice_received")
                self.assertEqual(state.waiting_players_list, [3])
                await self.action(state, 3, "changsha_exchange_seen")
                await self.wait_phase(state, "exchange_start_choice")
                self.assertEqual(flow.packet(3).state.remaining_time, 2)
                await self.action(state, 3, NO_EXCHANGE_ACTION)
                await self.wait_phase(state, "declarations")
                for seat in range(4):
                    await self.action(state, seat, "pass")
                await asyncio.wait_for(task, 1)
                self.assertEqual([player.remaining_time for player in state.player_list], banks)
            finally:
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)

    async def test_receipt_fallback_is_independent_of_large_time_bank_and_bot_speed(self):
        for bots in ((), (0, 1, 2, 3)):
            for after_exchange in (False, True):
                state = live_state(bots)
                for player in state.player_list:
                    player.remaining_time = 3600
                elapsed = 0 if bots else 10
                with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=elapsed), \
                     patch("server.gamestate.game_changsha.exchange_six.bot_delay", return_value=5):
                    await asyncio.wait_for(state.exchange_six.wait_for_receipt(after_exchange=after_exchange), 1)
                self.assertEqual([player.remaining_time for player in state.player_list], [3600] * 4)
                self.assertFalse(state.waiting_players_list)

    async def test_fast_first_response_during_broadcast_still_delivers_all_four_buttons(self):
        state = live_state()
        flow = state.exchange_six
        socket = state.game_server.user_id_to_connection[100].websocket
        original_send = socket.send_json
        async def first_viewer(message):
            await original_send(message)
            if message.get("state", {}).get("phase") == "exchange_start_choice":
                await self.action(state, 0, NO_EXCHANGE_ACTION)
        socket.send_json = first_viewer
        self.assertFalse(await asyncio.wait_for(flow.choose_exchange(), 1))
        self.assertEqual(flow.exchange_choice["seat"], 0)
        for seat in range(4):
            message = state.game_server.user_id_to_connection[100 + seat].websocket.messages[0]
            self.assertEqual(message["state"]["private_state"]["action_list"], [NO_EXCHANGE_ACTION])

    async def test_real_initial_choice_timeout_is_two_seconds(self):
        state = live_state()
        started = time.monotonic()
        self.assertTrue(await state.exchange_six.choose_exchange())
        elapsed = time.monotonic() - started
        self.assertGreaterEqual(elapsed, 1.95)
        self.assertLess(elapsed, 3.5)

    async def test_receipt_timeout_fallback_preserves_hands_and_normal_time(self):
        state = live_state((0, 2))
        state.player_list[3].tag_list.append("offline")
        flow = state.exchange_six
        before = [list(player.hand_tiles) for player in state.player_list]
        banks = [player.remaining_time for player in state.player_list]
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            answers = await flow.run_stage("exchange_received", {seat: ["changsha_exchange_seen"] for seat in range(4)},
                                           lambda seat: {"action_type": "changsha_exchange_seen"})
        self.assertEqual(set(answers), set(range(4)))
        self.assertEqual([player.hand_tiles for player in state.player_list], before)
        self.assertEqual([player.remaining_time for player in state.player_list], banks)
    async def wait_phase(self, state, phase, *, round_number=None):
        for _ in range(1000):
            flow = state.exchange_six
            if flow.phase == "deal_dice_received" and phase != "deal_dice_received":
                for seat in list(state.waiting_players_list):
                    await self.action(state, seat, "changsha_exchange_seen")
            if flow.phase == phase and state.waiting_players_list and (round_number is None or flow.exchange_round == round_number):
                return
            await asyncio.sleep(.001)
        self.fail(f"未进入生产阶段{phase}，当前{state.exchange_six.phase}")

    async def action(self, state, seat, action, *, tick=None, selected=None):
        await get_action(state, f"conn-{seat}", action, False, 0, 0, 0,
                         action_tick=state.server_action_tick if tick is None else tick, selected_tiles=selected)

    async def finish_first_exchange(self, state):
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await state.exchange_six.exchange()

    async def test_first_effective_request_wins_even_when_seat_order_is_reversed(self):
        for first_action in (NO_EXCHANGE_ACTION, REPEAT_EXCHANGE_ACTION):
            state = live_state()
            flow = state.exchange_six
            await self.finish_first_exchange(state)
            task = asyncio.create_task(flow.choose_exchange(after_first=True))
            await self.wait_phase(state, "exchange_repeat_choice")
            initial_time = [player.remaining_time for player in state.player_list]
            await self.action(state, 3, first_action)
            self.assertEqual(flow.packet(0).state.private_state["action_list"], [])
            await self.action(state, 0, REPEAT_EXCHANGE_ACTION if first_action == NO_EXCHANGE_ACTION else NO_EXCHANGE_ACTION)
            # 首次收件已在窗口内通过，之后调度延迟不能让截止判定覆盖它。
            with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
                result = await asyncio.wait_for(task, 1)
            self.assertEqual(result, first_action == REPEAT_EXCHANGE_ACTION)
            self.assertEqual(flow.exchange_choice, {"seat": 3, "action": first_action, "round": 1})
            self.assertEqual([player.remaining_time for player in state.player_list], initial_time)
            self.assertFalse(state.waiting_players_list)
            self.assertTrue(all(not choices for choices in state.action_dict.values()))

    async def test_reconnect_countdown_expired_and_old_tick_requests_cannot_decide(self):
        state = live_state()
        flow = state.exchange_six
        task = asyncio.create_task(flow.choose_exchange())
        await self.wait_phase(state, "exchange_start_choice")
        tick = state.server_action_tick
        self.assertEqual(flow.packet(0).state.remaining_time, 2)
        self.assertEqual(flow.packet(0).state.step_remaining, 0)
        await self.action(state, 2, NO_EXCHANGE_ACTION, tick=tick - 1)
        self.assertIsNone(flow.exchange_choice)
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=1.25):
            await reconnected_send_pending_ask(state, 100)
            packet = state.game_server.user_id_to_connection[100].websocket.messages[-1]["state"]
            self.assertEqual(packet["remaining_time"], 1)
            self.assertEqual(packet["action_tick"], tick)
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=2):
            await self.action(state, 3, NO_EXCHANGE_ACTION)
            self.assertTrue(await asyncio.wait_for(task, 1.5))
        self.assertIsNone(flow.exchange_choice["seat"])
        self.assertEqual(flow.exchange_choice["action"], "changsha_begin_exchange")
        await self.action(state, 0, NO_EXCHANGE_ACTION, tick=tick)
        self.assertEqual(flow.exchange_choice["action"], "changsha_begin_exchange")

    async def test_timeout_defaults_initial_yes_repeat_no_without_spending_round_time(self):
        for after_first, expected in ((False, True), (True, False)):
            state = live_state((0, 1, 2, 3))
            flow = state.exchange_six
            if after_first:
                await self.finish_first_exchange(state)
            before = [player.remaining_time for player in state.player_list]
            with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
                self.assertEqual(await flow.choose_exchange(after_first=after_first), expected)
            self.assertEqual([player.remaining_time for player in state.player_list], before)
            self.assertIsNone(flow.exchange_choice["seat"])

    async def test_bot_and_offline_players_do_not_preempt_human_choice(self):
        state = live_state((0, 2))
        state.player_list[3].tag_list.append("offline")
        flow = state.exchange_six
        task = asyncio.create_task(flow.choose_exchange())
        await self.wait_phase(state, "exchange_start_choice")
        await asyncio.sleep(.02)
        self.assertFalse(task.done())
        self.assertIsNone(flow.exchange_choice)
        await self.action(state, 1, NO_EXCHANGE_ACTION)
        self.assertFalse(await asyncio.wait_for(task, 1))
        self.assertEqual(flow.exchange_choice["seat"], 1)

    async def test_initial_skip_keeps_every_hand_and_enters_four_joy_then_declarations(self):
        state = live_state()
        flow = state.exchange_six
        original = [list(player.hand_tiles) for player in state.player_list]
        observations = []
        async def four_joy(seat, tiles):
            observations.append((seat, flow.completed_exchange_rounds, flow.exchange_closed))
        with patch.object(flow, "four_joy", side_effect=four_joy):
            task = asyncio.create_task(flow.opening())
            await self.wait_phase(state, "exchange_start_choice")
            await self.action(state, 2, NO_EXCHANGE_ACTION)
            await self.wait_phase(state, "declarations")
            for seat in range(4):
                await self.action(state, seat, "pass")
            await asyncio.wait_for(task, 1)
        self.assertEqual([player.hand_tiles for player in state.player_list], original)
        self.assertEqual(flow.completed_exchange_rounds, 0)
        self.assertEqual(observations, [(seat, 0, True) for seat in range(4)])
        self.assertTrue(flow.opening_done)
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertFalse(any(tick[0] == "changsha_exchange_six" for tick in ticks))
        self.assertIn(["changsha_exchange_decision", 0, 2, NO_EXCHANGE_ACTION], ticks)

    async def test_first_hand_must_be_received_before_second_choice_and_two_rounds_are_atomic(self):
        for repeat_action in (REPEAT_EXCHANGE_ACTION, NO_EXCHANGE_ACTION, None):
            state = live_state()
            flow = state.exchange_six
            original_pool = Counter(tile for player in state.player_list for tile in player.hand_tiles)
            captured = []
            async def four_joy(seat, tiles):
                captured.append(flow.completed_exchange_rounds)
            # 只加速自愿窗口的超时，选牌和收牌仍由真实get_action推进。
            def elapsed(game, seat):
                return 100 if flow.phase == "exchange_start_choice" or (flow.phase == "exchange_repeat_choice" and repeat_action is None) else 0
            with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", side_effect=elapsed), \
                 patch.object(flow, "four_joy", side_effect=four_joy):
                task = asyncio.create_task(flow.opening())
                await self.wait_phase(state, "exchange", round_number=1)
                first_tick = state.server_action_tick
                for seat in range(4):
                    await self.action(state, seat, EXCHANGE_ACTION, selected=flow.default_selection(seat))
                await self.wait_phase(state, "exchange_received")
                first_hands = [list(player.hand_tiles) for player in state.player_list]
                self.assertEqual(flow.completed_exchange_rounds, 1)
                for seat in range(3):
                    await self.action(state, seat, "changsha_exchange_seen")
                await self.action(state, 3, "changsha_exchange_seen", tick=first_tick)
                for _ in range(100):
                    if state.waiting_players_list == [3]:
                        break
                    await asyncio.sleep(.001)
                self.assertEqual(flow.phase, "exchange_received")
                self.assertEqual(state.waiting_players_list, [3])
                self.assertFalse(captured)
                await self.action(state, 3, "changsha_exchange_seen")
                if repeat_action is not None:
                    await self.wait_phase(state, "exchange_repeat_choice")
                    for seat in range(4):
                        packet = flow.packet(seat).state
                        self.assertEqual(packet.private_state["hand_tiles"], first_hands[seat])
                        self.assertEqual(packet.remaining_time, 2)
                    await self.action(state, 3, repeat_action)
                expected_rounds = 2 if repeat_action == REPEAT_EXCHANGE_ACTION else 1
                if expected_rounds == 2:
                    await self.wait_phase(state, "exchange", round_number=2)
                    self.assertEqual(flow.packet(0).state.remaining_time, state.exchange_selection_seconds)
                    self.assertEqual(flow.packet(0).state.public_state["dice"], [])
                    await self.action(state, 0, EXCHANGE_ACTION, tick=first_tick, selected=flow.default_selection(0))
                    self.assertNotIn(0, flow.submissions)
                    for seat in range(4):
                        await self.action(state, seat, EXCHANGE_ACTION, selected=flow.default_selection(seat))
                await self.wait_phase(state, "declarations")
                for seat in range(4):
                    await self.action(state, seat, "pass")
                await asyncio.wait_for(task, 1)
            self.assertEqual(flow.completed_exchange_rounds, expected_rounds)
            self.assertEqual(captured, [expected_rounds] * 4)
            self.assertEqual(Counter(tile for player in state.player_list for tile in player.hand_tiles), original_pool)
            self.assertEqual([len(player.hand_tiles) for player in state.player_list], [14,13,13,13])
            with self.assertRaises(ValueError):
                await flow.exchange(dice=[1,2])
            ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
            self.assertEqual(sum(tick[0] == "changsha_exchange_six" for tick in ticks), expected_rounds)
            self.assertEqual(sum(tick[0] == "changsha_exchange_decision" for tick in ticks), 2)

    async def test_cancel_clears_choice_window_and_reset_forgets_previous_winner(self):
        state = live_state()
        flow = state.exchange_six
        task = asyncio.create_task(flow.choose_exchange())
        await self.wait_phase(state, "exchange_start_choice")
        task.cancel()
        with self.assertRaises(asyncio.CancelledError):
            await task
        self.assertFalse(state.waiting_players_list)
        self.assertTrue(all(not actions for actions in state.action_dict.values()))
        task = asyncio.create_task(flow.choose_exchange())
        await self.wait_phase(state, "exchange_start_choice")
        await self.action(state, 1, NO_EXCHANGE_ACTION)
        self.assertFalse(await task)
        flow.reset()
        self.assertIsNone(flow.exchange_choice)
