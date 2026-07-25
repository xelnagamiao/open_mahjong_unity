using System;
using System.Collections.Generic;
using System.Linq;

namespace Taiwan {
    /// <summary>
    /// 客户端提示所需的台湾馆规子集。
    /// </summary>
    public sealed class TaiwanRuleConfig {
        public bool EightPairsHalf { get; private set; }
        public int FlowerKongTai { get; private set; } = 1;
        public string FlowerScoring { get; private set; } = "seat";
        public int NoFlowerTai { get; private set; }
        public string ScoringPreset { get; private set; } = "sml";
        public int HalfExposedTai { get; private set; }
        public int RiverBottomTai { get; private set; }
        public int AllWindsTai { get; private set; }
        public int NoHonorNoFlowerTai { get; private set; }
        public int OpenKongTai { get; private set; }
        public int ConcealedKongTai { get; private set; }
        public bool HeavenlyEarthlyReadyEnabled { get; private set; } = true;
        public int PublicReadyTai { get; private set; }
        public string EightImmortalsMode { get; private set; } = "optional_separate";
        public int? TaiCap { get; private set; }

        public static TaiwanRuleConfig FromDictionary(IDictionary<string, object> values) {
            var result = new TaiwanRuleConfig();
            if (values == null) return result;
            result.EightPairsHalf = ReadBool(values, "eight_pairs_half", false);
            result.FlowerKongTai = ReadInt(values, "flower_kong_tai", 1);
            result.FlowerScoring = ReadString(values, "flower_scoring", "seat");
            result.NoFlowerTai = ReadInt(values, "no_flower_tai", 0);
            result.ScoringPreset = ReadString(values, "scoring_preset", "sml");
            result.HalfExposedTai = ReadInt(values, "half_exposed_tai", 0);
            result.RiverBottomTai = ReadInt(values, "river_bottom_tai", 0);
            result.AllWindsTai = ReadInt(values, "all_winds_tai", 0);
            result.NoHonorNoFlowerTai = ReadInt(values, "no_honor_no_flower_tai", 0);
            result.OpenKongTai = ReadInt(values, "open_kong_tai", 0);
            result.ConcealedKongTai = ReadInt(values, "concealed_kong_tai", 0);
            result.HeavenlyEarthlyReadyEnabled = ReadBool(values, "heavenly_earthly_ready_enabled", true);
            result.PublicReadyTai = ReadInt(values, "public_ready_tai", 0);
            result.EightImmortalsMode = ReadString(values, "eight_immortals_mode", "optional_separate");
            result.TaiCap = ReadNullableInt(values, "tai_cap");
            return result;
        }

        public int ResolveFanTai(string fanName, int fallback) {
            switch (fanName) {
                case "花杠": return FlowerKongTai;
                case "无花": return NoFlowerTai;
                case "公开听牌": return PublicReadyTai;
                case "半求人": return HalfExposedTai;
                case "河底捞鱼": return RiverBottomTai;
                case "见字": return AllWindsTai;
                case "无字无花": return NoHonorNoFlowerTai;
                case "明杠": return OpenKongTai;
                case "暗杠": return ConcealedKongTai;
            }
            if (ScoringPreset == "star31") {
                switch (fanName) {
                    case "天胡": return 24;
                    case "地胡": return 16;
                    case "人胡": return 0;
                    case "天听": return 8;
                    case "地听": return 4;
                    case "全求人": return 2;
                    case "字一色": return 8;
                    case "平胡": return 2;
                }
            }
            else if (ScoringPreset == "shenlaiye") {
                switch (fanName) {
                    case "天胡": return 24;
                    case "地胡": return 16;
                    case "人胡": return 8;
                    case "天听": return 0;
                    case "地听": return 4;
                    case "全求人": return 2;
                    case "字一色": return 8;
                    case "平胡": return 2;
                }
            }
            return fallback;
        }

        private static int ReadInt(IDictionary<string, object> values, string key, int fallback) {
            if (!values.TryGetValue(key, out object raw) || raw == null) return fallback;
            return int.TryParse(raw.ToString(), out int value) ? value : fallback;
        }

        private static int? ReadNullableInt(IDictionary<string, object> values, string key) {
            if (!values.TryGetValue(key, out object raw) || raw == null) return null;
            return int.TryParse(raw.ToString(), out int value) ? value : (int?)null;
        }

        private static bool ReadBool(IDictionary<string, object> values, string key, bool fallback) {
            if (!values.TryGetValue(key, out object raw) || raw == null) return fallback;
            if (raw is bool value) return value;
            return bool.TryParse(raw.ToString(), out value) ? value : fallback;
        }

        private static string ReadString(IDictionary<string, object> values, string key, string fallback) {
            if (!values.TryGetValue(key, out object raw) || raw == null) return fallback;
            string value = raw.ToString();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }

