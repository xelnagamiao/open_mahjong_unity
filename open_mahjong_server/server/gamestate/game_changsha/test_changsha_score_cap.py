import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from server.gamestate.game_changsha.ChangshaGameState import ChangshaGameState
from server.gamestate.game_changsha import ChangshaGameState as state_module
from server.room.room_validators import ChangshaRoomValidator


class ChangshaScoreCapTest(unittest.TestCase):
    def make_state(self, cap_type, limit, no_dealer=True):
        state = ChangshaGameState.__new__(ChangshaGameState)
        state.player_list = []
        for index in range(4):
            state.player_list.append(SimpleNamespace(
                player_index=index, original_player_index=index, score=0,
                hand_tiles=[11 + index], huapai_list=[], combination_mask=[],
                record_counter=SimpleNamespace(dianhe_times=0, recorded_fans=[], win_score=0, win_turn=0, fangchong_times=0, fangchong_score=0),
            ))
        state.score_cap_type = cap_type
        state.score_cap_value = limit
        state.base_score_no_dealer = no_dealer
        state.small_hu_score = 2
        state.big_hu_score = 6
        state.dealer_bird = True
        state.bird_count = 2
        state.tiles_list = [11, 22, 33]
        state.current_player_index = 0
        state.current_round = state.round_index = state.max_round = 1
        state.player_action_tick = 0
        state.xunmu = 1
        state.round_random_seed = 123
        state.result_dict = {}
        state.game_record = {"game_title": {"rule": "changsha"}, "game_round": {"round_index_1": {"action_ticks": []}}}
        return state

    def test_each_self_draw_payer_is_capped_separately(self):
        state = self.make_state("score", 10)
        result = state._score_changsha_win(0, ["清一色", "杠上开花"], True)
        self.assertEqual(result["actual_hu_score"], 30)
        self.assertEqual([p.score for p in state.player_list], [30, -10, -10, -10])
        self.assertEqual(sum(p.score for p in state.player_list), 0)

    def test_big_hu_cap_precedes_bird_multiplier_in_both_modes(self):
        for no_dealer in (False, True):
            state = self.make_state("big_hu", 3, no_dealer)
            result = state._score_changsha_win(1, ["清一色", "杠上开花", "杠上开花", "杠上开花"], False, 0)
            expected = (6 if no_dealer else 7) * 3 * 4
            self.assertEqual(result["actual_hu_score"], expected)
            self.assertGreater(result["payer_details"][0]["uncapped_payment"], expected)

    def test_same_pair_batch_score_cap_is_64_not_96(self):
        state = self.make_state("score", 64)
        events = [
            {"tile": tile, "tile_order": i, "winner": 1, "hu_class": "hu_first", "result": (12, ["碰碰胡", "杠上炮"])}
            for i, tile in enumerate((21, 22))
        ]
        state.accepted_gang_replacement_ron_results = events
        state.pending_gang_replacement_ron_results = {1: events}
        captured = AsyncMock()
        with patch.object(state_module, "broadcast_result", captured), patch.object(state_module, "hu_result_ready_wait_seconds", return_value=0):
            result = asyncio.run(state._settle_gang_replacement_multi_ron({i: 0 for i in range(4)}))
        self.assertEqual(result["total_hu_score"], 64)
        self.assertEqual([p.score for p in state.player_list], [-64, 64, 0, 0])
        self.assertEqual([call.kwargs["hu_score"] for call in captured.call_args_list], [48, 16])
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        self.assertEqual([tick[2] for tick in ticks], [48, 16])
        self.assertEqual([tick[5] for tick in ticks], [21, 22])

    def test_big_hu_quantity_is_accumulated_across_batch(self):
        state = self.make_state("big_hu", 3)
        totals = {}
        first = state._score_changsha_win(1, ["碰碰胡", "杠上炮"], False, 0, batch_cap_totals=totals)
        second = state._score_changsha_win(1, ["清一色", "杠上炮"], False, 0, batch_cap_totals=totals)
        self.assertEqual(first["actual_hu_score"] + second["actual_hu_score"], 72)
        self.assertEqual(totals[(0, 1)], 3)

    def test_different_winners_have_separate_caps(self):
        state = self.make_state("score", 10)
        totals = {}
        for winner in (1, 2):
            state._score_changsha_win(winner, ["清一色", "杠上炮"], False, 0, batch_cap_totals=totals)
        self.assertEqual([p.score for p in state.player_list], [-20, 10, 10, 0])

    def test_initial_hu_ignores_both_cap_types(self):
        for cap_type in ("big_hu", "score"):
            state = self.make_state(cap_type, 1)
            state._roll_initial_hu_dice = lambda index: [1, 1]
            result = state._score_initial_hu(0, ["四喜"])
            self.assertEqual(result["actual_hu_score"], 24)

    def test_room_cap_validation_and_disabled_normalization(self):
        base = dict(room_name="长沙", game_round=1, round_timer=0, step_timer=5)
        for kind in ("score", "big_hu"):
            self.assertEqual(ChangshaRoomValidator(**base, score_cap_type=kind, score_cap_value=64).score_cap_value, 64)
            for value in (0, -1, True, 1000001):
                with self.assertRaises(ValueError):
                    ChangshaRoomValidator(**base, score_cap_type=kind, score_cap_value=value)
            with self.assertRaises(ValueError):
                ChangshaRoomValidator(**base, score_cap_type=kind)
        self.assertEqual(ChangshaRoomValidator(**base, score_cap_type="none", score_cap_value=64).score_cap_value, 0)
        with self.assertRaises(ValueError):
            ChangshaRoomValidator(**base, score_cap_type="unknown", score_cap_value=64)
