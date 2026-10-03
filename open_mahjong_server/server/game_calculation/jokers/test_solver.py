"""Rulebook counterexamples, physical-identity tests and independent small oracle."""

import random
import unittest
from collections import Counter
from dataclasses import replace
from functools import lru_cache
from itertools import combinations_with_replacement

from . import (NUMBERS, ORPHANS, TILES, ExternalMeld, JokerPolicy, cache_info,
               can_form_melds, can_form_pairs, can_win, clear_caches,
               iter_winning_shapes, structural_waits)


def man(text):
    return [10 + int(c) for c in text]


def brute_plain(values):
    """Independent remove-a-pair then first-real-tile recursion (no resource DP)."""
    @lru_cache(None)
    def melds(hand):
        if not hand:
            return True
        first = hand[0]
        values = list(hand)
        if values.count(first) >= 3:
            for _ in range(3):
                values.remove(first)
            if melds(tuple(values)):
                return True
        values = list(hand)
        if first < 40 and first % 10 <= 7 and first + 1 in values and first + 2 in values:
            for tile in (first, first + 1, first + 2):
                values.remove(tile)
            if melds(tuple(values)):
                return True
        return False
    for pair, count in Counter(values).items():
        if count >= 2:
            rest = list(values)
            rest.remove(pair)
            rest.remove(pair)
            if melds(tuple(sorted(rest))):
                return True
    return False


