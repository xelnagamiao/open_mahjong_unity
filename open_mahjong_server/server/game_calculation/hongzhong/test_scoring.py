"""计番输入为人工构造的独立解释；共用求解器另有原书端到端牌例。"""

import unittest

from .scoring import (CONFIG, FANS, Interpretation, bird_hits, kong_payments, meld_tiles,
                      normalize_config, reverse_kong_payments, score_interpretation,
                      valid_tiles, win_payments)


class ScoringTests(unittest.TestCase):
    def test_standard_fans_and_physical_red(self):
        logical = (11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 21, 21, 22, 22)
        shape = Interpretation("standard", logical, ((11,12,13),(14,15,16),(17,18,19),(21,21,21)), 22)
        red = list(logical); red[0] = 45
        self.assertEqual(score_interpretation(red, (), shape)["fan_names"], ["一条龙"])
        self.assertEqual(score_interpretation(logical, (), shape, replacement=True)["fan"], 3)

    def test_mixed_no_fans_and_pung(self):
        shape = Interpretation("standard", (11,12,13,22,23,24,34,35,36,27,27,27,28,28),
                               ((11,12,13),(22,23,24),(34,35,36),(27,27,27)), 28)
        self.assertEqual(score_interpretation([45], (), shape)["fan_names"], ["平和"])
        shape = Interpretation("standard", (18,18,18,19,19), ((18,18,18),), 19)
        result = score_interpretation([45], ("k11","g12","G13"), shape)
        self.assertEqual(result["fan_names"], ["大对子", "清一色"])

    def test_long_seven_pairs_requires_winning_fourth(self):
        tiles = (11,11,11,11,12,12,13,13,14,14,15,15,16,16)
        shape = Interpretation("seven_pairs", tiles, (), 11, ((45, 11),))
        result = score_interpretation([45], (), shape, replacement=True)
        self.assertEqual(result["fan_names"], ["杠上花", "清一色", "龙七对"])
        self.assertEqual((result["raw_fan"], result["fan"], result["base_score"]), (6,4,16))
        other = Interpretation("seven_pairs", tiles, (), 12)
        self.assertIn("七对子", score_interpretation(tiles, (), other)["fan_names"])
        self.assertNotIn("龙七对", score_interpretation(tiles, (), other)["fan_names"])

    def test_all_three_suits_can_make_dragon(self):
        for suit in (1,2,3):
            groups = tuple(tuple(suit*10+n for n in ranks) for ranks in ((1,2,3),(4,5,6),(7,8,9))) + ((11,11,11),)
            tiles = tuple(t for g in groups for t in g)+(22,22)
            shape = Interpretation("standard", tiles, groups, 22)
            self.assertIn("一条龙", score_interpretation([45], (), shape)["fan_names"])
        with self.assertRaises(ValueError):
            score_interpretation([45], (), Interpretation("orphans", (), (), 11))

    def test_birds_and_all_seat_payments(self):
        self.assertEqual(bird_hits([]), 0)
        for tile in (11,15,19,21,25,29,31,35,39,45):
            self.assertEqual(bird_hits([tile,12]), 1)
        for seat in range(4):
            for fan in range(8):
                changes = win_payments(seat,fan,[45,11])
                self.assertEqual(sum(changes.values()),0)
                self.assertEqual(changes[seat],3*((1 << min(4,fan))+2))
        for bad in ([11,12,13],[41],[True],None):
            with self.assertRaises(ValueError): bird_hits(bad)
        for seat,fan in ((4,0),(True,1),(0,-1),(0,True)):
            with self.assertRaises(ValueError): win_payments(seat,fan)

    def test_kong_ledger_and_draw_refund(self):
        ledger=[]
        for seat in range(4):
            for kind in ("direct","added","concealed"):
                changes=kong_payments(seat,kind,payer=(seat+1)%4)
                self.assertEqual(sum(changes.values()),0)
                ledger.append({"changes":changes})
        self.assertFalse(any(kong_payments(0,"added",drawn=False).values()))
        refund=reverse_kong_payments(ledger)
        for seat in range(4): self.assertEqual(refund[seat],-sum(e["changes"][seat] for e in ledger))
        for args in ((0,"bad",None),(0,"direct",0),(0,"direct",None),(True,"added",None)):
            with self.assertRaises(ValueError): kong_payments(args[0],args[1],payer=args[2])
        with self.assertRaises(ValueError): kong_payments(0,"added",drawn=1)

    def test_fixed_config_and_physical_inventory(self):
        self.assertEqual(normalize_config(),CONFIG)
        self.assertEqual(normalize_config({"fan_cap":4}),CONFIG)
        for raw in ([],{"fan_cap":True},{"fan_cap":5},{"extra":0}):
            with self.assertRaises(ValueError): normalize_config(raw)
        self.assertTrue(valid_tiles([11,11,12,13,14,21,22,23,31,32,33,45,45,45]))
        self.assertFalse(valid_tiles([11]*5+[12]*3+[13]*3+[14]*3))
        self.assertFalse(valid_tiles([11,11],("k11","k12","k13","k14")))
        self.assertFalse(valid_tiles(None))
        self.assertFalse(valid_tiles([11],("k11",)*5))
        self.assertFalse(valid_tiles([True]*14))
        self.assertFalse(valid_tiles([11]*11,("s12",)))
        self.assertEqual(meld_tiles("G11"),(11,)*4)
        for code in (None,"k45","kxx","s12","k111"):
            with self.assertRaises(ValueError): meld_tiles(code)


if __name__ == "__main__": unittest.main()
