"""Unit tests for Sichuan High-Performance Bot (user_id=3), including 2v2 and duplicate teamwork."""
import unittest
from types import SimpleNamespace

from server.gamestate.game_sichuan.heuristic_bot import (
    SichuanContext,
    SichuanPlayerPublicView,
    choose_best_cut,
    choose_claim_action,
    choose_dingque_suit,
    choose_opening_tiles,
    infer_missing_suit,
    partner_assist_bonus,
    should_hu_teammate_discard,
    tile_danger_for_opponent,
)
from server.gamestate.public.ai.guobiao_heuristic_gate import guobiao_heuristic_bot_reject_reason


class SichuanHeuristicBotTests(unittest.TestCase):
    def test_gate_allows_all_sichuan_subrules(self):
        for sub in (
            "sichuan/standard",
            "sichuan/sbr_exchange",
            "sichuan/xueliu",
            "sichuan/doubles",
            "sichuan/doubles_duplicate",
            "sichuan/xueliu_doubles",
            "sichuan/xueliu_doubles_duplicate",
        ):
            self.assertIsNone(guobiao_heuristic_bot_reject_reason({"room_rule": "sichuan", "sub_rule": sub}))

    def test_2v2_exchange_avoids_teammate_suit_and_gifts_good_tiles(self):
        # 队友刚换出万子(1)，己方有3张万子和3张条子，后手应避开万子改换条子
        hand = [11, 15, 19, 22, 23, 24, 25, 26, 27, 28, 34, 35, 36]
        chosen = choose_opening_tiles(hand, exchange=True, is_team_exchange=True, teammate_suit=1)
        self.assertEqual(len(chosen), 3)
        self.assertTrue(all(t // 10 == 3 for t in chosen))

    def test_opponent_missing_suit_is_zero_danger(self):
        opp = SichuanPlayerPublicView(seat=1, team_id=1, discards=(31, 32, 39), melds=("k12", "k15"), dingque_suit=3)
        vis = [0] * 34
        self.assertEqual(infer_missing_suit(opp), 3)
        self.assertEqual(tile_danger_for_opponent(35, opp, vis), 0.0)
        self.assertGreater(tile_danger_for_opponent(16, opp, vis), tile_danger_for_opponent(25, opp, vis))

    def test_2v2_partner_assist_rewards_partner_flush_suit(self):
        partner = SichuanPlayerPublicView(seat=2, team_id=0, discards=(31, 32, 39), melds=("k12", "k15"))
        ctx = SichuanContext(
            hand=[14, 21, 22, 23, 25, 26, 27, 28, 29, 34, 35, 36, 37, 38],
            melds=[],
            visible_34=(0,) * 34,
            is_team_game=True,
            my_seat=0,
            my_team_id=0,
            players=(partner,),
        )
        self.assertGreater(partner_assist_bonus(16, ctx), partner_assist_bonus(35, ctx))

    def test_2v2_teammate_ron_policy_xueliu_vs_sbr(self):
        ctx_xueliu = SichuanContext(
            hand=[], melds=[], visible_34=(0,) * 34, is_xueliu=True, is_team_game=True, team_first_two_end=False, wall_left=8
        )
        self.assertFalse(should_hu_teammate_discard(ctx_xueliu))

        ctx_sbr_late = SichuanContext(
            hand=[], melds=[], visible_34=(0,) * 34, is_xueliu=False, is_team_game=True, team_first_two_end=True, wall_left=12
        )
        self.assertTrue(should_hu_teammate_discard(ctx_sbr_late))

    def test_doubles_flower_pig_guard_sheds_third_suit_late(self):
        # 未定缺双打残局（wall=12）：手里有 1 张孤万子(19)，必须打掉避免花猪罚分
        hand = [19, 21, 22, 23, 24, 25, 26, 27, 28, 32, 33, 35, 36, 37]
        ctx = SichuanContext(
            hand=hand,
            melds=[],
            visible_34=(0,) * 34,
            meld_count=4,
            dingque_suit=0,
            wall_left=12,
            is_team_game=True,
            is_imp_duplicate=True,
        )
        tile, _, _ = choose_best_cut(ctx)
        self.assertEqual(tile, 19)

    def test_dingque_chooses_weakest_suit(self):
        hand = [11, 12, 13, 14, 15, 16, 22, 23, 24, 25, 26, 31, 39]
        self.assertEqual(choose_dingque_suit(hand, meld_count=4), 3)

    def test_claim_never_creates_third_suit_flower_pig(self):
        hand = [11, 12, 13, 22, 23, 35, 35, 36]
        ctx = SichuanContext(hand=hand, melds=["k15", "k28"], visible_34=(0,) * 34, meld_count=4, dingque_suit=0)
        self.assertEqual(choose_claim_action(ctx, ["peng", "pass"], 35), "pass")


if __name__ == "__main__":
    unittest.main()