    /// <summary>
    /// 台湾麻将的无状态客户端提示计算器。
    /// 除服务端明确下发的天地听/公开听牌资格外，只计算从牌面即可确定的台种。
    /// </summary>
    public static class TaiwanCalc {
        private static readonly HandStructure Structure = HandStructures.SixteenTile;
        private static readonly int[] StructureTiles = BuildStructureTiles();
        private static readonly int[] Winds = { 41, 42, 43, 44 };
        private static readonly int[] Dragons = { 45, 46, 47 };
        private const int TingpaiCacheCapacity = 4096;
        private static readonly object TingpaiCacheLock = new object();
        private static readonly Dictionary<string, int[]> TingpaiCache =
            new Dictionary<string, int[]>();
        private static readonly Queue<string> TingpaiCacheOrder = new Queue<string>();

        public static HashSet<int> TingpaiCheck(
            IList<int> handTiles,
            IList<string> meldCodes,
            TaiwanRuleConfig rules) {
            var waits = new HashSet<int>();
            if (rules == null) rules = new TaiwanRuleConfig();
            if (handTiles == null || !TryParseMelds(meldCodes, out List<TaiwanMeld> external)) {
                return waits;
            }

            int concealedNeeded = Structure.ConcealedMeldCount(external.Count);
            int expected = Structure.ConcealedTileCount(external.Count, false);
            if (concealedNeeded < 0 || handTiles.Count != expected || !AreValidHandTiles(handTiles)) {
                return waits;
            }

            Dictionary<int, int> counts = CountTiles(handTiles);
            string cacheKey = BuildCountKey(counts, concealedNeeded)
                + "|eight_pairs_half=" + (rules.EightPairsHalf ? "1" : "0");
            if (TryGetCachedWaits(cacheKey, out HashSet<int> cachedWaits)) {
                return cachedWaits;
            }

            var meldShapeCache = new Dictionary<string, bool>();
            foreach (int tile in StructureTiles) {
                if (counts.TryGetValue(tile, out int count) && count >= 4) continue;

                counts[tile] = count + 1;
                bool isEightPairsHalfWait = rules.EightPairsHalf
                    && concealedNeeded == Structure.MeldCount
                    && IsEightPairsHalfCounts(counts);
                bool isStandardWait = isEightPairsHalfWait || HasStandardShape(
                        counts,
                        concealedNeeded,
                        meldShapeCache);
                RemoveCount(counts, tile, 1);
                if (isStandardWait) waits.Add(tile);
            }
            CacheWaits(cacheKey, waits);
            return waits;
        }

        public static Tuple<int, List<string>> HepaiCheck(
            IList<int> handTiles,
            IList<string> meldCodes,
            int winningTile,
            bool isSelfDraw,
            int seatWind,
            int roundWind,
            IList<int> flowers,
            TaiwanRuleConfig rules,
            string readyQualification = null) {
            var empty = Tuple.Create(0, new List<string>());
            if (rules == null) rules = new TaiwanRuleConfig();
            if (handTiles == null
                || !IsStructureTile(winningTile)
                || !handTiles.Contains(winningTile)
                || !TryParseMelds(meldCodes, out List<TaiwanMeld> external)) {
                return empty;
            }

            List<TaiwanDecomposition> decompositions =
                EnumerateDecompositions(handTiles, external, winningTile);
            bool eightPairsHalf = rules.EightPairsHalf && IsEightPairsHalf(handTiles, external);
            if (decompositions.Count == 0 && !eightPairsHalf) return empty;

            var preWinTiles = new List<int>(handTiles);
            if (!preWinTiles.Remove(winningTile)) return empty;
            HashSet<int> waits = TingpaiCheck(preWinTiles, meldCodes, rules);

            int bestTai = -1;
            int bestInterpretationPriority = -1;
            List<TaiwanFan> bestFans = null;
            foreach (TaiwanDecomposition decomposition in decompositions) {
                List<TaiwanFan> fans = ScoreDecomposition(
                    decomposition,
                    winningTile,
                    waits,
                    isSelfDraw,
                    seatWind,
                    roundWind,
                    flowers ?? Array.Empty<int>(),
                    rules);
                AddReadyQualificationFan(fans, readyQualification, rules);
                int tai = fans.Sum(fan => fan.Tai * fan.Count);
                int interpretationPriority = rules.ScoringPreset == "star31"
                    && !isSelfDraw
                    && decomposition.WinningKind == "triplet"
                        ? 1
                        : 0;
                if (interpretationPriority > bestInterpretationPriority
                    || (interpretationPriority == bestInterpretationPriority
                        && (tai > bestTai
                            || (tai == bestTai
                                && (bestFans == null || fans.Count > bestFans.Count))))) {
                    bestInterpretationPriority = interpretationPriority;
                    bestTai = tai;
                    bestFans = fans;
                }
            }

            if (eightPairsHalf) {
                List<TaiwanFan> fans = ScoreEightPairsHalf(
                    handTiles,
                    isSelfDraw,
                    seatWind,
                    roundWind,
                    flowers ?? Array.Empty<int>(),
                    rules);
                AddReadyQualificationFan(fans, readyQualification, rules);
                int tai = fans.Sum(fan => fan.Tai * fan.Count);
                if (bestInterpretationPriority <= 0
                    && (tai > bestTai
                        || (tai == bestTai
                            && (bestFans == null || fans.Count > bestFans.Count)))) {
                    bestInterpretationPriority = 0;
                    bestTai = tai;
                    bestFans = fans;
                }
            }

            int displayedTai = Math.Max(0, bestTai);
            if (rules.TaiCap.HasValue) displayedTai = Math.Min(displayedTai, rules.TaiCap.Value);

            return Tuple.Create(
                displayedTai,
                bestFans == null
                    ? new List<string>()
                    : bestFans.Select(fan => fan.DisplayName).ToList());
        }

