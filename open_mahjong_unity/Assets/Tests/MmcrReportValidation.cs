#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Qingque13;

public static class MmcrReportValidation
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new InvalidOperationException(message);
            checks++;
        }
        var mixed = Qingque13Hepai.HepaiCheck(new List<int> {21,21,21,25,26,26,26},
            new List<string> {"s16","G11"}, new List<string> {"岭上开花","自风南"}, 27);
        Check(mixed.Item2.Contains("镜同") && Qingque13Hepai.GetBasePoint(mixed.Item1) == 322,
              "Report: mirrored triplet/kong scores 322 base points");
        var pairs = Qingque13Hepai.HepaiCheck(new List<int> {11,11,11,11,19,19,31,31,31,31,39,44,44},
            new List<string>(), new List<string> {"自摸","自风西"}, 39);
        Check(pairs.Item2.Contains("镜同三对") && Qingque13Hepai.GetBasePoint(pairs.Item1) == 1180,
              "Report: lower repeated mirrored pair scores 1180 base points");

        foreach (var suits in new[] {new[] {10,20}, new[] {10,30}, new[] {20,30}})
        {
            for (int repeated = 1; repeated <= 9; repeated++)
            for (int single = 1; single <= 9; single++)
            {
                if (repeated == single) continue;
                var hand = new List<int> {44};
                foreach (int suit in suits)
                {
                    hand.AddRange(Enumerable.Repeat(suit + repeated, 4));
                    hand.AddRange(Enumerable.Repeat(suit + single, 2));
                }
                var score = Qingque13Hepai.HepaiCheck(hand, new List<string>(), new List<string> {"自风西"}, 44);
                Check(score.Item2.Contains("镜同三对"), "Mirrored pairs in either rank order");
            }
            for (int layout = 0; layout < 81; layout++)
            {
                int code = layout;
                var melds = new List<string>();
                foreach (int suit in suits)
                foreach (int number in new[] {1,5})
                {
                    melds.Add("kgG"[code % 3].ToString() + (suit + number));
                    code /= 3;
                }
                var score = Qingque13Hepai.HepaiCheck(new List<int> {44}, melds, new List<string> {"自风西"}, 44);
                Check(score.Item2.Contains("镜同"), "Mirrored melds treat every triplet/kong combination equally");
            }
        }
        var impossible = new List<int> {11,11,11,11,13,14,15,16,17,18,19,19,19};
        var noMelds = new List<string>();
        Check(Qingque13Tingpai.TingpaiCheck(impossible, noMelds, false).SetEquals(new[] {12}), "Qingque excludes fifth concealed tile");
        Check(GBtingpai.TingpaiCheck(impossible, noMelds, false).SetEquals(new[] {12}), "Guobiao excludes fifth concealed tile");
        Check(new Sichuan.SichuanTingpaiCheck().TingpaiCheck(impossible, noMelds).SetEquals(new[] {12}), "Sichuan excludes fifth concealed tile");
        Check(new Classical.ClassicalTingpaiCheck().TingpaiCheck(impossible, noMelds).SetEquals(new[] {12}), "Classical excludes fifth concealed tile");
        Check(Riichi.RiichiTingpaiCheck.TingpaiCheckStatic(impossible, noMelds).SetEquals(new[] {12}), "Riichi still excludes fifth concealed tile");
        var kongHand = new List<int> {12,13,21,22,23,31,32,33,37,37};
        foreach (string kong in new[] {"g11", "G11"})
        {
            var melds = new List<string> {kong};
            Check(Qingque13Tingpai.TingpaiCheck(kongHand, melds, false).SetEquals(new[] {14}), "Qingque counts declared kong tiles");
            Check(GBtingpai.TingpaiCheck(kongHand, melds, false).SetEquals(new[] {14}), "Guobiao counts declared kong tiles");
            Check(new Sichuan.SichuanTingpaiCheck().TingpaiCheck(kongHand, melds).SetEquals(new[] {14}), "Sichuan counts declared kong tiles");
        }
        var formal = new List<int> {11,22,23,24,32,33,34,37,37,37};
        var pung = new List<string> {"k11"};
        Check(Riichi.RiichiTingpaiCheck.TingpaiCheckStatic(formal, pung, false, false).Contains(11), "Riichi concealed-only formal tenpai remains configurable");
        Check(!Riichi.RiichiTingpaiCheck.TingpaiCheckStatic(formal, pung, false, true).Contains(11), "Riichi all-owned formal tenpai excludes fifth tile");
        return checks;
    }
}
#endif
