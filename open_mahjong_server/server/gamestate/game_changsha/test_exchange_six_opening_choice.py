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
        self.assertEqual(flow.pending_ready_discard, 33)
        self.assertEqual(flow.open_hands, {0, 2})
        self.assertEqual(flow.reported_ready, {0, 1})
        self.assertEqual(state.server_action_tick, tick)
        self.assertFalse(state.waiting_players_list)
        self.assertFalse(await flow.submit(0, "changsha_open_hand_ready", action_tick=tick, target_tile=33))
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertEqual(sum(row[:3] == ["changsha_declaration", 0, "changsha_open_hand_ready"] for row in ticks), 1)
