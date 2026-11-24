import asyncio
import unittest
from unittest.mock import patch

from server.gamestate.game_changsha.test_exchange_six import make_state
from server.gamestate.public.ai.get_action import get_action


WAIT = [12, 12, 12, 13, 14, 21, 22, 23, 24, 25, 26, 31, 32]
UNREADY = [11, 12, 14, 16, 18, 21, 23, 25, 27, 31, 34, 36, 38]


class ExchangeSixClaimDisplayTests(unittest.IsolatedAsyncioTestCase):


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


    async def test_batch_is_public_to_every_seat_until_all_claims_end(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        for player in state.player_list:
            player.hand_tiles = list(UNREADY)
        state.player_list[0].discard_tiles = [23, 11]
        state.player_list[1].hand_tiles = [23, 23, 11, 14, 16, 18, 25, 27, 29, 31, 34, 36, 38]
        state.player_list[2].tag_list.append("peida")
        state.player_list[3].tag_list.append("peida")
        state.pending_gang_forced_discard = True
        tiles = [23, 22, 33, 39]
        with patch.object(flow, "take_open_kong_tiles", return_value=tiles):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_phase(state, "claims")
            self.assertEqual(flow.pending_kong, {"player_index": 0, "river_start": 2, "discarded": True})
            self.assertEqual(state.action_dict[1], ["peng", "pass"])
            for seat in range(4):
                visible = self.snapshots(state, seat)
                opened = next(packet for packet in visible if packet["phase"] == "open_kong_public")
                self.assertEqual(opened["public_state"]["kong_tiles"], tiles)
                self.assertFalse(opened["public_state"]["pending_kong"]["discarded"])
                self.assertEqual(visible[-1]["public_state"]["pending_kong"]["river_start"], 2)
            await flow.send_snapshot(102, 2, restoring=True)
            self.assertEqual(self.snapshots(state, 2)[-1]["public_state"]["pending_kong"], flow.pending_kong)
            await self.choose(state, 1, "pass")
            await asyncio.wait_for(task, 1)
        self.assertEqual(state.player_list[0].discard_tiles, [23, 11] + tiles)
        for seat in range(4):
            public = self.snapshots(state, seat)[-1]["public_state"]
            self.assertIsNone(public.get("pending_kong"))
            self.assertEqual(public["kong_tiles"], [])

    async def test_self_win_preview_is_public_and_never_enters_discard_history(self):
        state = make_state()
        flow = state.exchange_six
        state.player_list[0].hand_tiles = list(WAIT)
        state.pending_gang_forced_discard = True
        with patch.object(flow, "take_open_kong_tiles", return_value=[33, 18, 19, 39]):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_phase(state, "kong_self_win")
            for seat in range(4):
                self.assertFalse(self.snapshots(state, seat)[-1]["public_state"]["pending_kong"]["discarded"])
            self.assertEqual(state.player_list[0].discard_tiles, [])
            await self.choose(state, 0, "hu_self")
            await asyncio.wait_for(task, 1)
        self.assertIsNone(flow.pending_kong)
        self.assertEqual(state.player_list[0].discard_tiles, [])
        self.assertEqual(flow.payer, None)

    async def test_claim_packet_contains_exact_winning_tiles(self):
        state = make_state()
        flow = state.exchange_six
        flow.actual_draw_counts = {seat: 2 for seat in range(4)}
        state.player_list[1].hand_tiles = list(WAIT)
        for seat in (2, 3):
            state.player_list[seat].tag_list.append("peida")
        task = asyncio.create_task(flow.claims([33, 18, 33, 39], kong=True, allow_meld=False))
        await self.wait_phase(state, "claims")
        self.assertEqual(flow.packet(1).state.private_state["targets"], {"hu_first": [33]})
        self.assertEqual(state.action_dict[1], ["hu_first", "pass"])
        await self.choose(state, 1, "hu_first")
        await asyncio.wait_for(task, 1)
        self.assertEqual(flow.wins[1][0]["tiles"], [33, 33])

    async def test_cancelled_batch_clears_pending_state_and_waiters(self):
        state = make_state()
        flow = state.exchange_six
        state.player_list[0].hand_tiles = list(WAIT)
        state.pending_gang_forced_discard = True
        with patch.object(flow, "take_open_kong_tiles", return_value=[33, 18, 19, 39]):
            task = asyncio.create_task(flow.draw_after_kong())
            await self.wait_phase(state, "kong_self_win")
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
        self.assertIsNone(flow.pending_kong)
        self.assertEqual(flow.public_kong_tiles, [])
        self.assertFalse(state.waiting_players_list)
        self.assertTrue(all(queue.empty() for queue in state.action_queues.values()))
