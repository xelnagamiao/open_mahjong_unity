using System.Collections.Generic;
using System.Linq;

namespace Riichi {
    /// <summary>
    /// 立直麻将和牌计算主入口。整合：
    ///   1) 调用 RiichiCombinationSolver 求所有一般型拆解 + 七对子 + 国士无双
    ///   2) 每个候选结构用 RiichiYakuDetector 求役 + RiichiFuCalc 求符
    ///   3) 选择最高点数的组合，用 RiichiScoreCalc 计算出得分
    /// </summary>
    public class RiichiHandCalculator {
        private static readonly HashSet<int> Yaojiu = new HashSet<int> {
            11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
        };

        public RiichiHandResult Calculate(List<int> handList, List<string> tilesCombination,
                                          int winTile, RiichiHandContext context) {
            context = context ?? new RiichiHandContext();
            var result = new RiichiHandResult();

            // 归一化手牌（把赤 5 还原到普通 5 用于拆解）
            var normalizedHand = handList.Select(RiichiTileUtil.Normalize).ToList();
            normalizedHand.Sort();
            int winNorm = RiichiTileUtil.Normalize(winTile);

            // 检查胜牌是否包含在手牌中（按归一化值比较，忽略赤 5 与普通 5 差异）
            bool winTileInHand = false;
            foreach (int t in handList) {
                if (RiichiTileUtil.Normalize(t) == winNorm) { winTileInHand = true; break; }
            }
            if (!winTileInHand) {
                result.IsValid = false;
                result.Error = "胜牌不在手牌中";
                return result;
            }

            // 赤宝牌计数（若 context 未指定）
            if (!context.AkaCount.HasValue) {
                int aka = handList.Count(RiichiTileUtil.IsRedFive);
                if (context.CombinationMasks != null) {
                    foreach (var mask in context.CombinationMasks) {
                        aka += RiichiTileUtil.CountAkaInTiles(RiichiTileUtil.TilesFromMask(mask));
                    }
                }
                context.AkaCount = aka;
            }
            result.AkaCount = context.AkaCount.Value;

            // --- 检测国士/七对 ---
            RiichiYakuDetector.DetectResult bestDetect = null;
            int bestScore = -1;
            int bestHan = 0, bestFu = 0;

            var detectorCtx = context;

            // 国士无双：直接在计算器内判定十三面，不借助 YakuDetector 的 set 展开
            if (tilesCombination.Count == 0 && CheckKokushi(normalizedHand, winNorm, out int pairTileForKokushi)) {
                bool thirteenWait = pairTileForKokushi == winNorm;
                int mult = thirteenWait ? 2 : 1;
                var det = new RiichiYakuDetector.DetectResult { YakumanMultiplier = mult, HasYaku = true, IsClosed = true };
                det.Yaku.Add(new RiichiYakuDetector.YakuEntry {
                    Name = thirteenWait ? "国士无双十三面" : "国士无双",
                    IsYakuman = true,
                    YakumanMultiplier = mult,
                });
                int han = 0;
                int fu = 25;
                int score = RiichiScoreCalc.CalculateTotalScore(han, fu,
                    context.PlayerWind == RiichiTileUtil.East, context.IsTsumo, mult);
                if (score > bestScore) {
                    bestScore = score; bestDetect = det;
                    bestHan = han; bestFu = fu;
                }
            }

            // 七对子
            if (tilesCombination.Count == 0 && CheckChiitoitsu(normalizedHand, out var chiitoiSets)) {
                var det = new RiichiYakuDetector(detectorCtx).Detect(chiitoiSets, winTile, HandShape.Chiitoitsu);
                if (det.HasYaku) {
                    int han = det.Yaku.Sum(y => y.Han);
                    int fu = RiichiFuCalc.Calculate(chiitoiSets, winTile, RiichiWaitType.Tanki, context, det, HandShape.Chiitoitsu);
                    int score = RiichiScoreCalc.CalculateTotalScore(han, fu,
                        context.PlayerWind == RiichiTileUtil.East, context.IsTsumo, det.YakumanMultiplier);
                    if (score > bestScore) {
                        bestScore = score; bestDetect = det;
                        bestHan = han; bestFu = fu;
                    }
                }
            }

            // 一般型：使用 RiichiCombinationSolver 求所有拆解
            var allDecompositions = SolveNormal(normalizedHand, tilesCombination);
            foreach (var decomp in allDecompositions) {
                var det = new RiichiYakuDetector(detectorCtx).Detect(decomp, winTile, HandShape.Normal);
                if (!det.HasYaku) continue;
                int han = det.Yaku.Sum(y => y.Han);
                int fu = RiichiFuCalc.Calculate(decomp, winTile, det.WaitType, context, det, HandShape.Normal);
                int score = RiichiScoreCalc.CalculateTotalScore(han, fu,
                    context.PlayerWind == RiichiTileUtil.East, context.IsTsumo, det.YakumanMultiplier);
                if (score > bestScore) {
                    bestScore = score; bestDetect = det;
                    bestHan = han; bestFu = fu;
                }
            }

            if (bestDetect == null || !bestDetect.HasYaku) {
                result.IsValid = false;
                result.Error = "无役";
                return result;
            }

            if (bestDetect.YakumanMultiplier > 0) {
                bestFu = 25;
            }

            result.IsValid = true;
            result.Han = bestHan;
            result.Fu = bestFu;
            result.Score = bestScore;
            result.YakumanMultiplier = bestDetect.YakumanMultiplier;
            foreach (var y in bestDetect.Yaku) {
                string display = y.Han > 0 && !y.IsYakuman ? $"{y.Name}({y.Han}han)" : y.Name;
                result.Yaku.Add(display);
            }
            return result;
        }

        // ---------- 拆解：调用现有 solver ----------

        private List<List<RiichiSet>> SolveNormal(List<int> normalizedHand, List<string> tilesCombination) {
            var decompositions = new List<List<RiichiSet>>();
            int completeStep = tilesCombination.Count * 3;
            var pt = new RiichiPlayerTiles(normalizedHand, tilesCombination, completeStep);
            var solver = new RiichiCombinationSolver();
            var doneList = new List<RiichiPlayerTiles>();
            solver.NormalCheck(pt, doneList);

            foreach (var item in doneList) {
                decompositions.Add(RiichiSet.ParseCombinationList(item.CombinationList));
            }
            return decompositions;
        }

        // ---------- 七对子 / 国士 ----------

        private static bool CheckChiitoitsu(List<int> hand, out List<RiichiSet> sets) {
            sets = null;
            if (hand.Count != 14) return false;
            var counts = new Dictionary<int, int>();
            foreach (int t in hand) counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
            if (counts.Count != 7) return false;
            foreach (var kv in counts) if (kv.Value != 2) return false;
            sets = counts.Keys.Select(t => new RiichiSet { Type = RiichiSetType.Pair, Tile = t, Opened = false }).ToList();
            // 七对子不是 4+1，但我们用 7 个 Pair 表示即可；下游只需要 CollectAllTiles
            return true;
        }

        private static bool CheckKokushi(List<int> hand, int winTile, out int pairTile) {
            pairTile = -1;
            if (hand.Count != 14) return false;
            var yaoSet = new HashSet<int>();
            int duplicated = -1;
            foreach (int t in hand) {
                if (!Yaojiu.Contains(t)) return false;
                if (!yaoSet.Add(t)) {
                    if (duplicated != -1 && duplicated != t) return false;
                    duplicated = t;
                }
            }
            if (yaoSet.Count != 13 || duplicated == -1) return false;
            pairTile = duplicated;
            return true;
        }
    }
}
