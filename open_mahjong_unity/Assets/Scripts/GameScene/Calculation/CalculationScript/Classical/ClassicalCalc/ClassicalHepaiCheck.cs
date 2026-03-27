using System;
using System.Collections.Generic;
using System.Linq;

namespace Classical {
    /// <summary>
    /// 古典麻将和牌检测与副番计算。
    /// 返回 (基础副, 总副, 副番名列表, 番名列表)。
    /// </summary>
    public class ClassicalHepaiCheck {
        private static readonly HashSet<int> Dragons = new HashSet<int> { 45, 46, 47 };
        private static readonly HashSet<int> Winds = new HashSet<int> { 41, 42, 43, 44 };
        private static readonly HashSet<int> Honors = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 };
        private static readonly HashSet<int> Terminals = new HashSet<int> { 11, 19, 21, 29, 31, 39 };
        private static readonly HashSet<int> Yaojiu;
        private static readonly HashSet<string> ManganFanSet = new HashSet<string> {
            "大三元", "大四喜", "小四喜", "天和", "地和", "九莲宝灯", "国士无双"
        };

        private static readonly Dictionary<string, int> FuValueMap = new Dictionary<string, int> {
            { "和牌", 10 }, { "自摸", 2 }, { "边嵌吊", 2 }, { "门前清", 2 },
            { "刻子", 2 }, { "暗刻", 4 }, { "明杠", 8 }, { "暗杠", 16 },
            { "幺九刻", 4 }, { "幺九暗刻", 8 }, { "幺九明杠", 16 }, { "幺九暗杠", 32 },
            { "番牌刻", 8 }, { "番牌暗刻", 16 }, { "番牌明杠", 32 }, { "番牌暗杠", 64 },
        };

        private readonly ClassicalCombinationSolver _solver = new ClassicalCombinationSolver();
        private readonly bool _debug;

        static ClassicalHepaiCheck() {
            Yaojiu = new HashSet<int>(Terminals);
            foreach (int h in Honors) Yaojiu.Add(h);
        }

        public ClassicalHepaiCheck(bool debug = false) {
            _debug = debug;
        }

        /// <summary>
        /// 和牌检测主入口，返回最优拆解的 (基础副, 总副, 副番名列表, 番名列表)。
        /// hand_list 应包含和牌张。
        /// </summary>
        public Tuple<int, int, List<string>, List<string>> HepaiCheck(
            List<int> handList, List<string> tilesCombination,
            List<string> wayToHepai, int getTile) {
            int completeStep = tilesCombination.Count * 3;
            var pt = new ClassicalPlayerTiles(handList, tilesCombination, completeStep);
            var doneList = new List<ClassicalPlayerTiles>();
            _solver.NormalCheck(pt, doneList);

            int bestBaseFu = 0, bestTotalFu = 0;
            List<string> bestFuFans = new List<string>();
            List<string> bestFans = new List<string>();

            foreach (var item in doneList) {
                _solver.BuildHandAndCombinationMapping(item);
                var (baseFu, totalFu, fuFanList, fanList) = FanCount(item, getTile, wayToHepai);
                if (totalFu > bestTotalFu) {
                    bestBaseFu = baseFu;
                    bestTotalFu = totalFu;
                    bestFuFans = fuFanList;
                    bestFans = fanList;
                }
            }
            return Tuple.Create(bestBaseFu, bestTotalFu, bestFuFans, bestFans);
        }

        /// <summary>
        /// 仅返回最大基础副数（不含番倍），供副数提示使用。
        /// </summary>
        public int FushuCheck(
            List<int> handList, List<string> tilesCombination,
            List<string> wayToHepai, int getTile) {
            int completeStep = tilesCombination.Count * 3;
            var pt = new ClassicalPlayerTiles(handList, tilesCombination, completeStep);
            var doneList = new List<ClassicalPlayerTiles>();
            _solver.NormalCheck(pt, doneList);

            int best = 0;
            foreach (var item in doneList) {
                _solver.BuildHandAndCombinationMapping(item);
                var (baseFu, _, _, _) = FanCount(item, getTile, wayToHepai);
                if (baseFu > best) best = baseFu;
            }
            return best;
        }

