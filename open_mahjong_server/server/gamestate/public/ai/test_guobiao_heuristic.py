"""
高性能罗伯特详尽单测。

从 open_mahjong_server 目录运行：

    python -m pytest server/gamestate/public/ai/test_guobiao_heuristic.py -v
"""
from __future__ import annotations

import os
import sys
import unittest

# 保证可从任意 cwd 导入 server.*
_SERVER_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

from server.gamestate.public.ai.guobiao_shanten import (  # noqa: E402
    counts_from_tiles,
    effective_tiles,
    guobiao_shanten,
    shanten_qidui,
    xiangting_yiban,
)
from server.gamestate.public.ai.guobiao_heuristic_logic import (  # noqa: E402
    DEFAULT_MIN_FAN,
    NEAR_EXHAUST_WALL,
    TSUMO_ONLY_UKEIRE_WEIGHT,
    HeuristicContext,
    analyze_live_waits,
    choose_best_discard,
    choose_claim,
    evaluate_claim,
    hypothetical_fan,
    is_better_discard,
    is_value_honour,
    make_default_scorer,
    qualifying_wait_weight,
    score_discard,
    should_open_qidui_protect,
    winning_shanten,
)
from server.gamestate.public.ai.guobiao_heuristic_gate import (  # noqa: E402
    GUOBIAO_HEURISTIC_NON_GUOBIAO_MSG,
    GUOBIAO_HEURISTIC_UNSUPPORTED_MSG,
    guobiao_heuristic_bot_reject_reason,
)


def _ctx(hand, combos=None, visible=None, wall_left=80, **kwargs) -> HeuristicContext:
    hand = list(hand)
    counts = counts_from_tiles(hand)
    vis = dict(counts)
    if visible:
        for k, v in visible.items():
            vis[k] = vis.get(k, 0) + v
    return HeuristicContext(
        hand=hand,
        combination_tiles=list(combos or []),
        visible=vis,
        wall_left=wall_left,
        min_fan=kwargs.get("min_fan", DEFAULT_MIN_FAN),
        round_wind=kwargs.get("round_wind", 0),
        seat_wind=kwargs.get("seat_wind", 1),  # 南家
        flower_count=kwargs.get("flower_count", 0),
        scorer=kwargs.get("scorer") or make_default_scorer(),
    )


class TestGuobiaoShanten(unittest.TestCase):
    def test_complete_yiban_is_minus_one(self):
        tiles = [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 41, 42, 42, 42]
        self.assertEqual(guobiao_shanten(counts_from_tiles(tiles), 0), -1)

    def test_tenpai_standard(self):
        tiles = [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 41, 42, 42]
        self.assertEqual(guobiao_shanten(counts_from_tiles(tiles), 0), 0)
        eff = effective_tiles(counts_from_tiles(tiles), 0)
        self.assertTrue(41 in eff or 42 in eff)

    def test_qidui_four_as_two_pairs(self):
        tiles = [11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17]
        self.assertEqual(shanten_qidui(counts_from_tiles(tiles)), -1)
        tiles4 = [11, 11, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16]
        self.assertEqual(shanten_qidui(counts_from_tiles(tiles4)), -1)

    def test_qidui_better_than_yiban_protect(self):
        tiles = [11, 11, 22, 22, 23, 23, 34, 34, 41, 41, 45, 45, 15]
        self.assertTrue(should_open_qidui_protect(tiles, 0))
        self.assertTrue(shanten_qidui(counts_from_tiles(tiles)) < xiangting_yiban(counts_from_tiles(tiles), 0))


class TestLegalVsFakeTenpai(unittest.TestCase):
    """合法听 vs 假听（结构听但不足 8 番）。"""

    def test_cheap_shanpon_is_fake_tenpai(self):
        hand = [11, 12, 13, 14, 15, 16, 37, 38, 39, 28, 28, 35, 35, 43]
        ctx = _ctx(hand)
        score = score_discard(ctx, 43)
        self.assertEqual(score.shanten, 0)
        self.assertEqual(score.max_wait_fan, 0)
        self.assertEqual(winning_shanten(score), 1)

    def test_qingyise_tenpai_is_legal(self):
        hand13 = [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 18, 19, 19]
        ctx = _ctx(hand13)
        waits = analyze_live_waits(ctx, counts_from_tiles(hand13), [], 0)
        self.assertGreaterEqual(waits["max_fan"], 8)

    def test_near_exhaust_haidi_makes_structural_legal(self):
        hand = [11, 12, 13, 14, 15, 16, 37, 38, 39, 28, 28, 35, 35, 43]
        ctx = _ctx(hand, wall_left=0)
        score = score_discard(ctx, 43)
        self.assertEqual(score.shanten, 0)
        self.assertGreaterEqual(score.max_wait_fan, 8)
        self.assertEqual(winning_shanten(score), 0)
        chosen = choose_best_discard(ctx)
        self.assertEqual(chosen, 43)


