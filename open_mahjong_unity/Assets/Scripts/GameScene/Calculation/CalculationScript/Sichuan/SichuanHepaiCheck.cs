using System;
using System.Collections.Generic;
using System.Linq;

namespace Sichuan {
    /// <summary>
    /// 四川麻将（血战到底）和牌检查与计番，与服务端 sichuan_hepai_check.py 对齐。
    /// 番种（独立加法）：平和0 / 杠+1每 / 根+1每 / 大对子+1 / 金钩钓+1 / 清一色+2 / 七对+2 /
    /// 杠上花·杠上炮·抢杠·海底 各+1（情境番，由 wayToHepai 传入）。
    /// base = 2^min(总番,3)，3番封顶。
    /// </summary>
    public class SichuanHepaiCheck {
        private static readonly string[] SituationalTokens = { "杠上花", "杠上炮", "抢杠", "海底" };

        public static int BaseFromFan(int fan, List<string> fanList = null) {
            if (fanList != null && fanList.Count == 1 && fanList[0] == "平和") return 1;
            if (fan < 0) fan = 0;
            return 1 << Math.Min(fan, 3);
        }

        private static int CountOf(List<int> list, int value) {
            int c = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] == value) c++;
            return c;
        }

        private List<int> ExpandMeld(string comb) {
            if (string.IsNullOrEmpty(comb)) return new List<int>();
            char sign = comb[0];
            if (!int.TryParse(comb.Substring(1), out int tile)) return new List<int>();
            switch (sign) {
                case 'g':
                case 'G':
                    return new List<int> { tile, tile, tile, tile };
                case 'k':
                case 'K':
                    return new List<int> { tile, tile, tile };
                case 's':
                case 'S':
                    return new List<int> { tile - 1, tile, tile + 1 };
                case 'q':
                    return new List<int> { tile, tile };
                default:
                    return new List<int>();
            }
        }

        private List<int> ExpandAll(List<int> handList, List<string> tilesCombination) {
            var tiles = new List<int>(handList);
            foreach (var comb in tilesCombination) tiles.AddRange(ExpandMeld(comb));
            return tiles;
        }

        private bool HasDingque(List<int> tiles, int dingqueSuit) {
            if (dingqueSuit != 1 && dingqueSuit != 2 && dingqueSuit != 3) return false;
            return tiles.Any(t => (t / 10) == dingqueSuit);
        }

        private bool IsFlush(List<int> tiles) {
            var suits = new HashSet<int>();
            foreach (int t in tiles) suits.Add(t / 10);
            return suits.Count == 1;
        }

        private void GenCount(List<int> handList, List<string> tilesCombination, out int kongCount, out int genCount) {
            kongCount = 0;
            var kongTiles = new HashSet<int>();
            var physical = new Dictionary<int, int>();
            foreach (int t in handList) {
                physical.TryGetValue(t, out int c);
                physical[t] = c + 1;
            }
            foreach (var comb in tilesCombination) {
                char sign = comb[0];
                foreach (int t in ExpandMeld(comb)) {
                    physical.TryGetValue(t, out int c);
                    physical[t] = c + 1;
                }
                if (sign == 'g' || sign == 'G') {
                    kongCount++;
                    if (int.TryParse(comb.Substring(1), out int kt)) kongTiles.Add(kt);
                }
            }
            genCount = physical.Count(kv => kv.Value == 4 && !kongTiles.Contains(kv.Key));
        }

        private bool IsSevenPairs(List<int> handList) {
            if (handList.Count != 14) return false;
            var counts = new Dictionary<int, int>();
            foreach (int t in handList) {
                counts.TryGetValue(t, out int c);
                counts[t] = c + 1;
            }
            return counts.Values.All(c => c % 2 == 0);
        }

        private void GlobalFans(List<int> allTiles, int kongCount, int genCount, List<string> wayToHepai,
            bool includeSituational, out int fan, out List<string> names) {
            fan = 0;
            names = new List<string>();
            for (int i = 0; i < kongCount; i++) { fan += 1; names.Add("杠"); }
            for (int i = 0; i < genCount; i++) { fan += 1; names.Add("根"); }
            if (IsFlush(allTiles)) { fan += 2; names.Add("清一色"); }
            if (includeSituational && wayToHepai != null) {
                foreach (string token in SituationalTokens) {
                    if (wayToHepai.Contains(token)) { fan += 1; names.Add(token); }
                }
            }
        }

        /// <summary>把闭门手牌(含和牌张)穷举为 4 面子 + 1 雀头的完整拆解，返回各拆解的组合列表。</summary>
        private List<List<string>> EnumerateNormal(List<int> handList, List<string> tilesCombination) {
            var done = new List<List<string>>();
            var start = new SichuanTingTiles(handList, tilesCombination, tilesCombination.Count * 3);
            var allList = TraverseQuetou(start);
            while (allList.Count > 0) {
                var temp = allList[allList.Count - 1];
                allList.RemoveAt(allList.Count - 1);
                TraverseKezi(temp, allList);
                TraverseDazi(temp, allList);
                // 手牌全部消完即为合法完整拆解（牌数保证恰好一个雀头）
                if (temp.handTiles.Count == 0) {
                    bool hasPair = temp.combinationList.Any(c => c.Length > 0 && c[0] == 'q');
                    if (hasPair) done.Add(new List<string>(temp.combinationList));
                }
            }
            return done;
        }

        private List<SichuanTingTiles> TraverseQuetou(SichuanTingTiles playerTiles) {
            var allList = new List<SichuanTingTiles>();
            int pointer = 0;
            foreach (int tileId in playerTiles.handTiles) {
                if (CountOf(playerTiles.handTiles, tileId) >= 2 && tileId != pointer) {
                    var temp = playerTiles.Clone();
                    temp.handTiles.Remove(tileId);
                    temp.handTiles.Remove(tileId);
                    temp.completeStep += 2;
                    temp.combinationList.Add($"q{tileId}");
                    allList.Add(temp);
                    pointer = tileId;
                }
            }
            allList.Add(playerTiles.Clone());
            return allList;
        }

        private void TraverseKezi(SichuanTingTiles playerTiles, List<SichuanTingTiles> allList) {
            int same = 0;
            foreach (int tileId in playerTiles.handTiles) {
                if (CountOf(playerTiles.handTiles, tileId) >= 3 && tileId != same) {
                    var temp = playerTiles.Clone();
                    temp.handTiles.Remove(tileId);
                    temp.handTiles.Remove(tileId);
                    temp.handTiles.Remove(tileId);
                    temp.completeStep += 3;
                    temp.combinationList.Add($"k{tileId}");
                    allList.Add(temp);
                    same = tileId;
                }
            }
        }

        private void TraverseDazi(SichuanTingTiles playerTiles, List<SichuanTingTiles> allList) {
            int same = 0;
            foreach (int tileId in playerTiles.handTiles) {
                if (tileId <= 40) {
                    if (playerTiles.handTiles.Contains(tileId + 1) && playerTiles.handTiles.Contains(tileId + 2) && tileId != same) {
                        var temp = playerTiles.Clone();
                        temp.handTiles.Remove(tileId);
                        temp.handTiles.Remove(tileId + 1);
                        temp.handTiles.Remove(tileId + 2);
                        temp.completeStep += 3;
                        temp.combinationList.Add($"s{tileId + 1}");
                        allList.Add(temp);
                        same = tileId;
                    }
                }
            }
        }

        /// <summary>返回 (总番数, 番名列表)。不能和返回 (0, [])，平和返回 (0, ["平和"])。</summary>
        public Tuple<int, List<string>> HepaiCheck(List<int> handList, List<string> tilesCombination,
            List<string> wayToHepai, int getTile, int dingqueSuit = 0, bool includeSituational = true) {
            var combs = tilesCombination ?? new List<string>();
            var allTiles = ExpandAll(handList, combs);

            if (HasDingque(allTiles, dingqueSuit)) return Tuple.Create(0, new List<string>());

            GenCount(handList, combs, out int kongCount, out int genCount);
            GlobalFans(allTiles, kongCount, genCount, wayToHepai, includeSituational,
                out int baseGlobalFan, out List<string> baseGlobalNames);

            int bestFan = -1;
            List<string> bestNames = new List<string>();

            // 七对（仅在无副露时成立）
            if (combs.Count == 0 && IsSevenPairs(handList)) {
                int fan = baseGlobalFan + 2;
                var names = new List<string>(baseGlobalNames) { "七对" };
                if (fan > bestFan) { bestFan = fan; bestNames = names; }
            }

            // 一般型（4 面子 + 雀头）
            var decomps = EnumerateNormal(handList, combs);
            bool isJingoudiao = combs.Count == 4 && handList.Count == 2;

            foreach (var combinationList in decomps) {
                int seqCount = combinationList.Count(c => c.Length > 0 && (c[0] == 's' || c[0] == 'S'));
                int fan = baseGlobalFan;
                var names = new List<string>(baseGlobalNames);
                if (seqCount == 0) {
                    fan += 1;
                    names.Add("大对子");
                    if (isJingoudiao) {
                        fan += 1;
                        names.Add("金钩钓");
                    }
                }
                if (fan > bestFan) { bestFan = fan; bestNames = names; }
            }

            if (bestFan < 0) return Tuple.Create(0, new List<string>());
            if (bestFan == 0) return Tuple.Create(1, new List<string> { "平和" });
            return Tuple.Create(bestFan, bestNames);
        }

        /// <summary>查大叫用：遍历听牌的所有和牌张，返回理论最大番（不计情境番）。</summary>
        public Tuple<int, List<string>> MaxHepaiFan(List<int> handTileList, List<string> combinationList,
            int dingqueSuit = 0, HashSet<int> tingpaiTiles = null) {
            if (tingpaiTiles == null) {
                tingpaiTiles = new SichuanTingpaiCheck().TingpaiCheck(handTileList, combinationList);
            }
            int bestFan = 0;
            List<string> bestNames = new List<string>();
            foreach (int w in tingpaiTiles) {
                if ((dingqueSuit == 1 || dingqueSuit == 2 || dingqueSuit == 3) && (w / 10) == dingqueSuit) continue;
                var hand = new List<int>(handTileList) { w };
                var res = HepaiCheck(hand, combinationList, new List<string>(), w, dingqueSuit, false);
                if (res.Item1 > bestFan) { bestFan = res.Item1; bestNames = res.Item2; }
            }
            return Tuple.Create(bestFan, bestNames);
        }
    }
}
