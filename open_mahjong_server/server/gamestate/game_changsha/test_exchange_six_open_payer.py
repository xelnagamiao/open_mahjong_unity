import asyncio
import time
import unittest
from unittest.mock import patch

from server.gamestate.game_changsha.test_exchange_six import make_state
from server.gamestate.public.ai.get_action import get_action


SMALL_WAIT = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]


class ExchangeSixOpenPayerTests(unittest.IsolatedAsyncioTestCase):
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


    async def test_open_discarder_lifts_small_hu_ban_and_pays_big_hu(self):
        state = self.state()
        flow = state.exchange_six
        state.player_list[1].hand_tiles = list(SMALL_WAIT)
        for seat in (2, 3):
            state.player_list[seat].tag_list.append("peida")
        flow.small_hu_forbidden_seat = 1
        self.assertEqual(flow.result(1, 33), (0, []))
        flow.open_hands.add(0)
        task = asyncio.create_task(flow.claims([33], allow_meld=False))
        await self.wait_phase(state, "claims")
        self.assertIn("hu_first", state.action_dict[1])
        await self.choose(state, 1, "hu_first")
        await asyncio.wait_for(task, 1)
        with patch.object(state, "_draw_changsha_birds", return_value=[]):
            await flow.settle()
        self.assertEqual([player.score for player in state.player_list], [-8, 8, 0, 0])
