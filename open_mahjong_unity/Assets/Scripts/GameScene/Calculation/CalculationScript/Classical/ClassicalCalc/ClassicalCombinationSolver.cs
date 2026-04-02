using System.Collections.Generic;

namespace Classical {
    /// <summary>
    /// 古典麻将一般型组合解算（4面子+1雀头）与组合映射。
    /// </summary>
    public class ClassicalCombinationSolver {
        private static readonly Dictionary<string, int[]> CombinationToTilesDict = BuildCombinationDict();

        private static Dictionary<string, int[]> BuildCombinationDict() {
            var dict = new Dictionary<string, int[]>();
            int[][] suits = { new[] { 1 }, new[] { 2 }, new[] { 3 } };
            foreach (int s in new[] { 1, 2, 3 }) {
                for (int n = 2; n <= 8; n++) {
                    int mid = s * 10 + n;
                    int[] seq = { mid - 1, mid, mid + 1 };
                    dict[$"s{mid}"] = seq;
                    dict[$"S{mid}"] = seq;
                }
                for (int n = 1; n <= 9; n++) {
                    int t = s * 10 + n;
                    dict[$"k{t}"] = new[] { t, t, t };
                    dict[$"K{t}"] = new[] { t, t, t };
                    dict[$"q{t}"] = new[] { t, t };
                    dict[$"g{t}"] = new[] { t, t, t };
                    dict[$"G{t}"] = new[] { t, t, t };
                }
            }
            for (int t = 41; t <= 47; t++) {
                dict[$"k{t}"] = new[] { t, t, t };
                dict[$"K{t}"] = new[] { t, t, t };
                dict[$"q{t}"] = new[] { t, t };
                dict[$"g{t}"] = new[] { t, t, t };
                dict[$"G{t}"] = new[] { t, t, t };
            }
            return dict;
        }

        public void BuildHandAndCombinationMapping(ClassicalPlayerTiles pt) {
            var tiles = new List<int>();
            string combStr = "";
            foreach (string c in pt.CombinationList) {
                if (CombinationToTilesDict.TryGetValue(c, out int[] arr))
                    tiles.AddRange(arr);
                combStr += c;
            }
            tiles.Sort();
            pt.HandTilesMapped = tiles;
            pt.CombinationStr = combStr;
        }

        public void NormalCheck(ClassicalPlayerTiles pt, List<ClassicalPlayerTiles> doneList) {
            if (pt.CompleteStep == 14) {
                doneList.Add(pt);
                return;
            }
            if (pt.CompleteStep == 0 && !BlockCheck(pt))
                return;

            var allList = TraverseQuetou(pt);
            var endList = new List<ClassicalPlayerTiles>();
            while (allList.Count > 0) {
                var cur = allList[allList.Count - 1];
                allList.RemoveAt(allList.Count - 1);
                TraverseKezi(cur, allList);
                TraverseDazi(cur, allList);
                if (cur.CompleteStep == 14)
                    endList.Add(cur);
            }

            List<string> prevComb = null;
            foreach (var item in endList) {
                item.CombinationList.Sort();
                if (prevComb == null || !ListEqual(item.CombinationList, prevComb)) {
                    prevComb = item.CombinationList;
                    doneList.Add(item);
                }
            }
        }

        private bool BlockCheck(ClassicalPlayerTiles pt) {
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

        private List<ClassicalPlayerTiles> TraverseQuetou(ClassicalPlayerTiles pt) {
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

        private void TraverseKezi(ClassicalPlayerTiles pt, List<ClassicalPlayerTiles> allList) {
            int prevId = 0;
            foreach (int t in pt.HandTiles) {
                if (CountInList(pt.HandTiles, t) >= 3 && t != prevId) {
                    var copy = pt.DeepCopy();
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t);
                    copy.CompleteStep += 3;
                    copy.CombinationList.Add($"K{t}");
                    allList.Add(copy);
                    prevId = t;
                }
            }
        }

        private void TraverseDazi(ClassicalPlayerTiles pt, List<ClassicalPlayerTiles> allList) {
            int prevId = 0;
            foreach (int t in pt.HandTiles) {
                if (t <= 40 && pt.HandTiles.Contains(t + 1) && pt.HandTiles.Contains(t + 2) && t != prevId) {
                    var copy = pt.DeepCopy();
                    copy.HandTiles.Remove(t);
                    copy.HandTiles.Remove(t + 1);
                    copy.HandTiles.Remove(t + 2);
                    copy.CompleteStep += 3;
                    copy.CombinationList.Add($"S{t + 1}");
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

        private static bool ListEqual(List<string> a, List<string> b) {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