        private static void AddReadyQualificationFan(
            List<TaiwanFan> fans,
            string readyQualification,
            TaiwanRuleConfig rules) {
            if (fans == null || string.IsNullOrEmpty(readyQualification)) return;
            if (readyQualification == "heavenly" && rules.HeavenlyEarthlyReadyEnabled) {
                AddFan(fans, "天听", rules.ResolveFanTai("天听", 16));
            } else if (readyQualification == "earthly" && rules.HeavenlyEarthlyReadyEnabled) {
                if (rules.ScoringPreset == "shenlaiye") {
                    fans.RemoveAll(fan => fan.Name == "门清" || fan.Name == "公开听牌");
                }
                AddFan(fans, "地听", rules.ResolveFanTai("地听", 8));
            } else if (readyQualification == "public") {
                AddFan(fans, "公开听牌", rules.PublicReadyTai);
            }
        }

        private static List<TaiwanFan> ScoreDecomposition(
            TaiwanDecomposition decomposition,
            int winningTile,
            HashSet<int> waits,
            bool isSelfDraw,
            int seatWind,
            int roundWind,
            IList<int> flowers,
            TaiwanRuleConfig rules) {
            List<TaiwanMeld> melds = decomposition.Melds;
            var tripletTiles = new HashSet<int>(
                melds.Where(meld => meld.Kind == TaiwanMeldKind.Triplet || meld.Kind == TaiwanMeldKind.Kong)
                    .Select(meld => meld.Tile));
            List<int> structure = BuildStructureTiles(decomposition);

            bool menqing = melds.All(meld => meld.Concealed);
            bool allExposed = melds.Count == Structure.MeldCount
                && melds.All(meld => meld.External && !meld.Concealed);
            bool allSequences = melds.All(meld => meld.Kind == TaiwanMeldKind.Sequence);
            bool allTriplets = melds.All(
                meld => meld.Kind == TaiwanMeldKind.Triplet || meld.Kind == TaiwanMeldKind.Kong);

            bool pinfu;
            if (rules.ScoringPreset == "star31"
                || rules.ScoringPreset == "shenlaiye") {
                pinfu = allSequences
                    && !isSelfDraw
                    && flowers.Count == 0
                    && decomposition.Pair < 40
                    && structure.All(tile => tile < 40)
                    && waits.Count >= 2
                    && IsExclusivelyTwoSidedWait(decomposition, winningTile);
            } else {
                pinfu = allSequences
                    && melds.Any(meld => meld.External)
                    && waits.Count >= 2
                    && decomposition.WinningKind != "pair";
            }
            bool singleWait = waits.Count == 1
                && IsSingleWaitUse(decomposition, winningTile)
                && !allExposed;

            int dragonTripletCount = Dragons.Count(tripletTiles.Contains);
            bool bigDragons = dragonTripletCount == 3;
            bool smallDragons = !bigDragons
                && dragonTripletCount == 2
                && Dragons.Contains(decomposition.Pair);

            int windTripletCount = Winds.Count(tripletTiles.Contains);
            bool bigWinds = windTripletCount == 4;
            bool smallWinds = !bigWinds
                && windTripletCount == 3
                && Winds.Contains(decomposition.Pair);

            var suits = new HashSet<int>(structure.Where(tile => tile < 40).Select(tile => tile / 10));
            bool hasHonor = structure.Any(tile => tile >= 40);
            bool hasNumber = structure.Any(tile => tile < 40);
            bool fullFlush = suits.Count == 1 && !hasHonor;
            bool halfFlush = suits.Count == 1 && hasHonor && hasNumber;
            bool allHonors = !hasNumber;

            var fans = new List<TaiwanFan>();
            bool pinfuExcludesMenqing = pinfu
                && rules.ScoringPreset != "star31"
                && rules.ScoringPreset != "shenlaiye";
            if (menqing && !pinfuExcludesMenqing) AddFan(fans, "门清", 1);
            if (menqing && isSelfDraw) AddFan(fans, "不求人", 1);
            if (isSelfDraw) AddFan(fans, "自摸", 1);

            bool smallWindsKeepsWindFans = smallWinds
                && (rules.ScoringPreset == "shenlaiye" || rules.ScoringPreset == "cml");
            if (!bigWinds
                && (!smallWinds || smallWindsKeepsWindFans)
                && rules.AllWindsTai == 0) {
                if (tripletTiles.Contains(seatWind)) AddFan(fans, "门风刻", 1);
                if (tripletTiles.Contains(roundWind)) AddFan(fans, "圈风刻", 1);
            }

            AddFlowerFans(fans, flowers, seatWind, rules);

            if (!(bigDragons || smallDragons) && dragonTripletCount > 0) {
                AddFan(fans, "三元牌", 1, dragonTripletCount);
            }
            if (singleWait && !pinfu) AddFan(fans, "独听", 1);
            if (pinfu) AddFan(fans, "平胡", 2);

            int concealedTriplets = CountConcealedTriplets(decomposition, isSelfDraw);
            if (concealedTriplets >= Structure.MeldCount) AddFan(fans, "五暗刻", 8);
            else if (concealedTriplets >= 4) AddFan(fans, "四暗刻", 5);
            else if (concealedTriplets >= 3) AddFan(fans, "三暗刻", 2);

            if (allExposed && !isSelfDraw) {
                AddFan(fans, "全求人", rules.ResolveFanTai("全求人", 2));
            }
            if (allTriplets && !(rules.ScoringPreset == "shenlaiye" && allHonors)) {
                AddFan(fans, "碰碰胡", 4);
            }
            if (bigDragons) AddFan(fans, "大三元", 8);
            else if (smallDragons) AddFan(fans, "小三元", 4);
            if (halfFlush) AddFan(fans, "混一色", 4);
            if (bigWinds) AddFan(fans, "大四喜", 16);
            else if (smallWinds) AddFan(fans, "小四喜", 8);
            if (fullFlush) AddFan(fans, "清一色", 8);
            if (allHonors) AddFan(fans, "字一色", rules.ResolveFanTai("字一色", 16));

            AddExtensionFans(
                fans,
                melds,
                structure,
                tripletTiles,
                allExposed,
                isSelfDraw,
                flowers,
                rules);
            ApplyEightImmortalsToNormalHand(fans, flowers, rules);
            return fans;
        }

