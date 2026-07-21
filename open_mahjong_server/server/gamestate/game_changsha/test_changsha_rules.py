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
    refresh_waiting_tiles,
    filter_open_kong_replacement_actions,
)
from server.gamestate.game_changsha.boardcast import _build_do_action_payload
from server.gamestate.game_changsha.init_tiles import init_changsha_tiles
from server.gamestate.public.next_game_round import next_game_round_random_switchseat
from server.room.room_validators import ChangshaRoomValidator

wait_action_module = importlib.import_module("server.gamestate.game_changsha.wait_action")
changsha_state_module = importlib.import_module("server.gamestate.game_changsha.ChangshaGameState")
changsha_get_action_module = importlib.import_module("server.gamestate.game_changsha.get_action")
changsha_boardcast_module = importlib.import_module("server.gamestate.game_changsha.boardcast")


class DummyPlayer:
    def __init__(self, player_index, hand_tiles=None, waiting_tiles=None):
        self.player_index = player_index
        self.hand_tiles = list(hand_tiles or [])
        self.waiting_tiles = set(waiting_tiles or [])
        self.combination_tiles = []
        self.combination_mask = []
        self.discard_tiles = []
        self.discard_origin_tiles = []
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


class FixedCalculationAndTingpai:
    def __init__(self, result, waiting_tiles):
        self.result = result
        self.waiting_tiles = set(waiting_tiles)

    def Changsha_hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        return self.result

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set(self.waiting_tiles)


class FixedTingpai:
    def __init__(self, waiting_tiles):
        self.waiting_tiles = set(waiting_tiles)

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set(self.waiting_tiles)


class TileMappedCalculation:
    def __init__(self, results):
        self.results = results
        self.calls = []

    def Changsha_hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        self.calls.append((list(hand_list), get_tile))
        return self.results.get(get_tile, (0, []))

    def Changsha_tingpai_check(self, hand_list, tiles_combination):
        return set()

    def Changsha_base_from_fans(self, fan_list, dealer_related=False):
        return len(fan_list)


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


def attach_changsha_score_helpers(state):
    state.base_score_no_dealer = False
    state.small_hu_score = 2
    state.big_hu_score = 8
    state.dealer_bird = True
    state._changsha_base_from_fans = lambda fans, dealer_related=False: ChangshaGameState._changsha_base_from_fans(
        state,
        fans,
        dealer_related,
    )
    state._changsha_bird_origin = lambda winner: ChangshaGameState._changsha_bird_origin(state, winner)
    return state