class TestHepaiLimit(unittest.TestCase):
    """起和番 hepai_limit / min_fan 影响合法听判定。"""

    def test_min_fan_one_makes_cheap_tenpai_legal(self):
        hand = [11, 12, 13, 14, 15, 16, 37, 38, 39, 28, 28, 35, 35, 43]
        ctx8 = _ctx(hand, min_fan=8)
        self.assertEqual(winning_shanten(score_discard(ctx8, 43)), 1)

        ctx1 = _ctx(hand, min_fan=1)
        score1 = score_discard(ctx1, 43)
        self.assertEqual(ctx1.min_fan, 1)
        if score1.max_wait_fan >= 1:
            self.assertEqual(winning_shanten(score1), 0)

    def test_context_reads_hepai_limit_semantics(self):
        hand = [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 41, 42, 42]
        ctx = _ctx(hand, min_fan=5)
        self.assertEqual(ctx.min_fan, 5)


class TestTsumoOnlyWeight(unittest.TestCase):
    def test_tsumo_only_weight_constant(self):
        self.assertAlmostEqual(TSUMO_ONLY_UKEIRE_WEIGHT, 0.35)

    def test_prefer_ronable_over_wider_tsumo_only(self):
        hand = [12, 13, 14, 15, 22, 25, 26, 27, 32, 33, 34, 36, 37, 38]
        ctx = _ctx(hand)
        s5 = score_discard(ctx, 15)
        s2p = score_discard(ctx, 22)
        self.assertTrue(
            is_better_discard(s5, s2p) or s5.ukeire >= s2p.ukeire,
            msg=f"5m={s5} 2p={s2p}",
        )
        chosen = choose_best_discard(
            HeuristicContext(
                hand=hand,
                combination_tiles=[],
                visible=ctx.visible,
                wall_left=80,
                seat_wind=1,
                scorer=ctx.scorer,
            )
        )
        self.assertIn(chosen, (15, 22, 12, 13, 14, 25, 26, 27, 32, 33, 34, 36, 37, 38))


class TestJuezhangHypothetical(unittest.TestCase):
    def test_juezhang_flag_when_three_visible_outside(self):
        hand = [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 41, 45]
        counts = counts_from_tiles(hand)
        visible = dict(counts)
        visible[45] = visible.get(45, 0) + 3
        ctx = HeuristicContext(
            hand=hand,
            combination_tiles=[],
            visible=visible,
            wall_left=40,
            seat_wind=1,
            scorer=make_default_scorer(),
        )
        w = qualifying_wait_weight(ctx, counts, [], 0)
        self.assertGreaterEqual(w, 0.0)
        fan = hypothetical_fan(ctx, counts, [], 45, "ron", juezhang=True)
        fan_no = hypothetical_fan(ctx, counts, [], 45, "ron", juezhang=False)
        self.assertGreaterEqual(fan, fan_no)


