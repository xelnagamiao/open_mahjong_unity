import asyncio
import importlib
import unittest
from types import SimpleNamespace
from unittest.mock import patch

from server.game_calculation.changsha.changsha_hepai_check import (
    Changsha_Hepai_Check,
    INITIAL_HU_NAMES,
    changsha_base_from_fans,
    changsha_initial_hu_reveal_tiles,
    evaluate_changsha_initial_hu,
)
from server.gamestate.game_changsha.ChangshaGameState import ChangshaGameState
from server.gamestate.game_changsha.action_check import (
    check_action_after_cut,
    check_action_after_batch_gang_forced_cut,
    check_action_after_gang_forced_cut,
    check_action_hand_action,
    check_only_cut,
    check_hepai,
    filter_open_kong_replacement_actions,
    refresh_waiting_tiles,
)
from server.gamestate.game_changsha.boardcast import _build_do_action_payload, broadcast_ask_hand_action, broadcast_do_action
from server.gamestate.game_changsha.init_tiles import init_changsha_tiles
from server.gamestate.public.ai.get_action import get_action as public_get_action
from server.gamestate.public.next_game_round import next_game_round_random_switchseat
from server.room.room_validators import ChangshaRoomValidator

wait_action_module = importlib.import_module("server.gamestate.game_changsha.wait_action")
changsha_state_module = importlib.import_module("server.gamestate.game_changsha.ChangshaGameState")


class DummyPlayer:
    def __init__(self, player_index, hand_tiles=None, waiting_tiles=None):
        self.player_index = player_index
        self.hand_tiles = list(hand_tiles or [])
        self.discard_tiles = []
        self.discard_origin_tiles = []
        self.waiting_tiles = set(waiting_tiles or [])
        self.combination_tiles = []
        self.combination_mask = []
        self.tag_list = []
        self.has_draw_slot = False

    def get_tile(self, tiles_list, *, mark_draw_slot=True):
        tile = tiles_list.pop(0)
        self.hand_tiles.append(tile)
        if mark_draw_slot:
            self.has_draw_slot = True
        return tile


class FixedCalculation:
    def __init__(self, result):
        self.result = result

    def Changsha_hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        return self.result


class TileMappedCalculation:
    def __init__(self, results_by_tile):
        self.results_by_tile = dict(results_by_tile)
        self.calls = []

    def Changsha_hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        self.calls.append((list(hand_list), get_tile))
        return self.results_by_tile.get(get_tile, (0, []))


class FixedCalculationAndTingpai:
    def __init__(self, result, waiting_tiles):
        self.result = result
        self.waiting_tiles = set(waiting_tiles)

    def Changsha_hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        return self.result

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set(self.waiting_tiles)


class SeaBottomCalculation:
    def __init__(self, self_result=(0, []), ron_result=(0, []), waiting_tiles=None):
        self.self_result = self_result
        self.ron_result = ron_result
        self.waiting_tiles = set(waiting_tiles or [])

    def Changsha_hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        if "河底捞鱼" in way_to_hepai:
            return self.ron_result
        if "海底捞月" in way_to_hepai:
            return self.self_result
        return 0, []

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set(self.waiting_tiles)


class FixedTingpai:
    def __init__(self, waiting_tiles):
        self.waiting_tiles = set(waiting_tiles)

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set(self.waiting_tiles)


class ConditionalTingpai:
    def __init__(self, expected_hand, expected_melds, waiting_tiles):
        self.expected_hand = list(expected_hand)
        self.expected_melds = list(expected_melds)
        self.waiting_tiles = set(waiting_tiles)
        self.calls = []

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        hand = list(hand_list)
        melds = list(tiles_combination)
        self.calls.append((hand, melds))
        if hand == self.expected_hand and melds == self.expected_melds:
            return set(self.waiting_tiles)
        return set()


class HandMappedTingpai:
    def __init__(self, waits_by_hand):
        self.waits_by_hand = {
            tuple(hand): set(waits) for hand, waits in waits_by_hand.items()
        }

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set(self.waits_by_hand.get(tuple(hand_list), set()))


class FixedBaseCalculation:
    def Changsha_base_from_fans(self, fan_list, dealer_related=False):
        return 1


