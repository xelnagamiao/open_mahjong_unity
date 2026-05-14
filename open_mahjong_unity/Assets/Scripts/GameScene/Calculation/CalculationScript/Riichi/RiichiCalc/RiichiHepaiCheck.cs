using System;
using System.Collections.Generic;

namespace Riichi {
    /// <summary>
    /// 立直麻将本地和牌检测。
    /// 服务端使用 mahjong 库进行完整 役/番/符/点数 计算，客户端仅判断“能否和牌”。
    /// 若该张使 14 张手牌满足一般型 / 七对子 / 十三幺 任一形态即视为可和。
    /// 返回 (canHu ? 1 : 0, 可用的役形列表)，保持与其它规则接口类似形态。
    /// </summary>
    public class RiichiHepaiCheck {
        private static readonly HashSet<int> Yaojiu = new HashSet<int> {
            11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
        };
        private readonly RiichiCombinationSolver _solver = new RiichiCombinationSolver();

        public Tuple<int, List<string>> HepaiCheck(
            List<int> handList, List<string> tilesCombination,
            List<string> wayToHepai, int getTile) {
            var hand14 = Normalize(handList);
            var hands = new List<string>();

            if (CheckThirteenOrphans(hand14, tilesCombination.Count)) {
                hands.Add("国士无双");
            }
            if (CheckSevenPairs(hand14, tilesCombination.Count)) {
                hands.Add("七对子");
            }
            if (CheckNormal(hand14, tilesCombination)) {
                hands.Add("一般型");
            }
            return Tuple.Create(hands.Count > 0 ? 1 : 0, hands);
        }

        private static List<int> Normalize(List<int> hand) {
            var res = new List<int>(hand.Count);
            foreach (int t in hand) {
                if (t == 105) res.Add(15);
                else if (t == 205) res.Add(25);
                else if (t == 305) res.Add(35);
                else res.Add(t);
            }
            res.Sort();
            return res;
        }

        private static bool CheckThirteenOrphans(List<int> hand, int combCount) {
            if (combCount > 0 || hand.Count != 14) return false;
            var yaoSet = new HashSet<int>();
            int dup = 0;
            foreach (int t in hand) {
                if (!Yaojiu.Contains(t)) return false;
                if (!yaoSet.Add(t)) dup++;
            }
            return yaoSet.Count == 13 && dup == 1;
        }

        private static bool CheckSevenPairs(List<int> hand, int combCount) {
            if (combCount > 0 || hand.Count != 14) return false;
            var counts = new Dictionary<int, int>();
            foreach (int t in hand) {
                counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
            }
            if (counts.Count != 7) return false;
            foreach (var kv in counts) {
                if (kv.Value != 2) return false;
            }
            return true;
        }

        private bool CheckNormal(List<int> hand, List<string> combinationList) {
            int completeStep = combinationList.Count * 3;
            var pt = new RiichiPlayerTiles(hand, combinationList, completeStep);
            var doneList = new List<RiichiPlayerTiles>();
            _solver.NormalCheck(pt, doneList);
            return doneList.Count > 0;
        }
    }
}