        private static List<TaiwanFan> ScoreEightPairsHalf(
            IList<int> handTiles,
            bool isSelfDraw,
            int seatWind,
            int roundWind,
            IList<int> flowers,
            TaiwanRuleConfig rules) {
            Dictionary<int, int> counts = CountTiles(handTiles);
            int tripletTile = counts.First(entry => entry.Value == 3).Key;
            var fans = new List<TaiwanFan>();

            AddFan(fans, "门清", 1);
            if (isSelfDraw) {
                AddFan(fans, "不求人", 1);
                AddFan(fans, "自摸", 1);
            }
            if (rules.AllWindsTai == 0) {
                if (tripletTile == seatWind) AddFan(fans, "门风刻", 1);
                if (tripletTile == roundWind) AddFan(fans, "圈风刻", 1);
            }

            AddFlowerFans(fans, flowers, seatWind, rules);
            if (Dragons.Contains(tripletTile)) AddFan(fans, "三元牌", 1);

            var suits = new HashSet<int>(handTiles.Where(tile => tile < 40).Select(tile => tile / 10));
            bool hasHonor = handTiles.Any(tile => tile >= 40);
            bool hasNumber = handTiles.Any(tile => tile < 40);
            if (suits.Count == 1 && hasHonor && hasNumber) AddFan(fans, "混一色", 4);
            if (suits.Count == 1 && !hasHonor) AddFan(fans, "清一色", 8);
            if (!hasNumber) AddFan(fans, "字一色", rules.ResolveFanTai("字一色", 16));

            var tripletSet = new HashSet<int> { tripletTile };
            AddExtensionFans(fans, new List<TaiwanMeld>(), handTiles.ToList(), tripletSet, false, isSelfDraw, flowers, rules);

            ApplyEightImmortalsToNormalHand(fans, flowers, rules);
            AddFan(fans, "八对半", 8);
            return fans;
        }

