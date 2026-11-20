"""Focused equivalence/first-priority regressions for the offline Robert adapter."""
import importlib.util
import unittest
from dataclasses import replace
from pathlib import Path

spec = importlib.util.spec_from_file_location("robert_bridge_under_test", Path(__file__).with_name("robertBridge.py"))
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)
policy = bridge.policy


def item(hand, actual=None, **kwargs):
    visible = dict((t, hand.count(t)) for t in set(hand))
    return dict(id="representative", hand=hand, combination_tiles=[], visible=visible,
                wall_left=60, min_fan=8, round_wind=0, seat_wind=0, flower_count=0,
                actual_tile=hand[-1] if actual is None else actual, **kwargs)


class PolicyEquivalence(unittest.TestCase):
    def test_first_qualitative_priority_never_borrows_a_large_lower_ukeire_gap(self):
        best = policy.DiscardScore(shanten=1, max_wait_fan=0, ukeire=100)
        actual = policy.DiscardScore(shanten=0, max_wait_fan=0, ukeire=1)
        self.assertTrue(policy.is_better_discard(best, actual))
        gap = bridge.comparator_first_difference(best, actual, False, bridge.DEFAULT_THRESHOLDS)
        self.assertEqual(gap[0], "cheap_tenpai_preference")
        self.assertIsNone(gap[1])
        self.assertEqual(gap[4], "incomparable")

    def test_first_ukeire_priority_never_borrows_a_larger_lower_fan_gap(self):
        best = policy.DiscardScore(shanten=0, max_wait_fan=64, ukeire=10)
        actual = policy.DiscardScore(shanten=0, max_wait_fan=8, ukeire=8)
        gap = bridge.comparator_first_difference(best, actual, False, bridge.DEFAULT_THRESHOLDS)
        self.assertEqual(gap[0], "ukeire")
        self.assertEqual(gap[1], 2)
        self.assertEqual(gap[3], 4)

    def test_context_memo_keys_distinguish_hypothetical_information_and_rules(self):
        ctx = bridge.redacted_context(item([11,12,13,21,22,23,31,32,33,45,45,45,46,47]))
        key = bridge.context_key(ctx)
        for other in [replace(ctx, hand=[*ctx.hand[:-1], 46]), replace(ctx, combination_tiles=["k45"]),
                      replace(ctx, visible={**ctx.visible, 19: 1}), replace(ctx, wall_left=0),
                      replace(ctx, min_fan=16), replace(ctx, round_wind=1),
                      replace(ctx, seat_wind=1), replace(ctx, flower_count=1),
                      replace(ctx, scorer=lambda *args: (0, []))]:
            self.assertNotEqual(bridge.context_key(other), key)

    def test_representative_choices_match_unwrapped_live_repository_policy(self):
        fixtures = [
            item([11,12,13,21,22,23,31,32,33,45,45,45,46,47], actual=45),
            item([11,12,13,14,15,16,37,38,39,28,28,35,35,43], actual=43),
            item([11,12,13,19,24,25,34,35,37,38,46,46,46,47], actual=47),
            item([11,11,16,16,17,22,31,31,32,33,34,38,39,47], actual=47),
        ]
        stages = set()
        for source in fixtures:
            with self.subTest(hand=source["hand"]):
                unwrapped = policy.choose_best_discard(bridge.redacted_context(source))
                result = bridge.compare_one(source, bridge.DEFAULT_THRESHOLDS)
                self.assertEqual(result["recommended_tile"], unwrapped)
                stages.add(result["gap"]["policy_stage"])
        self.assertIn("discard_comparator", stages)
        self.assertTrue(stages.intersection({"qualifying_one_draw", "dead_shape_reshape"}))


if __name__ == "__main__":
    unittest.main()
