using System.Collections.Generic;

namespace Riichi {
    /// <summary>
    /// 立直麻将听牌检测：在古典一般型基础上额外支持七对子与十三幺。
    /// 输入支持赤宝牌 105/205/305，内部先归一化为 15/25/35 再检测。
    /// </summary>
    public class RiichiTingpaiCheck {
        private readonly HashSet<int> _waitingTiles = new HashSet<int>();
        private static readonly HashSet<int> InvalidTiles = new HashSet<int> { 10, 20, 30, 40 };
        private static readonly HashSet<int> Yaojiu = new HashSet<int> {
            11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
        };

        public HashSet<int> TingpaiCheck(List<int> handTileList, List<string> combinationList) {
            var normalized = NormalizeHand(handTileList);

            _waitingTiles.Clear();

            if (normalized.Count == 13) {
                GsCheck(normalized);
                QdCheckWithoutDragonSevenPairs(normalized);
            }

            int completeStep = combinationList.Count * 3;
            var pt = new RiichiPlayerTiles(normalized, combinationList, completeStep);
            NormalCheck(pt);

            _waitingTiles.RemoveWhere(t => InvalidTiles.Contains(t));
            return new HashSet<int>(_waitingTiles);
        }

        public static HashSet<int> TingpaiCheckStatic(List<int> handTileList, List<string> combinationList, bool debug = false) {
            var checker = new RiichiTingpaiCheck();
            return checker.TingpaiCheck(handTileList, combinationList);
        }

        private static List<int> NormalizeHand(List<int> handTileList) {
            var res = new List<int>(handTileList.Count);
            foreach (int t in handTileList) {
                if (t == 105) res.Add(15);
                else if (t == 205) res.Add(25);
                else if (t == 305) res.Add(35);
                else res.Add(t);
            }
            res.Sort();
            return res;
        }

        private void GsCheck(List<int> handTiles) {
            var yaoSet = new HashSet<int>();
            foreach (int t in handTiles) {
                if (!Yaojiu.Contains(t)) return;
                yaoSet.Add(t);
            }
            if (yaoSet.Count == 12) {
                foreach (int i in Yaojiu) {
                    if (!handTiles.Contains(i)) _waitingTiles.Add(i);
                }
            } else if (yaoSet.Count == 13) {
                foreach (int i in Yaojiu) _waitingTiles.Add(i);
            }
        }

        /// <summary>
        /// 七对子听牌（不含龙七对 dragon seven pairs）：仅 6 对 + 1 单张。
        /// 5 对 + 1 刻听第 4 张和牌后为同牌四张，非日麻七对子。
        /// </summary>
        private void QdCheckWithoutDragonSevenPairs(List<int> handTiles) {
            var counts = new Dictionary<int, int>();
            foreach (int t in handTiles) {
                counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
            }
            int single = 0;
            int waitingTile = -1;
            foreach (var kv in counts) {
                if (kv.Value == 1) {
                    single++;
                    waitingTile = kv.Key;
                } else if (kv.Value == 2) {
                    continue;
                } else {
                    return;
                }
                if (single >= 2) {
                    return;
                }
            }
            if (single == 1 && waitingTile >= 0) {
                _waitingTiles.Add(waitingTile);
            }
        }

        private void NormalCheck(RiichiPlayerTiles pt) {
            if (!BlockCheck(pt)) return;
            var allList = TraverseQuetou(pt);
            var endList = new List<RiichiPlayerTiles>();
            while (allList.Count > 0) {
                var cur = allList[allList.Count - 1];
                allList.RemoveAt(allList.Count - 1);
                TraverseKezi(cur, allList);
                TraverseDazi(cur, allList);
                if (cur.CompleteStep >= 11) endList.Add(cur);
            }

            foreach (var item in endList) {
                if (item.HandTiles.Count == 1) {
                    _waitingTiles.Add(item.HandTiles[0]);
                } else if (item.HandTiles.Count == 2) {
                    int a = item.HandTiles[0], b = item.HandTiles[1];
                    if (a == b) {
                        _waitingTiles.Add(a);
                    } else if (a == b - 1) {
                        if (a - 1 > 0 && a - 1 < 40) _waitingTiles.Add(a - 1);
                        if (a + 2 > 0 && a + 2 < 40) _waitingTiles.Add(a + 2);
                    } else if (a == b - 2) {
                        if (a + 1 > 0 && a + 1 < 40) _waitingTiles.Add(a + 1);
                    }
                }
            }
        }

        private static bool BlockCheck(RiichiPlayerTiles pt) {
            if (pt.HandTiles.Count == 0) return true;
            int blockCount = pt.CombinationList.Count;
            int pointer = pt.HandTiles[0];
            foreach (int t in pt.HandTiles) {
                if (t != pointer && t != pointer + 1) blockCount++;
                pointer = t;
            }
            return blockCount <= 6;
        }

        private static List<RiichiPlayerTiles> TraverseQuetou(RiichiPlayerTiles pt) {
            var result = new List<RiichiPlayerTiles>();
            int prevId = 0;
            foreach (int t in pt.HandTiles) {
                if (CountInList(pt.HandTiles, t) >= 2 && t != prevId) {
                    var copy = pt.DeepCopy();
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t);
                    copy.CompleteStep += 2;
                    copy.CombinationList.Add($"q{t}");
                    result.Add(copy);
                    prevId = t;
                }
            }
            result.Add(pt.DeepCopy());
            return result;
        }

        private static void TraverseKezi(RiichiPlayerTiles pt, List<RiichiPlayerTiles> allList) {
            int prevId = 0;
            foreach (int t in pt.HandTiles) {
                if (CountInList(pt.HandTiles, t) >= 3 && t != prevId) {
                    var copy = pt.DeepCopy();
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t);
                    copy.CompleteStep += 3;
                    copy.CombinationList.Add($"k{t}");
                    allList.Add(copy);
                    prevId = t;
                }
            }
        }

        private static void TraverseDazi(RiichiPlayerTiles pt, List<RiichiPlayerTiles> allList) {
            int prevId = 0;
            foreach (int t in pt.HandTiles) {
                if (t <= 40 && pt.HandTiles.Contains(t + 1) && pt.HandTiles.Contains(t + 2) && t != prevId) {
                    var copy = pt.DeepCopy();
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t + 1);
                    copy.HandTiles.Remove(t + 2);
                    copy.CompleteStep += 3;
                    copy.CombinationList.Add($"s{t + 1}");
                    allList.Add(copy);
                    prevId = t;
                }
            }
        }

        private static int CountInList(List<int> list, int value) {
            int count = 0;
            for (int i = 0; i < list.Count; i++) {
                if (list[i] == value) count++;
            }
            return count;
        }
    }
}
