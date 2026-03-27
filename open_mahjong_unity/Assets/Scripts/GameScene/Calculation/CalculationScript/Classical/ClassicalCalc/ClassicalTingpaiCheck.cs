using System.Collections.Generic;

namespace Classical {
    /// <summary>
    /// 古典麻将听牌检测。
    /// </summary>
    public class ClassicalTingpaiCheck {
        private readonly HashSet<int> _waitingTiles = new HashSet<int>();
        private static readonly HashSet<int> InvalidTiles = new HashSet<int> { 10, 20, 30, 40 };

        /// <summary>
        /// 外部调用入口：传入手牌（不含和牌张，即 13 / 10 / 7 / 4 / 1 张）与副露列表，返回听牌集合。
        /// </summary>
        public HashSet<int> TingpaiCheck(List<int> handTileList, List<string> combinationList) {
            int completeStep = combinationList.Count * 3;
            var pt = new ClassicalPlayerTiles(handTileList, combinationList, completeStep);
            CheckWaitingTiles(pt);
            _waitingTiles.RemoveWhere(t => InvalidTiles.Contains(t));
            return new HashSet<int>(_waitingTiles);
        }

        /// <summary>
        /// 静态便捷方法，供 External 层调用。
        /// </summary>
        public static HashSet<int> TingpaiCheckStatic(List<int> handTileList, List<string> combinationList, bool debug = false) {
            var checker = new ClassicalTingpaiCheck();
            return checker.TingpaiCheck(handTileList, combinationList);
        }

        private void CheckWaitingTiles(ClassicalPlayerTiles pt) {
            _waitingTiles.Clear();
            NormalCheck(pt);
        }

        private void NormalCheck(ClassicalPlayerTiles pt) {
            if (!BlockCheck(pt)) return;

            var allList = TraverseQuetou(pt);
            var endList = new List<ClassicalPlayerTiles>();
            while (allList.Count > 0) {
                var cur = allList[allList.Count - 1];
                allList.RemoveAt(allList.Count - 1);
                TraverseKezi(cur, allList);
                TraverseDazi(cur, allList);
                if (cur.CompleteStep >= 11)
                    endList.Add(cur);
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

        private static bool BlockCheck(ClassicalPlayerTiles pt) {
            if (pt.HandTiles.Count == 0) return true;
            int blockCount = pt.CombinationList.Count;
            int pointer = pt.HandTiles[0];
            foreach (int t in pt.HandTiles) {
                if (t != pointer && t != pointer + 1)
                    blockCount++;
                pointer = t;
            }
            return blockCount <= 6;
        }

        private static List<ClassicalPlayerTiles> TraverseQuetou(ClassicalPlayerTiles pt) {
            var result = new List<ClassicalPlayerTiles>();
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

        private static void TraverseKezi(ClassicalPlayerTiles pt, List<ClassicalPlayerTiles> allList) {
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

        private static void TraverseDazi(ClassicalPlayerTiles pt, List<ClassicalPlayerTiles> allList) {
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