        private (int baseFu, int totalFu, List<string> fuFanTags, List<string> fanList) FanCount(
            ClassicalPlayerTiles pt, int getTile, List<string> wayToHepai) {
            NormalizeMeldVisibility(pt, getTile, wayToHepai);
            var combinationList = new List<string>(pt.CombinationList);
            var fanpaiSet = GetActiveFanpaiSet(wayToHepai);

            int fu = 0, fan = 0;
            var fuFanTags = new List<string>();
            var fanList = new List<string>();
            bool isMangan = false;

            if (wayToHepai.Contains("和牌")) { fu += FuValueMap["和牌"]; fuFanTags.Add("和牌"); }
            if (wayToHepai.Contains("自摸")) { fu += FuValueMap["自摸"]; fuFanTags.Add("自摸"); fanList.Add("自摸"); }
            if (wayToHepai.Contains("边嵌吊")) { fu += FuValueMap["边嵌吊"]; fuFanTags.Add("边嵌吊"); }

            var (triplets, pairTile) = ExtractSets(combinationList);
            var fuRepeatCount = new Dictionary<string, int>();
            foreach (var (sign, tile) in triplets) {
                var (addFu, tag) = CalcSetFu(sign, tile, fanpaiSet);
                fu += addFu;
                fuRepeatCount.TryGetValue(tag, out int cnt);
                fuRepeatCount[tag] = cnt + 1;
            }

            var handTiles = pt.HandTilesMapped.Count > 0
                ? new List<int>(pt.HandTilesMapped)
                : new List<int>();
            var suitSet = new HashSet<int>();
            bool hasHonor = false;
            foreach (int t in handTiles) {
                if (Honors.Contains(t)) hasHonor = true;
                else suitSet.Add(t / 10);
            }

            if (hasHonor && suitSet.Count == 1) { fan++; fanList.Add("混一色"); }
            else if (!hasHonor && suitSet.Count == 1) { fan += 3; fanList.Add("清一色"); }
            else if (handTiles.All(t => Honors.Contains(t))) { fan += 3; fanList.Add("字一色"); }

            if (triplets.Count > 0 && triplets.All(x => "KkGg".Contains(x.sign))) { fan++; fanList.Add("鸾凤和鸣"); }

            var dragonPungs = new HashSet<int>();
            var windPungs = new HashSet<int>();
            foreach (var (_, tile) in triplets) {
                if (Dragons.Contains(tile)) dragonPungs.Add(tile);
                if (Winds.Contains(tile)) windPungs.Add(tile);
            }

            if (dragonPungs.Count == 3) { fanList.Add("大三元"); isMangan = true; }
            else if (dragonPungs.Count == 2 && Dragons.Contains(pairTile)) { fan += 2; fanList.Add("小三元"); }

            if (windPungs.Count == 4) { fanList.Add("大四喜"); isMangan = true; }
            else if (windPungs.Count == 3 && Winds.Contains(pairTile)) { fanList.Add("小四喜"); isMangan = true; }

            if (wayToHepai.Contains("岭上开花") || wayToHepai.Contains("杠上开花")) { fan++; fanList.Add("岭上开花"); }
            if (wayToHepai.Contains("海底捞月")) { fan++; fanList.Add("海底捞月"); }
            if (wayToHepai.Contains("金鸡夺食") || wayToHepai.Contains("抢杠和")) { fan++; fanList.Add("金鸡夺食"); }
            if (wayToHepai.Contains("天和")) { fanList.Add("天和"); isMangan = true; }
            if (wayToHepai.Contains("地和")) { fanList.Add("地和"); isMangan = true; }
            if (wayToHepai.Contains("九莲宝灯")) { fanList.Add("九莲宝灯"); isMangan = true; }
            if (wayToHepai.Contains("国士无双")) { fanList.Add("国士无双"); isMangan = true; }

            int baseFu = Math.Min(fu, 300);
            int totalFu = Math.Min(baseFu * (1 << fan), 300);

            foreach (var kv in fuRepeatCount) {
                fuFanTags.Add(kv.Value == 1 ? kv.Key : $"{kv.Key}*{kv.Value}");
            }

            if (isMangan) {
                var manganOnly = fanList.Where(f => ManganFanSet.Contains(f)).ToList();
                return (baseFu, 300, fuFanTags, manganOnly);
            }
            return (baseFu, totalFu, fuFanTags, fanList);
        }