class ChangshaRulesTest(unittest.TestCase):
    @staticmethod
    def _make_open_kong_locked_wait_state():
        players = [DummyPlayer(i) for i in range(4)]
        locked_player = players[0]
        locked_player.hand_tiles = [11, 12, 13, 29]
        locked_player.has_draw_slot = True
        locked_player.open_kong_locked = True
        for index, player in enumerate(players):
            player.user_id = 100 + index
            player.username = f"p{index}"
            player.remaining_time = 10

        state = SimpleNamespace(
            player_list=players,
            current_player_index=0,
            game_status="waiting_hand_action",
            action_dict={0: ["cut"], 1: [], 2: [], 3: []},
            action_events={index: asyncio.Event() for index in range(4)},
            action_queues={index: asyncio.Queue() for index in range(4)},
            waiting_players_list=[],
            action_priority={"cut": 1},
            step_time=1,
            tactical_call=False,
            tiles_list=[31, 32, 33],
            current_claim_cut_tile=None,
            last_draw_was_gang=False,
            xunmu=0,
            tips=False,
            server_action_tick=7,
            game_server=SimpleNamespace(
                players={"conn-0": SimpleNamespace(user_id=100)},
                user_id_to_connection={},
            ),
        )
        state.clear_hu_pass_after_own_discard = lambda player_index: None
        return state

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
        # 开局 14 张不标记摸牌区（与国标一致），庄家首打按手切
        self.assertFalse(any(p.has_draw_slot for p in state.player_list))

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

    def test_open_kong_locked_hand_actions_only_allow_win_angang_and_cut(self):
        locked_player = DummyPlayer(
            0,
            [11, 11, 11, 11, 17],
            waiting_tiles=[17],
        )
        locked_player.has_draw_slot = True
        locked_player.open_kong_locked = True
        locked_player.combination_tiles = ["k17"]
        state = SimpleNamespace(
            player_list=[
                locked_player,
                DummyPlayer(1),
                DummyPlayer(2),
                DummyPlayer(3),
            ],
            current_player_index=0,
            tiles_list=[31, 32, 33, 34],
            calculation_service=FixedCalculationAndTingpai((1, ["小胡"]), [17]),
            result_dict={},
            player_passed_hu_base={},
        )
        state._is_open_kong_ready_after_declared = lambda player, tile: True

        actions = check_action_hand_action(state, 0)

        self.assertEqual(actions[0], ["angang", "cut", "hu_self"])

    def test_open_kong_locked_real_and_ai_cuts_are_forced_to_draw_slot(self):
        async def exercise(use_ai):
            state = self._make_open_kong_locked_wait_state()
            payloads = []

            async def capture_broadcast(*args, **kwargs):
                payloads.append(kwargs)

            with patch.object(wait_action_module, "broadcast_do_action", capture_broadcast), \
                patch.object(wait_action_module, "player_action_record_cut", lambda *args, **kwargs: None), \
                patch.object(wait_action_module, "refresh_waiting_tiles", lambda *args, **kwargs: None), \
                patch.object(wait_action_module, "check_action_after_cut", lambda *args, **kwargs: {0: [], 1: [], 2: [], 3: []}), \
                patch.object(wait_action_module, "begin_claim_protection_interval", lambda *args, **kwargs: None):
                wait_task = asyncio.create_task(wait_action_module.wait_action(state))
                for _ in range(20):
                    if 0 in state.waiting_players_list:
                        break
                    await asyncio.sleep(0)
                self.assertIn(0, state.waiting_players_list)

                if use_ai:
                    await changsha_get_action_module.get_ai_action(
                        state, 0, "cut", False, 11, 0, None
                    )
                else:
                    await changsha_get_action_module.get_action(
                        state, "conn-0", "cut", False, 11, 0, None
                    )
                await asyncio.wait_for(wait_task, timeout=1)

            return state, payloads

        for use_ai in (False, True):
            with self.subTest(use_ai=use_ai):
                state, payloads = asyncio.run(exercise(use_ai))
                self.assertEqual(state.player_list[0].hand_tiles, [11, 12, 13])
                self.assertEqual(state.player_list[0].discard_tiles, [29])
                self.assertFalse(state.player_list[0].has_draw_slot)
                self.assertEqual(payloads[0]["cut_tile"], 29)
                self.assertTrue(payloads[0]["cut_class"])

    def test_open_kong_locked_timeout_cuts_draw_slot(self):
        state = self._make_open_kong_locked_wait_state()
        state.player_list[0].remaining_time = 0
        payloads = []

        async def capture_broadcast(*args, **kwargs):
            payloads.append(kwargs)

        with patch.object(wait_action_module, "broadcast_do_action", capture_broadcast), \
            patch.object(wait_action_module, "player_action_record_cut", lambda *args, **kwargs: None), \
            patch.object(wait_action_module, "refresh_waiting_tiles", lambda *args, **kwargs: None), \
            patch.object(wait_action_module, "check_action_after_cut", lambda *args, **kwargs: {0: [], 1: [], 2: [], 3: []}), \
            patch.object(wait_action_module, "begin_claim_protection_interval", lambda *args, **kwargs: None):
            asyncio.run(wait_action_module.wait_action(state))

        self.assertEqual(state.player_list[0].hand_tiles, [11, 12, 13])
        self.assertEqual(state.player_list[0].discard_tiles, [29])
        self.assertFalse(state.player_list[0].has_draw_slot)
        self.assertEqual(payloads[0]["cut_tile"], 29)
        self.assertTrue(payloads[0]["cut_class"])

    def test_open_kong_locked_reconnect_resends_forced_draw_slot(self):
        state = self._make_open_kong_locked_wait_state()
        sent_payloads = []

        class CapturingWebsocket:
            async def send_json(self, payload):
                sent_payloads.append(payload)

        state.game_server.user_id_to_connection[100] = SimpleNamespace(
            websocket=CapturingWebsocket()
        )

        asyncio.run(changsha_boardcast_module.reconnected_send_pending_ask(state, 100))

        ask_info = sent_payloads[0]["ask_hand_action_info"]
        self.assertEqual(ask_info["action_list"], ["cut"])
        self.assertEqual(ask_info["forced_cut_tiles"], [29])

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

    def test_initial_hu_reveal_uses_only_matching_tiles(self):
        hand = [11, 11, 11, 11, 13, 13, 13, 24, 24, 24, 15, 25, 35]
        self.assertEqual(
            changsha_initial_hu_reveal_tiles(hand, ["四喜"]),
            [11, 11, 11, 11],
        )
        self.assertEqual(
            changsha_initial_hu_reveal_tiles(hand, ["六六顺"]),
            [11, 11, 11, 13, 13, 13],
        )

        san_tong_hand = [11, 11, 21, 21, 31, 31, 12, 13, 14, 25, 26, 27, 39]
        self.assertEqual(
            changsha_initial_hu_reveal_tiles(san_tong_hand, ["三同"]),
            [11, 11, 21, 21, 31, 31],
        )

    def test_initial_hu_reveal_keeps_full_hand_for_non_subset_types(self):
        hand = [11, 13, 14, 16, 17, 19, 21, 23, 24, 26, 27, 29, 31]
        self.assertEqual(changsha_initial_hu_reveal_tiles(hand, ["板板胡"]), hand)
        self.assertEqual(changsha_initial_hu_reveal_tiles(hand, ["缺一色"]), hand)

    def test_initial_hu_reveal_merges_overlapping_subset_types(self):
        hand = [11, 11, 11, 11, 12, 12, 12, 21, 21, 31, 31, 25, 26]

        self.assertEqual(
            changsha_initial_hu_reveal_tiles(hand, ["四喜", "六六顺", "三同"]),
            [11, 11, 11, 11, 12, 12, 12, 21, 21, 31, 31],
        )
        self.assertEqual(changsha_initial_hu_reveal_tiles(hand, ["四喜", "缺一色"]), hand)

    def test_initial_hu_settlement_broadcasts_only_reveal_subset(self):
        hand = [11, 11, 11, 11, 12, 13, 14, 22, 23, 24, 31, 32, 33]
        players = [
            SimpleNamespace(
                player_index=index,
                original_player_index=index,
                score=0,
                hand_tiles=hand if index == 0 else [],
                huapai_list=[],
                combination_mask=[],
                record_counter=SimpleNamespace(zimo_times=0, recorded_fans=[], win_score=0),
            )
            for index in range(4)
        ]
        state = SimpleNamespace(
            initial_hu_types={0: ["四喜"]},
            player_list=players,
        )
        state._score_initial_hu = lambda player_index, hu_types: {
            "actual_hu_score": 6,
            "fan_display": list(hu_types),
            "dice": [1, 2],
            "bird_seats": [0, 1],
            "payer_details": [],
        }
        state._player_by_index = lambda player_index: players[player_index]
        payloads = []

        async def capture_broadcast(_state, **kwargs):
            payloads.append(kwargs)

        with patch.object(changsha_state_module, "broadcast_result", capture_broadcast):
            asyncio.run(ChangshaGameState._settle_initial_hu(state, 0))

        self.assertEqual(payloads[0]["hepai_player_hand"], [11, 11, 11, 11])

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

    def test_changsha_base_scores_support_no_dealer_mode_and_custom_values(self):
        for dealer_related in (False, True):
            self.assertEqual(
                changsha_base_from_fans(
                    ["小胡"],
                    dealer_related=dealer_related,
                    base_score_no_dealer=True,
                ),
                2,
            )
            self.assertEqual(
                changsha_base_from_fans(
                    ["碰碰胡", "清一色"],
                    dealer_related=dealer_related,
                    base_score_no_dealer=True,
                ),
                16,
            )

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
                ["碰碰胡", "清一色"],
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

        custom = ChangshaRoomValidator(**{**base, "base_score_no_dealer": True, "small_hu_score": 3, "big_hu_score": 9})
        self.assertEqual((custom.small_hu_score, custom.big_hu_score), (3, 9))
        with self.assertRaises(ValueError):
            ChangshaRoomValidator(**{**base, "small_hu_score": 0})
        with self.assertRaises(ValueError):
            ChangshaRoomValidator(**{**base, "big_hu_score": 1000})

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
            round_random_seed=12345,
            current_round=1,
        )
        attach_changsha_score_helpers(state)
        state.dealer_bird = False
        state._roll_initial_hu_dice = lambda winner: [1, 2]
        state._initial_hu_dice_seat = ChangshaGameState._initial_hu_dice_seat
        state._player_by_index = lambda index: players[index]

        result = ChangshaGameState._score_initial_hu(state, 1, ["四喜", "板板胡"])

        self.assertEqual(result["dice"], [1, 2])
        self.assertEqual(result["bird_seats"], [1, 2])
        self.assertEqual(players[1].score, 10)
        self.assertEqual([players[i].score for i in range(4)], [-4, 10, -4, -2])
        self.assertIn("四喜", result["fan_display"])
        self.assertIn("骰子:1,2", result["fan_display"])

    def test_bird_scoring_uses_configured_count_and_origin(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        origins = []
        state = SimpleNamespace(
            player_list=players,
            bird_count=1,
            dealer_bird=False,
        )
        attach_changsha_score_helpers(state)
        state.dealer_bird = False

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

    def test_bird_scoring_displays_tile_name_rank_and_multiplier(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        state = SimpleNamespace(
            player_list=players,
            bird_count=2,
            dealer_bird=False,
        )
        attach_changsha_score_helpers(state)
        state.dealer_bird = False
        state._draw_changsha_birds = lambda count: [24, 31]
        state._is_sea_bottom_win = ChangshaGameState._is_sea_bottom_win
        state._changsha_bird_seat = lambda tile, origin: {24: 2, 31: 0}[tile]
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = lambda index: players[index]

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

    def test_bird_origin_uses_seat_zero_when_dealer_bird_is_enabled(self):
        state = SimpleNamespace(dealer_bird=True)
        self.assertEqual(ChangshaGameState._changsha_bird_origin(state, 2), 0)

        state.dealer_bird = False
        self.assertEqual(ChangshaGameState._changsha_bird_origin(state, 2), 2)

    def test_no_dealer_mode_applies_same_base_to_dealer_and_non_dealer_win(self):
        players = [SimpleNamespace(player_index=i, score=0) for i in range(4)]
        state = SimpleNamespace(
            player_list=players,
            bird_count=0,
            dealer_bird=True,
            base_score_no_dealer=True,
            small_hu_score=2,
            big_hu_score=8,
        )
        state._changsha_base_from_fans = lambda fans, dealer_related=False: ChangshaGameState._changsha_base_from_fans(
            state,
            fans,
            dealer_related,
        )
        state._changsha_bird_origin = lambda winner: ChangshaGameState._changsha_bird_origin(state, winner)
        state._draw_changsha_birds = lambda count: []
        state._is_sea_bottom_win = ChangshaGameState._is_sea_bottom_win
        state._player_by_index = lambda index: players[index]

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=0,
            fan_list=["碰碰胡", "清一色"],
            is_zimo=False,
            discarder=1,
        )

        self.assertEqual(result["base_score"], 16)
        self.assertEqual(players[0].score, 16)
        self.assertEqual(players[1].score, -16)

    def test_quanqiuren_allows_self_draw_and_non_258_pair(self):
        checker = Changsha_Hepai_Check()
        hand = [11, 11]
        melds = ["k12", "k23", "k31", "k19"]

        for way in (["自摸"], ["点炮"]):
            score, fans = checker.hepai_check(hand, melds, way, 11)
            self.assertIn("全求人", fans)
            self.assertGreaterEqual(score, 6)

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
            tiles_list=[],
        )
        attach_changsha_score_helpers(state)
        state.dealer_bird = False
        state._draw_changsha_birds = lambda count: ChangshaGameState._draw_changsha_birds(state, count)
        state._is_sea_bottom_win = ChangshaGameState._is_sea_bottom_win
        state._sea_bottom_bird_tile = lambda winner: ChangshaGameState._sea_bottom_bird_tile(state, winner)
        state._changsha_bird_seat = ChangshaGameState._changsha_bird_seat
        state._format_changsha_bird_tile = ChangshaGameState._format_changsha_bird_tile
        state._player_by_index = lambda index: players[index]

        result = ChangshaGameState._score_changsha_win(
            state,
            winner=1,
            fan_list=["海底"],
            is_zimo=False,
            discarder=2,
        )

        self.assertEqual(result["birds"], [11])
        self.assertEqual(players[1].score, 12)
        self.assertEqual(players[2].score, -12)

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

    def test_sea_bottom_take_marks_last_tile_as_forced_cut(self):
        player = DummyPlayer(1, [11, 12, 13], waiting_tiles=[24])
        state = SimpleNamespace(
            player_list=[DummyPlayer(0), player, DummyPlayer(2), DummyPlayer(3)],
            tiles_list=[25],
            current_player_index=0,
            action_dict={0: [], 1: [], 2: [], 3: []},
            calculation_service=FixedCalculationAndTingpai((0, []), [24]),
            player_action_tick=0,
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)
        state._filter_sea_bottom_ron_actions = lambda actions: ChangshaGameState._filter_sea_bottom_ron_actions(state, actions)
        state.refresh_waiting_tiles = lambda index: None
        payloads = []

        async def capture_broadcast(**kwargs):
            payloads.append(kwargs)

        state.broadcast_do_action = capture_broadcast

        with patch.object(changsha_state_module, "player_action_record_cut", lambda *args, **kwargs: None):
            asyncio.run(ChangshaGameState._take_sea_bottom_tile(state, 1))

        self.assertEqual(player.hand_tiles, [11, 12, 13])
        self.assertFalse(player.has_draw_slot)
        self.assertIsNone(state.forced_cut_tile)
        self.assertEqual(state.forced_cut_tiles, [])
        self.assertEqual(player.discard_tiles, [25])
        self.assertEqual(state.game_status, "END")
        self.assertEqual(payloads[0]["action_list"], ["cut"])
        self.assertEqual(payloads[0]["cut_tile"], 25)
        self.assertTrue(payloads[0]["sea_bottom_discard"])

    def test_sea_bottom_take_self_win_ends_as_self_draw(self):
        player = DummyPlayer(1, [11, 12, 13], waiting_tiles=[25])
        state = SimpleNamespace(
            player_list=[DummyPlayer(0), player, DummyPlayer(2), DummyPlayer(3)],
            tiles_list=[25],
            current_player_index=0,
            action_dict={0: [], 1: [], 2: [], 3: []},
            calculation_service=FixedCalculationAndTingpai((6, ["海底"]), [25]),
        )
        state._player_by_index = lambda index: ChangshaGameState._player_by_index(state, index)
        payloads = []

        async def capture_broadcast(**kwargs):
            payloads.append(kwargs)

        state.broadcast_do_action = capture_broadcast
        with patch.object(changsha_state_module, "player_action_record_cut", lambda *args, **kwargs: None):
            asyncio.run(ChangshaGameState._take_sea_bottom_tile(state, 1))

        self.assertEqual(state.hu_class, "hu_self")
        self.assertEqual(state.game_status, "END")
        self.assertEqual(state.result_dict["hu_self"], (6, ["海底"]))
        self.assertEqual(player.hand_tiles, [11, 12, 13, 25])
        self.assertEqual(player.discard_tiles, [25])
        self.assertTrue(payloads[0]["sea_bottom_discard"])

    def test_sea_bottom_discard_allows_only_other_players_to_ron(self):
        state = SimpleNamespace()
        filtered = ChangshaGameState._filter_sea_bottom_ron_actions(
            state,
            {
                0: ["chi_left", "peng", "gang", "pass"],
                1: ["hu_first", "peng", "pass"],
                2: ["hu_second", "gang", "pass"],
                3: [],
            },
        )

        self.assertEqual(filtered[0], [])
        self.assertEqual(filtered[1], ["hu_first", "pass"])
        self.assertEqual(filtered[2], ["hu_second", "pass"])
        self.assertEqual(filtered[3], [])

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

    def test_meld_payload_requires_explicit_discard_source_and_tile(self):
        for action in ("chi_left", "peng", "gang"):
            payload = _build_do_action_payload(
                SimpleNamespace(server_action_tick=3),
                [action],
                1,
                0,
                combination_mask=[0, 11, 0, 11, 0, 11],
                combination_target="k11",
                cut_from_player=2,
                cut_tile=11,
            )

            self.assertEqual(payload["cut_from_player"], 2)
            self.assertEqual(payload["cut_tile"], 11)

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

    def test_open_kong_replacement_hu_checks_each_tile(self):
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
            calculation_service=checker,
            tiles_list=[31],
        )
        state._remember_gang_replacement_hu_hand = lambda player_index, hand, tile: ChangshaGameState._remember_gang_replacement_hu_hand(
            state,
            player_index,
            hand,
            tile,
        )

        has_hu = ChangshaGameState._collect_gang_replacement_hu_result(
            state,
            0,
            [11, 12],
            [21, 22, 23],
            {21, 22, 23},
        )

        self.assertTrue(has_hu)
        self.assertEqual(state.result_dict["hu_self"], (2, ["杠上开花", "杠上开花"]))
        self.assertEqual(state.pending_gang_replacement_hu_tile, 22)
        self.assertEqual(state.pending_gang_replacement_hu_hand, [11, 12, 21, 22])
        self.assertEqual(player.hand_tiles, [11, 12, 21, 22, 23])
        self.assertEqual(
            checker.calls,
            [([11, 12, 21], 21), ([11, 12, 22], 22), ([11, 12, 23], 23)],
        )

    def test_open_kong_replacement_hu_deduplicates_hand_fans(self):
        player = DummyPlayer(0, [11, 12, 21, 22], waiting_tiles=[21, 22])
        checker = TileMappedCalculation({
            21: (16, ["清一色", "杠上开花"]),
            22: (16, ["清一色", "杠上开花"]),
        })
        state = SimpleNamespace(
            player_list=[player, DummyPlayer(1), DummyPlayer(2), DummyPlayer(3)],
            current_player_index=0,
            result_dict={},
            player_passed_hu_base={},
            calculation_service=checker,
            tiles_list=[31],
        )
        state._remember_gang_replacement_hu_hand = lambda player_index, hand, tile: ChangshaGameState._remember_gang_replacement_hu_hand(
            state,
            player_index,
            hand,
            tile,
        )

        has_hu = ChangshaGameState._collect_gang_replacement_hu_result(
            state,
            0,
            [11, 12],
            [21, 22],
            {21, 22},
        )

        self.assertTrue(has_hu)
        self.assertEqual(state.result_dict["hu_self"], (3, ["清一色", "杠上开花", "杠上开花"]))


    def test_open_kong_replacement_actions_only_keep_self_hu(self):
        self.assertEqual(
            filter_open_kong_replacement_actions(["cut", "buzhang", "angang", "jiagang"]),
            [],
        )
        self.assertEqual(
            filter_open_kong_replacement_actions(["hu_self", "cut", "buzhang", "angang", "jiagang"]),
            ["hu_self"],
        )

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
