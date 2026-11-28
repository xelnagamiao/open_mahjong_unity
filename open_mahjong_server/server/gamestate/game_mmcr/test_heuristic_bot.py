"""Unit tests for Qingque High-Performance Heuristic Bot (user_id=3)."""
import unittest
from types import SimpleNamespace

from server.gamestate.game_mmcr.heuristic_bot import (
    QingqueContext,
    QingqueOpponentView,
    choose_claim_action,
    choose_discard_plan,
    choose_hand_action,
    evaluate_tenpai,
    hand_yaku_potential,
    opponent_expected_payout,
    opponent_threat,
    should_accept_qingque_hu,
    tile_danger_for_opponent,
)
from server.gamestate.public.ai.guobiao_heuristic_gate import guobiao_heuristic_bot_reject_reason


class QingqueHeuristicBotTests(unittest.TestCase):
    def test_gate_allows_qingque_room(self):
        self.assertIsNone(
            guobiao_heuristic_bot_reject_reason(
                {"room_rule": "qingque", "sub_rule": "qingque/standard"}
            )
        )

    def test_tenpai_eval_uses_quadratic_payout(self):
        # 清一色听牌（11,12,13, 14,15,16, 17,18,19, 12,12,12, 15）听 15/18 等，应具有高平方打点
        hand_flush = [11, 12, 13, 14, 15, 16, 17, 18, 19, 12, 12, 12, 15]
        ctx_flush = QingqueContext(hand=hand_flush, melds=[], visible={}, seat_wind=0, wall_left=40)
        te_flush = evaluate_tenpai(ctx_flush, hand_flush, [])
        self.assertGreater(te_flush.live_waits, 0)
        self.assertGreater(te_flush.avg_pts, 300.0)

        # 普通仅门前清听牌（无三色贯通/一气等，仅 3.61 番 -> 39 分）
        hand_cheap = [12, 13, 14, 23, 24, 25, 36, 37, 38, 17, 18, 29, 29]
        ctx_cheap = QingqueContext(hand=hand_cheap, melds=[], visible={}, seat_wind=0, wall_left=40)
        te_cheap = evaluate_tenpai(ctx_cheap, hand_cheap, [])
        self.assertGreater(te_cheap.live_waits, 0)
        self.assertLess(te_cheap.avg_pts, 100.0)
        self.assertGreater(te_flush.avg_pts, te_cheap.avg_pts * 5.0)

    def test_yaku_potential_rewards_flush_and_number_clusters(self):
        flush_hand = [11, 12, 13, 14, 15, 16, 17, 18, 19, 12, 15, 45, 45]
        mixed_hand = [11, 14, 18, 22, 26, 29, 31, 35, 39, 41, 42, 45, 46]
        self.assertGreater(
            hand_yaku_potential(flush_hand, [], 0),
            hand_yaku_potential(mixed_hand, [], 0) + 2.0,
        )

    def test_claim_refuses_breaking_concealed_for_tiny_6pt_fan_tile(self):
        # 门清 1 向听手牌，碰 45（红中）只能进 0 番假听或 6 分孤番牌时，早盘应拒绝破门清
        hand = [11, 12, 13, 23, 24, 25, 36, 37, 38, 18, 28, 45, 45]
        ctx = QingqueContext(hand=hand, melds=[], visible={}, seat_wind=0, wall_left=52)
        choice = choose_claim_action(ctx, ["peng", "pass"], 45)
        self.assertEqual(choice, "pass")

    def test_strategic_pass_rejects_early_6pt_win_but_accepts_late_or_big_win(self):
        gs_early = SimpleNamespace(
            tiles_list=[11] * 40,
            result_dict={"hu_first": (1.35, ["番牌"])},
        )
        self.assertFalse(should_accept_qingque_hu(gs_early, 0, "hu_first"))

        gs_late = SimpleNamespace(
            tiles_list=[11] * 12,
            result_dict={"hu_first": (1.35, ["番牌"])},
        )
        self.assertTrue(should_accept_qingque_hu(gs_late, 0, "hu_first"))

        gs_big = SimpleNamespace(
            tiles_list=[11] * 40,
            result_dict={"hu_first": (8.5, ["混一色"])},
        )
        self.assertTrue(should_accept_qingque_hu(gs_big, 0, "hu_first"))

    def test_full_ron_defense_scores_flush_threat_and_folds_endgame_hopeless_hand(self):
        opp = QingqueOpponentView(discards=(21, 31, 39), melds=("s12", "k15", "k18"))
        self.assertGreater(opponent_threat(opp, wall_left=12), 0.78)
        self.assertGreater(
            tile_danger_for_opponent(16, opp, {}),
            tile_danger_for_opponent(21, opp, {}),
        )
        # 残局绝望 3 向听手牌，应切对手现物安全牌
        hand = [15, 16, 21, 25, 29, 31, 34, 39, 41, 42, 43, 45, 46, 47]
        ctx = QingqueContext(
            hand=hand,
            melds=[],
            visible={t: 1 for t in hand},
            seat_wind=0,
            wall_left=12,
            opponents=(opp,),
        )
        plan = choose_discard_plan(ctx)
        self.assertIsNotNone(plan)
        self.assertIn(plan.tile, (21, 31, 39))

    def test_choose_hand_action_returns_valid_cut(self):
        hand = [11, 12, 13, 14, 15, 16, 17, 18, 19, 22, 23, 24, 35, 47]
        ctx = QingqueContext(hand=hand, melds=[], visible={}, seat_wind=0, wall_left=50)
        act, tile, idx = choose_hand_action(ctx, ["cut"])
        self.assertEqual(act, "cut")
        self.assertIn(tile, hand)
        self.assertEqual(hand[idx], tile)

    def test_v3_opponent_expected_payout_and_deep_potential(self):
        opp_flush = QingqueOpponentView(discards=(21, 31), melds=("s12", "k15"))
        opp_plain = QingqueOpponentView(discards=(21, 31), melds=("s12",))
        self.assertGreater(opponent_expected_payout(opp_flush), opponent_expected_payout(opp_plain))
        # 三色同刻 + 四张八归潜能应高于普通散牌
        hand_sanshoku_k = [15, 15, 15, 25, 25, 35, 35, 12, 12, 12, 12, 41, 42]
        hand_plain = [11, 14, 18, 22, 26, 29, 31, 35, 39, 41, 42, 45, 46]
        self.assertGreater(
            hand_yaku_potential(hand_sanshoku_k, [], 0, deep_v3=True),
            hand_yaku_potential(hand_plain, [], 0, deep_v3=True) + 3.0,
        )


if __name__ == "__main__":
    unittest.main()