        private static (List<(char sign, int tile)> triplets, int pairTile) ExtractSets(List<string> combinationList) {
            var triplets = new List<(char, int)>();
            int pairTile = -1;
            foreach (string c in combinationList) {
                if (c.Length < 2) continue;
                char sign = c[0];
                if (!int.TryParse(c.Substring(1), out int tile)) continue;
                if ("kKgG".IndexOf(sign) >= 0) triplets.Add((sign, tile));
                else if (sign == 'q') pairTile = tile;
            }
            return (triplets, pairTile);
        }

        private static (int fu, string tag) CalcSetFu(char sign, int tile, HashSet<int> fanpaiSet) {
            bool isKong = sign == 'g' || sign == 'G';
            bool isConcealed = sign == 'K' || sign == 'G';
            bool isFanpai = fanpaiSet.Contains(tile);
            bool isYaojiu = !isFanpai && Yaojiu.Contains(tile);

            if (isKong) {
                if (isFanpai) { string n = isConcealed ? "番牌暗杠" : "番牌明杠"; return (FuValueMap[n], n); }
                if (isYaojiu) { string n = isConcealed ? "幺九暗杠" : "幺九明杠"; return (FuValueMap[n], n); }
                { string n = isConcealed ? "暗杠" : "明杠"; return (FuValueMap[n], n); }
            }
            if (isFanpai) { string n = isConcealed ? "番牌暗刻" : "番牌刻"; return (FuValueMap[n], n); }
            if (isYaojiu) { string n = isConcealed ? "幺九暗刻" : "幺九刻"; return (FuValueMap[n], n); }
            { string n = isConcealed ? "暗刻" : "刻子"; return (FuValueMap[n], n); }
        }

        private static HashSet<int> GetActiveFanpaiSet(List<string> wayToHepai) {
            var set = new HashSet<int>(Dragons);
            if (wayToHepai.Contains("门风东")) set.Add(41);
            else if (wayToHepai.Contains("门风南")) set.Add(42);
            else if (wayToHepai.Contains("门风西")) set.Add(43);
            else if (wayToHepai.Contains("门风北")) set.Add(44);
            return set;
        }

        private static void NormalizeMeldVisibility(ClassicalPlayerTiles pt, int getTile, List<string> wayToHepai) {
            bool isZimo = wayToHepai.Contains("妙手回春") || wayToHepai.Contains("自摸")
                || wayToHepai.Contains("杠上开花") || wayToHepai.Contains("岭上开花");
            if (isZimo) return;

            for (int i = 0; i < pt.CombinationList.Count; i++) {
                string comb = pt.CombinationList[i];
                if (comb == $"G{getTile}") {
                    if (pt.CombinationList.Contains($"S{getTile}") ||
                        pt.CombinationList.Contains($"S{getTile + 1}") ||
                        pt.CombinationList.Contains($"S{getTile - 1}"))
                        continue;
                    pt.CombinationList[i] = $"g{comb.Substring(1)}";
                    return;
                }
                if (comb == $"K{getTile}") {
                    if (pt.CombinationList.Contains($"S{getTile}") ||
                        pt.CombinationList.Contains($"S{getTile + 1}") ||
                        pt.CombinationList.Contains($"S{getTile - 1}"))
                        continue;
                    pt.CombinationList[i] = $"k{comb.Substring(1)}";
                    return;
                }
            }
        }
    }
}
