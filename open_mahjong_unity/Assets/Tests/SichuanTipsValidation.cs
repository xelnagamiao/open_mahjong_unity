#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>川麻基本听张与普通和牌条件提示回归；不修改对局或场景状态。</summary>
public static class SichuanTipsValidation {
    public static string Run() {
        var checks = new List<string>();
        var manifest = new RuleManifest { RuleId = "sichuan", Tingpai = SichuanTips.Tingpai };
        void Check(string name, bool valid) {
            if (!valid) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        HashSet<int> Waiting(List<int> hand, List<string> melds, int excluded, string subRule = "sichuan/standard", Dictionary<string, object> config = null) {
            return RuleTips.ComputeWaiting(manifest, new TingpaiQuery {
                Hand = hand, Melds = melds, ExcludedSuit = excluded,
                SubRule = subRule, DetailedConfig = config,
            });
        }
        WaitTileHint Hint(List<int> hand, List<string> melds, int winTile, int excluded, string subRule = "sichuan/standard", Dictionary<string, object> config = null, int limit = 0) {
            return SichuanTips.Describe(new WaitHintQuery {
                HandWithWin = new List<int>(hand) { winTile }, HepaiTile = winTile,
                Melds = melds, ExcludedSuit = excluded, SubRule = subRule,
                DetailedConfig = config, HepaiLimit = limit,
            });
        }
        void Unsatisfied(string name, WaitTileHint hint) {
            Check(name, hint.Kind == WaitTileHint.KindNone && hint.Label == "未满足");
        }
        void BelowMinimum(string name, WaitTileHint hint) {
            Check(name, hint.Kind == WaitTileHint.KindNone && hint.Label == "未起和");
        }

        for (int rotation = 0; rotation < 3; rotation++) {
            int Tile(int tile) => ((tile / 10 - 1 + rotation) % 3 + 1) * 10 + tile % 10;
            List<int> Hand(params int[] tiles) => tiles.Select(Tile).ToList();
            int excluded = Tile(11) / 10;
            string suffix = $"（定缺{excluded}）";
            var normal = Hand(11,12,13,14,15,16,17,18,19,21,22,23,25);
            Check("一般型牌例确实听牌" + suffix, Waiting(normal, null, 0).SetEquals(new[] { Tile(25) }));
            Check("一般型仍持定缺牌保留基本听张" + suffix, Waiting(normal, null, excluded).SetEquals(new[] { Tile(25) }));
            Unsatisfied("一般型定缺未清显示未满足" + suffix, Hint(normal, null, Tile(25), excluded));

            var sevenPairs = Hand(11,11,12,12,13,13,14,14,21,21,22,22,25);
            Check("七对牌例确实听牌" + suffix, Waiting(sevenPairs, null, 0).Contains(Tile(25)));
            Check("七对仍持定缺牌保留基本听张" + suffix, Waiting(sevenPairs, null, excluded).Contains(Tile(25)));
            Unsatisfied("七对定缺未清显示未满足" + suffix, Hint(sevenPairs, null, Tile(25), excluded));

            var meldHand = Hand(21,22,23,24,25,26,27,28,29,25);
            foreach (string sign in new[] { "k", "g", "G" }) {
                var melds = new List<string> { sign + Tile(11) };
                Check("副露牌例确实听牌" + sign + suffix, Waiting(meldHand, melds, 0).Contains(Tile(25)));
                Check("定缺碰杠副露保留听张" + sign + suffix, Waiting(meldHand, melds, excluded).Contains(Tile(25)));
                Unsatisfied("定缺碰杠副露显示未满足" + sign + suffix, Hint(meldHand, melds, Tile(25), excluded));
                Check("换三张定缺副露保留听张" + sign + suffix, Waiting(meldHand, melds, excluded, "sichuan/xueliu_exchange").Contains(Tile(25)));
                Unsatisfied("换三张定缺副露显示未满足" + sign + suffix, Hint(meldHand, melds, Tile(25), excluded, "sichuan/xueliu_exchange"));
            }

            var legal = Hand(21,22,23,24,25,26,27,28,29,31,32,33,35);
            var expected = new[] { Tile(35) };
            Check("定缺清空恢复听牌" + suffix, Waiting(legal, null, excluded).SetEquals(expected));
            var beforeCut = new List<int>(legal) { Tile(11) };
            beforeCut.Remove(Tile(11));
            Check("切最后一张定缺牌可预览合法听牌" + suffix, Waiting(beforeCut, null, excluded).SetEquals(expected));
            Check("换三张仍持定缺牌保留基本听张" + suffix, Waiting(normal, null, excluded, "sichuan/xueliu_exchange").SetEquals(new[] { Tile(25) }));
            Unsatisfied("换三张定缺未清显示未满足" + suffix, Hint(normal, null, Tile(25), excluded, "sichuan/xueliu_exchange"));
            Check("换三张定缺清空恢复听牌" + suffix, Waiting(legal, null, excluded, "sichuan/xueliu_exchange").SetEquals(expected));
            var legacy = new Dictionary<string, object> { { "xueliu_exchange_scoring", false } };
            Check("旧换三张保留基本听张" + suffix, Waiting(normal, null, excluded, "sichuan/xueliu_exchange", legacy).Contains(Tile(25)));
            Unsatisfied("旧换三张仍遵守定缺" + suffix, Hint(normal, null, Tile(25), excluded, "sichuan/xueliu_exchange", legacy));

            var discardThree = Hand(11,12,13,14,15,16,21,22,23,25);
            Check("弃三张无定缺仍正常听牌" + suffix, Waiting(discardThree, null, 0, "sichuan/xueliu").SetEquals(new[] { Tile(25) }));
            Check("弃三张分开显示点和自摸番数" + suffix,
                Hint(discardThree, null, Tile(25), 0, "sichuan/xueliu").Label == "点和1番\n自摸2番");
            var threeSuits = Hand(11,12,13,21,22,23,31,32,33,35);
            Check("弃三张三门牌保留基本听张" + suffix, Waiting(threeSuits, null, 0, "sichuan/xueliu").Contains(Tile(35)));
            Unsatisfied("弃三张三门牌显示未满足" + suffix, Hint(threeSuits, null, Tile(35), 0, "sichuan/xueliu"));
            var exchangeThreeSuits = Hand(11,12,13,21,22,23,31,32,33,34,35,36,37);
            Check("换三张三门牌保留基本听张" + suffix, Waiting(exchangeThreeSuits, null, 0, "sichuan/xueliu_exchange").Contains(Tile(37)));
            Unsatisfied("换三张三门牌显示未满足" + suffix, Hint(exchangeThreeSuits, null, Tile(37), 0, "sichuan/xueliu_exchange"));
            Check("弃三张门槛只满足自摸显示黄色" + suffix,
                Hint(discardThree, null, Tile(25), 0, "sichuan/xueliu", limit: 2).Kind == WaitTileHint.KindTsumoOnly);
            BelowMinimum("弃三张未达起和门槛" + suffix,
                Hint(discardThree, null, Tile(25), 0, "sichuan/xueliu", limit: 3));
            BelowMinimum("换三张未达起和门槛" + suffix,
                Hint(normal, null, Tile(25), 0, "sichuan/xueliu_exchange", limit: 4));
            BelowMinimum("血战未达起和门槛" + suffix,
                Hint(legal, null, Tile(35), excluded, limit: 1));
        }
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks }, Formatting.Indented);
    }
}
#endif
