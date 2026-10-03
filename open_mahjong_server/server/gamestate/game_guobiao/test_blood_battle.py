import unittest
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

from . import blood_battle as blood
from .GuobiaoGameState import GuobiaoGameState
from .action_check import check_action_after_cut, check_action_hand_action, check_hepai
from .wait_action import wait_action
from ...game_calculation.game_calculation_service import GameCalculationService


def state_for_test(sub_rule=blood.SUB_RULE):
    server = SimpleNamespace(user_id_to_connection={})
    state = GuobiaoGameState(server, {
        "player_list": [0, 2, 3, 4], "room_id": "test", "round_timer": 10,
        "step_timer": 1, "game_round": 1, "tips": False, "room_rule": "guobiao",
        "room_type": "custom", "sub_rule": sub_rule,
    }, GameCalculationService(), Mock(), "test")
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
        player.hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45]
    state.tiles_list = [19, 29, 39]
    state.broadcast_result = AsyncMock()
    state.broadcast_refresh_player_tag_list = AsyncMock()
    return state


class ScoringTests(unittest.TestCase):
    def test_decreasing_payers_and_zero_sum(self):
        for active in ([0, 1, 2, 3], [1, 2, 3], [2, 3]):
            winner = active[-1]
            zimo = blood.score_event(active, [winner], {winner: 16})[winner]
            self.assertEqual(zimo[winner], (len(active) - 1) * 24)
            ron = blood.score_event(active, [winner], {winner: 16}, active[0])[winner]
            self.assertEqual(ron[winner], 16 + (len(active) - 1) * 8)
            for delta in (zimo, ron):
                self.assertEqual(sum(delta.values()), 0)
                for retired in set(range(4)) - set(active):
                    self.assertEqual(delta[retired], 0)

    def test_simultaneous_winners_do_not_pay_each_other(self):
        entries = blood.score_event(range(4), [1, 2], {1: 16, 2: 24}, 0)
        self.assertEqual([sum(item[i] for item in entries.values()) for i in range(4)], [-56, 32, 40, -16])
        self.assertEqual(entries[1][2], 0)
        self.assertEqual(entries[2][1], 0)
        self.assertEqual(entries, blood.score_event(range(4), [2, 1], {1: 16, 2: 24}, 0))

    def test_rule_defaults_are_isolated(self):
        for rule in ("guobiao/standard", "guobiao/xiaolin", "guobiao/kshen", "guobiao/lanshi"):
            state = state_for_test(rule)
            self.assertFalse(blood.enabled(state))
            self.assertEqual([state.action_priority[a] for a in ("hu_first", "hu_second", "hu_third")], [5, 4, 3])
            self.assertFalse(hasattr(state, "blood_win_events"))
        state = state_for_test()
        self.assertEqual(state.hepai_limit, 8)
        self.assertFalse(state.open_cuohe)
        self.assertFalse(state.tactical_call)

    def test_standard_chow_seat_is_unchanged_blood_skips_retired(self):
        for rule in ("guobiao/standard", blood.SUB_RULE):
            state = state_for_test(rule)
            state.player_list[1].is_hu = True
            state.player_list[2].hand_tiles = [12, 13]
            state.player_list[1].hand_tiles = [12, 13]
            actions = check_action_after_cut(state, 11)
            expected = 2 if blood.enabled(state) else 1
            self.assertIn("chi_right", actions[expected])
            if blood.enabled(state):
                self.assertEqual(actions[1], [])
                self.assertEqual(check_action_hand_action(state, 1)[1], [])

    def test_same_standard_and_blood_fan_calculation(self):
        results = []
        for rule in ("guobiao/standard", blood.SUB_RULE):
            state = state_for_test(rule)
            state.player_list[1].waiting_tiles = {45}
            actions = {i: [] for i in range(4)}
            check_hepai(state, actions, 45, 1, "dianhe")
            results.append((actions, state.result_dict))
        self.assertEqual(results[0], results[1])
        self.assertIn("hu_first", results[0][1])

    def test_snapshot_does_not_leak_retired_hand_or_self_draw_tile(self):
        state = state_for_test()
        player = state.player_list[1]
        player.hand_tiles.append(45)
        player.is_hu = True
        player.hu_order = 1
        player.blood_win_tile = 45
        player.blood_win_is_zimo = True
        other = blood.player_snapshot(player, 0)
        own = blood.player_snapshot(player, 1)
        self.assertIsNone(other["hand_tiles"])
        self.assertEqual(other["blood_hu_tile"], 0)
        self.assertEqual(other["hand_tiles_count"], 13)
        self.assertEqual(len(own["hand_tiles"]), 13)
        self.assertEqual(own["blood_hu_tile"], 45)


class FlowTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.state = state_for_test()
        self.ticks = []
        self.patches = [
            patch.object(blood, "append_action_tick", side_effect=lambda s, tick: self.ticks.append(deepcopy(tick))),
            patch.object(blood, "player_action_record_hu", Mock()),
            patch.object(blood, "player_action_record_round_end", Mock()),
            patch.object(blood, "record_fulu_rounds_for_players", Mock()),
            patch.object(blood.asyncio, "sleep", AsyncMock()),
        ]
        for patcher in self.patches:
            patcher.start()
            self.addCleanup(patcher.stop)

    async def win(self, source, winners, zimo=False, qianggang=False):
        state = self.state
        state.current_player_index = source
        state.hu_class = "hu_self" if zimo else "hu_first"
        state.blood_pending_claims = {w: ("hu_first", "hu_second", "hu_third")[(w - source) % 4 - 1] for w in winners} if not zimo else {}
        state.result_dict = {action: (16, ["测试番"]) for action in state.blood_pending_claims.values()}
        if zimo:
            state.result_dict = {"hu_self": (16, ["测试番"])}
            state.player_list[source].hand_tiles.append(45)
        elif qianggang:
            state.jiagang_tile = 45
            state.player_list[source].combination_tiles = ["g45"]
            state.player_list[source].combination_mask = [[3, 45, 1, 45, 0, 45, 0, 45]]
        else:
            state.player_list[source].discard_tiles.append(45)
        await blood.settle_win(state)

    async def test_three_successive_wins_and_one_final_settlement(self):
        await self.win(0, [1])
        self.assertEqual(self.state.game_status, "deal_card")
        self.assertEqual(blood.next_active_index(self.state, 1), 2)
        await self.win(2, [2], zimo=True)
        await self.win(3, [0])
        self.assertEqual(self.state.game_status, "END")
        self.assertEqual([p.score for p in self.state.player_list], [0] * 4)
        self.state.current_round = 4
        await blood.finish_round(self.state, dict.fromkeys(range(4), 0))
        self.assertEqual([p.score for p in self.state.player_list], [-24, 40, 40, -56])
        self.assertTrue(all(len(p.score_history) == 1 for p in self.state.player_list))
        await blood.finish_round(self.state, dict.fromkeys(range(4), 0))
        self.assertTrue(all(len(p.score_history) == 1 for p in self.state.player_list))
        self.assertEqual(len([t for t in self.ticks if t[1] == "settle_hu"]), 3)

    async def test_multi_ron_recycles_only_once_and_freezes_payers(self):
        await self.win(0, [1, 2])
        self.assertEqual(self.state.player_list[0].discard_tiles, [])
        self.assertEqual(self.state.blood_public_win_tiles, [45])
        payloads = [call.kwargs for call in self.state.broadcast_result.await_args_list]
        self.assertEqual([p["recycle_discard"] for p in payloads], [False, True])
        self.assertTrue(all("hu_fan" not in p for p in payloads))
        self.assertEqual(self.state.blood_win_events[0]["changes"][2], 0)

    async def test_rob_kong_reverts_meld_and_continues(self):
        await self.win(0, [1], qianggang=True)
        self.assertEqual(self.state.player_list[0].combination_tiles, ["k45"])
        self.assertEqual(len(self.state.player_list[0].combination_mask[0]), 6)
        self.assertIsNone(self.state.jiagang_tile)
        self.assertEqual(self.state.game_status, "deal_card")

    async def test_wall_exhaustion_with_zero_one_two_winners(self):
        for count in range(3):
            self.state = state_for_test()
            if count:
                await self.win(0, list(range(1, count + 1)))
            self.state.tiles_list = []
            self.state.current_round = 4
            await blood.finish_round(self.state, dict.fromkeys(range(4), 0))
            payload = self.state.broadcast_result.await_args.kwargs
            self.assertEqual(payload["blood_end_reason"], "wall_exhausted")
            self.assertEqual(sum(p.score for p in self.state.player_list), 0)


class ClaimCollectionTests(unittest.IsolatedAsyncioTestCase):
    async def test_one_hu_one_pass_only_confirmed_claim_is_recorded(self):
        state = state_for_test()
        state.game_status = "waiting_action_after_cut"
        state.claim_protection = False
        state.player_list[0].discard_tiles = [45]
        state.action_dict = {0: [], 1: ["hu_first", "pass"], 2: ["hu_second", "pass"], 3: []}
        state.result_dict = {"hu_first": (16, ["测试"]), "hu_second": (16, ["测试"])}
        state.action_queues[1].put_nowait({"action_type": "hu_first"})
        state.action_queues[2].put_nowait({"action_type": "pass"})
        with patch("server.gamestate.game_guobiao.wait_action.flush_unexecuted_claim_applications"), patch("server.gamestate.game_guobiao.wait_action.finalize_claim_protection", AsyncMock()):
            await wait_action(state)
        self.assertEqual(state.blood_pending_claims, {1: "hu_first"})
        self.assertEqual(state.game_status, "check_hepai")


if __name__ == "__main__":
    unittest.main()
