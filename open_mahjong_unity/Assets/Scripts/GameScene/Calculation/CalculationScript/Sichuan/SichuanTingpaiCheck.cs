using System.Collections.Generic;
using System.Linq;

namespace Sichuan {
    /// <summary>
    /// 四川麻将听牌检查：一般型（4 面子 + 雀头）+ 七对。
    /// 只有万(11-19)/饼(21-29)/条(31-39)三门数牌，无字牌。
    /// 返回可使其和牌的牌集合（不做定缺过滤，由调用方按需排除定缺花色）。
    /// 与服务端 sichuan_tingpai_check.py 对齐。
    /// </summary>
    internal class SichuanTingTiles {
        public List<int> handTiles;
        public List<string> combinationList;
        public int completeStep;

        public SichuanTingTiles(List<int> tiles, List<string> comb, int step) {
            handTiles = new List<int>(tiles);
            handTiles.Sort();
            combinationList = new List<string>(comb);
            completeStep = step;
        }

        public SichuanTingTiles Clone() {
            return new SichuanTingTiles(handTiles, combinationList, completeStep);
        }
    }

    public class SichuanTingpaiCheck {
        private static int CountOf(List<int> list, int value) {
            int c = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] == value) c++;
            return c;
        }

        public HashSet<int> TingpaiCheck(List<int> handTileList, List<string> combinationList) {
            var combs = combinationList ?? new List<string>();
            var test = new SichuanTingTiles(handTileList, combs, combs.Count * 3);
            var result = NormalWaits(test);
            if (combs.Count == 0) {
                result.UnionWith(SevenPairWaits(handTileList));
            }
            result.RemoveWhere(i => i == 10 || i == 20 || i == 30 || i == 40 || i < 11 || i > 39);
            return result;
        }

        private HashSet<int> NormalWaits(SichuanTingTiles playerTiles) {
            var result = new HashSet<int>();
            if (!Block(playerTiles)) return result;
            var allList = TraverseQuetou(playerTiles);
            var endList = new List<SichuanTingTiles>();
            while (allList.Count > 0) {
                var temp = allList[allList.Count - 1];
                allList.RemoveAt(allList.Count - 1);
                TraverseKezi(temp, allList);
                TraverseDazi(temp, allList);
                if (temp.completeStep >= 11) endList.Add(temp);
            }

            foreach (var i in endList) {
                if (i.handTiles.Count == 1) {
                    result.Add(i.handTiles[0]);
                } else if (i.handTiles.Count == 2) {
                    int a = i.handTiles[0], b = i.handTiles[1];
                    if (a == b) {
                        result.Add(a);
                    } else if (a == b - 1) {
                        if (a - 1 > a / 10 * 10) result.Add(a - 1);
                        if (a + 2 < (a / 10 * 10) + 10) result.Add(a + 2);
                    } else if (a == b - 2) {
                        result.Add(a + 1);
                    }
                }
            }
            return result;
        }

        private bool Block(SichuanTingTiles playerTiles) {
            if (playerTiles.handTiles.Count == 0) return true;
            int blockCount = playerTiles.combinationList.Count;
            int pointer = playerTiles.handTiles[0];
            foreach (int tileId in playerTiles.handTiles) {
                if (tileId == pointer || tileId == pointer + 1) {
                    // same block
                } else {
                    blockCount++;
                }
                pointer = tileId;
            }
            return blockCount <= 6;
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

        private HashSet<int> SevenPairWaits(List<int> handTileList) {
            var result = new HashSet<int>();
            if (handTileList.Count != 13) return result;
            var counts = new Dictionary<int, int>();
            foreach (int t in handTileList) {
                counts.TryGetValue(t, out int c);
                counts[t] = c + 1;
            }
            var singles = counts.Where(kv => kv.Value % 2 == 1).Select(kv => kv.Key).ToList();
            int odd = counts.Count(kv => kv.Value % 2 == 1);
            if (odd == 1 && singles.Count == 1) result.Add(singles[0]);
            return result;
        }
    }
}