        private static void AddExtensionFans(
            List<TaiwanFan> fans,
            IList<TaiwanMeld> melds,
            IList<int> structure,
            HashSet<int> tripletTiles,
            bool allExposed,
            bool isSelfDraw,
            IList<int> flowers,
            TaiwanRuleConfig rules) {
            if (rules.HalfExposedTai > 0 && allExposed && isSelfDraw) {
                AddFan(fans, "半求人", rules.HalfExposedTai);
            }
            if (rules.AllWindsTai > 0) {
                int count = Winds.Count(tripletTiles.Contains);
                if (count > 0) AddFan(fans, "见字", rules.AllWindsTai, count);
            }
            if (rules.NoHonorNoFlowerTai > 0
                && flowers.Count == 0
                && structure.All(tile => tile < 40)) {
                fans.RemoveAll(fan => fan.Name == "无花");
                AddFan(fans, "无字无花", rules.NoHonorNoFlowerTai);
            }
            if (rules.OpenKongTai > 0) {
                int count = melds.Count(meld => meld.Kind == TaiwanMeldKind.Kong && !meld.Concealed);
                if (count > 0) AddFan(fans, "明杠", rules.OpenKongTai, count);
            }
            if (rules.ConcealedKongTai > 0) {
                int count = melds.Count(meld => meld.Kind == TaiwanMeldKind.Kong && meld.Concealed);
                if (count > 0) AddFan(fans, "暗杠", rules.ConcealedKongTai, count);
            }
        }

        private static void ApplyEightImmortalsToNormalHand(
            List<TaiwanFan> fans,
            IList<int> flowers,
            TaiwanRuleConfig rules) {
            if (flowers.Distinct().Count() != 8
                || (rules.EightImmortalsMode != "add_to_normal"
                    && rules.EightImmortalsMode != "compound")) {
                return;
            }
            fans.RemoveAll(fan => fan.Name == "正花" || fan.Name == "见花" || fan.Name == "花杠");
            AddFan(fans, "八仙过海", 8);
        }

        private static int CountConcealedTriplets(
            TaiwanDecomposition decomposition,
            bool isSelfDraw) {
            int count = 0;
            for (int index = 0; index < decomposition.Melds.Count; index++) {
                TaiwanMeld meld = decomposition.Melds[index];
                if (!meld.Concealed
                    || (meld.Kind != TaiwanMeldKind.Triplet && meld.Kind != TaiwanMeldKind.Kong)) {
                    continue;
                }
                bool completedByDiscard = !isSelfDraw
                    && decomposition.WinningKind == "triplet"
                    && decomposition.WinningIndex == index
                    && meld.Kind == TaiwanMeldKind.Triplet;
                if (!completedByDiscard) count++;
            }
            return count;
        }

        private static void AddFlowerFans(
            List<TaiwanFan> fans,
            IList<int> flowers,
            int seatWind,
            TaiwanRuleConfig rules) {
            var flowerSet = new HashSet<int>(flowers);
            var flowerKongTiles = new HashSet<int>();
            int flowerKongCount = 0;
            foreach (int firstTile in new[] { 51, 55 }) {
                var group = Enumerable.Range(firstTile, 4);
                if (!group.All(flowerSet.Contains)) continue;
                flowerKongCount++;
                flowerKongTiles.UnionWith(group);
            }

            if (rules.FlowerScoring == "any") {
                if (flowers.Count > 0) AddFan(fans, "见花", 1, flowers.Count);
            } else {
                int firstFlower = 51 + Math.Max(0, Math.Min(3, seatWind - 41));
                int secondFlower = firstFlower + 4;
                int correctCount = flowers.Count(tile =>
                    (tile == firstFlower || tile == secondFlower)
                    && !flowerKongTiles.Contains(tile));
                if (correctCount > 0) AddFan(fans, "正花", 1, correctCount);
            }

            if (flowerKongCount > 0) AddFan(fans, "花杠", rules.FlowerKongTai, flowerKongCount);
            if (flowers.Count == 0 && rules.NoFlowerTai > 0) {
                AddFan(fans, "无花", rules.NoFlowerTai);
            }
        }

        private static void AddFan(List<TaiwanFan> fans, string name, int tai, int count = 1) {
            if (tai > 0 && count > 0) fans.Add(new TaiwanFan(name, tai, count));
        }

        private static bool IsSingleWaitUse(TaiwanDecomposition decomposition, int winningTile) {
            if (decomposition.WinningKind == "pair") return true;
            if (decomposition.WinningKind != "sequence"
                || decomposition.WinningIndex < 0
                || decomposition.WinningIndex >= decomposition.Melds.Count) {
                return false;
            }
            TaiwanMeld meld = decomposition.Melds[decomposition.WinningIndex];
            int low = meld.Tile - 1;
            int middle = meld.Tile;
            int high = meld.Tile + 1;
            if (winningTile == middle) return true;
            int rank = winningTile % 10;
            return (winningTile == high && rank == 3)
                || (winningTile == low && rank == 7);
        }

