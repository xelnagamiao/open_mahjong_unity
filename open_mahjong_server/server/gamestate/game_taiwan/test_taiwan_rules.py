"""台湾麻将规则与状态机边界回归测试。"""

import asyncio
import hashlib
import json
import time
import unittest
from collections import Counter
from dataclasses import asdict
from types import SimpleNamespace
from unittest.mock import ANY, AsyncMock, Mock, patch

from pydantic import ValidationError

from server.game_calculation.hand_structure import (
    SIXTEEN_TILE_MAHJONG,
    THIRTEEN_TILE_MAHJONG,
)
from server.game_calculation.taiwan.rules import (
    FLOWER_TILES,
    LIABILITY_FAN_CONFIG_FIELDS,
    STRUCTURE_TILES,
    HandContext,
    TaiwanRules,
)
from server.game_calculation.taiwan.fan import (
    FAN_DEFINITIONS,
    SCORING_PRESET_TABLES,
)
from server.game_calculation.taiwan.scoring import (
    TaiwanScorer,
    settle_win,
    settlement_display_fan_names,
)
from server.game_calculation.taiwan.solver import (
    enumerate_decompositions,
    parse_meld_code,
    structural_waits,
)
from server.game_calculation.taiwan.taiwan_hepai_check import Taiwan_Hepai_Check
from server.gamestate.game_taiwan.TaiwanGameState import TaiwanGameState, TaiwanPlayer
from server.gamestate.game_taiwan.action_check import (
    check_action_after_cut,
    check_action_hand_action,
    check_action_jiagang,
    strict_kuikae_forbidden,
)
from server.gamestate.game_taiwan.boardcast import (
    broadcast_ask_hand_action,
    broadcast_ask_other_action,
    broadcast_do_action,
)
from server.gamestate.game_taiwan.init_tiles import init_taiwan_tiles
from server.gamestate.game_taiwan.wait_action import (
    _build_ask_deadlines,
    _collect_responses,
    wait_action,
)
from server.gamestate.public.ai.auto_cut_ai import auto_cut_action
from server.gamestate.public.ai.smart_bot_logic import should_accept_hu
from server.room.room_manager import RoomManager
from server.room.room_validators import TaiwanRoomValidator


class DummyPlayer:
    def __init__(self, player_index: int, hand_tiles=None) -> None:
        self.player_index = player_index
        self.hand_tiles = list(hand_tiles or [])
        self.combination_tiles = []
        self.combination_mask = []
        self.discard_tiles = []
        self.discard_origin_tiles = []
        self.huapai_list = []
        self.waiting_tiles = set()
        self.water = False
        self.kuikae_forbidden_tiles = set()
        self.remaining_time = 5
        self.has_draw_slot = False
        self.last_drawn_tile = None
        self.normal_draw_count = 0
        self.pre_first_draw_waiting = False
        self.discard_count = 0
        self.qualification_alive = False
        self.qualification_ever = False
        self.heavenly_ready = False
        self.earthly_ready = False
        self.eight_flowers_declined = False
        self.pending_eight_flowers = False
        self.declared_ready = False
        self.ready_locked = False
        self.last_discarded_tile = None
        self.liability_payers = {}
        self.riichi_candidate_cuts = {}
        self.tag_list = []

    def get_tile(self, tiles_list, *, mark_draw_slot=True):
        tile = tiles_list.pop(0)
        self.hand_tiles.append(tile)
        if mark_draw_slot:
            self.has_draw_slot = True
        return tile


class TaiwanDetailCalculation:
    def __init__(self) -> None:
        self.checker = Taiwan_Hepai_Check()

    def Taiwan_hepai_detail(
        self,
        hand_list,
        tiles_combination,
        way_to_hepai,
        get_tile,
        context,
    ):
        return self.checker.hepai_detail(
            hand_list,
            tiles_combination,
            way_to_hepai,
            get_tile,
            context,
        )


def make_action_state(*, rules=None, can_draw=True):
    players = [DummyPlayer(i) for i in range(4)]
    state = SimpleNamespace(
        player_list=players,
        current_player_index=0,
        rules=rules or TaiwanRules(),
        result_dict={},
        supplement_win_allowed=True,
        last_draw_was_last=False,
    )
    state.can_take_wall_tile = lambda: can_draw
    state.score_candidate = lambda *_args, **_kwargs: None
    return state


def make_db_manager_stub():
    return SimpleNamespace(get_rank_data=lambda _user_id: None)


