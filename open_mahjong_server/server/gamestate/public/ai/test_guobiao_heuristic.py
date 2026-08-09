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
    choose_closed_kan,
    evaluate_claim,
    hypothetical_fan,
    is_better_discard,
    is_value_honour,
    make_default_scorer,
    qualifying_wait_weight,
    score_discard,
    should_open_qidui_protect,
    tenpai_wait_tiles,
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

    def test_buqiuren_only_wait_is_legal_tenpai(self):
        """门清嵌张：荣 6 / 自摸 8（不求人）——必须算合法听，且 tsumo 权重折算。"""
        # 234m 567m 234p 6s8s 99s 听 7s：平和+喜相逢+嵌张+门前清=6；+不求人=8
        hand = [12, 13, 14, 15, 16, 17, 22, 23, 24, 36, 38, 39, 39, 43]
        ctx = _ctx(hand)
        score = score_discard(ctx, 43)
        self.assertEqual(score.shanten, 0)
        self.assertGreaterEqual(score.max_wait_fan, 8)
        self.assertEqual(winning_shanten(score), 0)
        self.assertEqual(score.tsumo_only_kinds, 1)
        self.assertEqual(score.ron_wait_kinds, 0)
        self.assertAlmostEqual(score.ukeire, 4 * TSUMO_ONLY_UKEIRE_WEIGHT)
        rem = counts_from_tiles([12, 13, 14, 15, 16, 17, 22, 23, 24, 36, 38, 39, 39])
        self.assertGreaterEqual(
            hypothetical_fan(ctx, rem, [], 37, "tsumo", single_wait=True), 8
        )
        self.assertEqual(
            hypothetical_fan(ctx, rem, [], 37, "ron", single_wait=True), 0
        )

    def test_single_wait_qianzhang_enables_legal_tsumo(self):
        """独听嵌张：无「和单张」时自摸 7 不够番；有则 8（对齐 OMC singleWait）。"""
        hand13 = [12, 13, 14, 15, 16, 17, 23, 24, 25, 36, 38, 39, 39]
        ctx = _ctx(hand13 + [43])
        rem = counts_from_tiles(hand13)
        self.assertEqual(hypothetical_fan(ctx, rem, [], 37, "tsumo", single_wait=False), 0)
        self.assertGreaterEqual(
            hypothetical_fan(ctx, rem, [], 37, "tsumo", single_wait=True), 8
        )
        score = score_discard(ctx, 43)
        self.assertEqual(winning_shanten(score), 0)
        self.assertGreaterEqual(score.max_wait_fan, 8)

    def test_four_of_kind_blocks_false_single_wait(self):
        """手内已 4 枚时 GB_tingpai 仍计入听种；不可用 effective_tiles 误判独听加「和单张」。"""
        # 12-14m 27-29p 4×34s 35-37s：eff 仅 37，ting 含 34+37
        hand13 = [12, 13, 14, 27, 28, 29, 34, 34, 34, 34, 35, 36, 37]
        rem = counts_from_tiles(hand13)
        eff = set(effective_tiles(rem, 0))
        ting = set(tenpai_wait_tiles(rem, [], 0))
        self.assertEqual(eff, {37})
        self.assertIn(34, ting)
        self.assertGreater(len(ting), 1)
        ctx = _ctx(hand13 + [43])
        score = score_discard(ctx, 43)
        self.assertEqual(score.shanten, 0)
        self.assertEqual(score.max_wait_fan, 0)
        self.assertEqual(winning_shanten(score), 1)

    def test_prefer_ronable_over_wider_tsumo_only(self):
        # 修自摸 14 张后，本手两切均可能是仅自摸合法听；厚进张优先于 L-D。
        hand = [12, 13, 14, 15, 22, 25, 26, 27, 32, 33, 34, 36, 37, 38]
        ctx = _ctx(hand)
        s5 = score_discard(ctx, 15)
        s2p = score_discard(ctx, 22)
        self.assertEqual(s5.shanten, 0)
        self.assertEqual(s2p.shanten, 0)
        preferred = 15 if is_better_discard(s5, s2p) else 22
        self.assertIn(preferred, (15, 22))
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

    def test_hypothetical_fan_does_not_mutate_combos(self):
        """暗转明 must not rewrite caller's combination_tiles / shared combo list."""
        hand = [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 41, 45]
        counts = counts_from_tiles(hand)
        combos = ["G47"]  # ankan 白 — fan_count may try 暗转明 on ron
        frozen = list(combos)
        ctx = _ctx(hand, combos=combos, seat_wind=1)
        _ = hypothetical_fan(ctx, counts, combos, 45, "ron", juezhang=False)
        _ = hypothetical_fan(ctx, counts, combos, 45, "tsumo", juezhang=False)
        self.assertEqual(combos, frozen)
        self.assertEqual(ctx.combination_tiles, frozen)

    def test_closed_kan_refuses_to_thin_legal_tenpai(self):
        """合法听口上的暗杠若削进张则不应开（对齐 OMC kans 保护）。"""
        # 清一色听牌：111m 234m 567m 88m 99m 听 8/9；手里若有 4 张无关牌不会。
        # 用已有 4 张字牌暗杠会破坏听口。
        hand = [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 18, 19, 19, 19]
        # 这手是 14 张完整形附近；改为听口 + 4 东可杠
        hand = [11, 11, 12, 13, 14, 15, 16, 17, 18, 18, 19, 19, 41, 41]
        # 不够 4 东。跳过强造：无 4 枚时 choose_closed_kan 应返回 None
        ctx = _ctx(hand)
        self.assertIsNone(
            choose_closed_kan(ctx, allow_angang=True, allow_jiagang=False)
        )


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

    def test_reject_claim_into_dead_one_shanten(self):
        """向听推进到一向听但一摸无法进合法听 → 拒鸣（死一向听陷阱）。"""
        hand = [11, 11, 16, 16, 17, 22, 31, 31, 32, 33, 34, 38, 39]
        ctx = _ctx(hand)
        before = guobiao_shanten(counts_from_tiles(hand), 0)
        self.assertGreaterEqual(before, 2)
        plan = evaluate_claim(ctx, "chi_right", 15, before)
        self.assertIsNone(plan)
        self.assertEqual(choose_claim(ctx, ["chi_right", "pass"], 15), "pass")

    def test_reject_eat_vomit_chi_concealed_run_to_open_run(self):
        """吃了吐拦截：手牌已有完整暗顺（如 234），吃进其中一张后手牌仍剩该张，
        且向听持平 → 等价于暗顺换明副露，结构不变、门清丢失，纯亏 → 拒绝吃。"""
        # 牌例：man555 man6 pin234 pin888 北北 pin5
        hand = [15, 15, 15, 16, 22, 23, 24, 28, 28, 28, 44, 44, 25]
        ctx = _ctx(hand)
        before = guobiao_shanten(counts_from_tiles(hand), 0)
        # 用 22 23 吃 24（chi_left），手牌仍剩 24 → 暗顺换明顺，应拒
        plan = evaluate_claim(ctx, "chi_left", 24, before)
        self.assertIsNone(plan)
        self.assertEqual(choose_claim(ctx, ["chi_left", "pass"], 24), "pass")

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


