#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>川麻听牌提示的定缺约束回归；不修改对局或场景状态。</summary>
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

        for (int rotation = 0; rotation < 3; rotation++) {
            int Tile(int tile) => ((tile / 10 - 1 + rotation) % 3 + 1) * 10 + tile % 10;
            List<int> Hand(params int[] tiles) => tiles.Select(Tile).ToList();
            int excluded = Tile(11) / 10;
            string suffix = $"（定缺{excluded}）";
            var normal = Hand(11,12,13,14,15,16,17,18,19,21,22,23,25);
            Check("一般型牌例确实听牌" + suffix, Waiting(normal, null, 0).SetEquals(new[] { Tile(25) }));
            Check("一般型仍持定缺牌不提示" + suffix, Waiting(normal, null, excluded).Count == 0);

            var sevenPairs = Hand(11,11,12,12,13,13,14,14,21,21,22,22,25);
            Check("七对牌例确实听牌" + suffix, Waiting(sevenPairs, null, 0).Contains(Tile(25)));
            Check("七对仍持定缺牌不提示" + suffix, Waiting(sevenPairs, null, excluded).Count == 0);

            var meldHand = Hand(21,22,23,24,25,26,27,28,29,25);
            foreach (string sign in new[] { "k", "g", "G" }) {
                var melds = new List<string> { sign + Tile(11) };
                Check("副露牌例确实听牌" + sign + suffix, Waiting(meldHand, melds, 0).Contains(Tile(25)));
                Check("定缺碰杠副露不提示" + sign + suffix, Waiting(meldHand, melds, excluded).Count == 0);
                Check("换三张定缺副露不提示" + sign + suffix, Waiting(meldHand, melds, excluded, "sichuan/xueliu_exchange").Count == 0);
            }

            var legal = Hand(21,22,23,24,25,26,27,28,29,31,32,33,35);
            var expected = new[] { Tile(35) };
            Check("定缺清空恢复听牌" + suffix, Waiting(legal, null, excluded).SetEquals(expected));
            var beforeCut = new List<int>(legal) { Tile(11) };
            beforeCut.Remove(Tile(11));
            Check("切最后一张定缺牌可预览合法听牌" + suffix, Waiting(beforeCut, null, excluded).SetEquals(expected));
            Check("换三张仍持定缺牌不提示" + suffix, Waiting(normal, null, excluded, "sichuan/xueliu_exchange").Count == 0);
            Check("换三张定缺清空恢复听牌" + suffix, Waiting(legal, null, excluded, "sichuan/xueliu_exchange").SetEquals(expected));
            var legacy = new Dictionary<string, object> { { "xueliu_exchange_scoring", false } };
            Check("旧换三张番表仍遵守定缺" + suffix, Waiting(normal, null, excluded, "sichuan/xueliu_exchange", legacy).Count == 0);

            var discardThree = Hand(11,12,13,14,15,16,21,22,23,25);
            Check("弃三张无定缺仍正常听牌" + suffix, Waiting(discardThree, null, 0, "sichuan/xueliu").SetEquals(new[] { Tile(25) }));
        }
        return JsonConvert.SerializeObject(new { passed = checks.Count, checks }, Formatting.Indented);
    }
}
#endif
