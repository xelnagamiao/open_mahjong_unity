#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>国标多拆分算番回归：副露手不得继承其他拆分的门前清条件。</summary>
public static class GuobiaoFanIsolationValidation {
    public static object Run() {
        int checks = 0;
        void Check(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("Guobiao fan isolation: " + name);
            checks++;
        }

        var rules = new[] { "guobiao/standard", "guobiao/xiaolin", "guobiao/kshen" };
        var expected = new[] { new[] {25, 26, 27}, new[] {32, 33, 34}, new[] {34, 34, 38} };
        var ranks = new[] {1, 2, 3, 4, 4, 5, 6, 7, 7, 7};
        var waits = new[] {1, 4, 7};
        for (int rule = 0; rule < rules.Length; rule++) {
            for (int suit = 1; suit <= 3; suit++) {
                var waitingHand = ranks.Select(rank => suit * 10 + rank).ToList();
                var melds = new List<string> {"k" + (suit * 10 + 9)};
                Check(GuobiaoTips.Tingpai(new TingpaiQuery {
                    Hand = waitingHand, Melds = melds, SubRule = rules[rule]
                }).SetEquals(waits.Select(rank => suit * 10 + rank)), "waiting tiles " + rules[rule] + "/" + suit);

                for (int wait = 0; wait < waits.Length; wait++) {
                    int tile = suit * 10 + waits[wait];
                    var hand = new List<int>(waitingHand) {tile};
                    var way = new List<string> {"点和", "场风南", "自风西"};
                    var originalWay = way.ToArray();
                    var originalHand = hand.ToArray();
                    var originalMelds = melds.ToArray();
                    string name = rules[rule] + "/" + tile;
                    for (int repeat = 0; repeat < 2; repeat++) {
                        var score = Score(rules[rule], hand, melds, way, tile);
                        Check(score.Item1 == expected[rule][wait], "score " + name);
                        Check(!score.Item2.Contains("门前清"), "exposed pong " + name);
                        Check(way.SequenceEqual(originalWay) && hand.SequenceEqual(originalHand)
                            && melds.SequenceEqual(originalMelds), "inputs unchanged " + name);
                    }

                    var query = new WaitHintQuery {
                        HandWithWin = hand, Melds = melds, HepaiTile = tile,
                        SubRule = rules[rule], HepaiLimit = 8,
                        WayToHepai = new List<string> {"场风南", "自风西"},
                        SingleTileWay = new List<string>(), MergedWay = way
                    };
                    var hint = GuobiaoTips.Describe(query);
                    Check(hint.Label == expected[rule][wait] + "番" && hint.Kind == WaitTileHint.KindRon, "hint " + name);
                    Check(GuobiaoTips.TryDescribeCached(query, out var cached) && cached.Label == hint.Label
                        && way.SequenceEqual(originalWay), "cached hint " + name);

                    if (rule == 0 && wait == 1) {
                        var tsumo = Score(rules[rule], hand, melds, new List<string> {"自摸", "场风南", "自风西"}, tile);
                        Check(tsumo.Item1 == 27 && tsumo.Item2.Contains("自摸"), "self draw " + name);
                        query.HepaiLimit = 27;
                        Check(GuobiaoTips.Describe(query).Kind == WaitTileHint.KindTsumoOnly, "self draw threshold " + name);
                        query.HepaiLimit = 28;
                        Check(GuobiaoTips.Describe(query).Kind == WaitTileHint.KindNone, "below threshold " + name);
                    }
                }
            }

            // 点和补成刻子的真正门清手仍须计门前清，并保留同风的门风刻和圈风刻。
            var closed = new List<int> {11,11,11,22,23,24,35,36,37,41,41,41,29,29};
            var closedWay = new List<string> {"点和", "场风东", "自风东"};
            var closedBefore = closedWay.ToArray();
            var closedScore = Score(rules[rule], closed, new List<string>(), closedWay, 11);
            Check(closedScore.Item2.Contains("门前清"), "closed ron " + rules[rule]);
            Check(rule == 2 ? closedScore.Item2.Contains("役·门风刻")
                : closedScore.Item2.Contains("圈风刻") && closedScore.Item2.Contains("门风刻"), "winds " + rules[rule]);
            Check(closedWay.SequenceEqual(closedBefore), "closed input unchanged " + rules[rule]);
        }
        return new {checks, passed = true};
    }

    private static Tuple<int, List<string>> Score(string rule, List<int> hand, List<string> melds, List<string> way, int tile) {
        switch (rule) {
            case "guobiao/xiaolin": return GBhepaiXiaolin.HepaiCheck(hand, melds, way, tile);
            case "guobiao/kshen": return GBhepaiKshen.HepaiCheck(hand, melds, way, tile);
            default: return GBhepai.HepaiCheck(hand, melds, way, tile);
        }
    }
}
#endif