        private static bool IsExclusivelyTwoSidedWait(
            TaiwanDecomposition decomposition,
            int winningTile) {
            if (decomposition.Pair == winningTile) return false;
            List<TaiwanMeld> uses = decomposition.Melds
                .Where(meld => meld.Kind == TaiwanMeldKind.Sequence
                    && !meld.External
                    && meld.Contains(winningTile))
                .ToList();
            if (uses.Count == 0) return false;
            foreach (TaiwanMeld meld in uses) {
                int low = meld.Tile - 1;
                int middle = meld.Tile;
                int high = meld.Tile + 1;
                if (winningTile == middle) return false;
                int rank = winningTile % 10;
                if ((winningTile == high && rank == 3)
                    || (winningTile == low && rank == 7)) {
                    return false;
                }
            }
            return true;
        }

        private static List<int> BuildStructureTiles(TaiwanDecomposition decomposition) {
            var tiles = new List<int> { decomposition.Pair, decomposition.Pair };
            foreach (TaiwanMeld meld in decomposition.Melds) {
                if (meld.Kind == TaiwanMeldKind.Sequence) {
                    tiles.Add(meld.Tile - 1);
                    tiles.Add(meld.Tile);
                    tiles.Add(meld.Tile + 1);
                } else {
                    tiles.Add(meld.Tile);
                    tiles.Add(meld.Tile);
                    tiles.Add(meld.Tile);
                }
            }
            return tiles;
        }

        private static List<TaiwanDecomposition> EnumerateDecompositions(
            IList<int> handTiles,
            List<TaiwanMeld> external,
            int winningTile) {
            int concealedNeeded = Structure.ConcealedMeldCount(external.Count);
            if (concealedNeeded < 0
                || handTiles.Count != Structure.ConcealedTileCount(external.Count, true)
                || !AreValidHandTiles(handTiles)) {
                return new List<TaiwanDecomposition>();
            }

            Dictionary<int, int> counts = CountTiles(handTiles);
            var pairTiles = counts.Where(entry => entry.Value >= 2).Select(entry => entry.Key).OrderBy(tile => tile);
            var results = new List<TaiwanDecomposition>();
            var partitionCache = new Dictionary<string, List<List<TaiwanMeld>>>();

            foreach (int pair in pairTiles) {
                Dictionary<int, int> remainder = CloneCounts(counts);
                RemoveCount(remainder, pair, 2);
                List<List<TaiwanMeld>> partitions = EnumerateMeldPartitions(
                    remainder,
                    concealedNeeded,
                    partitionCache);
                foreach (List<TaiwanMeld> concealed in partitions) {
                    var melds = new List<TaiwanMeld>(external.Count + concealed.Count);
                    melds.AddRange(external);
                    melds.AddRange(concealed);

                    if (pair == winningTile) {
                        results.Add(new TaiwanDecomposition(pair, melds, "pair", -1));
                    }
                    for (int index = external.Count; index < melds.Count; index++) {
                        TaiwanMeld meld = melds[index];
                        if (!meld.Contains(winningTile)) continue;
                        results.Add(new TaiwanDecomposition(
                            pair,
                            melds,
                            meld.Kind == TaiwanMeldKind.Sequence ? "sequence" : "triplet",
                            index));
                    }
                }
            }

            var unique = new Dictionary<string, TaiwanDecomposition>();
            foreach (TaiwanDecomposition result in results) {
                unique[result.StableKey] = result;
            }
            return unique.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        }

        private static List<List<TaiwanMeld>> EnumerateMeldPartitions(
            Dictionary<int, int> counts,
            int needed,
            Dictionary<string, List<List<TaiwanMeld>>> cache) {
            string cacheKey = BuildCountKey(counts, needed);
            if (cache.TryGetValue(cacheKey, out List<List<TaiwanMeld>> cached)) return cached;

            int remaining = counts.Values.Sum();
            var results = new List<List<TaiwanMeld>>();
            if (needed == 0) {
                if (remaining == 0) results.Add(new List<TaiwanMeld>());
                cache[cacheKey] = results;
                return results;
            }
            if (remaining != needed * 3 || counts.Count == 0) {
                cache[cacheKey] = results;
                return results;
            }

            int tile = counts.Keys.Min();
            if (counts[tile] >= 3) {
                Dictionary<int, int> next = CloneCounts(counts);
                RemoveCount(next, tile, 3);
                foreach (List<TaiwanMeld> rest in EnumerateMeldPartitions(next, needed - 1, cache)) {
                    var partition = new List<TaiwanMeld> {
                        new TaiwanMeld(TaiwanMeldKind.Triplet, tile, true, false)
                    };
                    partition.AddRange(rest);
                    results.Add(partition);
                }
            }

            if (IsNumberTile(tile) && tile % 10 <= 7
                && counts.ContainsKey(tile + 1)
                && counts.ContainsKey(tile + 2)
                && (tile + 2) / 10 == tile / 10) {
                Dictionary<int, int> next = CloneCounts(counts);
                RemoveCount(next, tile, 1);
                RemoveCount(next, tile + 1, 1);
                RemoveCount(next, tile + 2, 1);
                foreach (List<TaiwanMeld> rest in EnumerateMeldPartitions(next, needed - 1, cache)) {
                    var partition = new List<TaiwanMeld> {
                        new TaiwanMeld(TaiwanMeldKind.Sequence, tile + 1, true, false)
                    };
                    partition.AddRange(rest);
                    results.Add(partition);
                }
            }

            results = results
                .GroupBy(partition => string.Join(",", partition.Select(meld => meld.PartitionKey)))
                .Select(group => group.First())
                .OrderBy(partition => string.Join(",", partition.Select(meld => meld.PartitionKey)))
                .ToList();
            cache[cacheKey] = results;
            return results;
        }