class JokerSolverTests(unittest.TestCase):
    def setUp(self):
        self.hz = JokerPolicy(joker_tiles=(46,), shapes=("standard", "seven_pairs"))
        self.gd = JokerPolicy(joker_tiles=(55, 56, 57, 58), physical_tiles=TILES + (55, 56, 57, 58),
                              closed_cap=4, shapes=("standard", "seven_pairs", "thirteen_orphans"))

    def shapes(self, hand, melds=(), policy=None, winning_tile=None):
        return list(iter_winning_shapes(hand, melds, policy or self.gd, winning_tile))

    def test_basic_natural_wildcard_and_waits(self):
        hand = man("12") + [46, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
        self.assertTrue(can_win(hand, policy=self.hz))
        self.assertEqual(structural_waits(hand[:-1], policy=self.hz), {13, 42, 46})
        shape = next(iter_winning_shapes(hand, policy=self.hz, winning_tile=46))
        self.assertEqual(shape.assignment[2], 13)
        self.assertEqual(shape.winning_logical_tile, 13)
        self.assertFalse(shape.is_natural)
        self.assertEqual(shape.pair, 42)
        self.assertEqual(hand[2], 46)

    def test_no_virtual_fifth_east_under_mil_cap(self):
        hand = man("123") + [21, 22, 23, 31, 32, 33] + [41] * 4 + [55]
        self.assertFalse(can_win(hand, policy=self.gd))
        self.assertFalse(self.shapes(hand))
        self.assertTrue(can_win(hand, policy=replace(self.gd, closed_cap=None)))

    def test_book_forbids_only_forced_four_not_whole_hand(self):
        hand = man("1234444556677") + [55]
        shapes = self.shapes(hand, winning_tile=55)
        self.assertTrue(shapes)
        self.assertEqual({s.winning_logical_tile for s in shapes}, {11, 17})
        self.assertEqual(structural_waits(hand[:-1], policy=self.gd), {11, 17, 55, 56, 57, 58})
        for shape in shapes:
            self.assertLessEqual(max(Counter(shape.closed_logical_tiles).values()), 4)

    def test_exposed_pung_excluded_from_logical_cap(self):
        hand = man("1234556677") + [55]
        shapes = self.shapes(hand, ("k14",), winning_tile=55)
        self.assertIn(14, {s.winning_logical_tile for s in shapes})
        self.assertNotIn(14, structural_waits(hand[:-1], ("k14",), self.gd))
        self.assertFalse(can_win(hand[:-1] + [14], ("k14",), self.gd))

    def test_exposed_kong_excluded_from_logical_cap(self):
        hand = man("1255667799") + [55]
        self.assertTrue(can_win(hand, ("G13",), self.gd))
        self.assertEqual({s.winning_logical_tile for s in self.shapes(hand, ("G13",), winning_tile=55)}, {13})
        self.assertFalse(can_win(hand[:-1] + [13], ("G13",), self.gd))

    def test_seventeen_tile_table_counterexample(self):
        hand = man("11122233344455566")
        policy = JokerPolicy(meld_count=5)
        self.assertTrue(can_win(hand, policy=policy))
        self.assertEqual(len(next(iter_winning_shapes(hand, policy=policy)).melds), 5)
        self.assertFalse(can_win(hand))

    def test_wenzhou_white_fixed_mapping_and_natural_path(self):
        policy = JokerPolicy(joker_tiles=(11,), natural_map=((46, 11),), meld_count=5,
                              shapes=("standard", "eight_pairs"))
        hand = [46, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
        self.assertTrue(can_win(hand, policy=policy))
        self.assertTrue(can_win(hand, policy=replace(policy, joker_tiles=())))
        shape = next(iter_winning_shapes(hand, policy=policy, winning_tile=46))
        self.assertEqual(shape.assignment[0], 11)
        self.assertTrue(shape.is_natural)
        meld = ExternalMeld("sequence", (46, 12, 13), (11, 12, 13))
        self.assertTrue(can_win(hand[3:], (meld,), policy))
        shape = next(iter_winning_shapes(hand[3:], (meld,), policy))
        self.assertEqual(shape.groups[0].physical, (46, 12, 13))
        self.assertEqual(shape.groups[0].logical, (11, 12, 13))
        self.assertFalse(can_win(hand[3:], (ExternalMeld("sequence", (47, 12, 13), (11, 12, 13)),), policy))

    def test_natural_joker_or_substituted_witness(self):
        hand = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
        policy = JokerPolicy(joker_tiles=(11,), meld_count=5)
        self.assertTrue(any(s.is_natural for s in iter_winning_shapes(hand, policy=policy)))
        hand[0] = 19
        policy = replace(policy, joker_tiles=(19,))
        self.assertTrue(can_win(hand, policy=policy))
        self.assertFalse(can_win(hand, policy=replace(policy, joker_tiles=())))
        self.assertFalse(any(s.is_natural for s in iter_winning_shapes(hand, policy=policy)))

    def test_eight_pairs_singleton_and_quad(self):
        policy = JokerPolicy(joker_tiles=(46,), meld_count=5, shapes=("eight_pairs",))
        for hand in ([11, 11, 14, 14, 17, 17, 21, 21, 24, 24, 27, 27, 31, 31, 34, 34, 42],
                     [11] * 4 + [14, 14, 17, 17, 21, 21, 24, 24, 27, 27, 31, 31, 42]):
            self.assertTrue(can_win(hand, policy=policy))
            shape = next(iter_winning_shapes(hand, policy=policy))
            self.assertEqual(shape.kind, "eight_pairs")
            self.assertEqual(sum(g.kind == "pair" for g in shape.groups), 8)
            self.assertEqual(sum(g.kind == "singleton" for g in shape.groups), 1)
        self.assertFalse(can_win([11, 12, 13] * 4 + [14, 15, 16, 17, 18], policy=policy))

    def test_seven_pairs_all_joker_pair_assignments(self):
        hand = [11, 11, 14, 14, 17, 17, 21, 21, 24, 24, 27, 27, 55, 56]
        policy = replace(self.gd, shapes=("seven_pairs",))
        shapes = self.shapes(hand, policy=policy, winning_tile=56)
        self.assertTrue(can_win(hand, policy=policy))
        self.assertEqual({s.winning_logical_tile for s in shapes}, set(TILES))
        for shape in shapes:
            self.assertEqual(Counter(shape.closed_logical_tiles), Counter(shape.assignment))
            self.assertEqual(sum(g.kind == "pair" for g in shape.groups), 7)

    def test_orphans_missing_required_and_pair_restriction(self):
        hand = list(ORPHANS) + [55]
        policy = replace(self.gd, shapes=("thirteen_orphans",))
        shapes = self.shapes(hand, policy=policy, winning_tile=55)
        self.assertEqual({s.winning_logical_tile for s in shapes}, set(ORPHANS))
        self.assertFalse(can_win(hand, policy=replace(policy, pair_tiles=(12,))))
        self.assertFalse(can_win(hand, policy=replace(policy, logical_tiles=NUMBERS)))
        self.assertFalse(can_win([12] + hand[1:], policy=policy))

    def test_melds_and_six_pairs_without_pair(self):
        self.assertTrue(can_form_melds(man("123") + [21, 22, 23, 31, 32, 46, 41, 41, 41], policy=self.hz))
        self.assertTrue(can_form_melds(man("123") + [21, 22, 23, 41, 41, 46], ("k32",), self.hz))
        self.assertTrue(can_form_pairs([11, 11, 14, 14, 17, 17, 21, 21, 24, 24, 46, 46], 6, self.hz))
        self.assertFalse(can_form_pairs([11, 11], 6, self.hz))
        self.assertTrue(can_form_melds([], policy=JokerPolicy(meld_count=0)))
        self.assertTrue(can_form_pairs([], 0))
        self.assertFalse(can_form_pairs([], -1))
        self.assertFalse(can_form_pairs([False], 1))

    def test_limited_domain_and_pair_domain(self):
        hand = man("12") + [55, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42]
        self.assertTrue(can_win(hand, policy=replace(self.gd, joker_targets=tuple(range(11, 20)))))
        self.assertFalse(can_win(hand, policy=replace(self.gd, joker_targets=tuple(range(21, 30)))))
        self.assertFalse(can_win(hand, policy=replace(self.gd, pair_tiles=NUMBERS)))
        self.assertFalse(can_win(hand, policy=replace(self.gd, joker_targets=())))

    def test_winning_identity_can_be_in_different_components(self):
        hand = man("111123") + [21, 22, 23, 31, 32, 33, 42, 42]
        shapes = self.shapes(hand, winning_tile=11)
        self.assertIn("triplet", {s.groups[s.winning_group].kind for s in shapes})
        self.assertIn("sequence", {s.groups[s.winning_group].kind for s in shapes})
        self.assertTrue(any(len(s.concealed_pungs(False)) < len(s.concealed_pungs(True)) for s in shapes))
        self.assertEqual(self.shapes(hand, winning_tile=19), [])
        self.assertEqual(list(iter_winning_shapes(hand, winning_index=100)), [])
        self.assertEqual(list(iter_winning_shapes(hand, winning_tile=42, winning_index=0)), [])

    def test_malformed_and_supply_rejected(self):
        for hand, melds in (([11] * 5, ()), ([True], ()), ([99], ()), (None, ()),
                            ([], ("s19",)), ([], ("x11",)), ([], ("kxx",)),
                            ([], (ExternalMeld("triplet", (11, 11, 12)),)),
                            ([], (ExternalMeld("sequence", (19, 21, 22)),)),
                            ([], (ExternalMeld("bad", (11, 11, 11)),)),
                            ([], (ExternalMeld("kong", (11, 11)),)),
                            ([55, 55], ())):
            with self.subTest(hand=hand, melds=melds):
                self.assertFalse(can_win(hand, melds, self.gd))
                self.assertFalse(structural_waits(hand, melds, self.gd))
                self.assertEqual(list(iter_winning_shapes(hand, melds, self.gd)), [])
        self.assertFalse(can_win([11, 11], ("k12",) * 5, self.gd))
        self.assertFalse(can_win([11] * 15, policy=self.gd))
        self.assertFalse(can_win([46, 46], policy=JokerPolicy(logical_tiles=NUMBERS)))

    def test_policy_validation(self):
        invalid = [dict(meld_count=6), dict(closed_cap=0), dict(logical_tiles=()),
                   dict(physical_tiles=()), dict(joker_tiles=(55,)), dict(logical_tiles=(55,)),
                   dict(physical_tiles=(99,)), dict(joker_tiles=(46, 46)),
                   dict(logical_tiles=(True,)), dict(joker_targets=(55,)),
                   dict(pair_tiles=(11, 11)), dict(natural_map=((46, 55),)),
                   dict(natural_map=((46, 11), (46, 12))), dict(physical_limits=((11, 0),)),
                   dict(physical_limits=((99, 1),)), dict(shapes=("unknown",))]
        for kwargs in invalid:
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                JokerPolicy(**kwargs)

    def test_cache_policy_isolation(self):
        clear_caches()
        hand = man("123") + [21, 22, 23, 31, 32, 33] + [41] * 4 + [55]
        self.assertTrue(can_win(hand, policy=replace(self.gd, closed_cap=None)))
        self.assertFalse(can_win(hand, policy=self.gd))
        structural_waits(hand[:-1], policy=self.gd)
        structural_waits(hand[:-1], policy=self.gd)
        self.assertGreater(cache_info()["waits"]["hits"], 0)
        self.assertLessEqual(cache_info()["groups"]["currsize"], cache_info()["groups"]["maxsize"])

    def test_small_oracle_and_every_generated_witness(self):
        rng = random.Random(20261002)
        domain = tuple(range(11, 20))
        for case in range(120):
            meld_count = 1 + case % 2
            g = case % 3
            hand = rng.sample([t for t in domain for _ in range(4)], 3 * meld_count + 2 - g) + [55, 56][:g]
            for cap in (None, 4):
                policy = JokerPolicy(joker_tiles=(55, 56), logical_tiles=domain,
                                     physical_tiles=domain + (55, 56), meld_count=meld_count,
                                     closed_cap=cap)
                ordinary = [t for t in hand if t not in policy.joker_tiles]
                expected = False
                for replacement in combinations_with_replacement(domain, g):
                    logical = ordinary + list(replacement)
                    if cap and max(Counter(logical).values()) > cap:
                        continue
                    if brute_plain(logical):
                        expected = True
                        break
                self.assertEqual(can_win(hand, policy=policy), expected, (case, hand, cap))
                witness = next(iter_winning_shapes(hand, policy=policy, winning_index=len(hand) - 1), None)
                self.assertEqual(witness is not None, expected, (case, hand, cap))
                if witness:
                    self.assertTrue(brute_plain(witness.assignment))
                    self.assertEqual(Counter(t for group in witness.groups for t in group.physical), Counter(hand))
                    self.assertEqual(sorted(i for group in witness.groups for i in group.indices), list(range(len(hand))))


if __name__ == "__main__":
    unittest.main()
