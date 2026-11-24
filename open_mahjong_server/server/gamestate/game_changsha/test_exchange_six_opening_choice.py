import asyncio
import unittest
from unittest.mock import AsyncMock, patch

from server.gamestate.game_changsha.test_exchange_six import make_state
from server.gamestate.public.ai.get_action import get_action
from server.gamestate.game_changsha.exchange_six_bot import choose_action


WAIT = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]
UNREADY = [11, 12, 14, 16, 18, 21, 23, 25, 27, 31, 34, 36, 38]


class ExchangeSixOpeningChoiceTests(unittest.IsolatedAsyncioTestCase):
    async def wait_phase(self, state, phase):
        for _ in range(300):
            if state.exchange_six.phase == phase and state.waiting_players_list:
                return
            await asyncio.sleep(.001)
        self.fail(f"没有进入{phase}")


    async def choose(self, state, seat, action, target=0):
        await get_action(state, f"conn-{seat}", action, False, 0, 0, target,
                         action_tick=state.server_action_tick)


    def snapshots(self, state, seat):
        return [message["state"] for message in state.game_server.user_id_to_connection[100 + seat].websocket.messages
                if message.get("type") == "gamestate/changsha/exchange_six_state"]


    def test_combined_menu_keeps_existing_bot_report_preference(self):
        actions = ["changsha_open_hand_ready", "changsha_open_hand", "changsha_report_ready", "pass"]
        targets = {"changsha_open_hand_ready": [33], "changsha_report_ready": [33]}
        result = choose_action("declarations", WAIT + [33], [], [0] * 34, actions, targets)
        self.assertEqual(result, {"action_type": "changsha_report_ready", "target_tile": 33})


    def test_bot_combined_action_uses_legal_dealer_target(self):
        result = choose_action("declarations", WAIT + [33], [], [0] * 34,
                               ["changsha_open_hand_ready", "pass"], {"changsha_open_hand_ready": [33]})
        self.assertEqual(result, {"action_type": "changsha_open_hand_ready", "target_tile": 33})


    async def test_one_opening_choice_per_seat_with_atomic_combination(self):
        state = make_state()
        flow = state.exchange_six
        state.player_list[0].hand_tiles = WAIT + [33]
        state.player_list[1].hand_tiles = list(WAIT)
        for seat in (2, 3):
            state.player_list[seat].hand_tiles = list(UNREADY)
        with patch.object(flow, "choose_exchange", new=AsyncMock(return_value=False)):
            task = asyncio.create_task(flow.opening())
            await self.wait_phase(state, "declarations")
            tick = state.server_action_tick
            self.assertEqual(state.action_dict[0], ["changsha_open_hand_ready", "changsha_open_hand", "changsha_report_ready", "pass"])
            self.assertEqual(state.action_dict[2], ["changsha_open_hand", "pass"])
            self.assertFalse(await flow.submit(2, "changsha_open_hand_ready", action_tick=tick))
            await self.choose(state, 0, "changsha_open_hand_ready", 33)
            for _ in range(100):
                if 0 not in state.waiting_players_list:
                    break
                await asyncio.sleep(.001)
            self.assertEqual(state.server_action_tick, tick)
            self.assertEqual(set(state.waiting_players_list), {1, 2, 3})
            for seat in range(4):
                public = self.snapshots(state, seat)[-1]["public_state"]
                self.assertEqual(public["open_hand_seats"], [0])
                self.assertEqual(public["reported_ready_seats"], [0])
            await self.choose(state, 1, "changsha_report_ready")
            await self.choose(state, 2, "changsha_open_hand")
            await self.choose(state, 3, "pass")
            await asyncio.wait_for(task, 1)
        self.assertTrue(flow.opening_done)
        self.assertTrue(flow.dealer_report_pending)
        self.assertEqual(flow.open_hands, {0, 2})
        self.assertEqual(flow.reported_ready, {0, 1})
        self.assertEqual(state.server_action_tick, tick)
        self.assertFalse(state.waiting_players_list)
        self.assertFalse(await flow.submit(0, "changsha_open_hand_ready", action_tick=tick, target_tile=33))
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertEqual(sum(row[:3] == ["changsha_declaration", 0, "changsha_open_hand_ready"] for row in ticks), 1)
        self.assertIn(["changsha_declaration", 0, "changsha_open_hand_ready", 0], ticks)

    async def declare_dealer_without_cut(self, hand):
        state = make_state()
        flow = state.exchange_six
        state.player_list[0].hand_tiles = list(hand)
        for seat in (1, 2, 3):
            state.player_list[seat].hand_tiles = list(UNREADY)
        with patch.object(flow, "choose_exchange", new=AsyncMock(return_value=False)), patch.object(flow, "four_joy", new=AsyncMock()):
            task = asyncio.create_task(flow.opening())
            await self.wait_phase(state, "declarations")
            self.assertIn("changsha_open_hand_ready", state.action_dict[0])
            own = flow.packet(0).state.private_state
            self.declaration_packet = flow.packet(0).dict(exclude_none=True)
            self.assertNotIn("changsha_open_hand_ready", own["targets"])
            self.assertNotIn("changsha_report_ready", own["targets"])
            await self.choose(state, 0, "changsha_open_hand_ready")
            for seat in (1, 2, 3):
                await self.choose(state, seat, "pass")
            await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[0].hand_tiles, hand)
        self.assertFalse(state.player_list[0].discard_tiles)
        return state

    async def test_dealer_can_open_report_then_heaven_win_without_discard(self):
        hand = [11, 11, 12, 12, 13, 13, 23, 23, 28, 28, 31, 31, 38, 38]
        state = await self.declare_dealer_without_cut(hand)
        flow = state.exchange_six
        task = asyncio.create_task(flow.hand_action(first=True))
        await self.wait_phase(state, "hand")
        self.assertIn("hu_self", state.action_dict[0])
        await self.choose(state, 0, "hu_self")
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.game_status, "END")
        self.assertFalse(state.player_list[0].discard_tiles)
        self.assertEqual(set(flow.wins[0][0]["fans"]), {"七小对", "天胡", "报听", "明牌"})

    async def test_dealer_can_open_report_then_kong_without_discard(self):
        hand = [22, 22, 23, 23, 23, 23, 24, 24, 24, 24, 25, 26, 28, 29]
        state = await self.declare_dealer_without_cut(hand)
        flow = state.exchange_six
        task = asyncio.create_task(flow.hand_action(first=True))
        await self.wait_phase(state, "hand")
        self.assertEqual(flow.targets[0]["angang"], [23])
        await self.choose(state, 0, "angang", 23)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[0].combination_tiles, ["G23"])
        self.assertFalse(state.player_list[0].discard_tiles)
        self.assertFalse(flow.dealer_report_pending)
        self.assertEqual(state.game_status, "deal_card_after_gang")
        self.assertEqual((flow.open_hands, flow.reported_ready), ({0}, {0}))

    async def test_dealer_report_allows_each_preserving_cut_but_rejects_other_tiles(self):
        hand = [22, 22, 23, 23, 23, 23, 24, 24, 24, 24, 25, 26, 28, 29]
        state = await self.declare_dealer_without_cut(hand)
        flow = state.exchange_six
        task = asyncio.create_task(flow.hand_action(first=True))
        await self.wait_phase(state, "hand")
        self.assertEqual(flow.packet(0).state.private_state["cut_tiles"], [22, 23, 24, 26, 29])
        await flow.send_snapshot(100, 0, restoring=True)
        self.assertEqual(self.snapshots(state, 0)[-1]["private_state"]["cut_tiles"], [22, 23, 24, 26, 29])
        self.assertFalse(await flow.submit(0, "cut", action_tick=state.server_action_tick, tile_id=25))
        await get_action(state, "conn-0", "cut", False, 22, 0, 0, action_tick=state.server_action_tick)
        await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[0].discard_tiles, [22])
        self.assertFalse(flow.dealer_report_pending)

    async def test_dealer_report_timeout_cuts_legal_tile_instead_of_unordered_last_tile(self):
        hand = [22, 22, 23, 23, 23, 23, 24, 24, 24, 24, 26, 28, 29, 25]
        state = await self.declare_dealer_without_cut(hand)
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await state.exchange_six.hand_action(first=True)
        self.assertEqual(state.player_list[0].discard_tiles, [29])

    async def test_ordinary_timeout_keeps_the_actual_last_draw(self):
        state = make_state()
        state.player_list[0].hand_tiles = list(UNREADY) + [11]
        with patch("server.gamestate.game_changsha.exchange_six.get_ask_elapsed", return_value=100):
            await state.exchange_six.hand_action(first=True)
        self.assertEqual(state.player_list[0].discard_tiles, [11])

    async def test_dealer_report_bot_uses_a_legal_cut_or_kong_before_any_discard(self):
        hand = [22, 22, 23, 23, 23, 23, 24, 24, 24, 24, 26, 28, 29, 25]
        state = await self.declare_dealer_without_cut(hand)
        flow = state.exchange_six
        task = asyncio.create_task(flow.hand_action(first=True))
        await self.wait_phase(state, "hand")
        answer = await flow.bot_action(0, lambda seat: {"action_type": "cut", "TileId": 25})
        if answer["action_type"] == "cut":
            self.assertEqual(answer["TileId"], 29)
        else:
            self.assertEqual((answer["action_type"], answer["target_tile"]), ("angang", 23))
        self.assertTrue(await flow.submit(0, answer["action_type"], action_tick=state.server_action_tick,
                                         tile_id=answer.get("TileId"), target_tile=answer.get("target_tile")))
        await asyncio.wait_for(task, 1)