class TaiwanScoringTest(unittest.TestCase):
    def setUp(self) -> None:
        self.scorer = TaiwanScorer()

    def score(self, hand, *, melds=None, tile=None, source="discard", **context):
        return self.scorer.score_hand(
            HandContext(
                hand_tiles=list(hand),
                meld_codes=list(melds or []),
                winning_tile=tile if tile is not None else hand[-1],
                win_source=source,
                **context,
            )
        )

    def test_shared_hand_structure_profiles(self):
        self.assertEqual(THIRTEEN_TILE_MAHJONG.base_hand_tile_count, 13)
        self.assertEqual(THIRTEEN_TILE_MAHJONG.complete_hand_tile_count, 14)
        self.assertEqual(SIXTEEN_TILE_MAHJONG.base_hand_tile_count, 16)
        self.assertEqual(SIXTEEN_TILE_MAHJONG.complete_hand_tile_count, 17)
        self.assertEqual(
            SIXTEEN_TILE_MAHJONG.concealed_tile_count(2, complete=False),
            10,
        )

    def test_initial_wall_and_sixteen_tile_deal(self):
        state = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            master_seed=0x123456,
            round_index=1,
        )

        init_taiwan_tiles(state)

        self.assertEqual([len(player.hand_tiles) for player in state.player_list], [17, 16, 16, 16])
        self.assertEqual(len(state.tiles_list), 79)
        self.assertFalse(any(player.has_draw_slot for player in state.player_list))
        all_tiles = list(state.tiles_list)
        for player in state.player_list:
            all_tiles.extend(player.hand_tiles)
        counts = Counter(all_tiles)
        self.assertEqual(len(all_tiles), 144)
        self.assertTrue(all(counts[tile] == 4 for tile in STRUCTURE_TILES))
        self.assertTrue(all(counts[tile] == 1 for tile in FLOWER_TILES))

        same_round = SimpleNamespace(
            player_list=[DummyPlayer(i) for i in range(4)],
            master_seed=state.master_seed,
            round_index=state.round_index,
        )
        init_taiwan_tiles(same_round)
        self.assertEqual(
            [player.hand_tiles for player in state.player_list],
            [player.hand_tiles for player in same_round.player_list],
        )
        self.assertEqual(state.tiles_list, same_round.tiles_list)

    def test_structural_waits_use_five_melds_and_pair(self):
        pre_win = [11, 12, 13, 14, 15, 16, 21, 22, 23, 24, 25, 26, 31, 32, 33, 45]
        self.assertEqual(structural_waits(pre_win, []), {45})
        self.assertEqual(structural_waits(pre_win[:-3], []), set())

    def test_eight_pairs_half_waits_are_unioned_with_standard_shape(self):
        pre_win = [tile for tile in range(21, 29) for _ in range(2)]
        expected = set(range(21, 29))
        rules = TaiwanRules(eight_and_a_half_pairs_enabled=True)

        # 只按五面子一将时 3p、6p 并不是待牌；开启八对半后，特殊结构
        # 必须与普通结构取并集，不能因同一手也存在普通拆分而被覆盖。
        self.assertEqual(
            structural_waits(pre_win, [], TaiwanRules()),
            expected - {23, 26},
        )
        self.assertEqual(structural_waits(pre_win, [], rules), expected)

        for winning_tile in sorted(expected):
            with self.subTest(winning_tile=winning_tile):
                result = self.score(
                    pre_win + [winning_tile],
                    tile=winning_tile,
                    source="self_draw",
                    rules=rules,
                )
                self.assertTrue(result.is_win)
                self.assertIn("eight_and_a_half_pairs", result.fan_ids)
                self.assertEqual(result.fan_ids.count("eight_and_a_half_pairs"), 1)
                self.assertEqual(result.waits, frozenset(expected))

    def test_eight_pairs_half_waits_count_four_identical_tiles_as_two_pairs(self):
        pre_win = [11] * 4 + [12] * 4 + [13] * 2 + [14] * 2 + [15] * 2 + [16] * 2
        disabled = structural_waits(pre_win, [], TaiwanRules())
        enabled = structural_waits(pre_win, [], TaiwanRules(eight_and_a_half_pairs_enabled=True))

        self.assertNotIn(15, disabled)
        self.assertIn(15, enabled)

    def test_eight_pairs_half_waits_cover_existing_triplet_shapes(self):
        rules = TaiwanRules(eight_and_a_half_pairs_enabled=True)
        triplet_and_single = (
            [11] * 3
            + [12]
            + [tile for tile in (14, 17, 21, 24, 27, 31) for _ in range(2)]
        )
        two_triplets = (
            [11] * 3
            + [12] * 3
            + [tile for tile in (14, 17, 21, 24, 27) for _ in range(2)]
        )

        self.assertEqual(structural_waits(triplet_and_single, [], TaiwanRules()), set())
        self.assertEqual(structural_waits(triplet_and_single, [], rules), {12})
        self.assertEqual(structural_waits(two_triplets, [], TaiwanRules()), set())
        self.assertEqual(structural_waits(two_triplets, [], rules), {11, 12})

        for pre_win, winning_tiles in (
            (triplet_and_single, (12,)),
            (two_triplets, (11, 12)),
        ):
            for winning_tile in winning_tiles:
                with self.subTest(pre_win=pre_win, winning_tile=winning_tile):
                    result = self.score(
                        pre_win + [winning_tile],
                        tile=winning_tile,
                        source="self_draw",
                        rules=rules,
                    )
                    self.assertTrue(result.is_win)
                    self.assertIn("eight_and_a_half_pairs", result.fan_ids)

    def test_zero_tai_is_a_legal_win(self):
        result = self.score(
            [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 45, 45],
            melds=["k11"],
            tile=21,
        )
        self.assertTrue(result.is_win)
        self.assertEqual(result.tai, 0)
        self.assertEqual(result.fan_ids, [])
        self.assertEqual(result.waits, frozenset({21, 24, 27}))

    def test_solver_rejects_physical_tile_overflow_across_external_melds(self):
        hand = [
            11, 11, 11, 12, 13, 14, 21,
            22, 23, 31, 32, 33, 45, 45,
        ]
        self.assertEqual(
            enumerate_decompositions(hand, ["k11"], winning_tile=45),
            [],
        )

    def test_structural_waits_reject_physical_tile_overflow_across_external_melds(self):
        # 牌形上 45 看似能补成一将，但暗手已有三张 11，外部碰又占用三张
        # 11；等待枚举不能把这副不存在的牌当成天地听/过水依据。
        pre_win = [
            11, 11, 11,
            12, 13, 14,
            15, 16, 17,
            21, 22, 23,
            45,
        ]
        self.assertEqual(structural_waits(pre_win, ["k11"]), set())

    def test_scoring_reference_examples(self):
        cases = {
            "menqing_self_draw": (
                [11, 12, 13, 14, 15, 16, 21, 22, 23, 24, 25, 26, 31, 32, 33, 45, 45],
                [],
                21,
                "self_draw",
                3,
                {"concealed_hand", "fully_concealed_hand", "self_draw"},
            ),
            "all_chows": (
                [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 45, 45],
                ["s12"],
                21,
                "discard",
                2,
                {"all_chows"},
            ),
            "five_concealed_triplets": (
                [11, 11, 11, 12, 12, 12, 21, 21, 21, 22, 22, 22, 31, 31, 31, 45, 45],
                [],
                31,
                "self_draw",
                15,
                {"concealed_hand", "fully_concealed_hand", "self_draw", "five_concealed_pungs", "all_pungs"},
            ),
            "big_dragons_replaces_dragon_fans": (
                [11, 12, 13, 21, 22, 23, 31, 31],
                ["k45", "k46", "k47"],
                11,
                "discard",
                8,
                {"big_three_dragons"},
            ),
            "all_begging": (
                [11, 11],
                ["s22", "s25", "s32", "s35", "k42"],
                11,
                "discard",
                2,
                {"all_begging"},
            ),
        }
        for name, (hand, melds, tile, source, tai, fan_ids) in cases.items():
            with self.subTest(name=name):
                result = self.score(hand, melds=melds, tile=tile, source=source)
                self.assertTrue(result.is_win)
                self.assertEqual(result.tai, tai)
                self.assertEqual(set(result.fan_ids), fan_ids)

    def test_four_kongs_is_a_scoring_fan_and_supports_custom_tai(self):
        hand = [25, 25, 25, 45, 45]
        melds = ["G11", "g12", "G13", "g14"]

        disabled = self.score(
            hand,
            melds=melds,
            tile=45,
            source="self_draw",
        )
        result = self.score(
            hand,
            melds=melds,
            tile=45,
            source="self_draw",
            rules=TaiwanRules(four_kongs_enabled=True),
        )
        custom = self.score(
            hand,
            melds=melds,
            tile=45,
            source="self_draw",
            rules=TaiwanRules(
                four_kongs_enabled=True,
                fan_tai_overrides={"four_kongs": 13},
            ),
        )

        self.assertNotIn("four_kongs", disabled.fan_ids)
        self.assertTrue(result.is_win)
        self.assertIn("four_kongs", result.fan_ids)
        self.assertEqual(
            next(fan.tai for fan in result.fans if fan.fan_id == "four_kongs"),
            8,
        )
        self.assertEqual(custom.tai - result.tai, 5)

    def test_five_kongs_is_a_16_tai_extension_and_replaces_four_kongs(self):
        hand = [45, 45]
        melds = ["G11", "g12", "G13", "g14", "G15"]

        disabled = self.score(
            hand,
            melds=melds,
            tile=45,
            source="self_draw",
        )
        enabled = self.score(
            hand,
            melds=melds,
            tile=45,
            source="self_draw",
            rules=TaiwanRules(
                four_kongs_enabled=True,
                five_kongs_enabled=True,
            ),
        )
        four_only = self.score(
            hand,
            melds=melds,
            tile=45,
            source="self_draw",
            rules=TaiwanRules(four_kongs_enabled=True),
        )

        self.assertNotIn("four_kongs", disabled.fan_ids)
        self.assertNotIn("five_kongs", disabled.fan_ids)
        self.assertIn("five_kongs", enabled.fan_ids)
        self.assertNotIn("four_kongs", enabled.fan_ids)
        self.assertEqual(
            next(fan.tai for fan in enabled.fans if fan.fan_id == "five_kongs"),
            16,
        )
        self.assertIn("four_kongs", four_only.fan_ids)

    def test_starting_win_suppresses_only_the_defined_fans(self):
        hand = [11, 12, 13, 14, 15, 16, 21, 22, 23, 24, 25, 26, 31, 32, 33, 45, 45]
        heavenly = self.score(
            hand,
            tile=21,
            source="self_draw",
            heavenly_win=True,
            out_with_replacement_tile=True,
        )
        self.assertEqual(heavenly.tai, 25)
        self.assertEqual(set(heavenly.fan_ids), {"self_draw", "heavenly_win"})

        earthly = self.score(hand, tile=21, source="self_draw", earthly_win=True)
        self.assertEqual(earthly.tai, 17)
        self.assertEqual(set(earthly.fan_ids), {"self_draw", "earthly_win"})

        human = self.score(hand, tile=21, source="discard", human_win=True)
        self.assertEqual(human.tai, 16)
        self.assertEqual(set(human.fan_ids), {"human_win"})

    def test_flower_scoring_and_no_flowers_extension(self):
        hand = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 45, 45]
        flower = self.score(
            hand,
            melds=["k11"],
            tile=21,
            flowers=[51, 52, 53, 54],
            seat_wind=41,
        )
        self.assertEqual(flower.tai, 1)
        self.assertEqual(flower.fan_ids, ["flower_kong"])

        any_flower = self.score(
            hand,
            melds=["k11"],
            tile=21,
            flowers=[51, 52, 53, 54],
            seat_wind=41,
            rules=TaiwanRules(
                all_flower_tiles_enabled=True,
                fan_tai_overrides={"flower_kong": 2},
            ),
        )
        self.assertEqual(any_flower.tai, 4)
        self.assertEqual(any_flower.fan_ids, ["flower_tile"])
        any_flower_fan = next(
            fan for fan in any_flower.fans if fan.fan_id == "flower_tile"
        )
        self.assertEqual(
            (any_flower_fan.name, any_flower_fan.count, any_flower_fan.total),
            ("花牌", 4, 4),
        )
        self.assertNotIn("flower_kong", any_flower.fan_ids)

        both_flower_kongs = self.score(
            [11, 12, 13, 14, 15, 16, 21, 22, 23, 24, 25, 26, 31, 32, 33, 45, 45],
            tile=21,
            source="self_draw",
            flowers=list(FLOWER_TILES),
            eight_flowers_declined=True,
        )
        flower_kong = next(fan for fan in both_flower_kongs.fans if fan.fan_id == "flower_kong")
        self.assertEqual(flower_kong.count, 2)
        self.assertEqual(flower_kong.total, 2)
        self.assertNotIn("flower_tile", both_flower_kongs.fan_ids)

        no_flowers = self.score(
            hand,
            melds=["k11"],
            tile=21,
            rules=TaiwanRules(no_flowers_enabled=True),
        )
        self.assertEqual(no_flowers.tai, 1)
        self.assertEqual(no_flowers.fan_ids, ["no_flowers"])

    def test_extension_fans_follow_their_exact_shapes(self):
        half_begging = self.score(
            [11, 11],
            melds=["s22", "s25", "s32", "s35", "k42"],
            tile=11,
            source="self_draw",
            rules=TaiwanRules(half_begging_enabled=True),
        )
        self.assertEqual(set(half_begging.fan_ids), {"self_draw", "half_begging"})

        base_hand = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 45, 45]
        river = self.score(
            base_hand,
            melds=["k11"],
            tile=21,
            last_tile_claim=True,
            rules=TaiwanRules(last_tile_claim_enabled=True),
        )
        self.assertEqual(river.fan_ids, ["last_tile_claim"])

        standard_winds = self.score(
            [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45],
            melds=["k41", "k42"],
            tile=11,
            seat_wind=41,
            round_wind=42,
        )
        self.assertIn("seat_wind_pung", standard_winds.fan_ids)
        self.assertIn("prevalent_wind_pung", standard_winds.fan_ids)
        self.assertNotIn("wind_pung", standard_winds.fan_ids)

        winds = self.score(
            [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45],
            melds=["k41", "k42"],
            tile=11,
            seat_wind=41,
            round_wind=42,
            rules=TaiwanRules(all_wind_pungs_enabled=True),
        )
        wind_fan = next(fan for fan in winds.fans if fan.fan_id == "wind_pung")
        self.assertEqual((wind_fan.name, wind_fan.count, wind_fan.total), ("风刻", 2, 2))
        self.assertNotIn("seat_wind_pung", winds.fan_ids)
        self.assertNotIn("prevalent_wind_pung", winds.fan_ids)

        no_honor = self.score(
            [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 39, 39],
            melds=["k11"],
            tile=21,
            rules=TaiwanRules(no_flowers_or_honors_enabled=True),
        )
        self.assertIn("no_flowers_or_honors", no_honor.fan_ids)
        self.assertEqual(
            next(
                fan.name
                for fan in no_honor.fans
                if fan.fan_id == "no_flowers_or_honors"
            ),
            "无字无花",
        )

        melded_kong = self.score(
            base_hand,
            melds=["g11"],
            tile=21,
            rules=TaiwanRules(melded_kong_enabled=True),
        )
        concealed_kong = self.score(
            base_hand,
            melds=["G11"],
            tile=21,
            rules=TaiwanRules(concealed_kong_enabled=True),
        )
        self.assertIn("melded_kong", melded_kong.fan_ids)
        self.assertIn("concealed_kong", concealed_kong.fan_ids)

    def test_eight_flowers_modes_and_seven_flowers_steal_eighth_are_fixed_specials(self):
        flowers = list(FLOWER_TILES)
        invalid_shape = [11] * 4
        for mode in ("optional_standalone", "forced_standalone"):
            with self.subTest(mode=mode):
                result = self.score(
                    invalid_shape,
                    tile=11,
                    source="self_draw",
                    flowers=flowers,
                    eight_flowers_and_seasons=True,
                    rules=TaiwanRules(eight_flowers_mode=mode),
                )
                self.assertTrue(result.is_win)
                self.assertEqual(result.tai, 8)
                self.assertEqual(result.fan_ids, ["eight_flowers_and_seasons"])
                self.assertEqual(result.as_dict()["decomposition"], ["special:eight_flowers_and_seasons"])

        seven = self.score(
            invalid_shape,
            tile=11,
            source="seven_flowers_steal_eighth",
            flowers=flowers,
            seven_flowers_steal_eighth=True,
        )
        self.assertTrue(seven.is_win)
        self.assertEqual(seven.tai, 8)
        self.assertEqual(seven.fan_ids, ["seven_flowers_steal_eighth"])

        starting_flower_win = self.score(
            invalid_shape,
            tile=11,
            source="self_draw",
            flowers=flowers,
            eight_flowers_and_seasons=True,
            heavenly_win=True,
            rules=TaiwanRules(
                eight_flowers_mode="compound",
                initial_flower_bonus_enabled=True,
            ),
        )
        self.assertEqual(starting_flower_win.tai, 12)
        self.assertEqual(
            starting_flower_win.fan_ids,
            ["eight_flowers_and_seasons", "initial_flower_bonus"],
        )
        opening_bonus = next(
            fan
            for fan in starting_flower_win.fans
            if fan.fan_id == "initial_flower_bonus"
        )
        self.assertEqual(opening_bonus.tai, 4)

        custom_starting_flower_win = self.score(
            invalid_shape,
            tile=11,
            source="self_draw",
            flowers=flowers,
            eight_flowers_and_seasons=True,
            heavenly_win=True,
            rules=TaiwanRules(
                eight_flowers_mode="compound",
                initial_flower_bonus_enabled=True,
                fan_tai_overrides={"initial_flower_bonus": 6},
            ),
        )
        self.assertEqual(custom_starting_flower_win.tai, 14)
        self.assertEqual(
            next(
                fan.tai
                for fan in custom_starting_flower_win.fans
                if fan.fan_id == "initial_flower_bonus"
            ),
            6,
        )

        starting_seven_flowers_steal_eighth = self.score(
            invalid_shape,
            tile=11,
            source="seven_flowers_steal_eighth",
            flowers=flowers,
            seven_flowers_steal_eighth=True,
            earthly_win=True,
            rules=TaiwanRules(initial_flower_bonus_enabled=True),
        )
        self.assertEqual(starting_seven_flowers_steal_eighth.tai, 12)
        self.assertEqual(
            starting_seven_flowers_steal_eighth.fan_ids,
            ["seven_flowers_steal_eighth", "initial_flower_bonus"],
        )

        disabled = self.score(
            invalid_shape,
            tile=11,
            source="seven_flowers_steal_eighth",
            flowers=flowers,
            seven_flowers_steal_eighth=True,
            rules=TaiwanRules(seven_flowers_steal_eighth_enabled=False),
        )
        self.assertFalse(disabled.is_win)
        self.assertIn("未启用七抢一", disabled.reason)

    def test_after_kong_only_applies_to_self_draw(self):
        hand = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 45, 45]
        self_draw = self.score(
            hand,
            melds=["k11"],
            tile=21,
            source="self_draw",
            out_with_replacement_tile=True,
        )
        self.assertIn("out_with_replacement_tile", self_draw.fan_ids)

        robbed_kong = self.score(
            hand,
            melds=["k11"],
            tile=21,
            source="robbing_kong",
            out_with_replacement_tile=True,
        )
        self.assertIn("robbing_kong", robbed_kong.fan_ids)
        self.assertNotIn("out_with_replacement_tile", robbed_kong.fan_ids)

    def test_compound_and_additive_eight_flowers_modes(self):
        flowers = list(FLOWER_TILES)
        hand = [11, 12, 13, 14, 15, 16, 21, 22, 23, 24, 25, 26, 31, 32, 33, 45, 45]
        compound_rules = TaiwanRules(eight_flowers_mode="compound")
        compound = self.score(
            hand,
            tile=21,
            source="self_draw",
            flowers=flowers,
            eight_flowers_and_seasons=True,
            rules=compound_rules,
        )
        self.assertEqual(compound.tai, 11)
        self.assertEqual(
            set(compound.fan_ids),
            {"concealed_hand", "fully_concealed_hand", "self_draw", "eight_flowers_and_seasons"},
        )
        self.assertNotIn("flower_tile", compound.fan_ids)
        self.assertNotIn("flower_kong", compound.fan_ids)

        compound_without_shape = self.score(
            [11] * 4,
            tile=11,
            source="self_draw",
            flowers=flowers,
            eight_flowers_and_seasons=True,
            rules=compound_rules,
        )
        self.assertEqual(compound_without_shape.fan_ids, ["eight_flowers_and_seasons"])

        additive = self.score(
            hand,
            tile=21,
            source="self_draw",
            flowers=flowers,
            rules=TaiwanRules(eight_flowers_mode="additive"),
        )
        self.assertEqual(additive.tai, 11)
        self.assertIn("eight_flowers_and_seasons", additive.fan_ids)

    def test_eight_pairs_half_uses_the_complete_hand_for_suit_fans(self):
        mixed = self.score(
            [11, 11, 11, 12, 12, 13, 13, 21, 21, 22, 22, 23, 23, 31, 31, 32, 32],
            tile=11,
            source="self_draw",
            rules=TaiwanRules(eight_and_a_half_pairs_enabled=True),
        )
        self.assertEqual(mixed.tai, 11)
        self.assertNotIn("full_flush", mixed.fan_ids)
        self.assertNotIn("half_flush", mixed.fan_ids)

        full_flush = self.score(
            [11, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18],
            tile=11,
            source="self_draw",
            rules=TaiwanRules(eight_and_a_half_pairs_enabled=True),
        )
        self.assertEqual(full_flush.tai, 19)
        self.assertIn("full_flush", full_flush.fan_ids)
        self.assertEqual(full_flush.as_dict()["decomposition"], ["special:eight_and_a_half_pairs"])

    def test_minimum_tai_rejects_zero_tai_but_default_accepts_it(self):
        hand = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 45, 45]
        result = self.score(
            hand,
            melds=["k11"],
            tile=21,
            rules=TaiwanRules(minimum_tai=1),
        )
        self.assertFalse(result.is_win)
        self.assertEqual(result.tai, 0)
        self.assertIn("最低 1 台", result.reason)

    def test_payment_examples_and_cap_order(self):
        idle_self = settle_win(
            winner=1,
            hand_tai=3,
            win_source="self_draw",
            dealer=0,
            dealer_streak=0,
        )
        self.assertEqual(idle_self.score_changes, {0: -9, 1: 25, 2: -8, 3: -8})

        zero_ron = settle_win(
            winner=1,
            hand_tai=0,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=2,
        )
        self.assertEqual(zero_ron.score_changes, {0: 0, 1: 5, 2: -5, 3: 0})

        dealer_ron = settle_win(
            winner=0,
            hand_tai=2,
            win_source="discard",
            dealer=0,
            dealer_streak=1,
            discarder=1,
        )
        self.assertEqual([payment.amount for payment in dealer_ron.payments], [10])
        self.assertEqual(
            settlement_display_fan_names(["门清"], dealer_ron),
            ["门清", "连庄拉庄*3"],
        )

        dealer_self = settle_win(
            winner=0,
            hand_tai=4,
            win_source="self_draw",
            dealer=0,
            dealer_streak=2,
        )
        self.assertEqual(dealer_self.score_changes, {0: 42, 1: -14, 2: -14, 3: -14})

        nondealer_self = settle_win(
            winner=1,
            hand_tai=6,
            win_source="self_draw",
            dealer=0,
            dealer_streak=1,
        )
        self.assertEqual(nondealer_self.score_changes, {0: -14, 1: 36, 2: -11, 3: -11})

        no_relation = settle_win(
            winner=1,
            hand_tai=2,
            win_source="discard",
            dealer=0,
            dealer_streak=2,
            discarder=2,
        )
        self.assertEqual(settlement_display_fan_names(["平胡"], no_relation), ["平胡"])

        capped = settle_win(
            winner=0,
            hand_tai=21,
            win_source="self_draw",
            dealer=0,
            dealer_streak=2,
            rules=TaiwanRules(tai_cap=16),
        )
        self.assertEqual([payment.amount for payment in capped.payments], [26, 26, 26])

    def test_settlement_rejects_invalid_tai_streak_and_seat_inputs(self):
        with self.assertRaises(ValueError):
            settle_win(
                winner=0,
                hand_tai=-1,
                win_source="self_draw",
                dealer=0,
                dealer_streak=0,
            )
        with self.assertRaises(ValueError):
            settle_win(
                winner=0,
                hand_tai=1,
                win_source="self_draw",
                dealer=0,
                dealer_streak=-1,
            )
        with self.assertRaises(ValueError):
            settle_win(
                winner=0,
                hand_tai=1,
                win_source="self_draw",
                dealer=0,
                dealer_streak=0,
                player_indices=(0, 0, 1, 2),
            )
        with self.assertRaises(ValueError):
            settle_win(
                winner=True,
                hand_tai=1,
                win_source="self_draw",
                dealer=0,
                dealer_streak=0,
            )
        with self.assertRaises(ValueError):
            settle_win(
                winner=0,
                hand_tai=1,
                win_source="discard",
                dealer=0,
                dealer_streak=0,
                discarder=True,
            )

    def test_liability_moves_all_self_draw_payments_to_one_player(self):
        settlement = settle_win(
            winner=1,
            hand_tai=3,
            win_source="self_draw",
            dealer=0,
            dealer_streak=0,
            liable_payer=2,
        )
        self.assertEqual(settlement.score_changes, {0: 0, 1: 25, 2: -25, 3: 0})
        self.assertEqual([payment.payer for payment in settlement.payments], [2, 2, 2])
        self.assertEqual([payment.amount for payment in settlement.payments], [9, 8, 8])

    def test_liability_ron_from_liable_payer_matches_normal_settlement(self):
        normal = settle_win(
            winner=1,
            hand_tai=3,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=2,
        )
        liable = settle_win(
            winner=1,
            hand_tai=3,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=2,
            liable_payer=2,
        )

        self.assertEqual(liable, normal)

    def test_liability_ron_from_other_player_is_paid_by_liable_payer(self):
        settlement = settle_win(
            winner=1,
            hand_tai=3,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=3,
            liable_payer=2,
        )

        self.assertEqual(settlement.score_changes, {0: 0, 1: 8, 2: -8, 3: 0})
        self.assertEqual([payment.payer for payment in settlement.payments], [2])
        self.assertEqual([payment.amount for payment in settlement.payments], [8])

    def test_liability_ron_split_rounds_both_shares_up(self):
        settlement = settle_win(
            winner=1,
            hand_tai=3,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=0,
            liable_payer=2,
            rules=TaiwanRules(liability_ron_split_enabled=True),
        )

        self.assertEqual(settlement.score_changes, {0: -5, 1: 10, 2: -5, 3: 0})
        self.assertEqual([payment.payer for payment in settlement.payments], [2, 0])
        self.assertEqual([payment.amount for payment in settlement.payments], [5, 5])

    def test_persisted_liability_requires_the_final_matching_fan(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            full_flush_liability_enabled=True,
            half_flush_liability_enabled=True,
        )
        state.player_list = [DummyPlayer(i) for i in range(4)]
        winner = state.player_list[1]
        winner.combination_tiles = ["k11", "k12", "k13", "k14"]

        state._remember_claim_liability(1, 2, 15)

        self.assertEqual(
            winner.liability_payers,
            {"full_flush": 2, "half_flush": 2},
        )
        half_flush = state._build_pending_winner(
            1,
            "self_draw",
            "hu_self",
            {"fan_ids": ["half_flush"], "tai": 4},
            45,
        )
        self.assertEqual(half_flush["liable_payer"], 2)

        full_flush = state._build_pending_winner(
            1,
            "self_draw",
            "hu_self",
            {"fan_ids": ["full_flush"], "tai": 8},
            15,
        )
        self.assertEqual(full_flush["liable_payer"], 2)

        non_matching = state._build_pending_winner(
            1,
            "self_draw",
            "hu_self",
            {"fan_ids": ["all_pungs"], "tai": 4},
            15,
        )
        self.assertIsNone(non_matching["liable_payer"])

    def test_liability_is_carried_into_discard_and_robbing_kong_wins(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(full_flush_liability_enabled=True)
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[1].liability_payers = {"full_flush": 2}

        discard_win = state._build_pending_winner(
            1,
            "discard",
            "hu_first",
            {"fan_ids": ["full_flush"], "tai": 8},
            15,
            3,
        )
        robbing_kong = state._build_pending_winner(
            1,
            "robbing_kong",
            "hu_first",
            {"fan_ids": ["full_flush", "robbing_kong"], "tai": 9},
            15,
            3,
        )

        self.assertEqual(discard_win["payer"], 3)
        self.assertEqual(discard_win["liable_payer"], 2)
        self.assertEqual(robbing_kong["payer"], 3)
        self.assertEqual(robbing_kong["liable_payer"], 2)

    def test_each_liability_fan_can_be_enabled_independently(self):
        cases = (
            (
                "all_pungs_liability_enabled",
                "all_pungs",
                ["k11", "k12", "k13", "k14"],
                14,
            ),
            (
                "half_flush_liability_enabled",
                "half_flush",
                ["s12", "k15", "k41", "k45"],
                45,
            ),
            (
                "full_flush_liability_enabled",
                "full_flush",
                ["k11", "k12", "k13", "k14"],
                14,
            ),
            (
                "big_three_dragons_liability_enabled",
                "big_three_dragons",
                ["k45", "k46", "k47"],
                47,
            ),
            (
                "little_three_dragons_liability_enabled",
                "little_three_dragons",
                ["k45", "k46"],
                46,
            ),
            (
                "big_four_winds_liability_enabled",
                "big_four_winds",
                ["k41", "k42", "k43", "k44"],
                44,
            ),
            (
                "little_four_winds_liability_enabled",
                "little_four_winds",
                ["k41", "k42", "k43"],
                43,
            ),
            (
                "all_honors_liability_enabled",
                "all_honors",
                ["k41", "k42", "k43", "k44"],
                44,
            ),
        )
        for field_name, fan_id, completed_melds, tile in cases:
            with self.subTest(fan_id=fan_id):
                state = object.__new__(TaiwanGameState)
                state.rules = TaiwanRules(**{field_name: True})
                state.player_list = [DummyPlayer(i) for i in range(4)]
                winner = state.player_list[1]
                winner.combination_tiles = completed_melds[:-1]
                state._remember_claim_liability(1, 3, tile)
                self.assertEqual(winner.liability_payers, {})

                winner.combination_tiles = completed_melds
                state._remember_claim_liability(1, 2, tile)

                self.assertEqual(winner.liability_payers, {fan_id: 2})
                pending = state._build_pending_winner(
                    1,
                    "self_draw",
                    "hu_self",
                    {"fan_ids": [fan_id], "tai": 8},
                    tile,
                )
                self.assertEqual(pending["liable_payer"], 2)

                state.rules = TaiwanRules()
                winner.liability_payers = {}
                state._remember_claim_liability(1, 3, tile)
                self.assertEqual(winner.liability_payers, {})

    def test_four_kongs_liability_requires_discard_claimed_fourth_kong(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            four_kongs_enabled=True,
            four_kongs_liability_enabled=True,
        )
        state.player_list = [DummyPlayer(i) for i in range(4)]
        winner = state.player_list[1]
        winner.combination_tiles = ["G11", "g12", "G13"]

        state._remember_claim_liability(1, 3, 14)
        self.assertEqual(winner.liability_payers, {})

        winner.combination_tiles.append("g14")
        state._remember_claim_liability(1, 2, 14)
        self.assertEqual(winner.liability_payers, {"four_kongs": 2})
        pending = state._build_pending_winner(
            1,
            "self_draw",
            "hu_self",
            {"fan_ids": ["self_draw"], "tai": 1},
            25,
        )
        self.assertIsNone(pending["liable_payer"])
        pending = state._build_pending_winner(
            1,
            "self_draw",
            "hu_self",
            {"fan_ids": ["self_draw", "four_kongs"], "tai": 9},
            25,
        )
        self.assertEqual(pending["liable_payer"], 2)

        self_acquired = object.__new__(TaiwanGameState)
        self_acquired.rules = TaiwanRules(
            four_kongs_enabled=True,
            four_kongs_liability_enabled=True,
        )
        self_acquired.player_list = [DummyPlayer(i) for i in range(4)]
        self_acquired.player_list[1].combination_tiles = [
            "G11",
            "g12",
            "G13",
            "G14",
        ]
        self_acquired._remember_claim_liability(1, 3, 14)
        self.assertEqual(self_acquired.player_list[1].liability_payers, {})

    def test_five_kongs_liability_requires_discard_claimed_fifth_kong(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            five_kongs_enabled=True,
            five_kongs_liability_enabled=True,
        )
        state.player_list = [DummyPlayer(i) for i in range(4)]
        winner = state.player_list[1]
        winner.combination_tiles = ["G11", "g12", "G13", "g14"]

        state._remember_claim_liability(1, 3, 15)
        self.assertEqual(winner.liability_payers, {})

        winner.combination_tiles.append("g15")
        state._remember_claim_liability(1, 2, 15)
        self.assertEqual(winner.liability_payers, {"five_kongs": 2})
        pending = state._build_pending_winner(
            1,
            "self_draw",
            "hu_self",
            {"fan_ids": ["self_draw", "five_kongs"], "tai": 17},
            25,
        )
        self.assertEqual(pending["liable_payer"], 2)

    def test_five_kongs_liability_precedes_compound_all_pungs_liability(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            five_kongs_enabled=True,
            five_kongs_liability_enabled=True,
            all_pungs_liability_enabled=True,
        )
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[1].liability_payers = {
            "all_pungs": 2,
            "five_kongs": 3,
        }

        self.assertEqual(
            state._liability_payer_for_win(
                1,
                "self_draw",
                None,
                25,
                {"fan_ids": ["all_pungs", "five_kongs"]},
            ),
            3,
        )

    def test_compound_liability_uses_stable_specific_fan_priority(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            big_four_winds_liability_enabled=True,
            all_honors_liability_enabled=True,
        )
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[1].liability_payers = {
            "all_honors": 2,
            "big_four_winds": 3,
        }
        self.assertEqual(
            state._liability_payer_for_win(
                1,
                "self_draw",
                None,
                45,
                {"fan_ids": ["all_honors", "big_four_winds"]},
            ),
            3,
        )

    def test_later_claim_does_not_replace_the_original_liability_payer(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            full_flush_liability_enabled=True,
            half_flush_liability_enabled=True,
        )
        state.player_list = [DummyPlayer(i) for i in range(4)]
        winner = state.player_list[1]
        winner.combination_tiles = ["k11", "k12", "k13", "k14"]
        state._remember_claim_liability(1, 2, 14)
        self.assertEqual(
            winner.liability_payers,
            {"full_flush": 2, "half_flush": 2},
        )

        winner.combination_tiles.append("k15")
        state._remember_claim_liability(1, 3, 15)
        self.assertEqual(
            winner.liability_payers,
            {"full_flush": 2, "half_flush": 2},
        )

    def test_removed_liability_master_switch_is_rejected(self):
        with self.assertRaisesRegex(
            ValueError,
            "dangerous_discard_liability",
        ):
            TaiwanRules.from_dict({"dangerous_discard_liability": True})

    def test_every_liability_target_is_a_published_scoring_fan(self):
        self.assertLessEqual(
            set(LIABILITY_FAN_CONFIG_FIELDS),
            set(FAN_DEFINITIONS),
        )

    def test_scoring_presets_only_override_fan_values(self):
        hand = [
            11, 12, 13, 14, 15, 16, 21, 22, 23,
            24, 25, 26, 31, 32, 33, 45, 45,
        ]
        shenlaiye = self.score(
            hand,
            tile=21,
            human_win=True,
            rules=TaiwanRules(scoring_preset="shenlaiye"),
        )
        star31 = self.score(
            hand,
            tile=21,
            human_win=True,
            rules=TaiwanRules(scoring_preset="star31"),
        )
        cml = self.score(
            hand,
            tile=21,
            heavenly_ready=True,
            rules=TaiwanRules(scoring_preset="cml"),
        )
        shenlaiye_heavenly_ready = self.score(
            hand,
            tile=21,
            heavenly_ready=True,
            rules=TaiwanRules(scoring_preset="shenlaiye"),
        )
        self.assertEqual(shenlaiye.tai, 8)
        self.assertEqual(shenlaiye.fan_ids, ["human_win"])
        self.assertEqual(star31.tai, 16)
        self.assertEqual(star31.fan_ids, ["human_win"])
        self.assertIn("heavenly_ready", cml.fan_ids)
        self.assertEqual(
            next(
                fan.tai
                for fan in shenlaiye_heavenly_ready.fans
                if fan.fan_id == "heavenly_ready"
            ),
            16,
        )

        ambiguous_hand = [
            13, 14, 14, 14, 14, 15, 25, 25, 32,
            32, 32, 35, 35, 35, 37, 38, 39,
        ]
        preset_only = self.score(
            ambiguous_hand,
            tile=14,
            rules=TaiwanRules(scoring_preset="star31"),
        )
        explicit_triplet_priority = self.score(
            [
                13, 14, 14, 14, 14, 15, 25, 25, 32,
                32, 32, 35, 35, 35, 37, 38, 39,
            ],
            tile=14,
            rules=TaiwanRules(
                scoring_preset="star31",
                prefer_triplet_decomposition_on_discard_win=True,
            ),
        )
        self.assertEqual(preset_only.decomposition.winning_component[0], "sequence")
        self.assertIn("three_concealed_pungs", preset_only.fan_ids)
        self.assertEqual(
            explicit_triplet_priority.decomposition.winning_component[0],
            "triplet",
        )
        self.assertNotIn("three_concealed_pungs", explicit_triplet_priority.fan_ids)

        # 台表选择不再补齐任何隐藏判定馆规；它只选择完整基础台表。
        self.assertEqual(
            SCORING_PRESET_TABLES["star31"]["flower_kong"],
            2,
        )
        self.assertEqual(
            TaiwanRules.from_dict({"scoring_preset": "cml"}).scoring_preset,
            "cml",
        )

    def test_fan_tai_overrides_are_sparse_and_apply_to_every_fan(self):
        rules = TaiwanRules.from_dict(
            {
                "scoring_preset": "star31",
                "fan_tai_overrides": {
                    "all_chows": 6,
                    "flower_kong": 2,
                },
            }
        )
        # 与明星三缺一基础台表相同的花杠值不重复保存。
        self.assertEqual(rules.fan_tai_overrides, {"all_chows": 6})

        hand = [
            21, 22, 23, 24, 25, 26, 31,
            32, 33, 34, 35, 36, 45, 45,
        ]
        result = self.score(
            hand,
            melds=["s12"],
            tile=21,
            rules=rules,
        )
        all_chows = next(fan for fan in result.fans if fan.fan_id == "all_chows")
        self.assertEqual(all_chows.tai, 6)

        with self.assertRaisesRegex(ValueError, "未知台湾麻将台种"):
            TaiwanRules.from_dict({"fan_tai_overrides": {"removed_fan": 8}})
        for invalid in (0, 65, True, "8"):
            with self.subTest(invalid=invalid):
                with self.assertRaisesRegex(ValueError, "1 至 64"):
                    TaiwanRules.from_dict(
                        {"fan_tai_overrides": {"all_chows": invalid}}
                    )

    def test_published_scoring_preset_contract(self):
        # 简易迁移保险：删除已发布预设或改变其完整台表都会失败。
        # 本次按允许的破坏性变更加入 five_kongs，显式更新完整台表契约。
        expected_hashes = {
            "sml": "d4afe7e589612aee8fcb87beefad1a52214b1432798a3499f36d3a385557f418",
            "cml": "d4afe7e589612aee8fcb87beefad1a52214b1432798a3499f36d3a385557f418",
            "star31": "d15b991235e535ac8a58ab01b181d4a5baa92998bfa98722a0eedc0258851220",
            "shenlaiye": "dfc4b8e693cfdaf0cf8a2bcf5769b05d897246fcc1a1294c610874251f0b5608",
        }
        self.assertEqual(set(SCORING_PRESET_TABLES), set(expected_hashes))
        for preset_id, expected_hash in expected_hashes.items():
            with self.subTest(preset_id=preset_id):
                table = SCORING_PRESET_TABLES[preset_id]
                self.assertEqual(set(table), set(FAN_DEFINITIONS))
                payload = json.dumps(
                    dict(table),
                    sort_keys=True,
                    separators=(",", ":"),
                ).encode()
                self.assertEqual(hashlib.sha256(payload).hexdigest(), expected_hash)
        with self.assertRaises(TypeError):
            FAN_DEFINITIONS["all_chows"] = ("平胡", 99)
        with self.assertRaises(TypeError):
            SCORING_PRESET_TABLES["sml"]["all_chows"] = 99

    def test_scoring_preset_does_not_infer_other_rule_fields(self):
        expected = asdict(TaiwanRules())
        expected.pop("scoring_preset")
        for preset in ("sml", "cml", "star31", "shenlaiye"):
            with self.subTest(preset=preset):
                actual = asdict(
                    TaiwanRules.from_dict({"scoring_preset": preset})
                )
                actual.pop("scoring_preset")
                self.assertEqual(actual, expected)

    def test_pinfu_definition_is_independent_from_scoring_preset(self):
        open_honor_hand = [
            21, 22, 23, 24, 25, 26, 31,
            32, 33, 34, 35, 36, 45, 45,
        ]
        open_result = self.score(
            open_honor_hand,
            melds=["s12"],
            tile=21,
            flowers=[51],
            rules=TaiwanRules(scoring_preset="star31"),
        )
        self.assertIn("all_chows", open_result.fan_ids)
        self.assertIn("flower_tile", open_result.fan_ids)

        open_self_draw = self.score(
            open_honor_hand,
            melds=["s12"],
            tile=21,
            source="self_draw",
            rules=TaiwanRules(),
        )
        self.assertNotIn("all_chows", open_self_draw.fan_ids)

        closed_result = self.score(
            [
                11, 12, 13, 14, 15, 16, 21, 22, 23,
                24, 25, 26, 31, 32, 33, 45, 45,
            ],
            tile=11,
            rules=TaiwanRules(),
        )
        self.assertNotIn("all_chows", closed_result.fan_ids)

        strict_hand = [
            21, 22, 23, 25, 26, 27, 31,
            32, 33, 35, 36, 37, 29, 29,
        ]
        strict_rules = TaiwanRules(
            scoring_preset="sml",
            all_chows_definition="strict",
        )
        strict_pair_wait = self.score(
            strict_hand,
            melds=["s12"],
            tile=29,
            rules=strict_rules,
        )
        self.assertEqual(strict_pair_wait.waits, frozenset({29}))
        self.assertEqual(
            strict_pair_wait.decomposition.winning_component[0],
            "pair",
        )
        self.assertNotIn("all_chows", strict_pair_wait.fan_ids)
        self.assertIn("single_wait", strict_pair_wait.fan_ids)

        open_pair_wait = self.score(
            strict_hand,
            melds=["s12"],
            tile=29,
            rules=TaiwanRules(all_chows_definition="relaxed"),
        )
        self.assertNotIn("all_chows", open_pair_wait.fan_ids)
        self.assertIn("single_wait", open_pair_wait.fan_ids)

        # 同一张 5 既存在于将牌、又可作为顺子和牌张时，
        # 应按形式听牌集合判定非独听，而不是被其它拆分落点否决。
        ambiguous_wait = [
            23, 24, 25, 25, 25,
            31, 32, 33,
            34, 35, 36,
            37, 38, 39,
        ]
        for definition in ("relaxed", "strict"):
            with self.subTest(all_chows_definition=definition, ambiguous_use=True):
                result = self.score(
                    ambiguous_wait,
                    melds=["s12"],
                    tile=25,
                    rules=TaiwanRules(all_chows_definition=definition),
                )
                self.assertIn("all_chows", result.fan_ids)
                # 同一张牌也可落在将牌或顺子拆分；平胡只看整手形式听牌
                # 是否为非独听，不要求把最终拆分强制选成顺子和牌。
                self.assertIn(
                    result.decomposition.winning_component[0],
                    ("pair", "sequence"),
                )

        # 牌张在某个拆分里虽然落在边张位置，但整手形式听牌有三张；
        # SML 只排除独听，不再附加“必须结构两面”的限制。
        edge_with_multiple_waits = self.score(
            [
                21, 22, 23, 24, 25, 26, 31,
                32, 33, 34, 35, 36, 29, 29,
            ],
            melds=["s12"],
            tile=21,
            rules=TaiwanRules(all_chows_definition="strict"),
        )
        self.assertEqual(edge_with_multiple_waits.waits, frozenset({21, 24, 27}))
        self.assertIn("all_chows", edge_with_multiple_waits.fan_ids)

        strict_self_draw = self.score(
            strict_hand,
            melds=["s12"],
            tile=29,
            source="self_draw",
            rules=strict_rules,
        )
        strict_with_flower = self.score(
            strict_hand,
            melds=["s12"],
            tile=29,
            flowers=[51],
            rules=strict_rules,
        )
        strict_with_honor = self.score(
            open_honor_hand,
            melds=["s12"],
            tile=45,
            rules=strict_rules,
        )
        self.assertNotIn("all_chows", strict_self_draw.fan_ids)
        self.assertNotIn("all_chows", strict_with_flower.fan_ids)
        self.assertNotIn("all_chows", strict_with_honor.fan_ids)

        self.assertEqual(
            TaiwanRules(scoring_preset="star31").all_chows_definition,
            "relaxed",
        )
        self.assertEqual(
            TaiwanRules.from_dict(
                {"scoring_preset": "shenlaiye"}
            ).all_chows_definition,
            "relaxed",
        )
        self.assertEqual(
            TaiwanRules.from_dict(
                {"all_chows_definition": "strict"}
            ).scoring_preset,
            "sml",
        )

    def test_small_winds_wind_fans_are_explicitly_configured(self):
        hand = [11, 12, 13, 21, 22, 23, 44, 44]
        preset_only = self.score(
            hand,
            melds=["k41", "k42", "k43"],
            tile=13,
            seat_wind=41,
            round_wind=42,
            rules=TaiwanRules(scoring_preset="shenlaiye"),
        )
        explicit = self.score(
            hand,
            melds=["k41", "k42", "k43"],
            tile=13,
            seat_wind=41,
            round_wind=42,
            rules=TaiwanRules(
                scoring_preset="sml",
                little_four_winds_add_wind_pungs=True,
            ),
        )
        self.assertIn("little_four_winds", preset_only.fan_ids)
        self.assertNotIn("seat_wind_pung", preset_only.fan_ids)
        self.assertNotIn("prevalent_wind_pung", preset_only.fan_ids)
        self.assertIn("little_four_winds", explicit.fan_ids)
        self.assertIn("seat_wind_pung", explicit.fan_ids)
        self.assertIn("prevalent_wind_pung", explicit.fan_ids)

    def test_all_honors_combination_is_explicitly_configured(self):
        hand = [
            41, 41, 41, 42, 42, 42, 43, 43, 43,
            44, 44, 44, 45, 45, 45, 46, 46,
        ]
        preset_only = self.score(
            hand,
            tile=46,
            rules=TaiwanRules(scoring_preset="shenlaiye"),
        )
        explicit_exclusion = self.score(
            hand,
            tile=46,
            rules=TaiwanRules(all_honors_add_all_pungs=False),
        )
        self.assertIn("all_honors", preset_only.fan_ids)
        self.assertIn("all_pungs", preset_only.fan_ids)
        self.assertIn("all_honors", explicit_exclusion.fan_ids)
        self.assertNotIn("all_pungs", explicit_exclusion.fan_ids)

    def test_earthly_ready_menqing_combination_is_explicitly_configured(self):
        hand = [
            11, 12, 13, 14, 15, 16, 21, 22, 23,
            24, 25, 26, 31, 32, 33, 45, 45,
        ]
        preset_only = self.score(
            hand,
            tile=21,
            earthly_ready=True,
            rules=TaiwanRules(scoring_preset="shenlaiye"),
        )
        explicit_exclusion = self.score(
            hand,
            tile=21,
            earthly_ready=True,
            rules=TaiwanRules(earthly_ready_excludes_concealed_and_declared_ready=True),
        )
        self.assertIn("concealed_hand", preset_only.fan_ids)
        self.assertIn("earthly_ready", preset_only.fan_ids)
        self.assertNotIn("concealed_hand", explicit_exclusion.fan_ids)
        self.assertIn("earthly_ready", explicit_exclusion.fan_ids)

    def test_public_ready_requires_the_public_declaration(self):
        hand = [
            11, 12, 13, 14, 15, 16, 21, 22, 23,
            24, 25, 26, 31, 32, 33, 45, 45,
        ]
        rules = TaiwanRules(public_ready_enabled=True)
        hidden = self.score(hand, tile=21, heavenly_ready=True, rules=rules)
        declared = self.score(
            hand,
            tile=21,
            heavenly_ready=True,
            declared_ready=True,
            rules=rules,
        )
        self.assertNotIn("heavenly_ready", hidden.fan_ids)
        self.assertIn("heavenly_ready", declared.fan_ids)

    def test_public_ready_must_be_declared_at_the_qualification_window(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(1, [11, 12])
        player.has_draw_slot = True
        player.last_drawn_tile = 12
        player.qualification_alive = True
        player.qualification_ever = True
        state.player_list = [DummyPlayer(0), player]
        state.rules = TaiwanRules(public_ready_enabled=True)
        state.rules_dict = asdict(state.rules)
        state.table_claim_or_kong = False
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args, **_kwargs: {21}
        )

        # 开启公开听牌时可选择任何能维持听牌的切牌；若不保留天听资格，
        # 仍可按普通公开听牌登记。
        self.assertEqual(set(state.ready_candidate_cuts(1)), {11, 12})
        state._finalize_ready_after_discard(1, declare_ready=False)
        self.assertFalse(player.qualification_alive)

        player.qualification_alive = True
        player.qualification_ever = True
        state.rules = TaiwanRules(public_ready_enabled=True)
        state.rules_dict = asdict(state.rules)
        self.assertEqual(set(state.ready_candidate_cuts(1)), {11, 12})

    def test_dealer_can_declare_ready_at_both_opening_qualification_windows(self):
        state = object.__new__(TaiwanGameState)
        dealer = DummyPlayer(0, [11, 12])
        state.player_list = [dealer] + [DummyPlayer(index) for index in range(1, 4)]
        state.rules = TaiwanRules(public_ready_enabled=True)
        state.rules_dict = asdict(state.rules)
        state.table_claim_or_kong = False
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args, **_kwargs: {21}
        )
        state.result_dict = {}
        state.supplement_win_allowed = True
        state.last_draw_was_last = False
        state.score_candidate = lambda *_args, **_kwargs: None
        state.can_establish_kong = lambda: True

        # 开局 17 张没有普通摸牌槽，庄家的第一打仍可声明天听。
        self.assertEqual(set(state.ready_candidate_cuts(0)), {11, 12})

        # 庄家第一打后，其第二个行动回合才是第一次正常摸牌；此时可声明地听。
        dealer.has_draw_slot = True
        dealer.last_drawn_tile = 12
        dealer.normal_draw_count = 1
        dealer.discard_count = 1
        self.assertEqual(set(state.ready_candidate_cuts(0)), {11, 12})
        self.assertIn("riichi_cut", check_action_hand_action(state, 0)[0])

        # 提交候选切牌后应登记地听并进入公开听牌锁牌状态。
        dealer.hand_tiles.remove(11)
        dealer.discard_count = 2
        state._finalize_ready_after_discard(0, declare_ready=True)
        self.assertTrue(dealer.earthly_ready)
        self.assertTrue(dealer.declared_ready)
        self.assertTrue(dealer.ready_locked)

    def test_hint_ready_qualification_covers_pre_discard_windows(self):
        state = object.__new__(TaiwanGameState)
        dealer = DummyPlayer(0)
        state.player_list = [dealer] + [DummyPlayer(index) for index in range(1, 4)]
        state.rules = TaiwanRules()
        state.rules_dict = asdict(state.rules)
        state.table_claim_or_kong = False

        self.assertEqual(state.taiwan_hint_ready_qualification(0), "heavenly")
        self.assertEqual(
            state.build_private_hand_action_info(0),
            {"ready_qualification": "heavenly"},
        )
        dealer.riichi_candidate_cuts = {11: [12, 15]}
        state.action_dict = {0: ["cut", "riichi_cut"]}
        self.assertEqual(
            state.build_private_hand_action_info(0),
            {
                "ready_qualification": "heavenly",
                "riichi_candidate_cuts": {11: [12, 15]},
            },
        )
        self.assertEqual(
            state.build_game_info_fields(),
            {"detailed_config": state.rules_dict},
        )
        self.assertEqual(
            state.build_record_title_fields(),
            {"detailed_config": state.rules_dict},
        )

        dealer.discard_count = 1
        dealer.normal_draw_count = 1
        self.assertEqual(state.taiwan_hint_ready_qualification(0), "earthly")

        dealer.qualification_ever = True
        dealer.declared_ready = True
        state.rules = TaiwanRules(public_ready_enabled=True)
        self.assertEqual(state.taiwan_hint_ready_qualification(0), "public")

        state.rules = TaiwanRules(ready_qualification_mode="disabled")
        dealer.declared_ready = False
        self.assertEqual(state.taiwan_hint_ready_qualification(0), "none")

    def test_dealer_cannot_delay_earthly_qualification_but_can_public_ready(self):
        state = object.__new__(TaiwanGameState)
        dealer = DummyPlayer(0, [11, 12])
        dealer.has_draw_slot = True
        dealer.last_drawn_tile = 12
        dealer.normal_draw_count = 1
        dealer.discard_count = 2
        state.player_list = [dealer]
        state.rules = TaiwanRules(public_ready_enabled=True)
        state.rules_dict = asdict(state.rules)
        state.table_claim_or_kong = False
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args, **_kwargs: {21}
        )

        # 错过地听时点后仍可进行普通公开听牌，但不能再取得地听资格。
        self.assertEqual(set(state.ready_candidate_cuts(0)), {11, 12})
        self.assertEqual(state.taiwan_hint_ready_qualification(0), "none")

        # 已取得并放弃天听者同样只剩普通公开听牌。
        dealer.discard_count = 1
        dealer.pre_first_draw_waiting = True
        dealer.qualification_ever = True
        self.assertEqual(set(state.ready_candidate_cuts(0)), {11, 12})
        self.assertEqual(state.taiwan_hint_ready_qualification(0), "none")

    def test_general_ready_remains_available_when_heavenly_ready_is_disabled(self):
        hand = [
            11, 12, 13, 14, 15, 16, 21, 22, 23,
            24, 25, 26, 31, 32, 33, 45, 45,
        ]
        result = self.score(
            hand,
            tile=21,
            heavenly_ready=True,
            declared_ready=True,
            rules=TaiwanRules(
                ready_qualification_mode="disabled",
                public_ready_enabled=True,
            ),
        )
        self.assertNotIn("heavenly_ready", result.fan_ids)
        self.assertIn("declared_ready", result.fan_ids)
        self.assertIn("报听", result.fan_names)

    def test_disabled_heavenly_ready_does_not_register_qualification(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(ready_qualification_mode="disabled")
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args, **_kwargs: {21}
        )
        state.rules_dict = asdict(state.rules)

        state._register_initial_heavenly_ready()

        self.assertFalse(any(player.qualification_alive for player in state.player_list))
        self.assertFalse(any(player.heavenly_ready for player in state.player_list))

    def test_starting_win_definitions_are_explicitly_configured(self):
        state = object.__new__(TaiwanGameState)
        state.player_list = [DummyPlayer(index) for index in range(4)]
        state.current_round = 1
        state.current_player_index = 0
        state.table_claim_or_kong = False
        state.opening_dealer_action = False
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.can_take_wall_tile = lambda: True

        discarder = state.player_list[0]
        winner = state.player_list[1]
        discarder.discard_count = 1
        state.rules = TaiwanRules(scoring_preset="star31")
        state.rules_dict = asdict(state.rules)
        self.assertTrue(
            state._score_context(1, "discard", include_special=False)["human_win"]
        )

        state.rules = TaiwanRules(human_win_definition="disabled")
        state.rules_dict = asdict(state.rules)
        self.assertFalse(
            state._score_context(1, "discard", include_special=False)["human_win"]
        )

        winner.normal_draw_count = 2
        state.rules = TaiwanRules(
            human_win_definition="discarder_first_discard"
        )
        state.rules_dict = asdict(state.rules)
        self.assertTrue(
            state._score_context(1, "discard", include_special=False)["human_win"]
        )
        discarder.discard_count = 2
        self.assertFalse(
            state._score_context(1, "discard", include_special=False)["human_win"]
        )

        winner.normal_draw_count = 1
        winner.discard_count = 0
        state.table_claim_or_kong = True
        state.rules = TaiwanRules(earthly_win_allows_open_calls=False)
        state.rules_dict = asdict(state.rules)
        self.assertFalse(
            state._score_context(1, "self_draw", include_special=False)[
                "earthly_win"
            ]
        )
        state.rules = TaiwanRules(earthly_win_allows_open_calls=True)
        state.rules_dict = asdict(state.rules)
        self.assertTrue(
            state._score_context(1, "self_draw", include_special=False)[
                "earthly_win"
            ]
        )
        state.player_list[2].combination_tiles = ["G22"]
        self.assertFalse(
            state._score_context(1, "self_draw", include_special=False)[
                "earthly_win"
            ]
        )

    def test_ready_qualification_mode_is_explicitly_configured(self):
        state = object.__new__(TaiwanGameState)
        state.player_list = [DummyPlayer(index) for index in range(4)]
        state.table_claim_or_kong = False
        state.rules = TaiwanRules(scoring_preset="star31")
        self.assertEqual(state.taiwan_hint_ready_qualification(1), "none")

        state.rules = TaiwanRules(
            ready_qualification_mode="first_eight_table_discards"
        )
        self.assertEqual(
            state.taiwan_hint_ready_qualification(0),
            "heavenly",
        )
        self.assertEqual(
            state.taiwan_hint_ready_qualification(1),
            "earthly",
        )
        for player in state.player_list:
            player.discard_count = 2
        self.assertEqual(state.taiwan_hint_ready_qualification(1), "none")

        for player in state.player_list:
            player.discard_count = 0
        state.rules = TaiwanRules(
            ready_qualification_mode="each_player_first_discard"
        )
        self.assertEqual(
            state.taiwan_hint_ready_qualification(1),
            "earthly",
        )
        state.player_list[1].discard_count = 1
        self.assertEqual(state.taiwan_hint_ready_qualification(1), "none")

    def test_non_dealer_standard_mode_excludes_dealer_heavenly_ready(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            scoring_preset="sml",
            ready_qualification_mode="standard_without_dealer_heavenly_ready",
        )
        state.rules_dict = asdict(state.rules)
        state.player_list = [DummyPlayer(index, [11, 12]) for index in range(4)]
        state.table_claim_or_kong = False
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args, **_kwargs: {21}
        )

        state._register_initial_heavenly_ready()

        self.assertFalse(state.player_list[0].heavenly_ready)
        self.assertTrue(all(player.heavenly_ready for player in state.player_list[1:]))
        state.player_list[0].discard_count = 1
        state._register_ready_after_discard(0)
        self.assertFalse(state.player_list[0].qualification_alive)
        self.assertEqual(state.taiwan_hint_ready_qualification(0), "none")

        state.rules = TaiwanRules(scoring_preset="cml")
        state.rules_dict = asdict(state.rules)
        state._register_ready_after_discard(0)
        self.assertTrue(state.player_list[0].heavenly_ready)
        self.assertTrue(state.player_list[0].qualification_alive)

    def test_concealed_kong_breaks_heavenly_earthly_win_and_ready(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            public_ready_enabled=True,
            earthly_win_allows_open_calls=True,
            ready_qualification_mode="each_player_first_discard",
        )
        state.rules_dict = asdict(state.rules)
        state.player_list = [DummyPlayer(index, [11, 12]) for index in range(4)]
        state.player_list[0].combination_tiles = ["G21"]
        state.table_claim_or_kong = True
        state.current_round = 1
        state.current_player_index = 1
        state.opening_dealer_action = False
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.can_take_wall_tile = lambda: True
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args, **_kwargs: {21}
        )

        player = state.player_list[1]
        player.normal_draw_count = 1
        context = state._score_context(1, "self_draw", include_special=False)
        self.assertFalse(context["earthly_win"])
        self.assertEqual(state.taiwan_hint_ready_qualification(1), "none")

        player.discard_count = 1
        state._register_ready_after_discard(1)
        self.assertFalse(player.qualification_alive)

        player.qualification_alive = True
        player.earthly_ready = True
        player.declared_ready = True
        player.ready_locked = True
        player.tag_list = ["declared_ready"]
        state._break_heavenly_earthly_ready_for_concealed_kong()
        self.assertFalse(player.qualification_alive)
        self.assertTrue(player.declared_ready)
        self.assertTrue(player.ready_locked)
        self.assertIn("declared_ready", player.tag_list)

    def test_passing_a_win_keeps_public_ready_and_draw_lock(self):
        state = TaiwanGameState.__new__(TaiwanGameState)
        player = DummyPlayer(0)
        player.qualification_alive = True
        player.earthly_ready = True
        player.declared_ready = True
        player.ready_locked = True
        player.tag_list.append("declared_ready")
        state.player_list = [player]
        state.rules = TaiwanRules(
            scoring_preset="shenlaiye",
            public_ready_enabled=True,
            declared_ready_win_policy="allow_pass",
        )

        tag_changed = state.enter_water(0)

        self.assertFalse(tag_changed)
        self.assertTrue(player.water)
        self.assertTrue(player.qualification_alive)
        self.assertTrue(player.declared_ready)
        self.assertTrue(player.ready_locked)
        self.assertIn("declared_ready", player.tag_list)

    def test_qualified_ready_default_follows_secret_registration_rules(self):
        state = TaiwanGameState.__new__(TaiwanGameState)
        player = DummyPlayer(0)
        player.qualification_alive = True
        player.earthly_ready = True
        state.player_list = [player]
        state.rules = TaiwanRules(
            qualified_ready_win_policy="follow_declared_ready_policy",
        )

        tag_changed = state.enter_water(0)

        self.assertFalse(tag_changed)
        self.assertTrue(player.water)
        self.assertFalse(player.qualification_alive)
        self.assertTrue(player.earthly_ready)

    def test_qualified_ready_pass_policy_can_revoke_only_earthly_ready(self):
        state = TaiwanGameState.__new__(TaiwanGameState)
        player = DummyPlayer(0)
        player.qualification_alive = True
        player.earthly_ready = True
        player.declared_ready = True
        player.ready_locked = True
        player.tag_list.append("declared_ready")
        state.player_list = [player]
        state.rules = TaiwanRules(
            public_ready_enabled=True,
            declared_ready_win_policy="allow_pass",
            qualified_ready_win_policy="lose_earthly_on_pass",
        )

        tag_changed = state.enter_water(0)

        self.assertFalse(tag_changed)
        self.assertTrue(player.water)
        self.assertFalse(player.qualification_alive)
        self.assertFalse(player.earthly_ready)
        self.assertTrue(player.declared_ready)
        self.assertTrue(player.ready_locked)

        player.qualification_alive = True
        player.heavenly_ready = True
        player.earthly_ready = False
        state.enter_water(0)
        self.assertTrue(player.qualification_alive)
        self.assertTrue(player.heavenly_ready)

    def test_invalid_rules_melds_and_payment_seats_fail_closed(self):
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"missed_win_blocks_self_draw": "false"})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"base_points": True})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"typo_rule": 1})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"all_chows_definition": "invalid"})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"human_win_definition": "preset"})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"ready_qualification_mode": "preset"})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"claim_wall_reserve": 4})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"full_flush_liability_enabled": 1})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"all_flower_tiles_enabled": "true"})
        with self.assertRaisesRegex(ValueError, "flower_scoring_mode"):
            TaiwanRules.from_dict({"flower_scoring_mode": "all_flowers"})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"qualified_ready_win_policy": 1})
        with self.assertRaises(ValueError):
            TaiwanRules.from_dict({"opening_flower_replacement_order": "simultaneous"})
        with self.assertRaises(ValueError):
            parse_meld_code("s11")
        with self.assertRaises(ValueError):
            parse_meld_code("g51")
        with self.assertRaises(ValueError):
            settle_win(
                winner=1,
                hand_tai=0,
                win_source="discard",
                dealer=0,
                dealer_streak=0,
                discarder=9,
            )