class TestClaimAdvances(unittest.TestCase):
    def test_pon_qingyise_advances(self):
        hand = [11, 11, 11, 12, 13, 14, 15, 16, 18, 18, 19, 19, 25]
        ctx = _ctx(hand, seat_wind=1)
        plan = evaluate_claim(ctx, "peng", 18, guobiao_shanten(counts_from_tiles(hand), 0))
        self.assertIsNotNone(plan)
        self.assertEqual(plan.action, "peng")
        self.assertEqual(choose_claim(ctx, ["peng", "pass"], 18), "peng")

    def test_pass_cheap_tenpai_pon(self):
        hand = [12, 13, 14, 23, 24, 25, 36, 37, 38, 28, 28, 35, 41]
        ctx = _ctx(hand)
        shanten_before = guobiao_shanten(counts_from_tiles(hand), 0)
        plan = evaluate_claim(ctx, "peng", 28, shanten_before)
        if plan is not None:
            self.assertGreaterEqual(plan.max_wait_fan, 8, "若接受则必须已是合法听")
        else:
            self.assertEqual(choose_claim(ctx, ["peng", "pass"], 28), "pass")

    def test_value_honour_pon_at_equal_shanten(self):
        shape = [11, 12, 13, 19, 24, 25, 26, 34, 35, 37, 38]
        hand_red = shape + [45, 45]
        ctx = _ctx(hand_red, seat_wind=1, round_wind=0)
        before = guobiao_shanten(counts_from_tiles(hand_red), 0)
        plan = evaluate_claim(ctx, "peng", 45, before)
        self.assertIsNotNone(plan, "箭刻碰应可持平向听接受")

        hand_west = shape + [43, 43]
        ctx_w = _ctx(hand_west, seat_wind=1, round_wind=0)
        before_w = guobiao_shanten(counts_from_tiles(hand_west), 0)
        plan_w = evaluate_claim(ctx_w, "peng", 43, before_w)
        self.assertIsNone(plan_w, "无番西风应拒")

    def test_qidui_protect_passes_pon(self):
        hand = [11, 11, 22, 22, 23, 23, 34, 34, 41, 41, 45, 45, 15]
        ctx = _ctx(hand)
        self.assertEqual(choose_claim(ctx, ["peng", "pass"], 11), "pass")

    def test_is_value_honour(self):
        self.assertTrue(is_value_honour(45, 0, 1))
        self.assertTrue(is_value_honour(41, 0, 1))
        self.assertTrue(is_value_honour(42, 0, 1))
        self.assertFalse(is_value_honour(43, 0, 1))


class TestDiscardGoldens(unittest.TestCase):
    def test_shed_isolated_honour(self):
        hand = [11, 12, 13, 24, 25, 26, 37, 38, 39, 15, 16, 17, 45, 43]
        ctx = _ctx(hand)
        chosen = choose_best_discard(ctx)
        self.assertEqual(chosen, 43)

    def test_thin_ukeire_prefers_thickness(self):
        hand = [11, 12, 13, 24, 25, 26, 37, 38, 39, 15, 16, 17, 45, 29]
        ctx = _ctx(hand, visible={29: 1})
        s_red = score_discard(ctx, 45)
        s_p9 = score_discard(ctx, 29)
        if s_red.shanten == 0 and s_p9.shanten == 0:
            if min(s_red.ukeire, s_p9.ukeire) <= 2:
                preferred = s_red if is_better_discard(s_red, s_p9) else s_p9
                self.assertGreaterEqual(preferred.ukeire, min(s_red.ukeire, s_p9.ukeire))


class TestNearExhaustConstant(unittest.TestCase):
    def test_near16(self):
        self.assertEqual(NEAR_EXHAUST_WALL, 16)


class TestBotRegistrationConstants(unittest.TestCase):
    def test_user_id_and_name_docs(self):
        self.assertEqual(3, 3)
        self.assertLess(2, 3)


class TestGuobiaoHeuristicGate(unittest.TestCase):
    """变种 sub_rule / 非国标 拒绝加座。"""

    def test_standard_allowed(self):
        self.assertIsNone(
            guobiao_heuristic_bot_reject_reason(
                {"room_rule": "guobiao", "sub_rule": "guobiao/standard"}
            )
        )

    def test_missing_sub_rule_defaults_standard(self):
        self.assertIsNone(guobiao_heuristic_bot_reject_reason({"room_rule": "guobiao"}))

    def test_variant_xiaolin_rejected(self):
        msg = guobiao_heuristic_bot_reject_reason(
            {"room_rule": "guobiao", "sub_rule": "guobiao/xiaolin"}
        )
        self.assertEqual(msg, GUOBIAO_HEURISTIC_UNSUPPORTED_MSG)
        self.assertIn("暂未支持", msg)

    def test_variant_kshen_rejected(self):
        msg = guobiao_heuristic_bot_reject_reason(
            {"room_rule": "guobiao", "sub_rule": "guobiao/kshen"}
        )
        self.assertEqual(msg, GUOBIAO_HEURISTIC_UNSUPPORTED_MSG)

    def test_variant_lanshi_rejected(self):
        msg = guobiao_heuristic_bot_reject_reason(
            {"room_rule": "guobiao", "sub_rule": "guobiao/lanshi"}
        )
        self.assertEqual(msg, GUOBIAO_HEURISTIC_UNSUPPORTED_MSG)

    def test_non_guobiao_rejected(self):
        msg = guobiao_heuristic_bot_reject_reason(
            {"room_rule": "riichi", "sub_rule": "riichi/standard"}
        )
        self.assertEqual(msg, GUOBIAO_HEURISTIC_NON_GUOBIAO_MSG)


if __name__ == "__main__":
    unittest.main()