        /// <summary>
        /// 求听只需要知道是否存在合法拆分；使用布尔短路搜索，避免为每个候选牌
        /// 构造完整的 TaiwanDecomposition、面子列表和所有等价拆分。
        /// </summary>
        private static bool HasStandardShape(
            Dictionary<int, int> counts,
            int concealedNeeded,
            Dictionary<string, bool> meldShapeCache) {
            if (counts.Values.Sum() != concealedNeeded * 3 + 2) return false;

            List<int> pairTiles = counts
                .Where(entry => entry.Value >= 2)
                .Select(entry => entry.Key)
                .OrderBy(tile => tile)
                .ToList();
            foreach (int pair in pairTiles) {
                Dictionary<int, int> remainder = CloneCounts(counts);
                RemoveCount(remainder, pair, 2);
                if (CanFormMelds(remainder, concealedNeeded, meldShapeCache)) return true;
            }
            return false;
        }

        private static bool CanFormMelds(
            Dictionary<int, int> counts,
            int needed,
            Dictionary<string, bool> cache) {
            string cacheKey = BuildCountKey(counts, needed);
            if (cache.TryGetValue(cacheKey, out bool cached)) return cached;

            int remaining = counts.Values.Sum();
            if (needed == 0) {
                bool complete = remaining == 0;
                cache[cacheKey] = complete;
                return complete;
            }
            if (remaining != needed * 3 || counts.Count == 0) {
                cache[cacheKey] = false;
                return false;
            }

            int tile = counts.Keys.Min();
            if (counts[tile] >= 3) {
                Dictionary<int, int> next = CloneCounts(counts);
                RemoveCount(next, tile, 3);
                if (CanFormMelds(next, needed - 1, cache)) {
                    cache[cacheKey] = true;
                    return true;
                }
            }

            if (IsNumberTile(tile) && tile % 10 <= 7
                && counts.ContainsKey(tile + 1)
                && counts.ContainsKey(tile + 2)
                && (tile + 2) / 10 == tile / 10) {
                Dictionary<int, int> next = CloneCounts(counts);
                RemoveCount(next, tile, 1);
                RemoveCount(next, tile + 1, 1);
                RemoveCount(next, tile + 2, 1);
                if (CanFormMelds(next, needed - 1, cache)) {
                    cache[cacheKey] = true;
                    return true;
                }
            }

            cache[cacheKey] = false;
            return false;
        }

        /// <summary>判断完整暗手是否由一组三张与其余七对组成；四张同牌按两对处理。</summary>
        private static bool IsEightPairsHalfCounts(Dictionary<int, int> counts) {
            if (counts.Values.Sum() != Structure.CompleteHandTileCount
                || counts.Values.Any(count => count > 4)) {
                return false;
            }
            List<int> triplets = counts
                .Where(entry => entry.Value == 3)
                .Select(entry => entry.Key)
                .ToList();
            if (triplets.Count != 1) return false;
            return counts
                .Where(entry => entry.Key != triplets[0])
                .Sum(entry => entry.Value / 2) == 7;
        }

        private static bool TryGetCachedWaits(string cacheKey, out HashSet<int> waits) {
            lock (TingpaiCacheLock) {
                if (TingpaiCache.TryGetValue(cacheKey, out int[] cached)) {
                    waits = new HashSet<int>(cached);
                    return true;
                }
            }
            waits = null;
            return false;
        }

        private static void CacheWaits(string cacheKey, HashSet<int> waits) {
            lock (TingpaiCacheLock) {
                if (TingpaiCache.ContainsKey(cacheKey)) return;
                while (TingpaiCache.Count >= TingpaiCacheCapacity && TingpaiCacheOrder.Count > 0) {
                    TingpaiCache.Remove(TingpaiCacheOrder.Dequeue());
                }
                TingpaiCache[cacheKey] = waits.OrderBy(tile => tile).ToArray();
                TingpaiCacheOrder.Enqueue(cacheKey);
            }
        }