class TaiwanActionAndRoomTest(unittest.TestCase):
    @staticmethod
    def _minimum_tai_state(*, open_cuohe: bool):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(minimum_tai=2)
        state.rules_dict = asdict(state.rules)
        state.hepai_limit = 2
        state.open_cuohe = open_cuohe
        state.current_round = 1
        state.current_player_index = 0
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.opening_dealer_action = False
        state.table_claim_or_kong = False
        state.supplement_win_allowed = True
        state.calculation_service = TaiwanDetailCalculation()
        state.can_take_wall_tile = lambda: True
        state.can_establish_kong = lambda: False
        state.ready_candidate_cuts = lambda _index: {}
        state.result_dict = {}
        state.player_list = [DummyPlayer(i) for i in range(4)]
        winner = state.player_list[0]
        winner.hand_tiles = [
            21, 22, 23,
            24, 25, 26,
            31, 32, 33,
            34, 35, 36,
            45, 45,
        ]
        winner.combination_tiles = ["k11"]
        winner.last_drawn_tile = 21
        return state

    @staticmethod
    def _seven_flower_minimum_state(*, open_cuohe: bool):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            minimum_tai=3,
            fan_tai_overrides={"seven_flowers_steal_eighth": 1},
        )
        state.rules_dict = asdict(state.rules)
        state.hepai_limit = 3
        state.open_cuohe = open_cuohe
        state.current_round = 1
        state.current_player_index = 1
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.opening_dealer_action = False
        state.table_claim_or_kong = False
        state.supplement_win_allowed = True
        state.calculation_service = TaiwanDetailCalculation()
        state.game_status = "playing"
        state.action_dict = {0: [], 1: [], 2: [], 3: []}
        state.pending_winners = []
        state.pending_cuohe = None
        state.jiagang_tile = None
        state.tiles_list = [22] * 16 + [21]
        state.dead_wall_count = 16
        state.replacement_wall_remaining = 16
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[0].hand_tiles = [11] * 4
        state.player_list[0].huapai_list = list(FLOWER_TILES[:-1])
        state.prepare_action_window = Mock()
        state.wait_action = AsyncMock(return_value=True)

        candidate = state._seven_flowers_steal_eighth_candidate(
            1,
            FLOWER_TILES[-1],
        )
        state._publish_flower(1, FLOWER_TILES[-1])
        return state, candidate

    @staticmethod
    def _discard_response_state(mode: str, details: dict):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(minimum_tai=1, multi_win_mode=mode)
        state.hepai_limit = 1
        state.open_cuohe = True
        state.current_player_index = 0
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[0].discard_tiles = [23]
        state.result_dict = details
        state.pending_winners = []
        state.pending_cuohe = None
        state.jiagang_tile = None
        state.pending_four_winds_abort = False
        state.action_priority = {}
        state.enter_water = Mock(return_value=False)
        return state

    @staticmethod
    def _cuohe_resolution_state(pending_cuohe: dict, *, cuohe_type: int = 0):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(minimum_tai=1)
        state.rules_dict = asdict(state.rules)
        state.hepai_limit = 1
        state.open_cuohe = True
        state.cuohe_type = cuohe_type
        state.current_round = 1
        state.round_index = 1
        state.current_player_index = 0
        state.pending_cuohe = pending_cuohe
        state.pending_winners = []
        state._hand_scores_before = {index: 100 for index in range(4)}
        state.player_list = [DummyPlayer(i, [11 + i]) for i in range(4)]
        for index, player in enumerate(state.player_list):
            player.original_player_index = index
            player.score = 100
            player.score_history = []
            player.round_number_history = []
            player.record_counter = SimpleNamespace(cuohe_times=0)
        state.player_list[0].discard_tiles = [23]
        state.game_record = {
            "game_round": {"round_index_1": {"action_ticks": []}}
        }
        state.player_action_tick = 0
        state.spectator_manager = None
        state.last_draw_was_last = False
        state.supplement_win_allowed = True
        state.pending_four_winds_abort = False
        state.jiagang_tile = None
        state.can_establish_kong = lambda: False
        state.can_take_normal_tile = lambda: True
        state.can_take_wall_tile = lambda: True
        state.score_action_candidate = lambda *_args: None
        state.ready_candidate_cuts = lambda _index: {}
        state.calculation_service = SimpleNamespace(
            Taiwan_tingpai_check=lambda *_args: set(),
        )
        state.broadcast_result = AsyncMock()
        state.run_hu_result_ready_phase = AsyncMock()
        state.broadcast_refresh_player_tag_list = AsyncMock()
        state.finalize_jiagang = AsyncMock()
        return state

    def test_open_cuohe_only_exposes_completed_hand_below_minimum_tai(self):
        disabled = self._minimum_tai_state(open_cuohe=False)
        self.assertIsNone(disabled.score_action_candidate(0, "self_draw"))
        self.assertNotIn("hu_self", check_action_hand_action(disabled, 0)[0])

        enabled = self._minimum_tai_state(open_cuohe=True)
        detail = enabled.score_action_candidate(0, "self_draw")
        self.assertIsNotNone(detail)
        self.assertFalse(detail["is_win"])
        self.assertTrue(detail["below_minimum"])
        self.assertEqual(detail["tai"], 1)
        self.assertIn("最低 2 台", detail["reason"])
        detail["reason"] = "localized diagnostic"
        self.assertTrue(enabled._is_cuohe_detail(detail))
        detail["below_minimum"] = False
        detail["reason"] = "未达到最低 2 台"
        self.assertFalse(enabled._is_cuohe_detail(detail))
        self.assertIn("hu_self", check_action_hand_action(enabled, 0)[0])

        enabled.player_list[0].hand_tiles = [11] * 14
        enabled.player_list[0].combination_tiles = ["k21"]
        enabled.player_list[0].last_drawn_tile = 11
        self.assertIsNone(enabled.score_action_candidate(0, "self_draw"))

    def test_low_tai_cuohe_is_never_forced_and_bot_declines_it(self):
        low_tai = {"is_win": False, "tai": 0, "capped_tai": 0}
        state = make_action_state(
            rules=TaiwanRules(
                minimum_tai=1,
                public_ready_enabled=True,
                declared_ready_win_policy="force_win",
            )
        )
        state.hepai_limit = 1
        state.player_list[0].declared_ready = True
        state.player_list[0].ready_locked = True
        state.player_list[0].hand_tiles = [11, 12, 13]
        state.score_action_candidate = lambda *_args: low_tai

        self.assertEqual(
            check_action_hand_action(state, 0)[0],
            ["hu_self", "cut"],
        )
        state.result_dict = {"hu_self": low_tai}
        self.assertFalse(should_accept_hu(state, 0, "hu_self"))

        async def pass_low_tai():
            discard_state = object.__new__(TaiwanGameState)
            discard_state.rules = TaiwanRules(
                minimum_tai=1,
                declared_ready_win_policy="force_win",
            )
            discard_state.current_player_index = 0
            discard_state.player_list = [DummyPlayer(i) for i in range(4)]
            discard_state.player_list[0].discard_tiles = [23]
            discard_state.player_list[1].declared_ready = True
            discard_state.result_dict = {"hu_first": low_tai}
            discard_state.pending_four_winds_abort = False
            discard_state.action_priority = {}
            discard_state.enter_water = Mock(return_value=True)
            await discard_state.resolve_discard_responses(
                {1: {"action_type": "pass"}},
                {1: ["hu_first", "pass"]},
            )
            return discard_state

        discard_state = asyncio.run(pass_low_tai())
        discard_state.enter_water.assert_not_called()
        self.assertEqual(discard_state.game_status, "deal_card")

    def test_smart_bot_accepts_riichi_han_result(self):
        state = SimpleNamespace(
            hepai_limit=1,
            result_dict={"hu_self": {"han": 3, "fu": 40}},
            player_list=[DummyPlayer(i) for i in range(4)],
        )

        self.assertTrue(should_accept_hu(state, 0, "hu_self"))
        state.hepai_limit = 4
        self.assertFalse(should_accept_hu(state, 0, "hu_self"))

    def test_head_bump_selects_nearest_declaration_before_cuohe_check(self):
        wrong = {
            "is_win": False,
            "below_minimum": True,
            "tai": 0,
            "capped_tai": 0,
        }
        legal = {"is_win": True, "tai": 1, "capped_tai": 1}
        state = self._discard_response_state(
            "head_bump",
            {"hu_first": wrong, "hu_second": legal},
        )

        asyncio.run(state.resolve_discard_responses(
            {
                1: {"action_type": "hu_first"},
                2: {"action_type": "hu_second"},
            },
            {
                1: ["hu_first", "pass"],
                2: ["hu_second", "pass"],
            },
        ))

        self.assertEqual(state.game_status, "check_cuohe")
        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["players"]],
            [1],
        )
        self.assertEqual(state.pending_cuohe["winners"], [])

    def test_multi_win_keeps_legal_and_cuohe_declarations(self):
        wrong = {
            "is_win": False,
            "below_minimum": True,
            "tai": 0,
            "capped_tai": 0,
        }
        legal = {"is_win": True, "tai": 1, "capped_tai": 1}
        state = self._discard_response_state(
            "multiple_winners",
            {"hu_first": wrong, "hu_second": legal},
        )

        asyncio.run(state.resolve_discard_responses(
            {
                1: {"action_type": "hu_first"},
                2: {"action_type": "hu_second"},
            },
            {
                1: ["hu_first", "pass"],
                2: ["hu_second", "pass"],
            },
        ))

        self.assertEqual(state.game_status, "check_cuohe")
        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["players"]],
            [1],
        )
        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["winners"]],
            [2],
        )

    def test_multi_rob_kong_cuohe_is_processed_before_legal_winner(self):
        wrong = {
            "is_win": False,
            "below_minimum": True,
            "tai": 0,
            "capped_tai": 0,
        }
        legal = {"is_win": True, "tai": 1, "capped_tai": 1}
        state = self._discard_response_state(
            "multiple_winners",
            {"hu_first": wrong, "hu_second": legal},
        )
        state.jiagang_tile = 23
        state.finalize_jiagang = AsyncMock()

        asyncio.run(state.resolve_rob_kong_responses(
            {
                1: {"action_type": "hu_first"},
                2: {"action_type": "hu_second"},
            },
            {
                1: ["hu_first", "pass"],
                2: ["hu_second", "pass"],
            },
        ))

        self.assertEqual(state.game_status, "check_cuohe")
        self.assertEqual(state.jiagang_tile, 23)
        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["players"]],
            [1],
        )
        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["winners"]],
            [2],
        )
        state.finalize_jiagang.assert_not_awaited()

        resolution_state = self._cuohe_resolution_state(state.pending_cuohe)
        resolution_state.jiagang_tile = 23
        asyncio.run(resolution_state._resolve_cuohe())
        self.assertEqual(resolution_state.game_status, "END")
        self.assertIsNone(resolution_state.jiagang_tile)
        self.assertEqual(
            [item["index"] for item in resolution_state.pending_winners],
            [2],
        )
        resolution_state.finalize_jiagang.assert_not_awaited()

    def test_hybrid_multi_win_counts_cuohe_declarations(self):
        wrong = {
            "is_win": False,
            "below_minimum": True,
            "tai": 0,
            "capped_tai": 0,
        }
        legal = {"is_win": True, "tai": 1, "capped_tai": 1}
        state = self._discard_response_state(
            "double_head_bump_triple_all",
            {
                "hu_first": wrong,
                "hu_second": legal,
                "hu_third": legal,
            },
        )

        asyncio.run(state.resolve_discard_responses(
            {
                1: {"action_type": "hu_first"},
                2: {"action_type": "hu_second"},
                3: {"action_type": "hu_third"},
            },
            {
                1: ["hu_first", "pass"],
                2: ["hu_second", "pass"],
                3: ["hu_third", "pass"],
            },
        ))

        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["players"]],
            [1],
        )
        self.assertEqual(
            [item["index"] for item in state.pending_cuohe["winners"]],
            [2, 3],
        )

    def test_peida_players_cannot_win_or_claim(self):
        hand_state = make_action_state()
        hand_player = hand_state.player_list[0]
        hand_player.tag_list.append("peida")
        hand_player.hand_tiles = [11, 11, 11, 11, 12]
        hand_state.score_action_candidate = lambda *_args: {"is_win": True}
        hand_state.can_establish_kong = lambda: True
        hand_actions = check_action_hand_action(hand_state, 0)[0]
        self.assertNotIn("hu_self", hand_actions)
        self.assertNotIn("riichi_cut", hand_actions)
        self.assertIn("angang", hand_actions)
        self.assertIn("cut", hand_actions)

        discard_state = make_action_state()
        discard_state.player_list[1].tag_list.append("peida")
        discard_state.player_list[1].hand_tiles = [23, 23, 23, 24, 25]
        discard_state.score_action_candidate = lambda *_args: {"is_win": True}
        self.assertEqual(check_action_after_cut(discard_state, 23)[1], [])
        self.assertEqual(check_action_jiagang(discard_state, 23)[1], [])

    def test_cuohe_penalty_enters_peida_and_resumes_same_hand(self):
        pending = {
            "players": [{
                "index": 0,
                "source": "self_draw",
                "payer": None,
                "liable_payer": None,
                "hu_class": "hu_self",
                "detail": {
                    "is_win": False,
                    "tai": 0,
                    "capped_tai": 0,
                    "fan_names": [],
                    "reason": "diagnostic",
                    "below_minimum": True,
                },
                "tile": 11,
            }],
            "winners": [],
        }
        state = self._cuohe_resolution_state(pending)
        state.player_list[0].last_drawn_tile = 11

        asyncio.run(state._resolve_cuohe())

        self.assertEqual([player.score for player in state.player_list], [70, 110, 110, 110])
        self.assertEqual(state.player_list[0].record_counter.cuohe_times, 1)
        self.assertIn("peida", state.player_list[0].tag_list)
        self.assertEqual(state.game_status, "waiting_hand_action")
        self.assertEqual(state.action_dict[0], ["cut"])
        self.assertIsNone(state.pending_cuohe)
        result = state.broadcast_result.await_args.kwargs
        self.assertEqual(result["hu_fan"], ["错和"])
        self.assertEqual(result["score_changes"], {0: -30, 1: 10, 2: 10, 3: 10})
        self.assertEqual(result["next_status"], "round_continue")
        self.assertEqual(
            state.game_record["game_round"]["round_index_1"]["action_ticks"][0][0:5],
            ["hu_self", 0, 0, ["错和"], [-30, 10, 10, 10]],
        )

    def test_multi_win_cuohe_penalty_then_settles_legal_winner(self):
        pending = {
            "players": [{
                "index": 1,
                "source": "discard",
                "payer": 0,
                "liable_payer": None,
                "hu_class": "hu_first",
                "detail": {
                    "is_win": False,
                    "below_minimum": True,
                    "tai": 0,
                    "capped_tai": 0,
                    "fan_names": [],
                },
                "tile": 23,
            }],
            "winners": [{
                "index": 2,
                "source": "discard",
                "payer": 0,
                "liable_payer": None,
                "hu_class": "hu_second",
                "detail": {
                    "is_win": True,
                    "tai": 1,
                    "capped_tai": 1,
                    "fan_names": ["门清"],
                },
                "tile": 23,
            }],
        }
        state = self._cuohe_resolution_state(pending)

        asyncio.run(state._resolve_cuohe())

        self.assertEqual([player.score for player in state.player_list], [110, 70, 110, 110])
        self.assertIn("peida", state.player_list[1].tag_list)
        self.assertEqual(state.game_status, "END")
        self.assertEqual([item["index"] for item in state.pending_winners], [2])
        self.assertIsNone(state.pending_cuohe)
        self.assertIsNone(state.jiagang_tile)
        state.finalize_jiagang.assert_not_awaited()

    def test_all_multi_win_cuohe_players_are_penalized_then_hand_continues(self):
        pending = {
            "players": [
                {
                    "index": index,
                    "source": "discard",
                    "payer": 0,
                    "liable_payer": None,
                    "hu_class": f"hu_{name}",
                    "detail": {
                        "is_win": False,
                        "below_minimum": True,
                        "tai": 0,
                        "capped_tai": 0,
                        "fan_names": [],
                    },
                    "tile": 23,
                }
                for index, name in ((1, "first"), (2, "second"))
            ],
            "winners": [],
        }
        state = self._cuohe_resolution_state(pending)

        asyncio.run(state._resolve_cuohe())

        self.assertEqual([player.score for player in state.player_list], [120, 80, 80, 120])
        self.assertEqual(
            [player.record_counter.cuohe_times for player in state.player_list],
            [0, 1, 1, 0],
        )
        self.assertIn("peida", state.player_list[1].tag_list)
        self.assertIn("peida", state.player_list[2].tag_list)
        self.assertEqual(state.game_status, "deal_card")
        self.assertIsNone(state.pending_cuohe)
        self.assertEqual(state.broadcast_result.await_count, 2)
        self.assertEqual(state.run_hu_result_ready_phase.await_count, 2)
        self.assertEqual(
            [
                tick[4]
                for tick in state.game_record["game_round"]["round_index_1"]["action_ticks"]
            ],
            [[10, -30, 10, 10], [10, 10, -30, 10]],
        )

    def test_cuohe_type_one_uses_fixed_offender_penalty(self):
        pending = {
            "players": [{
                "index": 0,
                "source": "self_draw",
                "payer": None,
                "liable_payer": None,
                "hu_class": "hu_self",
                "detail": {
                    "is_win": False,
                    "below_minimum": True,
                    "tai": 0,
                    "capped_tai": 0,
                    "fan_names": [],
                },
                "tile": 11,
            }],
            "winners": [],
        }
        state = self._cuohe_resolution_state(pending, cuohe_type=1)
        state.player_list[0].last_drawn_tile = 11

        asyncio.run(state._resolve_cuohe())

        self.assertEqual([player.score for player in state.player_list], [60, 100, 100, 100])
        self.assertEqual(
            state.broadcast_result.await_args.kwargs["score_changes"],
            {0: -40, 1: 0, 2: 0, 3: 0},
        )

    def test_public_ready_win_policy_controls_refusal_actions(self):
        for policy, may_refuse in (("allow_pass", True), ("force_win", False)):
            with self.subTest(policy=policy):
                rules = TaiwanRules(
                    public_ready_enabled=True,
                    declared_ready_win_policy=policy,
                )

                hand_state = make_action_state(rules=rules)
                hand_player = hand_state.player_list[0]
                hand_player.hand_tiles = [11, 12, 13]
                hand_player.declared_ready = True
                hand_player.ready_locked = True
                hand_state.score_candidate = lambda *_args, **_kwargs: {"is_win": True}
                hand_actions = check_action_hand_action(hand_state, 0)[0]
                self.assertEqual(hand_actions, ["hu_self", "cut"] if may_refuse else ["hu_self"])

                discard_state = make_action_state(rules=rules)
                discard_player = discard_state.player_list[1]
                discard_player.declared_ready = True
                discard_player.ready_locked = True
                discard_state.score_candidate = lambda index, *_args, **_kwargs: (
                    {"is_win": True} if index == 1 else None
                )
                discard_actions = check_action_after_cut(discard_state, 23)[1]
                self.assertEqual(
                    discard_actions,
                    ["hu_first", "pass"] if may_refuse else ["hu_first"],
                )

                rob_state = make_action_state(rules=rules)
                rob_player = rob_state.player_list[1]
                rob_player.declared_ready = True
                rob_player.ready_locked = True
                rob_state.score_candidate = lambda index, *_args, **_kwargs: (
                    {"is_win": True} if index == 1 else None
                )
                rob_actions = check_action_jiagang(rob_state, 23)[1]
                self.assertEqual(
                    rob_actions,
                    ["hu_first", "pass"] if may_refuse else ["hu_first"],
                )

    def test_qualified_ready_win_policy_is_explicitly_configured(self):
        state = make_action_state(
            rules=TaiwanRules(
                scoring_preset="star31",
                public_ready_enabled=True,
                declared_ready_win_policy="allow_pass",
            )
        )
        player = state.player_list[0]
        player.hand_tiles = [11, 12, 13]
        player.earthly_ready = True
        player.qualification_alive = True
        state.score_candidate = lambda *_args, **_kwargs: {"is_win": True}

        self.assertEqual(
            check_action_hand_action(state, 0)[0],
            ["hu_self", "cut"],
        )
        state.rules = TaiwanRules(
            public_ready_enabled=True,
            declared_ready_win_policy="allow_pass",
            qualified_ready_win_policy="force_win",
        )
        self.assertEqual(check_action_hand_action(state, 0)[0], ["hu_self"])

        player.earthly_ready = False
        player.qualification_alive = False
        self.assertEqual(check_action_hand_action(state, 0)[0], ["hu_self", "cut"])

    def test_forced_public_ready_timeout_auto_accepts_self_draw(self):
        async def run_case():
            players = [DummyPlayer(i) for i in range(4)]
            players[0].declared_ready = True
            players[0].ready_locked = True
            players[0].remaining_time = 0
            accepted = []
            state = SimpleNamespace(
                action_dict={0: ["hu_self"], 1: [], 2: [], 3: []},
                waiting_players_list=[],
                action_events=[asyncio.Event() for _ in range(4)],
                action_queues=[asyncio.Queue() for _ in range(4)],
                game_status="waiting_hand_action",
                step_time=0,
                player_list=players,
                current_player_index=0,
                rules=TaiwanRules(
                    public_ready_enabled=True,
                    declared_ready_win_policy="force_win",
                ),
                has_normal_self_draw=lambda _index: True,
                accept_self_draw=lambda index: accepted.append(index),
                enter_water=lambda *_args: self.fail("禁止拒胡时不应进入过水"),
                broadcast_refresh_player_tag_list=AsyncMock(),
            )
            await wait_action(state)
            return accepted

        self.assertEqual(asyncio.run(run_case()), [0])

    def test_forced_public_ready_missing_response_wins_discard_and_rob_kong(self):
        async def run_discard_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules(declared_ready_win_policy="force_win")
            state.current_player_index = 0
            state.player_list = [DummyPlayer(i) for i in range(4)]
            state.player_list[0].discard_tiles = [23]
            state.player_list[1].declared_ready = True
            state.result_dict = {"hu_first": {"is_win": True}}
            state._liability_payer_for_win = lambda *_args: None
            state.game_status = "waiting_action_after_cut"
            await state.resolve_discard_responses({}, {1: ["hu_first"]})
            return state

        async def run_rob_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules(declared_ready_win_policy="force_win")
            state.current_player_index = 0
            state.player_list = [DummyPlayer(i) for i in range(4)]
            state.player_list[1].declared_ready = True
            declarer = state.player_list[0]
            # 模拟 execute_jiagang 已进入抢杠窗口：牌面暂时是加杠，
            # 但 _pending_jiagang 保存着可被抢杠撤销的原始碰牌。
            declarer.hand_tiles = []
            declarer.combination_tiles = ["g23"]
            declarer.combination_mask = [[3, 23, 1, 23, 0, 23, 0, 23]]
            state._pending_jiagang = {
                "player_index": 0,
                "hand_tiles": [23],
                "combination_tiles": ["k23"],
                "combination_mask": [[1, 23, 0, 23, 0, 23]],
                "has_draw_slot": False,
                "last_drawn_tile": None,
                "water": False,
                "qualification_alive": False,
                "qualification_ever": False,
                "heavenly_ready": False,
                "earthly_ready": False,
                "declared_ready": False,
                "ready_locked": False,
                "tag_list": [],
                "table_claim_or_kong": False,
                "jiagang_tile": None,
                "is_mo_gang": False,
                "normal": 23,
            }
            state.table_claim_or_kong = True
            state.jiagang_tile = 23
            state.result_dict = {"hu_first": {"is_win": True}}
            state._liability_payer_for_win = lambda *_args: None
            state.game_status = "waiting_action_qianggang"
            await state.resolve_rob_kong_responses({}, {1: ["hu_first"]})
            return state

        discard_state = asyncio.run(run_discard_case())
        self.assertEqual(discard_state.game_status, "END")
        self.assertEqual(discard_state.pending_winners[0]["index"], 1)
        self.assertEqual(discard_state.pending_winners[0]["source"], "discard")

        rob_state = asyncio.run(run_rob_case())
        self.assertEqual(rob_state.game_status, "END")
        self.assertEqual(rob_state.pending_winners[0]["index"], 1)
        self.assertEqual(rob_state.pending_winners[0]["source"], "robbing_kong")
        self.assertIsNone(rob_state.jiagang_tile)
        self.assertFalse(rob_state.table_claim_or_kong)
        self.assertIsNone(rob_state._pending_jiagang)
        self.assertEqual(rob_state.player_list[0].hand_tiles, [])
        self.assertEqual(rob_state.player_list[0].combination_tiles, ["k23"])
        self.assertEqual(
            rob_state.player_list[0].combination_mask,
            [[1, 23, 0, 23, 0, 23]],
        )

    def test_dealer_continuation_options_and_limit(self):
        state = object.__new__(TaiwanGameState)
        state.pending_winners = []
        state.dealer_streak = 4
        state.rules = TaiwanRules(draw_continues_dealer=False)
        self.assertFalse(TaiwanGameState._dealer_continues(state))

        state.rules = TaiwanRules(dealer_streak_limit=9)
        state.dealer_streak = 8
        self.assertTrue(TaiwanGameState._dealer_continues(state))
        state.dealer_streak = 9
        self.assertFalse(TaiwanGameState._dealer_continues(state))

        state.pending_winners = [{"index": 1}]
        state.rules = TaiwanRules()
        self.assertFalse(TaiwanGameState._dealer_continues(state))
        state.pending_winners = [{"index": 0}, {"index": 2}]
        self.assertTrue(TaiwanGameState._dealer_continues(state))

    def test_real_state_constructor_and_deal_smoke(self):
        room = {
            "room_id": "taiwan-smoke",
            "room_type": "custom",
            "room_rule": "taiwan",
            "sub_rule": "taiwan/standard",
            "player_list": [11, 12, 13, 14],
            "player_settings": {},
            "tips": False,
            "game_round": 1,
            "step_timer": 0,
            "round_timer": 0,
            "random_seed": 123,
            "allow_spectator": False,
            "hepai_limit": 0,
            "detailed_config": None,
        }
        state = TaiwanGameState(
            SimpleNamespace(),
            room,
            TaiwanDetailCalculation(),
            make_db_manager_stub(),
            "smoke-id",
        )
        state.master_seed = 123
        state.round_index = 1

        init_taiwan_tiles(state)

        self.assertEqual(state.room_rule, "taiwan")
        self.assertEqual([len(player.hand_tiles) for player in state.player_list], [17, 16, 16, 16])
        self.assertEqual(state.playable_wall_count(), 63)

    def test_real_state_constructor_is_independent_from_guobiao(self):
        room = {
            "room_id": "taiwan-independent",
            "room_type": "custom",
            "room_rule": "taiwan",
            "sub_rule": "taiwan/standard",
            "player_list": [11, 12, 13, 14],
            "player_settings": {},
            "tips": False,
            "game_round": 1,
            "step_timer": 0,
            "round_timer": 0,
            "random_seed": 123,
            "allow_spectator": False,
            "detailed_config": None,
        }

        state = TaiwanGameState(
            SimpleNamespace(),
            room,
            TaiwanDetailCalculation(),
            SimpleNamespace(),
            "independent-id",
        )

        self.assertEqual(TaiwanGameState.__bases__, (object,))
        self.assertTrue(all(isinstance(player, TaiwanPlayer) for player in state.player_list))
        self.assertTrue(all(hasattr(player, "water") for player in state.player_list))
        self.assertTrue(all(not hasattr(player, "guobiao_rank") for player in state.player_list))
        self.assertFalse(state.tactical_call)
        self.assertFalse(state.claim_protection)

    def test_ready_phase_uses_taiwan_message_type(self):
        async def run_case():
            connection = SimpleNamespace(
                websocket=SimpleNamespace(send_json=AsyncMock()),
            )
            game_server = SimpleNamespace(
                user_id_to_connection={11: connection},
            )
            room = {
                "room_id": "taiwan-ready",
                "room_type": "custom",
                "room_rule": "taiwan",
                "sub_rule": "taiwan/standard",
                "player_list": [11, 0, 2, 3],
                "player_settings": {},
                "tips": False,
                "game_round": 1,
                "step_timer": 0,
                "round_timer": 0,
                "random_seed": 123,
                "allow_spectator": False,
                "detailed_config": None,
            }
            state = TaiwanGameState(
                game_server,
                room,
                TaiwanDetailCalculation(),
                make_db_manager_stub(),
                "ready-id",
            )
            for index, player in enumerate(state.player_list):
                player.player_index = index
                player.original_player_index = index

            async def submit_ready():
                state.action_dict[0] = []

            state.wait_action = submit_ready
            await state.run_hu_result_ready_phase(1)
            return connection

        connection = asyncio.run(run_case())
        payloads = [
            call.args[0]
            for call in connection.websocket.send_json.await_args_list
        ]
        self.assertTrue(payloads)
        self.assertTrue(all(
            payload["type"] == "gamestate/taiwan/ready_status"
            for payload in payloads
        ))

    def test_disconnect_uses_taiwan_lifecycle(self):
        async def run_case():
            players = [DummyPlayer(index) for index in range(2)]
            for user_id, player in zip((11, 12), players):
                player.user_id = user_id
            cleanup = AsyncMock()
            state = object.__new__(TaiwanGameState)
            state.player_list = players
            state.gamestate_id = "disconnect-id"
            state.game_server = SimpleNamespace(
                gamestate_manager=SimpleNamespace(
                    cleanup_game_state_complete=cleanup,
                ),
            )

            with patch(
                "server.gamestate.game_taiwan.TaiwanGameState.broadcast_refresh_player_tag_list",
                new_callable=AsyncMock,
            ) as refresh, patch(
                "server.gamestate.public.offline.schedule_offline_auto_on_disconnect",
            ) as schedule:
                await state.player_disconnect(11)
                await state.player_disconnect(11)
                await state.player_disconnect(12)
                return state, refresh, schedule, cleanup

        state, refresh, schedule, cleanup = asyncio.run(run_case())
        self.assertIn("offline", state.player_list[0].tag_list)
        self.assertIn("offline", state.player_list[1].tag_list)
        self.assertEqual(refresh.await_count, 2)
        self.assertEqual(schedule.call_count, 2)
        cleanup.assert_awaited_once_with(gamestate_id="disconnect-id")

    def test_run_game_loop_wrapper_preserves_lifecycle_contract(self):
        async def run_failure():
            state = object.__new__(TaiwanGameState)
            state.room_id = "failure-room"
            state.gamestate_id = "failure-id"
            state.game_loop_chinese = AsyncMock(side_effect=RuntimeError("boom"))
            state.cleanup_game_state = AsyncMock()
            await state.run_game_loop()
            state.game_loop_chinese.assert_awaited_once()
            state.cleanup_game_state.assert_awaited_once()

        async def run_cancel():
            state = object.__new__(TaiwanGameState)
            state.room_id = "cancel-room"
            state.gamestate_id = "cancel-id"
            state.game_loop_chinese = AsyncMock(side_effect=asyncio.CancelledError())
            state.cleanup_game_state = AsyncMock()
            with self.assertRaises(asyncio.CancelledError):
                await state.run_game_loop()
            state.cleanup_game_state.assert_not_awaited()

        asyncio.run(run_failure())
        asyncio.run(run_cancel())

    def test_cleanup_closes_rule_local_runtime_resources(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            state.room_id = "cleanup-room"
            outbound_task = asyncio.create_task(asyncio.Event().wait())
            game_task = asyncio.create_task(asyncio.Event().wait())
            state._outbound_tails = {0: outbound_task}
            state._outbound_closed = False
            state.game_task = game_task
            state.spectator_manager = SimpleNamespace(cleanup=AsyncMock())

            await state.cleanup_game_state()
            await asyncio.gather(outbound_task, return_exceptions=True)
            return state, outbound_task, game_task

        state, outbound_task, game_task = asyncio.run(run_case())
        self.assertTrue(state._outbound_closed)
        self.assertEqual(state._outbound_tails, {})
        self.assertTrue(outbound_task.cancelled())
        self.assertTrue(game_task.cancelled())
        state.spectator_manager.cleanup.assert_awaited_once()

    def test_eight_pairs_half_non_standard_waits_offer_self_draw_action(self):
        room = {
            "room_id": "eight-pairs-action",
            "room_type": "custom",
            "room_rule": "taiwan",
            "sub_rule": "taiwan/standard",
            "player_list": [11, 12, 13, 14],
            "player_settings": {},
            "tips": False,
            "game_round": 1,
            "step_timer": 0,
            "round_timer": 0,
            "random_seed": 123,
            "allow_spectator": False,
            "hepai_limit": 0,
            "detailed_config": {"eight_and_a_half_pairs_enabled": True},
        }
        state = TaiwanGameState(
            SimpleNamespace(),
            room,
            TaiwanDetailCalculation(),
            make_db_manager_stub(),
            "eight-pairs-action",
        )
        state.master_seed = 123
        state.round_index = 1
        init_taiwan_tiles(state)
        pre_win = [tile for tile in range(21, 29) for _ in range(2)]

        for winning_tile in (23, 26):
            with self.subTest(winning_tile=winning_tile):
                player = state.player_list[0]
                player.hand_tiles = pre_win + [winning_tile]
                player.combination_tiles = []
                player.last_drawn_tile = winning_tile
                player.has_draw_slot = True
                state.current_player_index = 0
                state.result_dict = {}

                actions = check_action_hand_action(state, 0)

                self.assertIn("hu_self", actions[0])
                self.assertEqual(
                    state.result_dict["hu_self"]["special"],
                    "eight_and_a_half_pairs",
                )

    def test_last_flower_replacement_is_marked_as_last_draw(self):
        state = object.__new__(TaiwanGameState)
        state.dead_wall_count = 16
        state.tiles_list = [11] * 16 + [21]
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.last_draw_was_last = False

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.player_action_record_deal"
        ), patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",
            new_callable=AsyncMock,
        ):
            tile = asyncio.run(TaiwanGameState._draw_tail_for_player(state, 1, opening=False))

        self.assertEqual(tile, 21)
        self.assertTrue(state.last_draw_was_last)
        self.assertFalse(state.can_take_wall_tile())

    def test_opening_flower_replacement_does_not_create_a_draw_slot(self):
        state = object.__new__(TaiwanGameState)
        state.dead_wall_count = 16
        state.tiles_list = [11] * 16 + [21]
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.last_draw_was_last = False

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.player_action_record_deal"
        ) as record_deal, patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",
            new_callable=AsyncMock,
        ) as broadcast:
            tile = asyncio.run(TaiwanGameState._draw_tail_for_player(state, 1, opening=True))

        self.assertEqual(tile, 21)
        self.assertFalse(state.player_list[1].has_draw_slot)
        self.assertIsNone(state.player_list[1].last_drawn_tile)
        record_deal.assert_called_once_with(state, 21, "bd", 1)
        self.assertNotIn("merge_deal_tile_into_hand", broadcast.await_args.kwargs)

    def test_drawn_flower_waits_for_buhua_action(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [11, 12, 51])
        player.has_draw_slot = True
        player.last_drawn_tile = 51
        state.player_list = [player]
        state.rules = TaiwanRules()
        state.last_draw_was_last = False
        state.last_draw_after_kong = False
        state.supplement_win_allowed = True
        state.game_status = "playing"

        continued = asyncio.run(state._process_drawn_flowers(0, "normal"))

        self.assertTrue(continued)
        self.assertEqual(player.hand_tiles, [11, 12, 51])
        self.assertEqual(player.huapai_list, [])
        self.assertEqual(check_action_hand_action(state, 0)[0], ["buhua"])

    def test_direct_kong_supplement_self_draw_policy_is_explicit(self):
        for allowed in (False, True):
            with self.subTest(allowed=allowed):
                state = object.__new__(TaiwanGameState)
                player = DummyPlayer(0, [11])
                state.player_list = [player]
                state.rules = TaiwanRules(
                    direct_kong_replacement_win_allowed=allowed,
                )
                state.last_draw_was_last = False
                state.game_status = "playing"

                continued = asyncio.run(
                    state._process_drawn_flowers(0, "direct_kong")
                )

                self.assertTrue(continued)
                self.assertTrue(state.last_draw_after_kong)
                self.assertEqual(state.supplement_win_allowed, allowed)

    def test_execute_buhua_replaces_only_after_action(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [11, 12, 51])
        player.has_draw_slot = True
        player.last_drawn_tile = 51
        state.player_list = [player] + [DummyPlayer(i) for i in range(1, 4)]
        state.rules = TaiwanRules()
        state.dead_wall_count = 16
        state.tiles_list = [11] * 16 + [21]
        state.last_draw_was_last = False
        state.last_draw_after_kong = False
        state.supplement_win_allowed = True
        state.game_status = "waiting_hand_action"
        state._broadcast_flower = AsyncMock()
        state._record_published_flower = Mock()
        state._prepare_hand_action_after_draw = AsyncMock()

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.player_action_record_deal"
        ), patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",
            new_callable=AsyncMock,
        ):
            asyncio.run(state.execute_buhua(0))

        self.assertEqual(player.hand_tiles, [11, 12, 21])
        self.assertEqual(player.huapai_list, [51])
        self.assertEqual(player.last_drawn_tile, 21)
        state._broadcast_flower.assert_awaited_once_with(
            0,
            51,
            is_drawn=True,
            record=False,
        )
        state._record_published_flower.assert_called_once_with(
            0,
            51,
            is_drawn=True,
            special=None,
        )
        state._prepare_hand_action_after_draw.assert_awaited_once()

    def test_opening_buhua_is_asked_before_replacement(self):
        state = object.__new__(TaiwanGameState)
        state.action_dict = {0: [], 1: [], 2: [], 3: []}
        state.prepare_action_window = Mock()
        state.wait_action = AsyncMock(return_value=True)

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_ask_hand_action",
            new_callable=AsyncMock,
        ) as broadcaster:
            asyncio.run(state._request_opening_buhua(2))

        self.assertEqual(state.current_player_index, 2)
        self.assertEqual(state.game_status, "waiting_buhua_round")
        self.assertEqual(state.action_dict[2], ["buhua"])
        state.prepare_action_window.assert_called_once()
        broadcaster.assert_awaited_once_with(state)
        state.wait_action.assert_awaited_once()

    def test_opening_flowers_are_replaced_one_at_a_time(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules(seven_flowers_steal_eighth_enabled=False)
            state.player_list = [
                DummyPlayer(0, [51, 52, 11]),
                DummyPlayer(1, [12]),
                DummyPlayer(2, [13]),
                DummyPlayer(3, [14]),
            ]
            state.game_status = "playing"
            events = []
            replacement_tiles = iter((21, 22))

            async def request(player_index):
                events.append(("ask", player_index))

            async def broadcast(owner_index, tile, **_kwargs):
                events.append(("flower", owner_index, tile))

            async def draw(player_index, *, opening):
                tile = next(replacement_tiles)
                state.player_list[player_index].hand_tiles.append(tile)
                events.append(("draw", player_index, tile, opening))
                return tile

            state._request_opening_buhua = request
            state._broadcast_flower = broadcast
            state._record_published_flower = Mock()
            state._draw_tail_for_player = draw
            state._ask_eight_flowers = AsyncMock()

            await state._opening_flower_replacement()
            return state, events

        state, events = asyncio.run(run_case())
        self.assertEqual(
            events,
            [
                ("ask", 0),
                ("flower", 0, 51),
                ("draw", 0, 21, True),
                ("ask", 0),
                ("flower", 0, 52),
                ("draw", 0, 22, True),
            ],
        )
        self.assertEqual(state.player_list[0].hand_tiles, [11, 21, 22])
        self.assertEqual(state.player_list[0].huapai_list, [51, 52])

    def test_round_robin_opening_flowers_defer_new_flowers_to_the_next_round(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules(
                seven_flowers_steal_eighth_enabled=False,
                opening_flower_replacement_order="round_robin",
            )
            state.player_list = [
                DummyPlayer(0, [51, 52, 11]),
                DummyPlayer(1, [53, 12]),
                DummyPlayer(2, [13]),
                DummyPlayer(3, [14]),
            ]
            state.game_status = "playing"
            events = []
            replacement_tiles = iter((54, 21, 22, 23))

            async def request(player_index):
                events.append(("ask", player_index))

            async def broadcast(owner_index, tile, **_kwargs):
                events.append(("flower", owner_index, tile))

            async def draw(player_index, *, opening):
                tile = next(replacement_tiles)
                state.player_list[player_index].hand_tiles.append(tile)
                events.append(("draw", player_index, tile, opening))
                return tile

            state._request_opening_buhua = request
            state._broadcast_flower = broadcast
            state._record_published_flower = Mock()
            state._draw_tail_for_player = draw
            state._ask_eight_flowers = AsyncMock()

            await state._opening_flower_replacement()
            return state, events

        state, events = asyncio.run(run_case())
        self.assertEqual(
            [event for event in events if event[0] == "ask"],
            [("ask", 0), ("ask", 0), ("ask", 1), ("ask", 0)],
        )
        self.assertEqual(state.player_list[0].hand_tiles, [11, 21, 23])
        self.assertEqual(state.player_list[0].huapai_list, [51, 52, 54])
        self.assertEqual(state.player_list[1].hand_tiles, [12, 22])
        self.assertEqual(state.player_list[1].huapai_list, [53])

    def test_seven_flowers_steal_eighth_transfers_authoritative_flower_ownership(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.player_list = [DummyPlayer(i) for i in range(4)]

        state.player_list[0].huapai_list = list(FLOWER_TILES[:-1])
        special = TaiwanGameState._seven_flowers_steal_eighth_candidate(
            state,
            1,
            FLOWER_TILES[-1],
        )
        self.assertEqual(state.player_list[1].huapai_list, [])
        TaiwanGameState._publish_flower(state, 1, FLOWER_TILES[-1])
        self.assertEqual(state.player_list[1].huapai_list, [FLOWER_TILES[-1]])
        recipient = TaiwanGameState._transfer_flower_win(state, special)
        self.assertEqual(recipient, 0)
        self.assertEqual(special["mode"], "seven_then_last")
        self.assertEqual(set(state.player_list[0].huapai_list), set(FLOWER_TILES))
        self.assertEqual(state.player_list[1].huapai_list, [])

        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[0].huapai_list = list(FLOWER_TILES[:6])
        state.player_list[1].huapai_list = [FLOWER_TILES[6]]
        special = TaiwanGameState._seven_flowers_steal_eighth_candidate(
            state,
            0,
            FLOWER_TILES[7],
        )
        self.assertEqual(state.player_list[1].huapai_list, [FLOWER_TILES[6]])
        TaiwanGameState._publish_flower(state, 0, FLOWER_TILES[7])
        recipient = TaiwanGameState._transfer_flower_win(state, special)
        self.assertEqual(recipient, 0)
        self.assertEqual(special["mode"], "six_plus_one")
        self.assertEqual(set(state.player_list[0].huapai_list), set(FLOWER_TILES))
        self.assertEqual(state.player_list[1].huapai_list, [])

    def test_seven_flowers_steal_eighth_can_be_declined_without_transferring_flower(self):
        players = [DummyPlayer(i) for i in range(4)]
        players[0].huapai_list = list(FLOWER_TILES[:-1])
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.player_list = players
        state.current_player_index = 1
        state.action_dict = {0: [], 1: [], 2: [], 3: []}
        state.prepare_action_window = Mock()
        state.wait_action = AsyncMock(return_value=False)
        state._preview_seven_flowers_steal_eighth_detail = Mock(
            return_value={"is_win": True},
        )
        candidate = state._seven_flowers_steal_eighth_candidate(1, FLOWER_TILES[-1])
        state._publish_flower(1, FLOWER_TILES[-1])

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_ask_hand_action",
            new_callable=AsyncMock,
        ) as broadcaster:
            special = asyncio.run(state._ask_seven_flowers_steal_eighth(candidate))

        self.assertIsNone(special)
        self.assertEqual(state.current_player_index, 1)
        self.assertEqual(state.action_dict[0], ["hu_flower", "pass"])
        broadcaster.assert_awaited_once_with(state)
        self.assertEqual(players[0].huapai_list, list(FLOWER_TILES[:-1]))
        self.assertEqual(players[1].huapai_list, [FLOWER_TILES[-1]])

    def test_seven_flowers_steal_eighth_is_asked_after_flower_is_published_before_replacement(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules()
            state.player_list = [DummyPlayer(i) for i in range(4)]
            state.player_list[0].huapai_list = list(FLOWER_TILES[:-1])
            state.player_list[1].hand_tiles = [FLOWER_TILES[-1]]
            events = []

            async def broadcast(owner_index, tile, **kwargs):
                self.assertEqual(owner_index, 1)
                self.assertEqual(tile, FLOWER_TILES[-1])
                self.assertFalse(kwargs["record"])
                self.assertEqual(
                    state.player_list[1].huapai_list,
                    [FLOWER_TILES[-1]],
                )
                events.append("buhua")

            async def ask(candidate, *, opening):
                self.assertEqual(events, ["buhua"])
                self.assertEqual(candidate["mode"], "seven_then_last")
                self.assertFalse(opening)
                events.append("ask")
                return candidate

            def record(*_args, **_kwargs):
                self.assertEqual(set(state.player_list[0].huapai_list), set(FLOWER_TILES))
                self.assertEqual(state.player_list[1].huapai_list, [])
                events.append("record")

            async def broadcast_win(_special):
                self.assertEqual(events, ["buhua", "ask", "record"])
                events.append("hu_flower")

            async def complete(_special, *, opening):
                self.assertFalse(opening)
                events.append("replacement")

            state._broadcast_flower = broadcast
            state._ask_seven_flowers_steal_eighth = ask
            state._record_published_flower = record
            state._broadcast_flower_win = broadcast_win
            state._complete_seven_flowers_steal_eighth = complete

            continued = await state._replace_one_flower(
                1,
                0,
                is_drawn=True,
                opening=False,
            )
            return state, events, continued

        state, events, continued = asyncio.run(run_case())
        self.assertFalse(continued)
        self.assertEqual(
            events,
            ["buhua", "ask", "record", "hu_flower", "replacement"],
        )
        self.assertEqual(set(state.player_list[0].huapai_list), set(FLOWER_TILES))
        self.assertEqual(state.player_list[1].huapai_list, [])

    def test_declined_seven_flowers_steal_eighth_draws_only_after_choice(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules()
            state.player_list = [DummyPlayer(i) for i in range(4)]
            state.player_list[0].huapai_list = list(FLOWER_TILES[:-1])
            state.player_list[1].hand_tiles = [FLOWER_TILES[-1]]
            events = []

            async def broadcast(*_args, **_kwargs):
                events.append("buhua")

            async def ask(candidate, *, opening):
                self.assertEqual(events, ["buhua"])
                self.assertIsNotNone(candidate)
                self.assertFalse(opening)
                events.append("ask")
                return None

            def record(*_args, **kwargs):
                self.assertIsNone(kwargs["special"])
                events.append("record")

            async def draw(player_index, *, opening):
                self.assertEqual(events, ["buhua", "ask", "record"])
                self.assertEqual(player_index, 1)
                self.assertFalse(opening)
                events.append("replacement")
                return 21

            state._broadcast_flower = broadcast
            state._ask_seven_flowers_steal_eighth = ask
            state._record_published_flower = record
            state._broadcast_flower_win = AsyncMock()
            state._complete_seven_flowers_steal_eighth = AsyncMock()
            state._draw_tail_for_player = draw

            continued = await state._replace_one_flower(
                1,
                0,
                is_drawn=False,
                opening=False,
            )
            return state, events, continued

        state, events, continued = asyncio.run(run_case())
        self.assertTrue(continued)
        self.assertEqual(events, ["buhua", "ask", "record", "replacement"])
        self.assertEqual(state.player_list[0].huapai_list, list(FLOWER_TILES[:-1]))
        self.assertEqual(state.player_list[1].huapai_list, [FLOWER_TILES[-1]])

    def test_seven_flower_below_minimum_is_not_offered_without_cuohe(self):
        state, candidate = self._seven_flower_minimum_state(open_cuohe=False)

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_ask_hand_action",
            new_callable=AsyncMock,
        ) as broadcaster:
            special = asyncio.run(
                state._ask_seven_flowers_steal_eighth(candidate)
            )

        self.assertIsNone(special)
        state.wait_action.assert_not_awaited()
        broadcaster.assert_not_awaited()
        self.assertEqual(
            state.player_list[0].huapai_list,
            list(FLOWER_TILES[:-1]),
        )
        self.assertEqual(
            state.player_list[1].huapai_list,
            [FLOWER_TILES[-1]],
        )
        self.assertEqual(state.game_status, "playing")

    def test_seven_flower_below_minimum_enters_cuohe_after_declaration(self):
        state, candidate = self._seven_flower_minimum_state(open_cuohe=True)

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_ask_hand_action",
            new_callable=AsyncMock,
        ):
            special = asyncio.run(
                state._ask_seven_flowers_steal_eighth(candidate)
            )

        self.assertIs(special, candidate)
        state._transfer_flower_win(special)

        async def draw_replacement(player_index, *, opening):
            self.assertEqual(player_index, 0)
            self.assertFalse(opening)
            tile = state.tiles_list.pop()
            state.player_list[player_index].hand_tiles.append(tile)
            state.last_draw_was_last = not state.can_take_normal_tile()
            return tile

        state._draw_tail_for_player = draw_replacement
        asyncio.run(
            state._complete_seven_flowers_steal_eighth(
                special,
                opening=False,
            )
        )

        self.assertEqual(state.game_status, "check_cuohe")
        self.assertEqual(state.pending_winners, [])
        self.assertEqual(state.pending_cuohe["players"][0]["index"], 0)
        detail = state.pending_cuohe["players"][0]["detail"]
        self.assertFalse(detail["is_win"])
        self.assertTrue(detail["below_minimum"])
        self.assertEqual(detail["tai"], 1)

    def test_legal_seven_flower_uses_the_unified_winner_queue(self):
        state, candidate = self._seven_flower_minimum_state(open_cuohe=False)
        state.rules = TaiwanRules()
        state.rules_dict = asdict(state.rules)
        state.hepai_limit = 0

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.broadcast_ask_hand_action",
            new_callable=AsyncMock,
        ):
            special = asyncio.run(
                state._ask_seven_flowers_steal_eighth(candidate)
            )

        self.assertIs(special, candidate)
        state._transfer_flower_win(special)

        async def draw_replacement(player_index, *, opening):
            tile = state.tiles_list.pop()
            state.player_list[player_index].hand_tiles.append(tile)
            state.last_draw_was_last = not state.can_take_normal_tile()
            return tile

        state._draw_tail_for_player = draw_replacement
        asyncio.run(
            state._complete_seven_flowers_steal_eighth(
                special,
                opening=False,
            )
        )

        self.assertEqual(state.game_status, "END")
        self.assertIsNone(state.pending_cuohe)
        self.assertEqual(len(state.pending_winners), 1)
        self.assertTrue(state.pending_winners[0]["detail"]["is_win"])
        self.assertEqual(
            state.pending_winners[0]["detail"]["fan_ids"],
            ["seven_flowers_steal_eighth"],
        )

    def test_seven_flower_minimum_is_applied_after_combining_normal_tai(self):
        state, _ = self._seven_flower_minimum_state(open_cuohe=False)
        normal_detail = {
            "is_win": True,
            "tai": 2,
            "capped_tai": 2,
            "fan_ids": ["normal"],
            "fan_names": ["普通"],
            "fan_detail": [],
            "decomposition": ["normal"],
        }
        flower_detail = {
            "is_win": True,
            "tai": 1,
            "capped_tai": 1,
            "fan_ids": ["seven_flowers_steal_eighth"],
            "fan_names": ["七抢一"],
            "fan_detail": [],
            "decomposition": ["special:seven_flowers_steal_eighth"],
        }

        detail = state._combine_flower_details(
            normal_detail,
            flower_detail,
        )

        self.assertTrue(detail["is_win"])
        self.assertFalse(detail["below_minimum"])
        self.assertEqual(detail["tai"], 3)

    def test_seven_flower_wall_preview_matches_actual_supplement_boundary(self):
        for mode in (
            "fixed_tail_16",
            "kong_expands_tail",
            "fixed_replacement_wall_16",
        ):
            for wall_length in (16, 17, 18, 24):
                with self.subTest(mode=mode, wall_length=wall_length):
                    state = object.__new__(TaiwanGameState)
                    state.rules = TaiwanRules(dead_wall_mode=mode)
                    state.tiles_list = [11] * wall_length
                    state.dead_wall_count = 16
                    state.replacement_wall_remaining = 16

                    preview = (
                        state._supplement_draw_will_exhaust_normal_wall()
                    )
                    state._take_supplement_tile()
                    actual = not state.can_take_normal_tile()

                    self.assertEqual(preview, actual)

    def test_seven_flowers_steal_eighth_rejects_an_impossible_ninth_flower(self):
        state = object.__new__(TaiwanGameState)
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.draw_reason = None
        state.game_status = "playing"
        state._draw_tail_for_player = AsyncMock(return_value=FLOWER_TILES[0])

        asyncio.run(
            state._complete_seven_flowers_steal_eighth(
                {
                    "winner": 0,
                    "payer": 1,
                    "tile": FLOWER_TILES[-1],
                    "mode": "seven_then_last",
                },
                opening=False,
            )
        )

        state._draw_tail_for_player.assert_awaited_once_with(0, opening=False)
        self.assertEqual(state.draw_reason, "invalid_flower_wall")
        self.assertEqual(state.game_status, "END")
        self.assertEqual(state.player_list[0].hand_tiles, [])

    def test_flower_replacement_rejects_a_ninth_flower_in_any_flow(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[0].huapai_list = list(FLOWER_TILES)
        state.tiles_list = [FLOWER_TILES[0]]
        state.dead_wall_count = 0
        state.replacement_wall_remaining = 0

        result = asyncio.run(
            state._draw_tail_for_player(0, opening=False)
        )

        self.assertIsNone(result)
        self.assertEqual(state.draw_reason, "invalid_flower_wall")
        self.assertEqual(state.game_status, "END")
        self.assertEqual(state.player_list[0].hand_tiles, [])

    def test_auto_cut_robot_passes_flower_win_after_window_opens(self):
        async def run_case():
            player = DummyPlayer(0)
            player.username = "auto-cut"
            state = SimpleNamespace(
                player_list=[player],
                server_action_tick=7,
                waiting_players_list=[],
            )
            with patch(
                "server.gamestate.public.ai.auto_cut_ai.get_ai_action",
                new_callable=AsyncMock,
            ) as submit:
                task = asyncio.create_task(
                    auto_cut_action(
                        state,
                        0,
                        ["hu_flower", "pass"],
                        "waiting_flower_choice",
                    )
                )
                await asyncio.sleep(0.02)
                state.waiting_players_list = [0]
                await task
                return submit

        submit = asyncio.run(run_case())
        submit.assert_awaited_once_with(
            ANY,
            0,
            "pass",
            None,
            None,
            None,
            None,
        )

    def test_auto_cut_robot_declines_forced_eight_flowers(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0)
        player.user_id = 0
        player.pending_eight_flowers = True
        state.player_list = [player]
        state.rules = TaiwanRules(eight_flowers_mode="forced_standalone")
        state.game_status = "playing"

        asyncio.run(state._ask_eight_flowers(0))

        self.assertFalse(player.pending_eight_flowers)
        self.assertTrue(player.eight_flowers_declined)
        self.assertEqual(state.game_status, "playing")

    def test_buhua_action_is_dispatched_from_hand_window(self):
        async def run_case():
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            await queues[0].put({"action_type": "buhua"})
            events[0].set()
            state = SimpleNamespace(
                action_dict={0: ["buhua"], 1: [], 2: [], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_hand_action",
                step_time=0,
                player_list=[DummyPlayer(i) for i in range(4)],
                current_player_index=0,
                has_normal_self_draw=lambda _index: False,
                execute_buhua=AsyncMock(),
            )
            await wait_action(state)
            return state.execute_buhua

        execute_buhua = asyncio.run(run_case())
        execute_buhua.assert_awaited_once_with(0)

    def test_buhua_timeout_executes_required_action(self):
        async def run_case():
            players = [DummyPlayer(i) for i in range(4)]
            players[0].remaining_time = 0
            state = SimpleNamespace(
                action_dict={0: ["buhua"], 1: [], 2: [], 3: []},
                waiting_players_list=[],
                action_events=[asyncio.Event() for _ in range(4)],
                action_queues=[asyncio.Queue() for _ in range(4)],
                game_status="waiting_hand_action",
                step_time=0,
                player_list=players,
                current_player_index=0,
                has_normal_self_draw=lambda _index: False,
                execute_buhua=AsyncMock(),
            )
            await wait_action(state)
            return state.execute_buhua

        execute_buhua = asyncio.run(run_case())
        execute_buhua.assert_awaited_once_with(0)

    def test_seven_flowers_steal_eighth_record_and_flower_win_broadcast_carry_transfer_metadata(self):
        cases = (
            (
                1,
                False,
                {"winner": 0, "payer": 1, "tile": 58, "mode": "seven_then_last"},
                ["bh", 58, 1, "F", 0, 1, 58],
                {"buhua_recipient": 0, "buhua_tile": 58},
            ),
            (
                0,
                True,
                {"winner": 0, "payer": 1, "tile": 58, "stolen": 57, "mode": "six_plus_one"},
                ["bh", 58, 0, "T", 0, 1, 57],
                {"buhua_recipient": 0, "buhua_tile": 57},
            ),
        )
        for owner, is_drawn, special, expected_tick, expected_payload in cases:
            with self.subTest(mode=special["mode"]):
                state = object.__new__(TaiwanGameState)
                state.player_action_tick = 0
                state.round_index = 1
                state.game_record = {
                    "game_round": {"round_index_1": {"action_ticks": []}}
                }
                with patch(
                    "server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",
                    new_callable=AsyncMock,
                ) as broadcaster:
                    TaiwanGameState._record_published_flower(
                        state,
                        owner,
                        58,
                        is_drawn=is_drawn,
                        special=special,
                    )
                    asyncio.run(TaiwanGameState._broadcast_flower_win(state, special))

                self.assertEqual(
                    state.game_record["game_round"]["round_index_1"]["action_ticks"],
                    [expected_tick],
                )
                payload = broadcaster.await_args.kwargs
                self.assertEqual(payload["action_list"], ["hu_flower"])
                self.assertEqual(payload["action_player"], special["winner"])
                self.assertTrue(payload["silent"])
                for key, value in expected_payload.items():
                    self.assertEqual(payload[key], value)

    def test_upper_discard_cannot_be_directly_konged(self):
        state = make_action_state()
        state.player_list[1].hand_tiles = [11, 11, 11, 25]
        state.player_list[2].hand_tiles = [11, 11, 11, 26]

        actions = check_action_after_cut(state, 11)

        self.assertIn("peng", actions[1])
        self.assertNotIn("gang", actions[1])
        self.assertIn("peng", actions[2])
        self.assertIn("gang", actions[2])

    def test_claim_wall_reserve_is_explicitly_configured(self):
        disabled_rules = TaiwanRules()
        enabled_rules = TaiwanRules(claim_wall_reserve=True)
        self.assertEqual(disabled_rules.required_claim_wall_reserve, 0)
        self.assertEqual(enabled_rules.required_claim_wall_reserve, 4)
        self.assertNotIn("required_claim_wall_reserve", asdict(enabled_rules))

        preset_only = make_action_state(
            rules=TaiwanRules(
                scoring_preset="cml",
                allow_kong_from_upper_discard=True,
            )
        )
        preset_only.player_list[1].hand_tiles = [12, 13, 25]
        preset_only.player_list[2].hand_tiles = [11, 11, 11, 26]
        preset_only.playable_wall_count = lambda: 3
        preset_actions = check_action_after_cut(preset_only, 11)
        self.assertIn("chi_right", preset_actions[1])
        self.assertIn("peng", preset_actions[2])

        state = make_action_state(
            rules=TaiwanRules(
                allow_kong_from_upper_discard=True,
                claim_wall_reserve=True,
                dead_wall_mode="kong_expands_tail",
            )
        )
        state.player_list[1].hand_tiles = [12, 13, 25]
        state.player_list[2].hand_tiles = [11, 11, 11, 26]

        state.playable_wall_count = lambda: 3
        actions = check_action_after_cut(state, 11)
        self.assertNotIn("chi_left", actions[1])
        self.assertNotIn("peng", actions[2])

        state.playable_wall_count = lambda: 4
        actions = check_action_after_cut(state, 11)
        self.assertIn("chi_right", actions[1])
        self.assertIn("peng", actions[2])
        self.assertNotIn("gang", actions[2])

        state.playable_wall_count = lambda: 5
        # ``kong_expands_tail`` consumes two playable units when a replacement is
        # available, so five tiles cannot both preserve the four-tile reserve
        # and complete the kong.
        self.assertNotIn("gang", check_action_after_cut(state, 11)[2])
        state.playable_wall_count = lambda: 6
        self.assertIn("gang", check_action_after_cut(state, 11)[2])

        state.player_list[0].hand_tiles = [21, 21, 21, 21, 25]
        state.rules = TaiwanRules(
            claim_wall_reserve=True,
            dead_wall_mode="kong_expands_tail",
        )
        state.playable_wall_count = lambda: 5
        self.assertNotIn("angang", check_action_hand_action(state, 0)[0])
        state.playable_wall_count = lambda: 6
        self.assertIn("angang", check_action_hand_action(state, 0)[0])

    def test_same_round_claim_limit_is_explicitly_configured(self):
        preset_only = make_action_state(
            rules=TaiwanRules(scoring_preset="cml")
        )
        preset_only.playable_wall_count = lambda: 20
        preset_only.player_list[1].hand_tiles = [12, 13, 25]
        preset_only.player_list[1].discard_tiles = [14]
        preset_only.player_list[2].hand_tiles = [11, 11, 26]
        preset_only.player_list[2].discard_tiles = [11]
        preset_actions = check_action_after_cut(preset_only, 11)
        self.assertIn("chi_right", preset_actions[1])
        self.assertIn("peng", preset_actions[2])

        state = make_action_state(
            rules=TaiwanRules(same_turn_claim_forbidden=True)
        )
        state.playable_wall_count = lambda: 20
        state.player_list[1].hand_tiles = [12, 13, 25]
        state.player_list[1].discard_tiles = [14]
        state.player_list[2].hand_tiles = [11, 11, 26]
        state.player_list[2].discard_tiles = [11]

        actions = check_action_after_cut(state, 11)

        self.assertNotIn("chi_right", actions[1])
        self.assertNotIn("peng", actions[2])

        state.player_list[1].discard_tiles = [21]
        state.player_list[2].discard_tiles = [21]
        actions = check_action_after_cut(state, 11)
        self.assertIn("chi_right", actions[1])
        self.assertIn("peng", actions[2])

        state.player_list[1].discard_tiles = []
        state.player_list[1].discard_origin_tiles = [14]
        state.player_list[1].last_discarded_tile = 14
        state.player_list[2].discard_tiles = []
        state.player_list[2].discard_origin_tiles = [11]
        state.player_list[2].last_discarded_tile = 11
        actions = check_action_after_cut(state, 11)
        self.assertNotIn("chi_right", actions[1])
        self.assertNotIn("peng", actions[2])

    def test_terminal_flower_follows_the_actual_supplement_wall_boundary(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [11, 12, 51])
        state.player_list = [player]
        state.rules = TaiwanRules()
        state.dead_wall_count = 16
        state.tiles_list = [21] * 16
        state.last_draw_was_last = True
        state.game_status = "playing"
        state._broadcast_flower = AsyncMock()

        continued = asyncio.run(state._process_drawn_flowers(0, "normal"))

        self.assertFalse(continued)
        self.assertEqual(player.hand_tiles, [11, 12])
        self.assertEqual(player.huapai_list, [51])
        self.assertEqual(state.draw_reason, "terminal_flower")
        self.assertEqual(state.game_status, "END")
        state._broadcast_flower.assert_awaited_once_with(
            0,
            51,
            is_drawn=True,
        )

        replacement_state = object.__new__(TaiwanGameState)
        replacement_player = DummyPlayer(0, [11, 12, 51])
        replacement_state.player_list = [replacement_player]
        replacement_state.rules = TaiwanRules(dead_wall_mode="fixed_replacement_wall_16")
        replacement_state.dead_wall_count = 16
        replacement_state.replacement_wall_remaining = 16
        replacement_state.tiles_list = [21] * 16
        replacement_state.last_draw_was_last = True
        replacement_state.game_status = "playing"

        self.assertTrue(
            asyncio.run(
                replacement_state._process_drawn_flowers(0, "normal")
            )
        )
        self.assertEqual(replacement_player.hand_tiles, [11, 12, 51])
        self.assertEqual(replacement_state.game_status, "playing")

    def test_last_normal_draw_may_complete_kong_without_replacement(self):
        state = make_action_state(
            rules=TaiwanRules(),
            can_draw=False,
        )
        state.last_draw_was_last = True
        state.player_list[0].hand_tiles = [11, 11, 11, 11, 12]

        self.assertIn("angang", check_action_hand_action(state, 0)[0])

    def test_declared_ready_auto_added_kong_is_explicitly_configured(self):
        state = object.__new__(TaiwanGameState)
        state.current_player_index = 0
        state.player_list = [DummyPlayer(i) for i in range(4)]
        player = state.player_list[0]
        player.hand_tiles = [12]
        player.combination_tiles = ["k12"]
        player.last_drawn_tile = 12
        player.has_draw_slot = True
        player.declared_ready = True
        player.ready_locked = True
        state.last_draw_was_last = False
        state.supplement_win_allowed = True
        state.can_establish_kong = lambda: True
        state.score_candidate = lambda *_args, **_kwargs: None
        state.execute_jiagang = AsyncMock()

        state.rules = TaiwanRules(scoring_preset="shenlaiye")
        self.assertIsNone(state._declared_ready_auto_jiagang_tile(0))
        state.rules = TaiwanRules(declared_ready_auto_added_kong=True)
        asyncio.run(state._prepare_hand_action_after_draw())

        state.execute_jiagang.assert_awaited_once_with(0, 12)

    def test_declared_ready_auto_added_kong_respects_claim_wall_reserve(self):
        state = object.__new__(TaiwanGameState)
        state.current_player_index = 0
        state.player_list = [DummyPlayer(i) for i in range(4)]
        player = state.player_list[0]
        player.hand_tiles = [12, 11]
        player.combination_tiles = ["k11"]
        player.combination_mask = [[1, 11, 0, 11, 0, 11]]
        player.last_drawn_tile = 11
        player.has_draw_slot = True
        player.ready_locked = True
        state.rules = TaiwanRules(
            declared_ready_auto_added_kong=True,
            claim_wall_reserve=True,
            dead_wall_mode="kong_expands_tail",
        )
        state.rules_dict = asdict(state.rules)
        state.dead_wall_count = 16
        state.replacement_wall_remaining = 16
        state.tiles_list = list(range(21))  # playable=5，杠后只剩 3 张
        state.last_draw_was_last = False
        state.result_dict = {}
        state.game_status = "deal_card"

        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.check_action_hand_action",
            return_value={0: [], 1: [], 2: [], 3: []},
        ):
            asyncio.run(state._prepare_hand_action_after_draw())

        self.assertEqual(state.game_status, "waiting_hand_action")
        self.assertEqual(player.hand_tiles, [12, 11])
        self.assertEqual(player.combination_tiles, ["k11"])

        # 补杠后的自动摸牌流程从 deal_card_after_gang 进入；自动动作若
        # 被执行层拒绝，也必须回到询问窗口，不能重复消耗补牌墙。
        state.game_status = "deal_card_after_gang"
        state.execute_jiagang = AsyncMock()
        with patch(
            "server.gamestate.game_taiwan.TaiwanGameState.check_action_hand_action",
            return_value={0: [], 1: [], 2: [], 3: []},
        ):
            asyncio.run(state._prepare_hand_action_after_draw())
        self.assertEqual(state.game_status, "waiting_hand_action")

    def test_automatic_added_kong_keeps_public_ready_lock(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(declared_ready_auto_added_kong=True)
        player = DummyPlayer(0)
        player.declared_ready = True
        player.ready_locked = True
        player.qualification_alive = True
        player.tag_list = ["declared_ready"]
        state.player_list = [player]

        cancelled = state._revoke_qualification(
            0,
            keep_declared=True,
        )

        self.assertFalse(cancelled)
        self.assertFalse(player.qualification_alive)
        self.assertTrue(player.declared_ready)
        self.assertTrue(player.ready_locked)
        self.assertIn("declared_ready", player.tag_list)

    def test_last_discard_allows_only_win_claims(self):
        state = make_action_state(can_draw=False)
        state.player_list[1].hand_tiles = [11, 11, 11, 12, 13]
        state.player_list[2].hand_tiles = [11, 11, 11]
        detail = {"is_win": True, "tai": 0, "capped_tai": 0}
        state.score_candidate = lambda index, source, tile=None: detail if index == 3 else None

        actions = check_action_after_cut(state, 11)

        self.assertEqual(actions[1], [])
        self.assertEqual(actions[2], [])
        self.assertEqual(actions[3], ["hu_third", "pass"])

    def test_optional_abortive_draws_trigger_after_claim_and_rob_windows(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(four_winds_abort=True)
        state.player_list = [DummyPlayer(i) for i in range(4)]
        for player in state.player_list:
            player.discard_count = 1
            player.discard_tiles = [41]
        state.table_claim_or_kong = False
        self.assertTrue(state._is_four_winds_abort())
        state.table_claim_or_kong = True
        self.assertFalse(state._is_four_winds_abort())

        state.rules = TaiwanRules(
            dead_wall_mode="kong_expands_tail",
            four_kongs_abort=True,
        )
        state.dead_wall_count = 16
        state.player_list[0].combination_tiles = ["g11", "G12", "g13", "G14"]
        state.game_status = "waiting_hand_action"
        state.round_index = 1
        state.game_record = {"game_round": {"round_index_1": {}}}
        self.assertTrue(state._on_kong_established())
        self.assertEqual(state.game_status, "END")
        self.assertEqual(state.draw_reason, "four_kongs_abort")
        self.assertEqual(state.dead_wall_count, 16)

    def test_kong_add_one_requires_a_tile_beyond_the_new_dead_wall(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(dead_wall_mode="kong_expands_tail")
        state.dead_wall_count = 16
        state.tiles_list = list(range(17))
        state.player_list = [DummyPlayer(i) for i in range(4)]
        state.player_list[0].hand_tiles = [11, 11, 11, 11, 12]
        state.result_dict = {}
        state.supplement_win_allowed = True
        state.last_draw_was_last = False
        state.score_candidate = lambda *_args, **_kwargs: None

        self.assertFalse(state.can_establish_kong())
        self.assertNotIn("angang", check_action_hand_action(state, 0)[0])

        state.tiles_list.append(99)
        self.assertTrue(state.can_establish_kong())
        self.assertIn("angang", check_action_hand_action(state, 0)[0])

        state.tiles_list.pop()
        state.rules = TaiwanRules(dead_wall_mode="kong_expands_tail", four_kongs_abort=True)
        state.player_list[1].combination_tiles = ["g21", "G22", "g23"]
        self.assertTrue(state.can_establish_kong())

    def test_expected_playable_wall_after_kong_models_each_boundary(self):
        def make_wall_state(mode, playable, *, four_kongs=False):
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules(
                dead_wall_mode=mode,
                four_kongs_abort=four_kongs,
            )
            state.dead_wall_count = 16
            state.replacement_wall_remaining = 16
            state.tiles_list = list(range(16 + playable))
            state.player_list = [DummyPlayer(i) for i in range(4)]
            return state

        # A normal kong in kong_expands_tail consumes the boundary extension and,
        # when possible, one replacement draw as well.
        self.assertEqual(
            make_wall_state("kong_expands_tail", 6).expected_playable_wall_after_kong(),
            4,
        )
        self.assertEqual(
            make_wall_state("kong_expands_tail", 3).expected_playable_wall_after_kong(),
            1,
        )
        # At p=1 the boundary grows but no replacement exists (cost one);
        # at p=0 there is no further wall movement at all.
        self.assertEqual(
            make_wall_state("kong_expands_tail", 1).expected_playable_wall_after_kong(),
            0,
        )
        self.assertEqual(
            make_wall_state("kong_expands_tail", 0).expected_playable_wall_after_kong(),
            0,
        )

        # Fixed and replacement walls consume one only when a replacement can
        # actually be drawn.
        self.assertEqual(
            make_wall_state("fixed_tail_16", 2).expected_playable_wall_after_kong(),
            1,
        )
        self.assertEqual(
            make_wall_state("fixed_tail_16", 0).expected_playable_wall_after_kong(),
            0,
        )
        self.assertEqual(
            make_wall_state("fixed_replacement_wall_16", 2).expected_playable_wall_after_kong(),
            1,
        )
        self.assertEqual(
            make_wall_state("fixed_replacement_wall_16", 0).expected_playable_wall_after_kong(),
            0,
        )

        # The fourth kong aborts before either boundary extension or
        # replacement draw, so the reserve is evaluated against the unchanged
        # playable wall.
        abort_state = make_wall_state(
            "kong_expands_tail",
            4,
            four_kongs=True,
        )
        abort_state.player_list[0].combination_tiles = [
            "g21",
            "G22",
            "g23",
        ]
        self.assertEqual(
            abort_state.expected_playable_wall_after_kong(),
            4,
        )

        multi_state = make_wall_state(
            "kong_expands_tail",
            6,
            four_kongs=True,
        )
        multi_state.player_list[0].combination_tiles = ["g21", "G22"]
        self.assertEqual(
            multi_state.expected_playable_wall_after_kong(additional_kongs=2),
            4,
        )

    def test_kong_add_one_reserve_checks_the_post_kong_wall(self):
        state = make_action_state(
            rules=TaiwanRules(
                dead_wall_mode="kong_expands_tail",
                claim_wall_reserve=True,
                allow_kong_from_upper_discard=True,
            )
        )
        state.player_list[2].hand_tiles = [11, 11, 11, 26]
        state.can_establish_kong = lambda: True

        state.playable_wall_count = lambda: 5
        self.assertNotIn("gang", check_action_after_cut(state, 11)[2])

        state.playable_wall_count = lambda: 6
        self.assertIn("gang", check_action_after_cut(state, 11)[2])

        # 第四杠流局在成立后不摸补牌，刚好保留四张时应放行。
        state.rules = TaiwanRules(
            dead_wall_mode="kong_expands_tail",
            claim_wall_reserve=True,
            allow_kong_from_upper_discard=True,
            four_kongs_abort=True,
        )
        state.player_list[0].combination_tiles = ["g21", "G22", "g23"]
        state.playable_wall_count = lambda: 4
        self.assertIn("gang", check_action_after_cut(state, 11)[2])

    def test_terminal_kong_still_honors_claim_wall_reserve(self):
        state = make_action_state(
            rules=TaiwanRules(
                dead_wall_mode="kong_expands_tail",
                claim_wall_reserve=True,
            ),
            can_draw=False,
        )
        state.last_draw_was_last = True
        state.player_list[0].hand_tiles = [21, 21, 21, 21, 25]
        # 尾牌区扩充后无法保留四张可摸牌，因此不得成立该杠。
        state.playable_wall_count = lambda: 4
        self.assertNotIn("angang", check_action_hand_action(state, 0)[0])
        state.rules = TaiwanRules(dead_wall_mode="kong_expands_tail")
        self.assertIn("angang", check_action_hand_action(state, 0)[0])

        fixed = make_action_state(
            rules=TaiwanRules(
                dead_wall_mode="fixed_tail_16",
            ),
            can_draw=False,
        )
        fixed.last_draw_was_last = True
        fixed.player_list[0].hand_tiles = [21, 21, 21, 21, 25]
        fixed.playable_wall_count = lambda: 0
        # Fixed-tail terminal kong consumes zero when the wall is exhausted.
        self.assertIn("angang", check_action_hand_action(fixed, 0)[0])

    def test_water_switches_independently_control_self_draw_and_claims(self):
        state = make_action_state(rules=TaiwanRules(missed_win_blocks_self_draw=False))
        player = state.player_list[0]
        player.hand_tiles = [11, 12, 13]
        player.water = True
        state.score_candidate = lambda *_args, **_kwargs: {"is_win": True}
        self.assertIn("hu_self", check_action_hand_action(state, 0)[0])

        blocked = make_action_state()
        blocked.player_list[0].hand_tiles = [11, 12, 13]
        blocked.player_list[0].water = True
        blocked.score_candidate = lambda *_args, **_kwargs: {"is_win": True}
        self.assertNotIn("hu_self", check_action_hand_action(blocked, 0)[0])

        claims = make_action_state(rules=TaiwanRules(missed_win_blocks_claims=False))
        claims.player_list[2].hand_tiles = [11, 11, 11, 25]
        claims.player_list[2].water = True
        claim_actions = check_action_after_cut(claims, 11)
        self.assertIn("peng", claim_actions[2])
        self.assertIn("gang", claim_actions[2])

    def test_water_release_uses_legal_score_not_structural_wait_only(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0)
        player.water = True
        state.player_list = [player]
        state.score_candidate = Mock(
            side_effect=[
                {"is_win": False, "below_minimum": True},
                {"is_win": True},
            ]
        )

        self.assertFalse(state._discard_may_win(0, 21))
        self.assertTrue(player.water)
        self.assertTrue(state._discard_may_win(0, 21))
        self.assertTrue(player.water)
        self.assertEqual(
            state.score_candidate.call_args_list[0].kwargs,
            {"include_special": False},
        )

    def test_kong_water_release_checks_the_fourth_tile_in_complete_hand(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [
            11, 11, 11, 11,
            12, 13, 14,
            21, 22, 23,
            24, 25, 26,
            31, 32, 33,
            45,
        ])
        player.water = True
        state.player_list = [player]
        original_hand = list(player.hand_tiles)

        def score_with_explicit_fourth(_index, source, tile, **kwargs):
            self.assertEqual(source, "self_draw")
            self.assertEqual(tile, 11)
            self.assertEqual(len(player.hand_tiles), 17)
            self.assertEqual(kwargs, {"include_special": False})
            return {"is_win": True}

        state.score_candidate = score_with_explicit_fourth
        self.assertTrue(state._kong_fourth_may_win(0, 11))
        self.assertEqual(player.hand_tiles, original_hand)
        self.assertTrue(player.water)

    def test_kong_water_release_uses_real_complete_hand_scoring(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [
            11, 11, 11, 11,
            12, 13, 14,
            15, 16, 17,
            18, 19, 21,
            22, 23,
            45, 45,
        ])
        player.water = True
        state.player_list = [player]
        state.rules = TaiwanRules()
        state.rules_dict = asdict(state.rules)
        state.current_round = 1
        state.current_player_index = 0
        state.table_claim_or_kong = False
        state.opening_dealer_action = False
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.calculation_service = TaiwanDetailCalculation()

        self.assertTrue(state._kong_fourth_may_win(0, 11))
        self.assertTrue(player.water)

    def test_strict_kuikae_and_chi_without_legal_discard(self):
        self.assertEqual(strict_kuikae_forbidden("chi_left", 15, "strict"), {12, 15})
        self.assertEqual(strict_kuikae_forbidden("chi_mid", 15, "strict"), {15})
        self.assertEqual(strict_kuikae_forbidden("chi_right", 15, "strict"), {15, 18})
        self.assertEqual(strict_kuikae_forbidden("chi_left", 15, "none"), set())

        strict_state = make_action_state()
        strict_state.player_list[1].hand_tiles = [12, 12, 13, 14]
        self.assertNotIn("chi_left", check_action_after_cut(strict_state, 15)[1])

        permissive_state = make_action_state(rules=TaiwanRules(chow_discard_restriction_mode="none"))
        permissive_state.player_list[1].hand_tiles = [12, 12, 13, 14]
        self.assertIn("chi_left", check_action_after_cut(permissive_state, 15)[1])

    def test_execute_cut_rechecks_kuikae_server_side(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [12, 15, 21])
        player.kuikae_forbidden_tiles = {12, 15}
        state.player_list = [player]

        asyncio.run(
            TaiwanGameState.execute_cut(
                state,
                0,
                {"TileId": 12, "cutClass": False, "cutIndex": 0},
            )
        )

        self.assertEqual(player.hand_tiles, [12, 15, 21])

    def test_execute_cut_cannot_discard_a_flower(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [FLOWER_TILES[0], 21])
        state.player_list = [player]

        asyncio.run(
            TaiwanGameState.execute_cut(
                state,
                0,
                {"TileId": FLOWER_TILES[0], "cutClass": False, "cutIndex": 0},
            )
        )

        self.assertEqual(player.hand_tiles, [FLOWER_TILES[0], 21])

    def test_execute_kong_rechecks_target_server_side(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0, [11, 11, 11, 11, 12])
        player.combination_tiles = ["k11"]
        state.player_list = [player]

        asyncio.run(TaiwanGameState.execute_angang(state, 0, 12))
        asyncio.run(TaiwanGameState.execute_jiagang(state, 0, 12))

        self.assertEqual(player.hand_tiles, [11, 11, 11, 11, 12])
        self.assertEqual(player.combination_tiles, ["k11"])

    def test_added_kong_updates_snapshot_before_rob_window(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.player_list = [DummyPlayer(i) for i in range(4)]
        player = state.player_list[0]
        player.hand_tiles = [12, 11]
        player.has_draw_slot = True
        player.last_drawn_tile = 11
        player.combination_tiles = ["k11"]
        player.combination_mask = [[1, 11, 0, 11, 0, 11]]
        state._revoke_qualification = lambda *_args, **_kwargs: None
        state.table_claim_or_kong = False
        state.round_index = 1
        state.player_action_tick = 0
        state.game_record = {
            "game_round": {"round_index_1": {"action_ticks": []}}
        }

        actions = {0: [], 1: ["hu_first"], 2: [], 3: []}
        broadcast = AsyncMock()
        with (
            patch(
                "server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",
                new=broadcast,
            ),
            patch(
                "server.gamestate.game_taiwan.TaiwanGameState.check_action_jiagang",
                return_value=actions,
            ),
        ):
            asyncio.run(TaiwanGameState.execute_jiagang(state, 0, 11))

        self.assertEqual(player.hand_tiles, [12])
        self.assertEqual(player.combination_tiles, ["g11"])
        self.assertEqual(
            player.combination_mask,
            [[3, 11, 1, 11, 0, 11, 0, 11]],
        )
        self.assertEqual(state.game_status, "waiting_action_qianggang")
        # 牌谱记录“声明加杠”；只有后续补杠摸牌才表示该杠成立。
        self.assertEqual(
            state.game_record["game_round"]["round_index_1"]["action_ticks"],
            [["jg", 11, "T"]],
        )
        broadcast.assert_awaited_once()

    def test_robbed_added_kong_restores_original_pung_and_hand(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.rules_dict = asdict(state.rules)
        state.current_player_index = 0
        state.player_list = [DummyPlayer(i) for i in range(4)]
        player = state.player_list[0]
        player.hand_tiles = [12, 11]
        player.has_draw_slot = True
        player.last_drawn_tile = 11
        player.combination_tiles = ["k11"]
        player.combination_mask = [[1, 11, 0, 11, 0, 11]]
        state.dead_wall_count = 16
        state.replacement_wall_remaining = 16
        state.tiles_list = list(range(30))
        state.last_draw_was_last = False
        state.table_claim_or_kong = False
        state.jiagang_tile = None
        state.game_status = "waiting_hand_action"
        state.round_index = 1
        state.player_action_tick = 0
        state.server_action_tick = 0
        state.game_record = {
            "game_round": {"round_index_1": {"action_ticks": []}},
        }
        state.result_dict = {"hu_first": {"is_win": True}}
        state._liability_payer_for_win = lambda *_args: None

        with (
            patch(
                "server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action",
                new=AsyncMock(),
            ),
            patch(
                "server.gamestate.game_taiwan.TaiwanGameState.check_action_jiagang",
                return_value={0: [], 1: ["hu_first"], 2: [], 3: []},
            ),
        ):
            asyncio.run(TaiwanGameState.execute_jiagang(state, 0, 11))
            self.assertEqual(player.combination_tiles, ["g11"])
            self.assertEqual(
                state.game_record["game_round"]["round_index_1"]["action_ticks"],
                [["jg", 11, "T"]],
            )
            asyncio.run(
                state.resolve_rob_kong_responses(
                    {1: {"action_type": "hu_first"}},
                    {1: ["hu_first"]},
                )
            )

        self.assertEqual(player.hand_tiles, [12])
        self.assertFalse(player.has_draw_slot)
        self.assertIsNone(player.last_drawn_tile)
        self.assertEqual(player.combination_tiles, ["k11"])
        self.assertEqual(
            player.combination_mask,
            [[1, 11, 0, 11, 0, 11]],
        )
        self.assertFalse(state.table_claim_or_kong)
        self.assertIsNone(state._pending_jiagang)
        self.assertEqual(state.game_status, "END")
        self.assertEqual(
            state.game_record["game_round"]["round_index_1"]["action_ticks"],
            [["jg", 11, "T"]],
        )

    def test_robbed_hand_added_kong_preserves_the_other_draw_slot(self):
        state = object.__new__(TaiwanGameState)
        state.player_list = [DummyPlayer(i) for i in range(4)]
        player = state.player_list[0]
        player.hand_tiles = [12]
        player.combination_tiles = ["g11"]
        player.combination_mask = [[3, 11, 1, 11, 0, 11, 0, 11]]
        player.has_draw_slot = False
        player.last_drawn_tile = None
        state.table_claim_or_kong = True
        state.jiagang_tile = 11
        state._pending_jiagang = {
            "player_index": 0,
            "combination_index": 0,
            "hand_tiles": [11, 12],
            "combination_tiles": ["k11"],
            "combination_mask": [[1, 11, 0, 11, 0, 11]],
            "has_draw_slot": True,
            "last_drawn_tile": 12,
            "water": False,
            "qualification_alive": False,
            "qualification_ever": False,
            "heavenly_ready": False,
            "earthly_ready": False,
            "declared_ready": False,
            "ready_locked": False,
            "tag_list": [],
            "table_claim_or_kong": False,
            "jiagang_tile": None,
            "is_mo_gang": False,
            "normal": 11,
            "actual_tile": 11,
        }

        state._rollback_pending_jiagang(consume_robbed_tile=True)

        self.assertEqual(player.hand_tiles, [12])
        self.assertTrue(player.has_draw_slot)
        self.assertEqual(player.last_drawn_tile, 12)
        self.assertEqual(player.combination_tiles, ["k11"])
        self.assertEqual(player.combination_mask, [[1, 11, 0, 11, 0, 11]])
        self.assertFalse(state.table_claim_or_kong)
        self.assertIsNone(state.jiagang_tile)
        self.assertIsNone(state._pending_jiagang)

    def test_recommended_multi_win_mode_is_two_head_three_all(self):
        state = object.__new__(TaiwanGameState)
        state.current_player_index = 0
        state.rules = TaiwanRules()
        two = TaiwanGameState._selected_winners(
            state,
            [(3, "hu_third"), (1, "hu_first")],
        )
        self.assertEqual(two, [(1, "hu_first")])

        three = TaiwanGameState._selected_winners(
            state,
            [(3, "hu_third"), (2, "hu_second"), (1, "hu_first")],
        )
        self.assertEqual(three, [(1, "hu_first"), (2, "hu_second"), (3, "hu_third")])

    def test_optional_eight_flowers_is_a_fixed_separate_flower_win(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.rules_dict = asdict(state.rules)
        state.current_round = 1
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.opening_dealer_action = False
        state.table_claim_or_kong = False
        state.calculation_service = TaiwanDetailCalculation()
        state.can_take_wall_tile = lambda: True
        players = [DummyPlayer(i) for i in range(4)]
        winner = players[1]
        winner.hand_tiles = [
            11, 11, 11,
            12, 12, 12,
            21, 21, 21,
            22, 22, 22,
            31, 31, 31,
            45, 45,
        ]
        winner.last_drawn_tile = 31
        winner.huapai_list = list(FLOWER_TILES)
        winner.pending_eight_flowers = True
        state.player_list = players

        detail = TaiwanGameState.score_candidate(state, 1, "self_draw")

        self.assertIsNotNone(detail)
        self.assertEqual(detail["tai"], 8)
        self.assertEqual(detail["special"], "eight_flowers_and_seasons")
        self.assertEqual(
            detail["fan_ids"],
            ["eight_flowers_and_seasons"],
        )

    def test_special_flower_below_minimum_cannot_end_without_cuohe(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            minimum_tai=3,
            fan_tai_overrides={"eight_flowers_and_seasons": 1},
            eight_flowers_mode="forced_standalone",
        )
        state.rules_dict = asdict(state.rules)
        state.current_round = 1
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.opening_dealer_action = False
        state.table_claim_or_kong = False
        state.calculation_service = TaiwanDetailCalculation()
        state.open_cuohe = False
        state.hepai_limit = 3
        state.game_status = "waiting_hand_action"
        state.result_dict = {}
        players = [DummyPlayer(i) for i in range(4)]
        winner = players[1]
        winner.user_id = 10
        winner.hand_tiles = [
            11, 11, 11,
            12, 12, 12,
            21, 21, 21,
            22, 22, 22,
            31, 31, 31,
            45, 45,
        ]
        winner.last_drawn_tile = 31
        winner.huapai_list = list(FLOWER_TILES)
        winner.pending_eight_flowers = True
        state.player_list = players

        asyncio.run(state._ask_eight_flowers(1))

        self.assertNotEqual(state.game_status, "END")
        self.assertFalse(winner.pending_eight_flowers)
        self.assertTrue(winner.eight_flowers_declined)

    def test_special_flower_below_minimum_can_enter_cuohe_only_after_declaration(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(
            minimum_tai=3,
            fan_tai_overrides={"eight_flowers_and_seasons": 1},
            eight_flowers_mode="forced_standalone",
        )
        state.rules_dict = asdict(state.rules)
        state.current_round = 1
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.opening_dealer_action = False
        state.table_claim_or_kong = False
        state.calculation_service = TaiwanDetailCalculation()
        state.open_cuohe = True
        state.hepai_limit = 3
        state.game_status = "waiting_hand_action"
        state.result_dict = {}
        state._liability_payer_for_win = lambda *_args: None
        players = [DummyPlayer(i) for i in range(4)]
        winner = players[1]
        winner.user_id = 10
        winner.hand_tiles = [
            11, 11, 11,
            12, 12, 12,
            21, 21, 21,
            22, 22, 22,
            31, 31, 31,
            45, 45,
        ]
        winner.last_drawn_tile = 31
        winner.huapai_list = list(FLOWER_TILES)
        winner.pending_eight_flowers = True
        state.player_list = players

        asyncio.run(state._ask_eight_flowers(1))

        self.assertEqual(state.game_status, "check_cuohe")
        self.assertEqual(state.pending_cuohe["players"][0]["index"], 1)

    def test_opening_flower_win_uses_starting_timing_bonus(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules(initial_flower_bonus_enabled=True)
        state.rules_dict = asdict(state.rules)
        state.current_round = 1
        state.last_draw_after_kong = False
        state.last_draw_was_last = False
        state.opening_dealer_action = True
        state.table_claim_or_kong = False
        state.calculation_service = TaiwanDetailCalculation()
        state.can_take_wall_tile = lambda: True
        state.player_list = [DummyPlayer(i) for i in range(4)]
        winner = state.player_list[1]
        winner.hand_tiles = [11] * 16
        winner.huapai_list = list(FLOWER_TILES)

        detail = TaiwanGameState._special_flower_detail(
            state,
            1,
            opening=True,
        )

        self.assertEqual(detail["tai"], 12)
        self.assertEqual(
            detail["fan_ids"],
            ["eight_flowers_and_seasons", "initial_flower_bonus"],
        )

    def test_six_plus_one_uses_split_normal_and_flower_payments(self):
        state = object.__new__(TaiwanGameState)
        state.rules = TaiwanRules()
        state.dealer_streak = 0
        item = {
            "index": 1,
            "source": "seven_flowers_steal_eighth",
            "payer": 2,
            "detail": {"tai": 17},
            "seven_robs_mode": "six_plus_one",
            "flower_detail": {"tai": 8},
            "normal_detail": {"tai": 9},
        }

        settlement = state._settlement_for_winner(item)

        self.assertEqual(settlement.score_changes, {0: -15, 1: 56, 2: -27, 3: -14})
        self.assertEqual(
            [(payment.payer, payment.hand_tai) for payment in settlement.payments],
            [(2, 8), (0, 9), (2, 9), (3, 9)],
        )

    def test_multiple_winners_broadcasts_scores_after_each_winner(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules()
            state.dealer_streak = 0
            state.current_round = 1
            state.max_round = 4
            state.xunmu = 1
            state.round_index = 1
            state.player_action_tick = 0
            state.game_record = {
                "game_round": {
                    "round_index_1": {"action_ticks": []},
                },
            }
            state.run_hu_result_ready_phase = AsyncMock()
            state.player_list = [DummyPlayer(index) for index in range(4)]
            for player in state.player_list:
                player.score = 100
                player.original_player_index = player.player_index
                player.score_history = []
                player.round_number_history = []
                player.record_counter = SimpleNamespace(
                    zimo_times=0,
                    dianhe_times=0,
                    fangchong_times=0,
                    fangchong_score=0,
                    recorded_fans=[],
                    win_score=0,
                    win_turn=0,
                    round_score_total=0,
                )

            def detail(tai):
                return {
                    "tai": tai,
                    "capped_tai": tai,
                    "fan_names": ["平胡"],
                    "fan_detail": [],
                    "decomposition": [],
                }

            state.pending_winners = [
                {
                    "index": 1,
                    "source": "discard",
                    "payer": 0,
                    "liable_payer": None,
                    "tile": 23,
                    "hu_class": "hu_first",
                    "detail": detail(1),
                },
                {
                    "index": 2,
                    "source": "discard",
                    "payer": 0,
                    "liable_payer": None,
                    "tile": 23,
                    "hu_class": "hu_second",
                    "detail": detail(2),
                },
            ]

            score_snapshots = []

            async def capture_result(_state, **kwargs):
                score_snapshots.append(dict(kwargs["player_to_score"]))

            with patch(
                "server.gamestate.game_taiwan.TaiwanGameState.broadcast_result",
                side_effect=capture_result,
            ):
                await state._settle_hand({index: 100 for index in range(4)})
            return state, score_snapshots

        state, score_snapshots = asyncio.run(run_case())
        first = settle_win(
            winner=1,
            hand_tai=1,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=0,
        )
        second = settle_win(
            winner=2,
            hand_tai=2,
            win_source="discard",
            dealer=0,
            dealer_streak=0,
            discarder=0,
        )
        expected_first = {
            index: 100 + first.score_changes[index]
            for index in range(4)
        }
        expected_final = {
            index: expected_first[index] + second.score_changes[index]
            for index in range(4)
        }
        self.assertEqual(score_snapshots, [expected_first, expected_final])
        self.assertEqual(
            {player.player_index: player.score for player in state.player_list},
            expected_final,
        )
        expected_histories = {
            index: [first.score_changes[index], second.score_changes[index]]
            for index in range(4)
        }
        for player in state.player_list:
            self.assertEqual(
                [int(change) for change in player.score_history],
                expected_histories[player.player_index],
            )
            self.assertEqual(player.round_number_history, [state.current_round, state.current_round])

    def test_special_win_records_keep_the_trigger_tile(self):
        async def run_case(source, tile):
            state = object.__new__(TaiwanGameState)
            state.rules = TaiwanRules()
            state.dealer_streak = 0
            state.current_round = 1
            state.max_round = 4
            state.xunmu = 1
            state.round_index = 1
            state.player_action_tick = 0
            state.game_record = {
                "game_round": {
                    "round_index_1": {"action_ticks": []},
                },
            }
            state.run_hu_result_ready_phase = AsyncMock()
            state.player_list = [DummyPlayer(index) for index in range(4)]
            for player in state.player_list:
                player.score = 100
                player.original_player_index = player.player_index
                player.score_history = []
                player.round_number_history = []
                player.record_counter = SimpleNamespace(
                    zimo_times=0,
                    dianhe_times=0,
                    fangchong_times=0,
                    fangchong_score=0,
                    recorded_fans=[],
                    win_score=0,
                    win_turn=0,
                    round_score_total=0,
                )

            state.pending_winners = [{
                "index": 1,
                "source": source,
                "payer": 0,
                "liable_payer": None,
                "tile": tile,
                "hu_class": "hu_first",
                "detail": {
                    "tai": 1,
                    "capped_tai": 1,
                    "fan_names": ["测试"],
                    "fan_detail": [],
                    "decomposition": [],
                },
            }]

            result_broadcast = AsyncMock()
            with patch(
                "server.gamestate.game_taiwan.TaiwanGameState.broadcast_result",
                new=result_broadcast,
            ):
                await state._settle_hand({index: 100 for index in range(4)})
            return (
                state.game_record["game_round"]["round_index_1"]["action_ticks"],
                result_broadcast.await_args.kwargs,
            )

        for source, tile in (("robbing_kong", 23), ("seven_flowers_steal_eighth", FLOWER_TILES[-1])):
            with self.subTest(source=source):
                ticks, result = asyncio.run(run_case(source, tile))
                self.assertEqual(ticks[0][0], "hu_first")
                self.assertEqual(ticks[0][5], tile)
                self.assertEqual(result["hepai_tile"], tile)
                self.assertEqual(
                    result["is_qianggang"],
                    True if source == "robbing_kong" else None,
                )
                self.assertEqual(
                    result["ron_discarder_index"],
                    0 if source == "robbing_kong" else None,
                )

    def test_drawn_eighth_flower_opens_separate_choice_before_normal_actions(self):
        async def run_case():
            state = object.__new__(TaiwanGameState)
            player = DummyPlayer(0, [11])
            player.huapai_list = list(FLOWER_TILES)
            player.last_drawn_tile = 11
            state.player_list = [player] + [DummyPlayer(i) for i in range(1, 4)]
            state.rules = TaiwanRules(eight_flowers_mode="optional_standalone")
            state.current_player_index = 0
            state.game_status = "playing"
            state.result_dict = {}
            state.last_draw_after_kong = False
            state.supplement_win_allowed = True
            state.action_events = [asyncio.Event() for _ in range(4)]
            state.action_queues = [asyncio.Queue() for _ in range(4)]
            state._special_flower_detail = lambda _index, **_kwargs: {
                "is_win": True,
                "tai": 8,
                "special": "eight_flowers_and_seasons",
                "fan_ids": ["eight_flowers_and_seasons"],
            }
            state.wait_action = AsyncMock(return_value=False)

            with patch(
                "server.gamestate.game_taiwan.TaiwanGameState.broadcast_ask_hand_action",
                new=AsyncMock(),
            ):
                continued = await state._process_drawn_flowers(0, "normal")
            return state, continued

        state, continued = asyncio.run(run_case())
        self.assertTrue(continued)
        self.assertEqual(state.game_status, "waiting_flower_choice")
        self.assertEqual(state.action_dict[0], ["hu_flower", "pass"])
        self.assertEqual(state.result_dict["hu_self"]["special"], "eight_flowers_and_seasons")
        state.wait_action.assert_awaited_once()

    def test_flower_choice_returns_hu_flower_or_decline(self):
        async def run_case(action_type):
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            if action_type is not None:
                await queues[0].put({"action_type": action_type})
                events[0].set()
            state = SimpleNamespace(
                action_dict={0: ["hu_flower", "pass"], 1: [], 2: [], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_flower_choice",
                step_time=0,
                player_list=[DummyPlayer(i) for i in range(4)],
                current_player_index=0,
            )
            if action_type is None:
                state.player_list[0].remaining_time = 0
            return await wait_action(state)

        self.assertTrue(asyncio.run(run_case("hu_flower")))
        self.assertFalse(asyncio.run(run_case("pass")))
        self.assertFalse(asyncio.run(run_case(None)))

    def test_immediate_response_is_not_drained_after_broadcast(self):
        async def run_case():
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            await queues[1].put({"action_type": "hu_first"})
            events[1].set()
            state = SimpleNamespace(
                action_dict={0: [], 1: ["hu_first", "pass"], 2: [], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_action_after_cut",
                step_time=0,
                player_list=[DummyPlayer(i) for i in range(4)],
            )
            return await _collect_responses(state)

        responses, allowed = asyncio.run(run_case())
        self.assertEqual(responses[1]["action_type"], "hu_first")
        self.assertEqual(allowed[1], ["hu_first", "pass"])

    def test_invalid_queued_response_does_not_hide_following_valid_response(self):
        async def run_case():
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            await queues[1].put(None)
            await queues[1].put({"action_type": "not_allowed"})
            await queues[1].put({"action_type": "pass"})
            events[1].set()
            state = SimpleNamespace(
                action_dict={0: [], 1: ["pass"], 2: [], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_action_after_cut",
                step_time=0,
                player_list=[DummyPlayer(i) for i in range(4)],
            )
            return await _collect_responses(state)

        responses, _ = asyncio.run(run_case())
        self.assertEqual(responses[1]["action_type"], "pass")

    def test_response_time_is_charged_per_player(self):
        async def run_case():
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            players = [DummyPlayer(i) for i in range(4)]
            players[1].remaining_time = 30
            players[2].remaining_time = 30
            for index in (1, 2):
                await queues[index].put({"action_type": "pass"})
                events[index].set()
            state = SimpleNamespace(
                action_dict={0: [], 1: ["pass"], 2: ["pass"], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_action_after_cut",
                step_time=5,
                player_list=players,
            )
            elapsed = {1: 6.2, 2: 20.9}
            with patch(
                "server.gamestate.game_taiwan.wait_action.get_ask_elapsed",
                side_effect=lambda _state, index: elapsed[index],
            ):
                await _collect_responses(state)
            return players

        players = asyncio.run(run_case())
        self.assertEqual(players[1].remaining_time, 29)
        self.assertEqual(players[2].remaining_time, 15)

    def test_ask_deadline_starts_at_each_seat_delivery(self):
        players = [DummyPlayer(i) for i in range(4)]
        players[1].remaining_time = 10
        players[2].remaining_time = 10
        state = SimpleNamespace(
            player_list=players,
            _ask_delivered_at={1: 100.0, 2: 101.5},
            _ask_broadcast_time=99.0,
        )

        # 第二个座位晚 1.5 秒送达，因此 deadline 也应晚 1.5 秒，
        # 而不是与第一个座位共享广播结束时刻。
        with patch(
            "server.gamestate.game_taiwan.wait_action.time.time",
            return_value=101.7,
        ):
            deadlines = _build_ask_deadlines(state, (1, 2), grace=0.5, started=200.0)

        self.assertAlmostEqual(deadlines[1], 208.8)
        self.assertAlmostEqual(deadlines[2], 210.3)

    def test_response_already_past_delivery_deadline_is_rejected(self):
        async def run_case():
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            players = [DummyPlayer(i) for i in range(4)]
            players[1].remaining_time = 1
            await queues[1].put({"action_type": "pass"})
            events[1].set()
            now_wall = time.time()
            state = SimpleNamespace(
                action_dict={0: [], 1: ["pass"], 2: [], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_action_after_cut",
                step_time=0,
                player_list=players,
                _ask_delivered_at={1: now_wall - 2},
                _ask_broadcast_time=now_wall - 2,
            )
            return await _collect_responses(state)

        responses, allowed = asyncio.run(run_case())
        self.assertEqual(allowed[1], ["pass"])
        self.assertEqual(responses, {})

    def test_done_event_is_rechecked_against_deadline_before_consumption(self):
        async def run_case():
            events = [asyncio.Event() for _ in range(4)]
            queues = [asyncio.Queue() for _ in range(4)]
            players = [DummyPlayer(i) for i in range(4)]
            await queues[1].put({"action_type": "pass"})
            events[1].set()
            deadlines = {}
            state = SimpleNamespace(
                action_dict={0: [], 1: ["pass"], 2: [], 3: []},
                waiting_players_list=[],
                action_events=events,
                action_queues=queues,
                game_status="waiting_action_after_cut",
                step_time=0,
                player_list=players,
            )

            def build_deadlines(current_state, allowed, grace, started):
                deadlines.update(
                    _build_ask_deadlines(current_state, allowed, grace, started)
                )
                return deadlines

            async def wait_and_expire(tasks, timeout, return_when):
                await asyncio.gather(*tasks)
                # Simulate the event becoming ready just before the deadline,
                # followed by a scheduling delay before the consumer handles it.
                deadlines[1] = time.monotonic() - 1
                return set(tasks), set()

            with patch(
                "server.gamestate.game_taiwan.wait_action._build_ask_deadlines",
                side_effect=build_deadlines,
            ), patch(
                "server.gamestate.game_taiwan.wait_action.asyncio.wait",
                new=wait_and_expire,
            ):
                return await _collect_responses(state)

        responses, _ = asyncio.run(run_case())
        self.assertEqual(responses, {})

    def test_taiwan_uses_rule_local_broadcast_module(self):
        self.assertEqual(
            TaiwanGameState.broadcast_game_start.__module__,
            "server.gamestate.game_taiwan.boardcast",
        )
        self.assertEqual(
            TaiwanGameState.broadcast_do_action.__module__,
            "server.gamestate.game_taiwan.boardcast",
        )
        self.assertEqual(
            TaiwanGameState.reconnected_send_pending_ask.__module__,
            "server.gamestate.game_taiwan.boardcast",
        )
        self.assertEqual(
            TaiwanGameState.send_realtime_spectator_snapshot.__module__,
            "server.gamestate.game_taiwan.boardcast",
        )

    def test_hand_ask_broadcast_keeps_private_info_and_delivery_timing(self):
        async def run_case():
            players = [DummyPlayer(i) for i in range(4)]
            connections = {}
            for index, player in enumerate(players):
                player.user_id = 100 + index
                player.username = f"player-{index}"
                connections[player.user_id] = SimpleNamespace(
                    websocket=SimpleNamespace(send_json=AsyncMock())
                )
            state = SimpleNamespace(
                server_action_tick=7,
                player_list=players,
                current_player_index=2,
                action_dict={
                    0: [],
                    1: [],
                    2: ["cut", "riichi_cut"],
                    3: [],
                },
                game_server=SimpleNamespace(user_id_to_connection=connections),
                playable_wall_count=lambda: 36,
                build_private_hand_action_info=lambda index: {
                    "ready_qualification": "earthly",
                    "riichi_candidate_cuts": {11: [12, 13]},
                } if index == 2 else {},
                send_to_realtime_spectators=AsyncMock(),
            )
            await broadcast_ask_hand_action(state)
            return state, connections

        state, connections = asyncio.run(run_case())

        self.assertEqual(state.server_action_tick, 8)
        self.assertEqual(set(state._ask_delivered_at), {0, 1, 2, 3})
        for index in range(4):
            payload = connections[100 + index].websocket.send_json.await_args.args[0]
            info = payload["ask_hand_action_info"]
            self.assertEqual(payload["type"], "gamestate/taiwan/broadcast_hand_action")
            self.assertEqual(info["player_index"], 2)
            self.assertEqual(info["remain_tiles"], 36)
            self.assertEqual(
                info["action_list"],
                ["cut", "riichi_cut"] if index == 2 else [],
            )
            if index == 2:
                self.assertEqual(info["ready_qualification"], "earthly")
                self.assertEqual(info["riichi_candidate_cuts"], {11: [12, 13]})
            else:
                self.assertNotIn("ready_qualification", info)

    def test_qianggang_ask_broadcast_uses_added_kong_tile(self):
        async def run_case():
            players = [DummyPlayer(i) for i in range(4)]
            connections = {}
            for index, player in enumerate(players):
                player.user_id = 100 + index
                player.username = f"player-{index}"
                connections[player.user_id] = SimpleNamespace(
                    websocket=SimpleNamespace(send_json=AsyncMock())
                )
            state = SimpleNamespace(
                server_action_tick=3,
                player_list=players,
                current_player_index=0,
                game_status="waiting_action_qianggang",
                jiagang_tile=35,
                action_dict={0: [], 1: [], 2: ["hu_second", "pass"], 3: []},
                game_server=SimpleNamespace(user_id_to_connection=connections),
                send_to_realtime_spectators=AsyncMock(),
            )
            await broadcast_ask_other_action(state)
            return state, connections

        state, connections = asyncio.run(run_case())

        self.assertEqual(state.server_action_tick, 4)
        self.assertEqual(set(state._ask_delivered_at), {2})
        connections[100].websocket.send_json.assert_not_awaited()
        connections[101].websocket.send_json.assert_not_awaited()
        connections[103].websocket.send_json.assert_not_awaited()
        payload = connections[102].websocket.send_json.await_args.args[0]
        info = payload["ask_other_action_info"]
        self.assertEqual(payload["type"], "gamestate/taiwan/ask_other_action")
        self.assertEqual(info["cut_tile"], 35)
        self.assertEqual(info["player_index"], 2)
        self.assertEqual(info["action_list"], ["hu_second", "pass"])

    def test_flower_win_broadcast_uses_existing_action_fields_only(self):
        async def run_case():
            players = [DummyPlayer(i) for i in range(4)]
            connections = {}
            for index, player in enumerate(players):
                player.user_id = 100 + index
                player.username = f"player-{index}"
                connections[player.user_id] = SimpleNamespace(
                    websocket=SimpleNamespace(send_json=AsyncMock())
                )
            state = SimpleNamespace(
                server_action_tick=10,
                player_list=players,
                game_server=SimpleNamespace(user_id_to_connection=connections),
                build_private_do_action_info=lambda _action, _viewer: {},
                send_to_realtime_spectators=AsyncMock(),
            )
            await broadcast_do_action(
                state,
                action_list=["hu_flower"],
                action_player=0,
                silent=True,
                buhua_tile=57,
                buhua_recipient=0,
            )
            return state, connections

        state, connections = asyncio.run(run_case())

        self.assertEqual(state.server_action_tick, 11)
        for connection in connections.values():
            payload = connection.websocket.send_json.await_args.args[0]
            info = payload["do_action_info"]
            self.assertEqual(payload["type"], "gamestate/taiwan/do_action")
            self.assertEqual(info["action_list"], ["hu_flower"])
            self.assertEqual(info["action_player"], 0)
            self.assertEqual(info["buhua_tile"], 57)
            self.assertEqual(info["buhua_recipient"], 0)
            self.assertTrue(info["silent"])
            self.assertNotIn("buhua_transfer_from", info)
            self.assertNotIn("buhua_transfer_tile", info)
            self.assertNotIn("is_claim", info)

    def test_game_start_records_taiwan_spectator_headers_locally(self):
        state = object.__new__(TaiwanGameState)
        state.room_id = 1
        state.gamestate_id = "taiwan-record-test"
        state.tips = True
        state.current_player_index = 0
        state.server_action_tick = 3
        state.max_round = 4
        state.playable_wall_count = lambda: 20
        state.commitment = 123
        state.salt = "salt"
        state.current_round = 2
        state.round_index = 3
        state.step_time = 5
        state.round_time = 20
        state.room_type = "custom"
        state.room_rule = "taiwan"
        state.sub_rule = "taiwan/standard"
        state.hepai_limit = 0
        state.open_cuohe = False
        state.show_moqie_hint = False
        state.tactical_call = False
        state.claim_protection = False
        state.isPlayerSetRandomSeed = False
        state.rules = TaiwanRules()
        state.rules_dict = asdict(state.rules)
        state.player_list = []

        spectator = SimpleNamespace(game_title={}, round_headers={})
        spectator.record_game_title = lambda: spectator.game_title.update({"rule": "taiwan"})
        spectator.record_round_start = lambda: spectator.round_headers.update({
            state.round_index: {"data": {"current_round": state.current_round}}
        })
        state.spectator_manager = spectator

        asyncio.run(state.broadcast_game_start())

        self.assertEqual(spectator.game_title["detailed_config"], state.rules_dict)
        round_data = spectator.round_headers[state.round_index]["data"]
        self.assertEqual(round_data, {"current_round": state.current_round})

    def test_realtime_spectator_snapshot_is_fully_rule_local(self):
        state = object.__new__(TaiwanGameState)
        state.room_id = 1
        state.gamestate_id = "taiwan-test"
        state.tips = True
        state.current_player_index = 0
        state.server_action_tick = 7
        state.max_round = 8
        state.tiles_list = [11, 12, 13, 14]
        state.dead_wall_count = 2
        state.commitment = 123
        state.salt = "salt"
        state.current_round = 1
        state.step_time = 5
        state.round_time = 20
        state.room_type = "custom"
        state.room_rule = "taiwan"
        state.sub_rule = "taiwan/standard"
        state.hepai_limit = 0
        state.open_cuohe = False
        state.show_moqie_hint = False
        state.tactical_call = False
        state.claim_protection = False
        state.isPlayerSetRandomSeed = False
        state.rules_dict = {"minimum_tai": 0}
        state.player_entry_order = [100, 101, 102, 103]
        state.game_status = "waiting_action_qianggang"
        state.jiagang_tile = 35
        state.action_dict = {0: [], 1: ["hu_first", "pass"], 2: [], 3: []}

        state.player_list = [DummyPlayer(i, [11 + i]) for i in range(4)]
        for index, player in enumerate(state.player_list):
            player.user_id = 100 + index
            player.username = f"player-{index}"
            player.original_player_index = index
            player.score = 25000
            player.title_used = None
            player.profile_used = None
            player.character_used = None
            player.voice_used = None
            player.score_history = []
            player.round_number_history = []

        websocket = SimpleNamespace(send_json=AsyncMock())
        state.game_server = SimpleNamespace(
            user_id_to_connection={999: SimpleNamespace(websocket=websocket)},
        )

        asyncio.run(state.send_realtime_spectator_snapshot(999, 1))

        self.assertEqual(websocket.send_json.await_count, 2)
        snapshot = websocket.send_json.await_args_list[0].args[0]
        self.assertEqual(snapshot["type"], "gamestate/taiwan/game_start")
        self.assertEqual(snapshot["game_info"]["room_rule"], "taiwan")
        self.assertEqual(snapshot["game_info"]["view_player_index"], 1)
        self.assertEqual(
            snapshot["game_info"]["detailed_config"],
            {"minimum_tai": 0},
        )
        self.assertNotIn("hand_tiles", snapshot["game_info"]["players_info"][0])
        self.assertEqual(snapshot["game_info"]["players_info"][1]["hand_tiles"], [12])
        pending_ask = websocket.send_json.await_args_list[1].args[0]
        self.assertEqual(pending_ask["type"], "gamestate/taiwan/ask_other_action")
        self.assertEqual(pending_ask["ask_other_action_info"]["cut_tile"], 35)
        self.assertNotIn("is_qianggang", pending_ask["ask_other_action_info"])

    def test_ready_state_ticks_are_shared_with_delayed_spectators(self):
        state = object.__new__(TaiwanGameState)
        player = DummyPlayer(0)
        player.earthly_ready = True
        player.qualification_alive = True
        state.player_list = [player]
        state.round_index = 1
        state.player_action_tick = 0
        round_data = {"action_ticks": []}
        state.game_record = {"game_round": {"round_index_1": round_data}}
        spectator_ticks = []
        state.spectator_manager = SimpleNamespace(
            enabled=True,
            record_tick=lambda tick: spectator_ticks.append(tick),
        )

        state._record_ready_state(0)
        player.qualification_alive = False
        player.declared_ready = True
        state._record_ready_state(0)
        player.declared_ready = False
        state._record_ready_state(0)

        expected = [
            ["state", "ready", 0, "earthly", "F"],
            ["state", "ready", 0, "public", "T"],
            ["state", "ready", 0, "none", "F"],
        ]
        self.assertEqual(round_data["action_ticks"], expected)
        self.assertEqual(spectator_ticks, expected)
        self.assertEqual(state.player_action_tick, 3)

    def test_water_state_ticks_are_shared_with_delayed_spectators(self):
        state = object.__new__(TaiwanGameState)
        state.player_list = [DummyPlayer(0)]
        state.rules = TaiwanRules()
        state.round_index = 1
        state.player_action_tick = 0
        round_data = {"action_ticks": []}
        state.game_record = {"game_round": {"round_index_1": round_data}}
        spectator_ticks = []
        state.spectator_manager = SimpleNamespace(
            enabled=True,
            record_tick=lambda tick: spectator_ticks.append(tick),
        )

        state.enter_water(0)
        state._clear_water(0)

        expected = [
            ["state", "water", 0, "T"],
            ["state", "water", 0, "F"],
        ]
        self.assertEqual(round_data["action_ticks"], expected)
        self.assertEqual(spectator_ticks, expected)

    def test_round_record_keeps_taiwan_draw_reason(self):
        state = SimpleNamespace(
            game_record={
                "game_round": {"round_index_3": {"action_ticks": []}}
            },
            round_index=3,
            player_action_tick=0,
        )
        TaiwanGameState._record_liuju(state, "four_winds_abort")
        self.assertEqual(
            state.game_record["game_round"]["round_index_3"]["action_ticks"],
            [["liuju", "four_winds_abort"]],
        )

    def test_room_validator_normalizes_defaults_and_rejects_bad_rules(self):
        validated = TaiwanRoomValidator(
            room_name=" 台湾麻将 ",
            game_round=2,
            round_timer=20,
            step_timer=5,
            random_seed=0,
        )
        self.assertEqual(validated.room_name, "台湾麻将")
        self.assertEqual(validated.sub_rule, "taiwan/standard")
        self.assertFalse(validated.open_cuohe)
        self.assertEqual(validated.cuohe_type, 0)
        self.assertEqual(validated.detailed_config["dead_wall_count"], 16)
        self.assertEqual(validated.detailed_config["multi_win_mode"], "double_head_bump_triple_all")
        self.assertEqual(validated.detailed_config["minimum_tai"], 0)
        self.assertFalse(validated.detailed_config["initial_flower_bonus_enabled"])
        self.assertEqual(validated.detailed_config["ready_qualification_mode"], "standard_with_dealer_heavenly_ready")
        self.assertFalse(validated.detailed_config["public_ready_enabled"])
        self.assertEqual(validated.detailed_config["declared_ready_win_policy"], "allow_pass")
        self.assertEqual(
            validated.detailed_config["qualified_ready_win_policy"],
            "follow_declared_ready_policy",
        )
        self.assertEqual(validated.detailed_config["all_chows_definition"], "relaxed")
        self.assertFalse(validated.detailed_config["little_four_winds_add_wind_pungs"])
        self.assertEqual(
            validated.detailed_config["human_win_definition"],
            "before_first_draw",
        )
        self.assertEqual(
            validated.detailed_config["opening_flower_replacement_order"],
            "player_complete",
        )
        self.assertIs(validated.detailed_config["claim_wall_reserve"], False)

        custom = TaiwanRoomValidator(
            room_name="custom",
            game_round=4,
            round_timer=20,
            step_timer=5,
            open_cuohe=True,
            cuohe_type=1,
            detailed_config={
                "draw_continues_dealer": False,
                "draw_increments_streak": False,
                "dealer_streak_limit": 9,
                "negative_score_ends_match": True,
                "dead_wall_mode": "fixed_replacement_wall_16",
                "multi_win_mode": "multiple_winners",
                "chow_discard_restriction_mode": "same_tile",
                "pung_same_tile_discard_forbidden": False,
                "allow_kong_from_upper_discard": True,
                "missed_win_blocks_self_draw": False,
                "missed_win_released_by_kong": False,
                "missed_win_blocks_claims": False,
                "direct_kong_replacement_win_allowed": True,
                "allow_rob_added_kong": False,
                "four_winds_abort": True,
                "four_kongs_abort": True,
                "eight_flowers_mode": "compound",
                "seven_flowers_steal_eighth_enabled": False,
                "initial_flower_bonus_enabled": True,
                "fan_tai_overrides": {
                    "flower_kong": 3,
                    "all_chows": 6,
                },
                "all_flower_tiles_enabled": True,
                "no_flowers_enabled": True,
                "ready_qualification_mode": "each_player_first_discard",
                "public_ready_enabled": True,
                "declared_ready_win_policy": "force_win",
                "qualified_ready_win_policy": "force_win",
                "eight_and_a_half_pairs_enabled": True,
                "four_kongs_enabled": True,
                "five_kongs_enabled": True,
                "scoring_preset": "shenlaiye",
                "all_chows_definition": "strict",
                "little_four_winds_add_wind_pungs": True,
                "all_honors_add_all_pungs": False,
                "prefer_triplet_decomposition_on_discard_win": True,
                "human_win_definition": "discarder_first_discard",
                "earthly_win_allows_open_calls": True,
                "earthly_ready_excludes_concealed_and_declared_ready": True,
                "declared_ready_auto_added_kong": True,
                "opening_flower_replacement_order": "round_robin",
                "claim_wall_reserve": True,
                "same_turn_claim_forbidden": True,
                "half_begging_enabled": True,
                "last_tile_claim_enabled": True,
                "all_wind_pungs_enabled": True,
                "no_flowers_or_honors_enabled": True,
                "melded_kong_enabled": True,
                "concealed_kong_enabled": True,
                "minimum_tai": 3,
                "tai_cap": 24,
                "liability_ron_split_enabled": True,
                "all_pungs_liability_enabled": True,
                "half_flush_liability_enabled": True,
                "full_flush_liability_enabled": True,
                "little_three_dragons_liability_enabled": True,
                "big_three_dragons_liability_enabled": True,
                "little_four_winds_liability_enabled": True,
                "big_four_winds_liability_enabled": True,
                "all_honors_liability_enabled": True,
                "five_kongs_liability_enabled": True,
                "four_kongs_liability_enabled": True,
            },
        )
        self.assertEqual(custom.detailed_config["dead_wall_mode"], "fixed_replacement_wall_16")
        self.assertTrue(custom.open_cuohe)
        self.assertEqual(custom.cuohe_type, 1)
        self.assertEqual(custom.detailed_config["scoring_preset"], "shenlaiye")
        self.assertEqual(
            custom.detailed_config["all_chows_definition"],
            "strict",
        )
        self.assertTrue(custom.detailed_config["prefer_triplet_decomposition_on_discard_win"])
        self.assertEqual(
            custom.detailed_config["human_win_definition"],
            "discarder_first_discard",
        )
        self.assertEqual(
            custom.detailed_config["ready_qualification_mode"],
            "each_player_first_discard",
        )
        self.assertIs(custom.detailed_config["claim_wall_reserve"], True)
        self.assertEqual(
            custom.detailed_config["opening_flower_replacement_order"],
            "round_robin",
        )
        self.assertTrue(custom.detailed_config["initial_flower_bonus_enabled"])
        self.assertTrue(custom.detailed_config["all_flower_tiles_enabled"])
        self.assertTrue(custom.detailed_config["public_ready_enabled"])
        self.assertEqual(custom.detailed_config["declared_ready_win_policy"], "force_win")
        self.assertEqual(custom.detailed_config["qualified_ready_win_policy"], "force_win")
        self.assertTrue(custom.detailed_config["liability_ron_split_enabled"])
        self.assertTrue(custom.detailed_config["all_pungs_liability_enabled"])
        self.assertTrue(custom.detailed_config["half_flush_liability_enabled"])
        self.assertTrue(custom.detailed_config["full_flush_liability_enabled"])
        self.assertTrue(custom.detailed_config["little_three_dragons_liability_enabled"])
        self.assertTrue(custom.detailed_config["big_three_dragons_liability_enabled"])
        self.assertTrue(custom.detailed_config["little_four_winds_liability_enabled"])
        self.assertTrue(custom.detailed_config["big_four_winds_liability_enabled"])
        self.assertTrue(custom.detailed_config["all_honors_liability_enabled"])
        self.assertTrue(custom.detailed_config["five_kongs_liability_enabled"])
        self.assertTrue(custom.detailed_config["four_kongs_liability_enabled"])
        self.assertTrue(custom.detailed_config["four_kongs_enabled"])
        self.assertTrue(custom.detailed_config["five_kongs_enabled"])
        self.assertEqual(custom.detailed_config["tai_cap"], 24)
        self.assertEqual(
            custom.detailed_config["fan_tai_overrides"],
            {"flower_kong": 3, "all_chows": 6},
        )

        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="bad cuohe type",
                game_round=1,
                round_timer=20,
                step_timer=5,
                cuohe_type=2,
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="bad",
                game_round=1,
                round_timer=20,
                step_timer=5,
                detailed_config={"missed_win_blocks_claims": "false"},
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="bad ready policy",
                game_round=1,
                round_timer=20,
                step_timer=5,
                detailed_config={"declared_ready_win_policy": "sometimes"},
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="bad flower bonus",
                game_round=1,
                round_timer=20,
                step_timer=5,
                detailed_config={"initial_flower_bonus_enabled": 1},
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="hidden points",
                game_round=1,
                round_timer=20,
                step_timer=5,
                detailed_config={"base_points": 999999},
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="invalid fan value",
                game_round=1,
                round_timer=20,
                step_timer=5,
                detailed_config={"fan_tai_overrides": {"flower_kong": 999999}},
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="tips and cuohe",
                game_round=1,
                round_timer=20,
                step_timer=5,
                tips=True,
                open_cuohe=True,
            )
        with self.assertRaises(ValidationError):
            TaiwanRoomValidator(
                room_name="unsupported sub-rule",
                game_round=1,
                round_timer=20,
                step_timer=5,
                sub_rule="taiwan/unsupported",
            )

    def test_room_manager_persists_taiwan_cuohe_type(self):
        async def create_room(cuohe_type: int, room_id: str, *, tips: bool = False):
            player = SimpleNamespace(
                user_id=1,
                username="host",
                current_room_id=None,
            )
            game_server = SimpleNamespace(
                players={"connection": player},
                db_manager=SimpleNamespace(
                    get_user_settings=lambda _user_id: {
                        "username": "host",
                        "title_id": 1,
                        "profile_image_id": 1,
                        "character_id": 1,
                        "voice_id": 1,
                    },
                ),
                gamestate_manager=SimpleNamespace(
                    is_user_in_active_game=lambda _user_id: False,
                ),
                match_manager=None,
            )
            manager = RoomManager(game_server)
            manager._generate_room_id = lambda: room_id
            manager._broadcast_room_info = AsyncMock()
            response = await manager.create_Taiwan_room(
                "connection",
                "台湾错和测试",
                1,
                "",
                20,
                5,
                tips,
                0,
                "taiwan/standard",
                False,
                True,
                True,
                cuohe_type,
                {"minimum_tai": 1},
                None,
            )
            return response, manager

        for requested, expected, room_id in ((1, 1, "123456"), (9, 0, "654321")):
            with self.subTest(requested=requested):
                response, manager = asyncio.run(create_room(requested, room_id))
                self.assertTrue(response.success)
                self.assertEqual(response.room_info["cuohe_type"], expected)
                self.assertTrue(response.room_info["open_cuohe"])
                self.assertEqual(manager.rooms[room_id]["cuohe_type"], expected)
                manager._broadcast_room_info.assert_awaited_once_with(room_id)

        response, manager = asyncio.run(
            create_room(0, "112233", tips=True)
        )
        self.assertFalse(response.success)
        self.assertIn("提示与错和不能同时开启", response.message)
        self.assertNotIn("112233", manager.rooms)


if __name__ == "__main__":
    unittest.main()
