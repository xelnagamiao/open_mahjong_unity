"""Unit tests for Riichi High-Performance Bot (user_id=3)."""
import unittest

from server.gamestate.public.ai.guobiao_heuristic_gate import guobiao_heuristic_bot_reject_reason
from server.gamestate.public.ai.riichi_heuristic_bot import (
    RiichiContext,
    RiichiOpponentView,
    choose_claim_plan,
    choose_hand_plan,
    estimate_hand_han,
    indicator_to_dora,
    tile_danger_for_opponent,
)


class RiichiHeuristicBotTests(unittest.TestCase):
    def test_gate_allows_riichi_room(self):
        self.assertIsNone(
            guobiao_heuristic_bot_reject_reason({"room_rule": "riichi", "sub_rule": "riichi/standard"})
        )

    def test_dora_indicator_conversion_4p_and_sanma(self):
        self.assertEqual(indicator_to_dora(19, is_sanma=False), 11)
        self.assertEqual(indicator_to_dora(11, is_sanma=True), 19)
        self.assertEqual(indicator_to_dora(44, is_sanma=False), 41)
        self.assertEqual(indicator_to_dora(47, is_sanma=False), 45)

    def test_betaori_folds_to_genbutsu_against_riichi_on_far_hand(self):
        # 对手已立直，弃牌河含 29（现物）；己方 3 向听，必须 Betaori 切 29 现物而非无筋中张/宝牌
        opp_r = RiichiOpponentView(seat=1, discards=(29, 31, 41), is_riichi=True)
        hand = [14, 15, 25, 29, 34, 36, 38, 42, 43, 45, 46, 47, 11, 22]
        ctx = RiichiContext(
            hand=tuple(hand),
            combs=(),
            meld_count=0,
            visible_34=(0,) * 34,
            dora_tiles=(15,),
            round_wind=41,
            seat_wind=41,
            open_tanyao=True,
            is_riichi=False,
            is_dealer=False,
            is_furiten=False,
            wall_left=35,
            opponents=(opp_r,),
        )
        self.assertEqual(tile_danger_for_opponent(29, opp_r, ctx.visible_34, ctx.dora_tiles), 0.0)
        action, tile, _ = choose_hand_plan(ctx, ["cut"], {}, frozenset(), ())
        self.assertEqual(action, "cut")
        self.assertEqual(tile, 29)

    def test_riichi_declaration_keeps_dora_and_picks_riichi_cut(self):
        # 听牌手牌，立直候选中切 39 保留赤 5 听两面，应宣言立直
        hand = [12, 13, 14, 205, 26, 27, 32, 33, 34, 17, 18, 22, 22, 39]
        ctx = RiichiContext(
            hand=tuple(hand),
            combs=(),
            meld_count=0,
            visible_34=(0,) * 34,
            dora_tiles=(25,),
            round_wind=41,
            seat_wind=42,
            open_tanyao=True,
            is_riichi=False,
            is_dealer=False,
            is_furiten=False,
            wall_left=40,
            opponents=(),
        )
        action, tile, _ = choose_hand_plan(ctx, ["cut", "riichi_cut"], {39: [16, 19]}, frozenset(), ())
        self.assertEqual(action, "riichi_cut")
        self.assertEqual(tile, 39)

    def test_estimate_hand_han_counts_dora_aka_and_yakuhai(self):
        hand = [12, 13, 14, 205, 26, 27, 45, 45, 45, 32, 33, 34, 22]
        han = estimate_hand_han(hand, (), (25,), 41, 42, True, assume_riichi=False)
        # 役牌(1) + 赤5(1) + 宝牌25(1) = >=3 番
        self.assertGreaterEqual(han, 3.0)

    def test_claim_blocks_far_hand_meld_against_riichi(self):
        opp_r = RiichiOpponentView(seat=1, discards=(19, 29), is_riichi=True)
        hand = [12, 14, 25, 26, 33, 37, 41, 42, 45, 45, 46, 47, 18]
        ctx = RiichiContext(
            hand=tuple(hand),
            combs=(),
            meld_count=0,
            visible_34=(0,) * 34,
            dora_tiles=(11,),
            round_wind=41,
            seat_wind=41,
            open_tanyao=True,
            is_riichi=False,
            is_dealer=False,
            is_furiten=False,
            wall_left=30,
            opponents=(opp_r,),
        )
        self.assertEqual(choose_claim_plan(ctx, ["peng", "pass"], 45), "pass")


if __name__ == "__main__":
    unittest.main()