class ChangshaRulesTest(unittest.TestCase):
    def bind_changsha_score_helpers(self, state):
        if not hasattr(state, "small_hu_score"):
            state.small_hu_score = 2
        if not hasattr(state, "big_hu_score"):
            state.big_hu_score = 8
        if not hasattr(state, "dealer_bird"):
            state.dealer_bird = True
        if not hasattr(state, "base_score_no_dealer"):
            state.base_score_no_dealer = False
        state._changsha_base_from_fans = (
            lambda fans, dealer_related=False: ChangshaGameState._changsha_base_from_fans(state, fans, dealer_related)
        )
        state._changsha_bird_origin = (
            lambda winner: ChangshaGameState._changsha_bird_origin(state, winner)
        )

    def test_initial_deal_gives_dealer_fourteen_tiles(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            master_seed=1234,
            current_round=1,
            Debug=False,
        )

        init_changsha_tiles(state)

        self.assertEqual([len(p.hand_tiles) for p in state.player_list], [14, 13, 13, 13])
        self.assertEqual(len(state.tiles_list), 55)
        self.assertTrue(state.player_list[0].has_draw_slot)
        self.assertFalse(any(p.has_draw_slot for p in state.player_list[1:]))

    def test_discard_check_asks_all_eligible_peng_and_gang_players(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [12, 13, 14]),
                DummyPlayer(1, [11, 11, 15]),
                DummyPlayer(2, [11, 11, 11]),
                DummyPlayer(3, [11, 11, 19]),
            ],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=True,
            calculation_service=FixedTingpai([12]),
        )

        actions = check_action_after_cut(state, 11)

        self.assertEqual(actions[0], [])
        self.assertIn("peng", actions[1])
        self.assertIn("peng", actions[2])
        self.assertIn("peng", actions[3])
        self.assertIn("gang", actions[2])

    def test_discard_check_allows_upper_player_to_chi(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [11, 12, 25]),
                DummyPlayer(1, [31, 32, 33]),
                DummyPlayer(2, [12, 14, 26]),
                DummyPlayer(3, [11, 12, 13]),
            ],
            current_player_index=3,
            tiles_list=[21],
            dihe_possible=True,
            calculation_service=FixedTingpai([11]),
        )

        actions = check_action_after_cut(state, 13)

        self.assertIn("chi_left", actions[0])
        self.assertIn("pass", actions[0])
        self.assertEqual(actions[3], [])
        self.assertNotIn("chi_mid", actions[2])
        self.assertNotIn("chi_mid", actions[1])

    def test_discard_check_rejects_lower_player_chi(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [11, 12, 25]),
                DummyPlayer(1, [31, 32, 33]),
                DummyPlayer(2, [12, 14, 26]),
                DummyPlayer(3, [11, 12, 13]),
            ],
            current_player_index=1,
            tiles_list=[21],
            dihe_possible=True,
            calculation_service=FixedTingpai([11]),
        )

        actions = check_action_after_cut(state, 13)

        self.assertNotIn("chi_left", actions[0])
        self.assertNotIn("chi_mid", actions[0])
        self.assertNotIn("chi_right", actions[0])

    def test_forced_gang_discard_prioritizes_win_claims(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [12, 13, 14]),
                DummyPlayer(1, [11, 12, 13, 13]),
                DummyPlayer(2, [13, 13, 13]),
                DummyPlayer(3, [21, 22, 23]),
            ],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=True,
            calculation_service=FixedCalculationAndTingpai((1, ["小胡"]), [13]),
            result_dict={},
            player_passed_hu_base={},
            current_claim_cut_tile=None,
        )
        actions = check_action_after_gang_forced_cut(state, 13)

        self.assertEqual(actions[1], ["hu_first", "pass"])
        for player_index in (2, 3):
            self.assertEqual(actions[player_index], [])
        self.assertEqual(actions[0], [])
        self.assertEqual(state.current_claim_cut_tile, 13)

    def test_forced_gang_discard_offers_best_meld_without_hu(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [12, 13, 14]),
                DummyPlayer(1, [11, 12, 13, 13]),
                DummyPlayer(2, [13, 13, 13]),
                DummyPlayer(3, [21, 22, 23]),
            ],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=True,
            calculation_service=FixedTingpai([11]),
            result_dict={},
            player_passed_hu_base={},
            current_claim_cut_tile=None,
        )

        actions = check_action_after_gang_forced_cut(state, 13)

        self.assertEqual(actions[0], [])
        self.assertEqual(actions[1], ["peng", "pass"])
        self.assertEqual(actions[2], ["peng", "gang", "pass"])
        self.assertEqual(actions[3], [])
        self.assertEqual(state.current_claim_cut_tile, 13)

    def test_batch_forced_gang_discard_only_one_tile_can_be_claimed(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [12, 13, 14]),
                DummyPlayer(1, [11, 12, 13, 13]),
                DummyPlayer(2, [14, 14]),
                DummyPlayer(3, [21, 22, 23]),
            ],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=True,
            calculation_service=FixedTingpai([]),
            result_dict={},
            player_passed_hu_base={},
            current_claim_cut_tile=None,
        )

        actions = check_action_after_batch_gang_forced_cut(state, [13, 14])

        self.assertEqual(state.current_claim_cut_tile, 13)
        self.assertEqual(actions[1], ["peng", "pass"])
        self.assertEqual(actions[2], [])
        self.assertEqual(actions[3], [])

    def test_open_kong_locked_player_can_win_but_not_meld(self):
        locked_player = DummyPlayer(1, [11, 12, 13, 13, 13], waiting_tiles=[13])
        locked_player.open_kong_locked = True
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [21, 22, 23]),
                locked_player,
                DummyPlayer(2, [13, 13, 13]),
                DummyPlayer(3, [21, 22, 23]),
            ],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=False,
            calculation_service=FixedCalculationAndTingpai((1, ["小胡"]), [13]),
            result_dict={},
            player_passed_hu_base={},
        )

        actions = check_action_after_cut(state, 13)

        self.assertTrue(any(action.startswith("hu_") for action in actions[1]))
        self.assertIn("pass", actions[1])
        self.assertNotIn("chi_left", actions[1])
        self.assertNotIn("chi_mid", actions[1])
        self.assertNotIn("chi_right", actions[1])
        self.assertNotIn("peng", actions[1])
        self.assertNotIn("gang", actions[1])

    def test_discard_open_kong_requires_ready_hand(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [12, 13, 14]),
                DummyPlayer(1, [11, 11, 11]),
                DummyPlayer(2, [11, 11, 11]),
                DummyPlayer(3, [21, 22, 23]),
            ],
            current_player_index=0,
            tiles_list=[31],
            dihe_possible=True,
            calculation_service=FixedTingpai([]),
        )

        actions = check_action_after_cut(state, 11)

        self.assertIn("peng", actions[1])
        self.assertIn("peng", actions[2])
        self.assertNotIn("gang", actions[1])
        self.assertNotIn("gang", actions[2])

    def test_discard_open_kong_checks_ready_after_declared_kong(self):
        checker = ConditionalTingpai(
            expected_hand=[12, 13, 14],
            expected_melds=["g11"],
            waiting_tiles=[15],
        )
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [21, 22, 23]),
                DummyPlayer(1, [11, 11, 11, 12, 13, 14]),
                DummyPlayer(2, [21, 22, 23]),
                DummyPlayer(3, [31, 32, 33]),
            ],
            current_player_index=0,
            tiles_list=[31],
            dihe_possible=True,
            calculation_service=checker,
        )

        actions = check_action_after_cut(state, 11)

        self.assertIn("gang", actions[1])
        self.assertIn(([12, 13, 14], ["g11"]), checker.calls)

    def test_self_replacement_without_ready_only_offers_buzhang(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [17, 12, 13]),
                DummyPlayer(1),
                DummyPlayer(2),
                DummyPlayer(3),
            ],
            tiles_list=[31],
            calculation_service=FixedTingpai([]),
        )
        state.player_list[0].combination_tiles = ["k17"]
        state._is_open_kong_ready_after_declared = lambda player, tile: ChangshaGameState._is_open_kong_ready_after_declared(state, player, tile)

        actions = check_action_hand_action(state, 0)

        self.assertIn("buzhang", actions[0])
        self.assertNotIn("jiagang", actions[0])

    def test_self_replacement_ready_offers_buzhang_and_open_kong(self):
        checker = ConditionalTingpai(
            expected_hand=[12, 13],
            expected_melds=["g17"],
            waiting_tiles=[14],
        )
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [17, 12, 13]),
                DummyPlayer(1),
                DummyPlayer(2),
                DummyPlayer(3),
            ],
            tiles_list=[31],
            calculation_service=checker,
        )
        state.player_list[0].combination_tiles = ["k17"]
        state._is_open_kong_ready_after_declared = lambda player, tile: ChangshaGameState._is_open_kong_ready_after_declared(state, player, tile)

        actions = check_action_hand_action(state, 0)

        self.assertIn("buzhang", actions[0])
        self.assertIn("jiagang", actions[0])

    def test_after_claim_turn_can_open_kong_before_discard(self):
        checker = ConditionalTingpai(
            expected_hand=[12, 13],
            expected_melds=["G17"],
            waiting_tiles=[14],
        )
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [17, 17, 17, 17, 12, 13]),
                DummyPlayer(1),
                DummyPlayer(2),
                DummyPlayer(3),
            ],
            tiles_list=[31],
            calculation_service=checker,
        )
        state._is_open_kong_ready_after_declared = lambda player, tile: ChangshaGameState._is_open_kong_ready_after_declared(state, player, tile)

        actions = check_only_cut(state, 0)

        self.assertIn("cut", actions[0])
        self.assertIn("buzhang", actions[0])
        self.assertIn("angang", actions[0])

    def test_after_claim_turn_can_open_jiagang_before_discard(self):
        checker = ConditionalTingpai(
            expected_hand=[12, 13],
            expected_melds=["g17"],
            waiting_tiles=[14],
        )
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [17, 12, 13]),
                DummyPlayer(1),
                DummyPlayer(2),
                DummyPlayer(3),
            ],
            tiles_list=[31],
            calculation_service=checker,
        )
        state.player_list[0].combination_tiles = ["k17"]
        state._is_open_kong_ready_after_declared = lambda player, tile: ChangshaGameState._is_open_kong_ready_after_declared(state, player, tile)

        actions = check_only_cut(state, 0)

        self.assertIn("cut", actions[0])
        self.assertIn("buzhang", actions[0])
        self.assertIn("jiagang", actions[0])

    def test_initial_hu_types_match_classic_changsha_patterns(self):
        hand = [11, 11, 11, 11, 13, 13, 23, 23, 33, 33, 24, 24, 24]

        self.assertCountEqual(
            evaluate_changsha_initial_hu(hand),
            ["四喜", "板板胡", "六六顺", "三同"],
        )

    def test_initial_hu_si_xi_accepts_more_than_four_same_tiles(self):
        hand = [11, 11, 11, 11, 11, 13, 23, 33, 24, 24, 24, 26, 27, 28]

        self.assertIn("四喜", evaluate_changsha_initial_hu(hand))

    def test_initial_hu_reveal_tiles_only_concrete_sixi_group(self):
        hand = [11, 11, 11, 11, 13, 14, 16, 17, 19, 21, 23, 24, 26]

        reveal = changsha_initial_hu_reveal_tiles(hand, [INITIAL_HU_NAMES["siXi"]])

        self.assertEqual(reveal, [11, 11, 11, 11])

    def test_initial_hu_reveal_tiles_only_two_triplets_for_liuliushun(self):
        hand = [11, 11, 11, 22, 22, 22, 13, 14, 16, 17, 19, 23, 24]

        reveal = changsha_initial_hu_reveal_tiles(hand, [INITIAL_HU_NAMES["liuLiuShun"]])

        self.assertEqual(reveal, [11, 11, 11, 22, 22, 22])

    def test_initial_hu_reveal_tiles_only_three_pairs_for_santong(self):
        hand = [14, 14, 24, 24, 34, 34, 11, 12, 13, 16, 17, 18, 19]

        reveal = changsha_initial_hu_reveal_tiles(hand, [INITIAL_HU_NAMES["sanTong"]])

        self.assertEqual(reveal, [14, 14, 24, 24, 34, 34])

    def test_follow_hu_blocks_same_or_lower_after_pass(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=False,
            calculation_service=FixedCalculation((1, ["小胡"])),
            result_dict={},
            player_passed_hu_base={1: 1},
        )
        actions = {0: [], 1: [], 2: [], 3: []}

        check_hepai(state, actions, 11, 1, "dianhe")

        self.assertEqual(actions[1], [])

    def test_follow_hu_allows_higher_win_after_pass(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=False,
            calculation_service=FixedCalculation((6, ["碰碰胡"])),
            result_dict={},
            player_passed_hu_base={1: 1},
        )
        actions = {0: [], 1: [], 2: [], 3: []}

        check_hepai(state, actions, 11, 1, "dianhe")

        self.assertTrue(any(action.startswith("hu_") for action in actions[1]))

    def test_self_draw_ignores_follow_hu_pass_limit(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            current_player_index=1,
            tiles_list=[21],
            calculation_service=FixedCalculation((1, ["灏忚儭"])),
            result_dict={},
            player_passed_hu_base={1: 1},
        )
        state.player_list[1].hand_tiles = [11, 12, 13]
        actions = {0: [], 1: [], 2: [], 3: []}

        check_hepai(state, actions, 13, 1, "handgot")

        self.assertIn("hu_self", actions[1])

    def test_own_discard_clears_follow_hu_pass_limit(self):
        state = SimpleNamespace(player_passed_hu_base={1: 1})

        ChangshaGameState.clear_hu_pass_after_own_discard(state, 1)

        self.assertEqual(state.player_passed_hu_base, {})

    def test_follow_hu_limit_persists_until_own_discard_refresh(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            current_player_index=0,
            tiles_list=[21],
            dihe_possible=False,
            last_draw_was_gang=False,
            calculation_service=FixedCalculation((1, ["小胡"])),
            result_dict={},
            player_passed_hu_base={},
        )
        state.record_hu_pass = lambda player_index, allowed_actions: ChangshaGameState.record_hu_pass(state, player_index, allowed_actions)
        state.clear_hu_pass_after_own_discard = lambda player_index: ChangshaGameState.clear_hu_pass_after_own_discard(state, player_index)

        first_actions = {0: [], 1: [], 2: [], 3: []}
        check_hepai(state, first_actions, 14, 1, "dianhe")
        self.assertTrue(any(action.startswith("hu_") for action in first_actions[1]))

        state.record_hu_pass(1, first_actions[1])
        self.assertEqual(state.player_passed_hu_base[1], 1)

        for next_discarder in (2, 3):
            state.current_player_index = next_discarder
            blocked_actions = {0: [], 1: [], 2: [], 3: []}
            check_hepai(state, blocked_actions, 14, 1, "dianhe")
            self.assertFalse(any(action.startswith("hu_") for action in blocked_actions[1]))

        state.clear_hu_pass_after_own_discard(1)
        state.current_player_index = 0
        refreshed_actions = {0: [], 1: [], 2: [], 3: []}
        check_hepai(state, refreshed_actions, 14, 1, "dianhe")

        self.assertTrue(any(action.startswith("hu_") for action in refreshed_actions[1]))

    def test_changsha_base_scores_are_classic_double_bird_units(self):
        self.assertEqual(changsha_base_from_fans(["小胡"], dealer_related=False), 1)
        self.assertEqual(changsha_base_from_fans(["小胡"], dealer_related=True), 2)
        self.assertEqual(changsha_base_from_fans(["碰碰胡", "清一色"], dealer_related=False), 12)
        self.assertEqual(changsha_base_from_fans(["碰碰胡", "清一色"], dealer_related=True), 14)

    def test_changsha_base_scores_can_ignore_dealer_relation(self):
        self.assertEqual(
            changsha_base_from_fans(
                ["小胡"],
                small_hu_score=3,
                big_hu_score=9,
                base_score_no_dealer=True,
            ),
            3,
        )
        self.assertEqual(
            changsha_base_from_fans(
                ["小胡"],
                dealer_related=True,
                small_hu_score=3,
                big_hu_score=9,
                base_score_no_dealer=True,
            ),
            3,
        )
        self.assertEqual(
            changsha_base_from_fans(
                ["碰碰胡", "清一色"],
                dealer_related=True,
                small_hu_score=3,
                big_hu_score=9,
                base_score_no_dealer=True,
            ),
            18,
        )

    def test_jiangjianghu_is_detected_and_displayed(self):
        score, fan_list = Changsha_Hepai_Check().hepai_check(
            [12, 12, 12, 15, 15, 15, 18, 18, 18, 22, 22, 22, 25, 25],
            [],
            ["自摸"],
            25,
        )

        self.assertEqual(score, 12)
        self.assertIn("将将胡", fan_list)

    def test_jiangjianghu_only_requires_all_jiang_tiles(self):
        score, fan_list = Changsha_Hepai_Check().hepai_check(
            [12, 12, 15, 15, 18, 18, 22, 22, 25, 25, 28, 28, 32, 35],
            [],
            ["自摸"],
            35,
        )

        self.assertEqual(score, 6)
        self.assertEqual(fan_list, ["将将胡"])

    def test_luxury_seven_pairs_counts_quad_levels(self):
        checker = Changsha_Hepai_Check()

        normal_score, normal_fans = checker.hepai_check(
            [11, 11, 13, 13, 14, 14, 16, 16, 21, 21, 23, 23, 34, 34],
            [],
            ["自摸"],
            34,
        )
        luxury_score, luxury_fans = checker.hepai_check(
            [11, 11, 11, 11, 13, 13, 14, 14, 16, 16, 21, 21, 23, 23],
            [],
            ["自摸"],
            23,
        )
        double_score, double_fans = checker.hepai_check(
            [11, 11, 11, 11, 13, 13, 13, 13, 21, 21, 23, 23, 34, 34],
            [],
            ["自摸"],
            34,
        )
        triple_score, triple_fans = checker.hepai_check(
            [11, 11, 11, 11, 13, 13, 13, 13, 21, 21, 21, 21, 34, 34],
            [],
            ["自摸"],
            34,
        )

        self.assertEqual((normal_score, normal_fans), (6, ["七小对"]))
        self.assertEqual((luxury_score, luxury_fans), (12, ["豪华七小对"]))
        self.assertEqual((double_score, double_fans), (18, ["双豪华七小对"]))
        self.assertEqual((triple_score, triple_fans), (24, ["三豪华七小对"]))

    def test_luxury_seven_pairs_stacks_with_other_big_hu(self):
        score, fan_list = Changsha_Hepai_Check().hepai_check(
            [11, 11, 11, 11, 13, 13, 13, 13, 14, 14, 16, 16, 17, 17],
            [],
            ["自摸"],
            17,
        )

        self.assertIn("清一色", fan_list)
        self.assertIn("双豪华七小对", fan_list)
        self.assertEqual(score, 24)

    def test_quanqiuren_all_melded_single_wait_ignores_jiang_pair_and_zimo(self):
        checker = Changsha_Hepai_Check()
        melds = ["s12", "s22", "k31", "k35"]

        zimo_score, zimo_fans = checker.hepai_check([14, 14], melds, ["自摸"], 14)
        ron_score, ron_fans = checker.hepai_check([14, 14], melds, [], 14)

        self.assertEqual(zimo_score, 6)
        self.assertIn("全求人", zimo_fans)
        self.assertEqual(ron_score, 6)
        self.assertIn("全求人", ron_fans)

    def test_changsha_room_validator_uses_four_eight_sixteen_hands(self):
        base = dict(
            room_name="test",
            game_round=1,
            round_timer=20,
            step_timer=5,
            random_seed=0,
            open_kong_replacement_count=2,
            bird_count=2,
        )

        for game_round in (1, 2, 4):
            cfg = ChangshaRoomValidator(**{**base, "game_round": game_round})
            self.assertEqual(cfg.game_round, game_round)

        with self.assertRaises(ValueError):
            ChangshaRoomValidator(**{**base, "game_round": 3})

    def test_initial_hu_room_toggles_filter_detected_types(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [11, 11, 11, 11, 13, 13, 23, 23, 33, 33, 24, 24, 24]),
                DummyPlayer(1, [11, 12, 13]),
            ],
            initial_hu_enabled={
                INITIAL_HU_NAMES["siXi"]: False,
                INITIAL_HU_NAMES["banBanHu"]: True,
                INITIAL_HU_NAMES["queYiSe"]: True,
                INITIAL_HU_NAMES["liuLiuShun"]: True,
                INITIAL_HU_NAMES["sanTong"]: True,
            },
        )

        ChangshaGameState._detect_initial_hu_types(state)

        self.assertNotIn(INITIAL_HU_NAMES["siXi"], state.initial_hu_types[0])
        self.assertIn(INITIAL_HU_NAMES["banBanHu"], state.initial_hu_types[0])

    def test_initial_hu_actions_ask_only_detected_players(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            initial_hu_types={1: ["四喜"]},
            current_player_index=0,
            action_dict={0: [], 1: [], 2: [], 3: []},
            game_status="waiting_initial_hu",
        )

        state.current_player_index = 1
        state.action_dict = {0: [], 1: ["initial_hu", "pass"], 2: [], 3: []}

        self.assertEqual(state.action_dict[1], ["initial_hu", "pass"])
        self.assertEqual(state.action_dict[0], [])
        self.assertEqual(state.action_dict[2], [])
        self.assertEqual(state.action_dict[3], [])

    def test_initial_hu_dice_birds_score_without_wall_draw(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        state = SimpleNamespace(
            player_list=players,
            calculation_service=FixedBaseCalculation(),
            round_random_seed=12345,
            current_round=1,
            small_hu_score=2,
            big_hu_score=8,
            dealer_bird=True,
            base_score_no_dealer=False,
        )
        state._roll_initial_hu_dice = lambda winner: [1, 2]
        state._initial_hu_dice_seat = ChangshaGameState._initial_hu_dice_seat
        state._player_by_index = lambda index: players[index]
        self.bind_changsha_score_helpers(state)

        result = ChangshaGameState._score_initial_hu(state, 1, ["四喜", "板板胡"])

        self.assertEqual(result["dice"], [1, 2])
        self.assertEqual(result["bird_seats"], [0, 1])
        self.assertEqual(players[1].score, 12)
        self.assertEqual([players[i].score for i in range(4)], [-8, 12, -2, -2])
        self.assertIn("四喜", result["fan_display"])
        self.assertIn("骰子:1,2", result["fan_display"])

    def test_initial_hu_broadcast_includes_revealed_hand(self):
        players = [
            SimpleNamespace(
                player_index=i,
                original_player_index=i,
                score=0,
                hand_tiles=[12, 12, 12, 12, 13, 14, 16, 17, 19, 21, 23, 24, 26] if i == 1 else [11 + i, 12 + i, 13 + i],
                huapai_list=[],
                combination_mask=[],
                record_counter=SimpleNamespace(zimo_times=0, recorded_fans=[], win_score=0),
            )
            for i in range(4)
        ]
        state = SimpleNamespace(
            player_list=players,
            initial_hu_types={1: ["四喜"]},
            round_random_seed=12345,
            current_round=1,
            small_hu_score=2,
            big_hu_score=8,
            dealer_bird=True,
            base_score_no_dealer=False,
        )
        state._roll_initial_hu_dice = lambda winner: [1, 2]
        state._initial_hu_dice_seat = ChangshaGameState._initial_hu_dice_seat
        state._player_by_index = lambda index: players[index]
        self.bind_changsha_score_helpers(state)
        state._score_initial_hu = lambda winner, hu_types: ChangshaGameState._score_initial_hu(state, winner, hu_types)
        payloads = []

        async def capture_broadcast(*args, **kwargs):
            payloads.append(kwargs)

        with patch.object(changsha_state_module, "broadcast_result", capture_broadcast):
            asyncio.run(ChangshaGameState._settle_initial_hu(state, 1))

        self.assertEqual(payloads[0]["hepai_player_hand"], [12, 12, 12, 12])
        self.assertEqual(payloads[0]["hu_class"], "initial_hu")
        self.assertTrue(payloads[0]["round_continues"])

    def test_bird_scoring_uses_configured_count_and_origin(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        origins = []
        state = SimpleNamespace(
            player_list=players,
            bird_count=1,
            dealer_bird=False,
            calculation_service=FixedBaseCalculation(),
            small_hu_score=2,
            big_hu_score=8,
            base_score_no_dealer=False,
        )

        def draw_birds(count):
            state.requested_bird_count = count
            return [11] * count

        def bird_seat(tile, origin):
            origins.append(origin)
            return 1

        def player_by_index(index):
            return players[index]

        state._draw_changsha_birds = draw_birds
        state._changsha_bird_seat = bird_seat
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = player_by_index
        self.bind_changsha_score_helpers(state)

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=1,
            fan_list=["小胡"],
            is_zimo=False,
            discarder=2,
        )

        self.assertEqual(state.requested_bird_count, 1)
        self.assertEqual(origins, [1])
        self.assertEqual(players[1].score, 2)
        self.assertEqual(players[2].score, -2)
        self.assertEqual(result["bird_seats"], [1])

    def test_changsha_bird_origin_follows_room_config(self):
        dealer_origin_state = SimpleNamespace(dealer_bird=True)
        winner_origin_state = SimpleNamespace(dealer_bird=False)

        self.assertEqual(ChangshaGameState._changsha_bird_origin(dealer_origin_state, 2), 0)
        self.assertEqual(ChangshaGameState._changsha_bird_origin(winner_origin_state, 2), 2)

    def test_bird_scoring_uses_initial_dealer_origin_when_configured(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        origins = []
        state = SimpleNamespace(
            player_list=players,
            bird_count=1,
            dealer_bird=True,
            small_hu_score=2,
            big_hu_score=8,
            base_score_no_dealer=False,
        )
        state._draw_changsha_birds = lambda count: [11]
        state._changsha_bird_seat = lambda tile, origin: origins.append(origin) or origin
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = lambda index: players[index]
        self.bind_changsha_score_helpers(state)

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=1,
            fan_list=["小胡"],
            is_zimo=False,
            discarder=2,
        )

        self.assertEqual(origins, [0])
        self.assertEqual(result["bird_seats"], [0])
        self.assertEqual(players[1].score, 1)
        self.assertEqual(players[2].score, -1)

    def test_win_scoring_base_score_defaults_to_dealer_relation(self):
        def score_zimo(winner):
            players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
            state = SimpleNamespace(
                player_list=players,
                bird_count=0,
                dealer_bird=True,
                small_hu_score=2,
                big_hu_score=8,
                base_score_no_dealer=False,
            )
            state._draw_changsha_birds = lambda count: []
            state._changsha_bird_seat = ChangshaGameState._changsha_bird_seat
            state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
            state._player_by_index = lambda index: players[index]
            self.bind_changsha_score_helpers(state)
            return ChangshaGameState._score_changsha_win(
                state,
                winner=winner,
                fan_list=["小胡"],
                is_zimo=True,
            )

        dealer_win = score_zimo(0)
        non_dealer_win = score_zimo(1)

        self.assertEqual(dealer_win["actual_hu_score"], 6)
        self.assertEqual(non_dealer_win["actual_hu_score"], 4)
        self.assertEqual([item["base"] for item in dealer_win["payer_details"]], [2, 2, 2])
        self.assertEqual([item["base"] for item in non_dealer_win["payer_details"]], [2, 1, 1])

    def test_win_scoring_can_ignore_dealer_relation(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        state = SimpleNamespace(
            player_list=players,
            bird_count=0,
            dealer_bird=True,
            small_hu_score=2,
            big_hu_score=8,
            base_score_no_dealer=True,
        )
        state._draw_changsha_birds = lambda count: []
        state._changsha_bird_seat = ChangshaGameState._changsha_bird_seat
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = lambda index: players[index]
        self.bind_changsha_score_helpers(state)

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=1,
            fan_list=["小胡"],
            is_zimo=True,
        )

        self.assertEqual(result["actual_hu_score"], 6)
        self.assertEqual([item["base"] for item in result["payer_details"]], [2, 2, 2])

    def test_bird_scoring_displays_tile_name_rank_and_multiplier(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        state = SimpleNamespace(
            player_list=players,
            bird_count=2,
            dealer_bird=False,
            calculation_service=FixedBaseCalculation(),
            small_hu_score=2,
            big_hu_score=8,
            base_score_no_dealer=False,
        )
        state._draw_changsha_birds = lambda count: [24, 31]
        state._is_sea_bottom_win = ChangshaGameState._is_sea_bottom_win
        state._changsha_bird_seat = lambda tile, origin: {24: 2, 31: 0}[tile]
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = lambda index: players[index]
        self.bind_changsha_score_helpers(state)

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=1,
            fan_list=["小胡"],
            is_zimo=False,
            discarder=2,
        )

        self.assertEqual(ChangshaGameState._format_changsha_bird_tile(24), "四筒")
        self.assertIn("鸟牌:四筒,一条", result["fan_display"])
        self.assertIn("中鸟:四筒", result["fan_display"])
        self.assertIn("扎鸟倍数:x2", result["fan_display"])
        self.assertEqual(players[1].score, 2)
        self.assertEqual(players[2].score, -2)

    def test_bird_draw_uses_remaining_wall_front(self):
        state = SimpleNamespace(tiles_list=[11, 22, 33])

        birds = ChangshaGameState._draw_changsha_birds(state, 2)

        self.assertEqual(birds, [11, 22])
        self.assertEqual(state.tiles_list, [33])

    def test_sea_bottom_win_uses_winning_tile_as_bird_when_wall_empty(self):
        players = [
            SimpleNamespace(player_index=0, score=0, hand_tiles=[]),
            SimpleNamespace(player_index=1, score=0, hand_tiles=[11]),
            SimpleNamespace(player_index=2, score=0, hand_tiles=[]),
            SimpleNamespace(player_index=3, score=0, hand_tiles=[]),
        ]
        state = SimpleNamespace(
            player_list=players,
            bird_count=2,
            dealer_bird=False,
            calculation_service=FixedBaseCalculation(),
            tiles_list=[],
            small_hu_score=2,
            big_hu_score=8,
            base_score_no_dealer=True,
        )
        state._draw_changsha_birds = lambda count: ChangshaGameState._draw_changsha_birds(state, count)
        state._is_sea_bottom_win = ChangshaGameState._is_sea_bottom_win
        state._sea_bottom_bird_tile = lambda winner: ChangshaGameState._sea_bottom_bird_tile(state, winner)
        state._changsha_bird_seat = ChangshaGameState._changsha_bird_seat
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = lambda index: players[index]
        self.bind_changsha_score_helpers(state)

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=1,
            fan_list=["海底"],
            is_zimo=False,
            discarder=2,
        )

        self.assertEqual(result["birds"], [11])
        self.assertEqual(players[1].score, 16)
        self.assertEqual(players[2].score, -16)

    def test_sea_bottom_skips_noten_players(self):
        p1_hand = [11, 12, 13]
        p2_hand = [21, 22, 23]
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [31, 32, 33]),
                DummyPlayer(1, p1_hand),
                DummyPlayer(2, p2_hand),
                DummyPlayer(3, [17, 18, 19]),
            ],
            current_player_index=0,
            calculation_service=HandMappedTingpai({
                tuple(p1_hand): [],
                tuple(p2_hand): [24],
            }),
        )
        state.refresh_waiting_tiles = lambda player_index: refresh_waiting_tiles(state, player_index)

        self.assertEqual(ChangshaGameState._next_sea_bottom_player(state), 2)

    def test_sea_bottom_prepares_choice_for_next_tenpai_player(self):
        p1_hand = [11, 12, 13]
        p2_hand = [21, 22, 23]
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [31, 32, 33]),
                DummyPlayer(1, p1_hand),
                DummyPlayer(2, p2_hand),
                DummyPlayer(3, [17, 18, 19]),
            ],
            current_player_index=0,
            sea_bottom_candidates=[1, 2],
            calculation_service=HandMappedTingpai({
                tuple(p1_hand): [],
                tuple(p2_hand): [24],
            }),
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)

        self.assertTrue(ChangshaGameState._prepare_next_sea_bottom_choice(state))
        self.assertEqual(state.current_player_index, 2)
        self.assertEqual(state.action_dict[2], ["sea_bottom", "pass"])
        self.assertEqual(state.game_status, "waiting_sea_bottom")

    def test_sea_bottom_rechecks_and_clears_stale_waiting_tiles(self):
        stale_player = DummyPlayer(1, [11, 12, 13], waiting_tiles=[14])
        tenpai_player = DummyPlayer(2, [21, 22, 23])
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [31, 32, 33]),
                stale_player,
                tenpai_player,
                DummyPlayer(3, [17, 18, 19]),
            ],
            current_player_index=0,
            calculation_service=HandMappedTingpai({
                tuple(stale_player.hand_tiles): [],
                tuple(tenpai_player.hand_tiles): [24],
            }),
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)

        self.assertEqual(ChangshaGameState._next_sea_bottom_player(state), 2)
        self.assertEqual(stale_player.waiting_tiles, set())

    def test_sea_bottom_ends_when_no_player_is_tenpai(self):
        state = SimpleNamespace(
            player_list=[
                DummyPlayer(0, [31, 32, 33]),
                DummyPlayer(1, [11, 12, 13]),
                DummyPlayer(2, [21, 22, 23]),
                DummyPlayer(3, [17, 18, 19]),
            ],
            current_player_index=0,
            calculation_service=FixedTingpai([]),
        )
        state.refresh_waiting_tiles = lambda player_index: refresh_waiting_tiles(state, player_index)

        self.assertIsNone(ChangshaGameState._next_sea_bottom_player(state))

    def test_sea_bottom_choice_exhaustion_ends_when_all_pass(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i, [11, 12, 13]) for i in range(4)],
            sea_bottom_candidates=[],
            action_dict={0: [], 1: [], 2: [], 3: []},
        )

        self.assertFalse(ChangshaGameState._prepare_next_sea_bottom_choice(state))
        self.assertEqual(state.action_dict, {0: [], 1: [], 2: [], 3: []})

    def test_sea_bottom_take_reveals_tile_to_river_without_touching_hand(self):
        player = DummyPlayer(1, [11, 12, 13], waiting_tiles=[24])
        state = SimpleNamespace(
            player_list=[DummyPlayer(0), player, DummyPlayer(2), DummyPlayer(3)],
            tiles_list=[25],
            current_player_index=0,
            action_dict={0: [], 1: [], 2: [], 3: []},
            calculation_service=SeaBottomCalculation(waiting_tiles=[24]),
            result_dict={},
            current_claim_cut_tile=None,
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)
        state.refresh_waiting_tiles = lambda index: None
        payloads = []

        async def capture_broadcast(**kwargs):
            payloads.append(kwargs)

        state.broadcast_do_action = capture_broadcast

        with patch.object(changsha_state_module, "player_action_record_cut", lambda *args, **kwargs: None):
            asyncio.run(ChangshaGameState._take_sea_bottom_tile(state, 1))

        self.assertEqual(player.hand_tiles, [11, 12, 13])
        self.assertFalse(player.has_draw_slot)
        self.assertEqual(player.discard_tiles, [25])
        self.assertEqual(state.tiles_list, [])
        self.assertIsNone(state.forced_cut_tile)
        self.assertEqual(state.forced_cut_tiles, [])
        self.assertEqual(state.action_dict, {0: [], 1: [], 2: [], 3: []})
        self.assertEqual(state.game_status, "END")
        self.assertEqual(payloads[0]["action_list"], ["cut"])
        self.assertEqual(payloads[0]["cut_tile"], 25)
        self.assertTrue(payloads[0]["sea_bottom_discard"])

    def test_sea_bottom_take_self_win_ends_as_sea_bottom_tsumo(self):
        player = DummyPlayer(1, [11, 12, 13], waiting_tiles=[25])
        state = SimpleNamespace(
            player_list=[DummyPlayer(0), player, DummyPlayer(2), DummyPlayer(3)],
            tiles_list=[25],
            current_player_index=0,
            action_dict={0: [], 1: [], 2: [], 3: []},
            calculation_service=SeaBottomCalculation(self_result=(8, ["海底"]), waiting_tiles=[25]),
            result_dict={},
            current_claim_cut_tile=None,
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)
        state.refresh_waiting_tiles = lambda index: None

        async def noop_broadcast_do_action(**kwargs):
            return None

        state.broadcast_do_action = noop_broadcast_do_action

        with patch.object(changsha_state_module, "player_action_record_cut", lambda *args, **kwargs: None):
            asyncio.run(ChangshaGameState._take_sea_bottom_tile(state, 1))

        self.assertEqual(state.hu_class, "hu_self")
        self.assertEqual(state.result_dict["hu_self"], (8, ["海底"]))
        self.assertEqual(player.hand_tiles, [11, 12, 13, 25])
        self.assertEqual(player.discard_tiles, [25])
        self.assertEqual(state.game_status, "END")

    def test_sea_bottom_take_without_self_win_asks_other_hu_only(self):
        players = [
            DummyPlayer(0, [11, 12, 13], waiting_tiles=[25]),
            DummyPlayer(1, [14, 15, 16]),
            DummyPlayer(2, [21, 22, 23], waiting_tiles=[25]),
            DummyPlayer(3, [31, 32, 33], waiting_tiles=[25]),
        ]
        state = SimpleNamespace(
            server_action_tick=0,
            player_list=players,
            tiles_list=[25],
            current_player_index=1,
            action_dict={0: [], 1: [], 2: [], 3: []},
            calculation_service=SeaBottomCalculation(ron_result=(8, ["海底"]), waiting_tiles=[25]),
            result_dict={},
            current_claim_cut_tile=None,
            action_priority={"hu_first": 5, "hu_second": 4, "hu_third": 3, "pass": 0},
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)
        state.refresh_waiting_tiles = lambda index: None

        async def noop_do_action(**kwargs):
            return None

        state.broadcast_do_action = noop_do_action

        with patch.object(changsha_state_module, "player_action_record_cut", lambda *args, **kwargs: None), \
             patch.object(changsha_state_module, "begin_claim_protection_interval", lambda *args, **kwargs: None):
            asyncio.run(ChangshaGameState._take_sea_bottom_tile(state, 1))

        self.assertEqual(players[1].discard_tiles, [25])
        self.assertEqual(state.current_claim_cut_tile, 25)
        self.assertEqual(state.action_dict[2], ["hu_first", "pass"])
        self.assertEqual(state.action_dict[3], ["hu_second", "pass"])
        self.assertEqual(state.action_dict[0], ["hu_third", "pass"])
        self.assertEqual(state.game_status, "waiting_action_after_cut")

    def test_sea_bottom_choice_processes_prequeued_click_directly(self):
        class FlagEvent:
            def __init__(self):
                self.is_set = False

            def clear(self):
                self.is_set = False

            def set(self):
                self.is_set = True

            async def wait(self):
                while not self.is_set:
                    await asyncio.sleep(0)
                return True

        original_wait_action = wait_action_module.wait_action
        players = [
            SimpleNamespace(player_index=i, hand_tiles=[], remaining_time=1)
            for i in range(4)
        ]
        action_queues = [asyncio.Queue() for _ in range(4)]
        action_queues[1].put_nowait({"action_type": "sea_bottom", "action_data": True})
        state = SimpleNamespace(
            game_status="waiting_sea_bottom",
            step_time=1,
            action_dict={0: [], 1: ["sea_bottom", "pass"], 2: [], 3: []},
            action_events=[FlagEvent() for _ in range(4)],
            action_queues=action_queues,
            player_list=players,
            action_priority={"pass": 0, "sea_bottom": 0, "cut": 0},
        )
        taken = []

        async def take_sea_bottom_tile(player_index):
            self.assertEqual(player_index, 1)
            taken.append(player_index)
            state.game_status = "END"
            state.action_dict = {0: [], 1: [], 2: [], 3: []}

        async def keep_action(self, action_type, player_index, action_data, **kwargs):
            return action_type, player_index, action_data, None

        state._take_sea_bottom_tile = take_sea_bottom_tile

        with patch.object(wait_action_module, "apply_tactical_claim_if_needed", keep_action):
            asyncio.run(original_wait_action(state))

        self.assertEqual(taken, [1])
        self.assertEqual(state.game_status, "END")
        self.assertEqual(state.action_dict, {0: [], 1: [], 2: [], 3: []})

    def test_sea_bottom_action_uses_changsha_player_index_when_player_list_order_differs(self):
        class FlagEvent:
            def __init__(self):
                self.is_set = False

            def set(self):
                self.is_set = True

        players = [
            SimpleNamespace(player_index=2, user_id=102, hand_tiles=[], combination_tiles=[]),
            SimpleNamespace(player_index=0, user_id=100, hand_tiles=[], combination_tiles=[]),
            SimpleNamespace(player_index=1, user_id=101, hand_tiles=[], combination_tiles=[]),
            SimpleNamespace(player_index=3, user_id=103, hand_tiles=[], combination_tiles=[]),
        ]
        action_queues = [asyncio.Queue() for _ in range(4)]
        action_events = [FlagEvent() for _ in range(4)]
        state = SimpleNamespace(
            room_rule="changsha",
            player_list=players,
            waiting_players_list=[1],
            game_status="waiting_sea_bottom",
            current_player_index=1,
            action_dict={0: [], 1: ["sea_bottom", "pass"], 2: [], 3: []},
            action_queues=action_queues,
            action_events=action_events,
            game_server=SimpleNamespace(
                players={"conn-101": SimpleNamespace(user_id=101)}
            ),
        )

        asyncio.run(public_get_action(
            state,
            "conn-101",
            "sea_bottom",
            cutClass=None,
            TileId=None,
            cutIndex=None,
            target_tile=0,
            chi_combo_index=0,
            action_tick=None,
        ))

        queued = action_queues[1].get_nowait()
        self.assertEqual(queued["action_type"], "sea_bottom")
        self.assertTrue(action_events[1].is_set)
        self.assertTrue(action_queues[2].empty())

    def test_sea_bottom_ask_hand_includes_deal_tile_fallback(self):
        sent_by_user = {}

        class FakeWebSocket:
            def __init__(self, user_id):
                self.user_id = user_id

            async def send_json(self, payload):
                sent_by_user[self.user_id] = payload

        players = [
            SimpleNamespace(player_index=0, user_id=100, username="p0", tag_list=[], remaining_time=5),
            SimpleNamespace(player_index=1, user_id=101, username="p1", tag_list=[], remaining_time=5),
            SimpleNamespace(player_index=2, user_id=102, username="p2", tag_list=[], remaining_time=5),
            SimpleNamespace(player_index=3, user_id=103, username="p3", tag_list=[], remaining_time=5),
        ]
        state = SimpleNamespace(
            server_action_tick=0,
            player_list=players,
            current_player_index=1,
            tiles_list=[],
            forced_cut_tile=25,
            forced_cut_tiles=[25],
            action_dict={0: [], 1: ["cut", "pass"], 2: [], 3: []},
            game_server=SimpleNamespace(
                user_id_to_connection={
                    player.user_id: SimpleNamespace(websocket=FakeWebSocket(player.user_id))
                    for player in players
                }
            ),
        )

        async def noop_spectator(*args, **kwargs):
            return None

        state.send_to_realtime_spectators = noop_spectator

        asyncio.run(broadcast_ask_hand_action(state))

        ask_info = sent_by_user[101]["ask_hand_action_info"]
        self.assertEqual(ask_info["deal_tile"], 25)
        self.assertEqual(ask_info["forced_cut_tiles"], [25])

    def test_forced_cut_consumes_sea_bottom_tile_instead_of_clicked_hand_tile(self):
        player = DummyPlayer(1, [11, 12, 13, 25])
        player.has_draw_slot = True
        state = SimpleNamespace(
            player_list=[DummyPlayer(0), player, DummyPlayer(2), DummyPlayer(3)],
            forced_cut_tile=25,
            forced_cut_tiles=[25],
        )

        cut_tiles = wait_action_module._consume_forced_gang_cut_tiles(state, 1)

        self.assertEqual(cut_tiles, [25])
        self.assertEqual(player.hand_tiles, [11, 12, 13])
        self.assertFalse(player.has_draw_slot)
        self.assertIsNone(state.forced_cut_tile)
        self.assertEqual(state.forced_cut_tiles, [])

    def test_sea_bottom_discard_without_win_ends_after_claim_window(self):
        state = SimpleNamespace(
            pending_gang_forced_discard=False,
            pending_gang_replacement_count=0,
            forced_cut_tile=11,
            forced_cut_tiles=[11],
            current_claim_cut_tile=11,
            tiles_list=[],
        )

        self.assertEqual(ChangshaGameState.next_status_after_claim_window(state), "END")
        self.assertFalse(state.pending_gang_forced_discard)
        self.assertEqual(state.forced_cut_tiles, [])
        self.assertIsNone(state.current_claim_cut_tile)

    def test_changsha_angang_broadcast_reveals_mask_and_target(self):
        state = SimpleNamespace(server_action_tick=3)
        mask = [0, 11, 0, 11, 0, 11, 0, 11]

        payload = _build_do_action_payload(
            state,
            ["angang"],
            0,
            1,
            combination_mask=mask,
            combination_target="G11",
        )

        self.assertEqual(payload["combination_mask"], mask)
        self.assertEqual(payload["combination_target"], "G11")

    def test_sea_bottom_deal_tile_is_visible_to_action_player_when_player_list_order_differs(self):
        sent_by_user = {}

        class FakeWebSocket:
            def __init__(self, user_id):
                self.user_id = user_id

            async def send_json(self, payload):
                sent_by_user[self.user_id] = payload

        players = [
            SimpleNamespace(player_index=2, user_id=102, username="p2", tag_list=[]),
            SimpleNamespace(player_index=0, user_id=100, username="p0", tag_list=[]),
            SimpleNamespace(player_index=1, user_id=101, username="p1", tag_list=[]),
            SimpleNamespace(player_index=3, user_id=103, username="p3", tag_list=[]),
        ]
        state = SimpleNamespace(
            server_action_tick=0,
            player_list=players,
            game_server=SimpleNamespace(
                user_id_to_connection={
                    player.user_id: SimpleNamespace(websocket=FakeWebSocket(player.user_id))
                    for player in players
                }
            ),
            claim_protection=False,
        )
        state._player_by_index = lambda index: next(player for player in state.player_list if player.player_index == index)
        state.send_to_realtime_spectators = lambda *args, **kwargs: None

        async def noop_spectator(*args, **kwargs):
            return None

        state.send_to_realtime_spectators = noop_spectator

        asyncio.run(broadcast_do_action(
            state,
            action_list=["deal_tile"],
            action_player=1,
            deal_tile=25,
        ))

        self.assertEqual(sent_by_user[101]["do_action_info"]["deal_tile"], 25)
        self.assertIsNone(sent_by_user[100]["do_action_info"].get("deal_tile"))
        self.assertIsNone(sent_by_user[102]["do_action_info"].get("deal_tile"))
        self.assertIsNone(sent_by_user[103]["do_action_info"].get("deal_tile"))

    def test_changsha_cut_broadcast_recovers_missing_cut_tile_from_river(self):
        sent_by_user = {}

        class FakeWebSocket:
            def __init__(self, user_id):
                self.user_id = user_id

            async def send_json(self, payload):
                sent_by_user[self.user_id] = payload

        players = [
            SimpleNamespace(player_index=0, user_id=100, username="p0", tag_list=[], discard_tiles=[25]),
            SimpleNamespace(player_index=1, user_id=101, username="p1", tag_list=[], discard_tiles=[]),
            SimpleNamespace(player_index=2, user_id=102, username="p2", tag_list=[], discard_tiles=[]),
            SimpleNamespace(player_index=3, user_id=103, username="p3", tag_list=[], discard_tiles=[]),
        ]
        state = SimpleNamespace(
            server_action_tick=0,
            player_list=players,
            current_claim_cut_tile=None,
            game_server=SimpleNamespace(
                user_id_to_connection={
                    player.user_id: SimpleNamespace(websocket=FakeWebSocket(player.user_id))
                    for player in players
                }
            ),
            claim_protection=False,
        )
        state._player_by_index = lambda index: next(player for player in state.player_list if player.player_index == index)

        async def noop_spectator(*args, **kwargs):
            return None

        state.send_to_realtime_spectators = noop_spectator

        asyncio.run(broadcast_do_action(
            state,
            action_list=["cut"],
            action_player=0,
            cut_class=True,
        ))

        self.assertEqual(sent_by_user[100]["do_action_info"]["cut_tile"], 25)
        self.assertEqual(sent_by_user[101]["do_action_info"]["cut_tile"], 25)

    def test_buzhang_from_peng_uses_single_replacement_and_broadcasts_buzhang(self):
        player = DummyPlayer(0, [17, 12, 13])
        player.combination_tiles = ["k17"]
        player.combination_mask = [[1, 17, 0, 17, 0, 17]]
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            action_dict={},
            jiagang_tile=None,
        )
        payloads = []
        state.prepare_gang_replacement = lambda count, forced: setattr(
            state, "replacement_args", (count, forced)
        )

        async def capture_broadcast(*args, **kwargs):
            payloads.append(kwargs)

        with patch.object(wait_action_module, "broadcast_do_action", capture_broadcast), \
            patch.object(wait_action_module, "player_action_record_jiagang", lambda *args, **kwargs: None), \
            patch.object(wait_action_module, "check_action_jiagang", lambda *args, **kwargs: {0: [], 1: [], 2: [], 3: []}):
            asyncio.run(wait_action_module._execute_jiagang_replacement(state, 0, 17, "buzhang", 1, False))

        self.assertEqual(player.combination_tiles, ["g17"])
        self.assertEqual(state.replacement_args, (1, False))
        self.assertEqual(state.game_status, "deal_card_after_gang")
        self.assertEqual(payloads[0]["action_list"], ["buzhang"])
        self.assertEqual(payloads[0]["combination_target"], "k17")

    def test_open_jiagang_uses_configured_replacement_and_forced_discard(self):
        player = DummyPlayer(0, [17, 12, 13])
        player.combination_tiles = ["k17"]
        player.combination_mask = [[1, 17, 0, 17, 0, 17]]
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            action_dict={},
            jiagang_tile=None,
        )
        payloads = []
        state.prepare_gang_replacement = lambda count, forced: setattr(
            state, "replacement_args", (count, forced)
        )

        async def capture_broadcast(*args, **kwargs):
            payloads.append(kwargs)

        with patch.object(wait_action_module, "broadcast_do_action", capture_broadcast), \
            patch.object(wait_action_module, "player_action_record_jiagang", lambda *args, **kwargs: None), \
            patch.object(wait_action_module, "check_action_jiagang", lambda *args, **kwargs: {0: [], 1: [], 2: [], 3: []}):
            asyncio.run(wait_action_module._execute_jiagang_replacement(state, 0, 17, "jiagang", 2, True))

        self.assertEqual(player.combination_tiles, ["g17"])
        self.assertEqual(state.replacement_args, (2, True))
        self.assertEqual(state.game_status, "deal_card_after_gang")
        self.assertEqual(payloads[0]["action_list"], ["jiagang"])
        self.assertEqual(payloads[0]["combination_target"], "k17")

    def test_angang_execution_broadcasts_revealed_mask(self):
        player = DummyPlayer(0, [11, 11, 11, 11, 12, 13])
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
        )
        payloads = []
        state.prepare_gang_replacement = lambda count, forced: setattr(
            state, "replacement_args", (count, forced)
        )

        async def capture_broadcast(*args, **kwargs):
            payloads.append(kwargs)

        with patch.object(wait_action_module, "broadcast_do_action", capture_broadcast), \
            patch.object(wait_action_module, "player_action_record_angang", lambda *args, **kwargs: None):
            asyncio.run(wait_action_module._execute_angang_replacement(state, 0, 11, "angang", 2, True))

        self.assertEqual(player.combination_tiles, ["G11"])
        self.assertEqual(player.combination_mask, [[0, 11, 0, 11, 0, 11, 0, 11]])
        self.assertEqual(state.replacement_args, (2, True))
        self.assertEqual(payloads[0]["action_list"], ["angang"])
        self.assertEqual(payloads[0]["combination_mask"], [0, 11, 0, 11, 0, 11, 0, 11])

    def test_open_kong_replacements_are_forced_cut_as_batch(self):
        player = DummyPlayer(0, [11, 12, 41, 42])
        player.discard_tiles = []
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            current_player_index=0,
            forced_cut_tile=42,
            forced_cut_tiles=[41, 42],
            pending_gang_forced_discard=True,
            pending_gang_replacement_count=0,
            current_claim_cut_tile=None,
            action_dict={0: [], 1: [], 2: [], 3: []},
            xunmu=0,
            last_draw_was_gang=True,
            calculation_service=FixedTingpai([]),
        )
        state.next_status_after_claim_window = lambda: ChangshaGameState.next_status_after_claim_window(state)
        payloads = []

        async def capture_broadcast(**kwargs):
            payloads.append(kwargs)

        state.broadcast_do_action = capture_broadcast

        with patch.object(changsha_state_module, "player_action_record_cut", lambda *args, **kwargs: None), \
            patch.object(changsha_state_module, "check_action_after_batch_gang_forced_cut", lambda *args, **kwargs: {0: [], 1: [], 2: [], 3: []}), \
            patch.object(changsha_state_module, "begin_claim_protection_interval", lambda *args, **kwargs: None):
            asyncio.run(ChangshaGameState.force_cut_gang_replacement_tiles(state))

        self.assertEqual(player.hand_tiles, [11, 12])
        self.assertEqual(player.discard_tiles, [41, 42])
        self.assertEqual(state.forced_cut_tiles, [])
        self.assertEqual(state.game_status, "deal_card")
        self.assertEqual(payloads[0]["action_list"], ["cut"])
        self.assertEqual(payloads[0]["cut_tile"], 42)
        self.assertEqual(payloads[0]["cut_tiles"], [41, 42])

    def test_open_kong_replacement_actions_only_keep_hu_self(self):
        self.assertEqual(
            filter_open_kong_replacement_actions(["cut", "buzhang", "angang", "jiagang"]),
            [],
        )
        self.assertEqual(
            filter_open_kong_replacement_actions(["hu_self", "cut", "buzhang", "angang", "jiagang"]),
            ["hu_self"],
        )

    def test_open_kong_replacement_hu_scores_each_winning_tile(self):
        player = DummyPlayer(0, [11, 12, 21, 22, 23], waiting_tiles=[21, 22, 23])
        checker = TileMappedCalculation({
            21: (8, ["杠上开花"]),
            22: (8, ["杠上开花"]),
            23: (0, []),
        })
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            current_player_index=0,
            result_dict={},
            player_passed_hu_base={},
            tiles_list=[31],
            calculation_service=checker,
            base_score_no_dealer=True,
        )
        self.bind_changsha_score_helpers(state)

        has_hu = ChangshaGameState._collect_gang_replacement_hu_result(
            state,
            0,
            [11, 12],
            [21, 22, 23],
            {21, 22, 23},
        )

        self.assertTrue(has_hu)
        self.assertEqual(state.result_dict["hu_self"], (16, ["杠上开花", "杠上开花"]))
        self.assertEqual(state.pending_gang_replacement_hu_tile, 22)
        self.assertEqual(state.pending_gang_replacement_hu_hand, [11, 12, 21, 22])
        self.assertEqual(player.hand_tiles, [11, 12, 21, 22, 23])
        self.assertEqual(
            checker.calls,
            [([11, 12, 21], 21), ([11, 12, 22], 22), ([11, 12, 23], 23)],
        )

    def test_open_kong_hu_display_hand_uses_actual_replacement_tile(self):
        player = DummyPlayer(0, [11, 12, 21, 22])
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            hu_class="hu_self",
        )

        ChangshaGameState._remember_gang_replacement_hu_hand(state, 0, [11, 12], 21)

        self.assertEqual(ChangshaGameState._hepai_display_hand(state, 0), [11, 12, 21])
        self.assertEqual(player.hand_tiles, [11, 12, 21, 22])

    def test_open_kong_hu_display_hand_falls_back_for_other_win_classes(self):
        player = DummyPlayer(0, [11, 12, 21, 22])
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            hu_class="hu_first",
        )

        ChangshaGameState._remember_gang_replacement_hu_hand(state, 0, [11, 12], 21)

        self.assertEqual(ChangshaGameState._hepai_display_hand(state, 0), [11, 12, 21, 22])

    def test_winner_becomes_next_round_dealer(self):
        players = [
            SimpleNamespace(
                player_index=i,
                original_player_index=i,
                hand_tiles=[11 + i],
                huapai_list=[i],
                discard_tiles=[21 + i],
                waiting_tiles={31 + i},
                combination_tiles=[f"p{i}"],
                combination_mask=[i],
                remaining_time=99,
                tag_list=["peida"],
            )
            for i in range(4)
        ]
        state = SimpleNamespace(
            player_list=players,
            current_round=1,
            round_index=1,
            current_player_index=2,
            xunmu=8,
            round_time=20,
            hu_class="hu_self",
            action_dict={0: ["ready"]},
            backward_tiles_list_type="double",
        )

        ChangshaGameState._make_player_next_dealer(state, 2)
        next_game_round_random_switchseat(state, keep_dealer_seat=True)

        self.assertEqual(
            [player.original_player_index for player in state.player_list],
            [2, 3, 0, 1],
        )
        self.assertEqual(state.player_list[0].player_index, 0)
        self.assertEqual(state.player_list[0].original_player_index, 2)
        self.assertEqual(state.current_player_index, 0)
        self.assertEqual(state.current_round, 2)
        self.assertEqual(state.player_list[0].hand_tiles, [])

    def test_sea_bottom_taker_becomes_next_round_dealer_on_draw(self):
        players = [
            SimpleNamespace(
                player_index=i,
                original_player_index=i,
                hand_tiles=[11 + i],
                huapai_list=[i],
                discard_tiles=[21 + i],
                waiting_tiles={31 + i},
                combination_tiles=[f"p{i}"],
                combination_mask=[i],
                remaining_time=99,
                tag_list=[],
            )
            for i in range(4)
        ]
        state = SimpleNamespace(
            player_list=players,
            current_round=1,
            round_index=1,
            current_player_index=0,
            xunmu=8,
            round_time=20,
            hu_class="liuju",
            action_dict={0: ["ready"]},
            backward_tiles_list_type="double",
            sea_bottom_player_index=3,
        )

        ChangshaGameState._make_player_next_dealer(state, state.sea_bottom_player_index)
        next_game_round_random_switchseat(state, keep_dealer_seat=True)

        self.assertEqual(state.player_list[0].original_player_index, 3)
        self.assertEqual(state.player_list[0].player_index, 0)
        self.assertEqual(state.current_player_index, 0)
        self.assertEqual(state.current_round, 2)


if __name__ == "__main__":
    unittest.main()