        private static bool TryParseMelds(
            IList<string> codes,
            out List<TaiwanMeld> melds) {
            melds = new List<TaiwanMeld>();
            if (codes == null) return true;
            if (codes.Count > Structure.MeldCount) return false;

            foreach (string code in codes) {
                if (string.IsNullOrEmpty(code) || code.Length < 2) return false;
                char sign = code[0];
                if (!int.TryParse(code.Substring(1), out int tile)) return false;

                if (sign == 's') {
                    if (!IsNumberTile(tile) || tile % 10 < 2 || tile % 10 > 8) return false;
                    melds.Add(new TaiwanMeld(TaiwanMeldKind.Sequence, tile, false, true));
                } else if (sign == 'k' && IsStructureTile(tile)) {
                    melds.Add(new TaiwanMeld(TaiwanMeldKind.Triplet, tile, false, true));
                } else if (sign == 'g' && IsStructureTile(tile)) {
                    melds.Add(new TaiwanMeld(TaiwanMeldKind.Kong, tile, false, true));
                } else if (sign == 'G' && IsStructureTile(tile)) {
                    melds.Add(new TaiwanMeld(TaiwanMeldKind.Kong, tile, true, true));
                } else {
                    return false;
                }
            }
            return true;
        }

        private static bool IsEightPairsHalf(
            IList<int> handTiles,
            IList<TaiwanMeld> external) {
            if (external.Count != 0
                || handTiles.Count != Structure.CompleteHandTileCount
                || !AreValidHandTiles(handTiles)) {
                return false;
            }
            Dictionary<int, int> counts = CountTiles(handTiles);
            return IsEightPairsHalfCounts(counts);
        }

        private static bool AreValidHandTiles(IList<int> handTiles) {
            if (handTiles.Any(tile => !IsStructureTile(tile))) return false;
            return CountTiles(handTiles).Values.All(count => count <= 4);
        }

        private static Dictionary<int, int> CountTiles(IEnumerable<int> tiles) {
            var counts = new Dictionary<int, int>();
            foreach (int tile in tiles) {
                counts[tile] = counts.TryGetValue(tile, out int count) ? count + 1 : 1;
            }
            return counts;
        }

        private static Dictionary<int, int> CloneCounts(Dictionary<int, int> counts) {
            return counts.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        private static void RemoveCount(Dictionary<int, int> counts, int tile, int amount) {
            int remaining = counts[tile] - amount;
            if (remaining == 0) counts.Remove(tile);
            else counts[tile] = remaining;
        }

        private static string BuildCountKey(Dictionary<int, int> counts, int needed) {
            return needed + ":" + string.Join(",", counts.OrderBy(entry => entry.Key)
                .Select(entry => entry.Key + "x" + entry.Value));
        }

        private static bool IsStructureTile(int tile) {
            return IsNumberTile(tile) || (tile >= 41 && tile <= 47);
        }

        private static bool IsNumberTile(int tile) {
            int suit = tile / 10;
            int rank = tile % 10;
            return suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9;
        }

        private static int[] BuildStructureTiles() {
            var tiles = new List<int>();
            for (int suit = 1; suit <= 3; suit++) {
                for (int rank = 1; rank <= 9; rank++) tiles.Add(suit * 10 + rank);
            }
            for (int tile = 41; tile <= 47; tile++) tiles.Add(tile);
            return tiles.ToArray();
        }
    }

    internal enum TaiwanMeldKind {
        Sequence,
        Triplet,
        Kong,
    }

    internal sealed class TaiwanMeld {
        public TaiwanMeldKind Kind { get; }
        public int Tile { get; }
        public bool Concealed { get; }
        public bool External { get; }

        public TaiwanMeld(
            TaiwanMeldKind kind,
            int tile,
            bool concealed,
            bool external) {
            Kind = kind;
            Tile = tile;
            Concealed = concealed;
            External = external;
        }

        public bool Contains(int tile) {
            if (Kind == TaiwanMeldKind.Sequence) {
                return tile >= Tile - 1 && tile <= Tile + 1;
            }
            return tile == Tile;
        }

        public string PartitionKey => ((int)Kind) + ":" + Tile;
        public string StableKey => PartitionKey + ":" + (Concealed ? "c" : "o") + (External ? "e" : "i");
    }

    internal sealed class TaiwanDecomposition {
        public int Pair { get; }
        public List<TaiwanMeld> Melds { get; }
        public string WinningKind { get; }
        public int WinningIndex { get; }

        public TaiwanDecomposition(
            int pair,
            List<TaiwanMeld> melds,
            string winningKind,
            int winningIndex) {
            Pair = pair;
            Melds = new List<TaiwanMeld>(melds);
            WinningKind = winningKind;
            WinningIndex = winningIndex;
        }

        public string StableKey => Pair
            + "|" + string.Join(",", Melds.Select(meld => meld.StableKey))
            + "|" + WinningKind + ":" + WinningIndex;
    }

    internal sealed class TaiwanFan {
        public string Name { get; }
        public int Tai { get; }
        public int Count { get; }

        public TaiwanFan(string name, int tai, int count) {
            Name = name;
            Tai = tai;
            Count = count;
        }

        public string DisplayName => Count == 1 ? Name : Name + "*" + Count;
    }
}