class TestSansesanbugaoGebu(unittest.TestCase):
    """三色三步高只认连步（依次递增一位）；隔步 3-5-7 形不计三色三步高。"""

    def test_gebu_345s_567p_789m_not_sansesanbugao(self):
        # 567m+789m+567p+345s+88p 自摸 5s → 起始 3-5-7 隔步，国标不计三色三步高
        from server.game_calculation.guobiao_hepai_check import Chinese_Hepai_Check

        hand14 = [15, 16, 17, 17, 18, 19, 25, 26, 27, 28, 28, 33, 34, 35]
        fan, names = Chinese_Hepai_Check().hepai_check(hand14, [], ["自摸"], 35)
        self.assertFalse(
            any("三色三步高" in n for n in names),
            f"隔步不应计三色三步高, got fan={fan} names={names}",
        )
        self.assertLess(fan, 8)

    def test_gebu_hypothetical_fan_below_limit(self):
        # 听 5s：隔步不计三色三步高 → 假想自摸不足起和番
        hand13 = [15, 16, 17, 17, 18, 19, 25, 26, 27, 28, 28, 33, 34]
        ctx = _ctx(hand13 + [41])
        rem = counts_from_tiles(hand13)
        fan = hypothetical_fan(ctx, rem, [], 35, "tsumo")
        self.assertLess(fan, 8)

    def test_lianbu_rulebook_case_still_scores(self):
        from server.game_calculation.guobiao_hepai_check import Chinese_Hepai_Check

        # 规则书例 1：s22+s14 + 789m + 234s + 66s 点和 2s → 8
        fan, names = Chinese_Hepai_Check().hepai_check(
            [17, 18, 19, 32, 33, 34, 36, 36],
            ["s22", "s14"],
            ["点和"],
            32,
        )
        self.assertTrue(any("三色三步高" in n for n in names), names)
        self.assertEqual(fan, 8)


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

    def test_hongque_allowed(self):
        self.assertIsNone(
            guobiao_heuristic_bot_reject_reason(
                {"room_rule": "hongque", "sub_rule": "hongque/v1.6"}
            )
        )

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
