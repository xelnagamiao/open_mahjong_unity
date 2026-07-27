using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Taiwan {
    /// <summary>
    /// 台种目录的稳定展示分类。仅用于分组与排序，不参与计分判定或房间序列化。
    /// </summary>
    public enum TaiwanFanCategory {
        Basic = 0,
        HandPattern = 1,
        Honors = 2,
        Flowers = 3,
        Kongs = 4,
        Ready = 5,
        WinEvent = 6,
        SpecialWin = 7,
    }

    /// <summary>
    /// 台种的展示计数方式。实际成立次数仍由计分结果中的 Count 决定。
    /// </summary>
    public enum TaiwanFanCountMode {
        Once = 0,
        PerGroup = 1,
        PerTile = 2,
    }

    public sealed class TaiwanFanDefinition {
        public TaiwanFanCategory Group { get; }
        public string Id { get; }
        public string Name { get; }
        public int Fan { get; }
        public TaiwanFanCountMode Unit { get; }

        public TaiwanFanDefinition(
            TaiwanFanCategory group,
            string id,
            string name,
            int fan,
            TaiwanFanCountMode unit) {
            if (!Enum.IsDefined(typeof(TaiwanFanCategory), group)) {
                throw new ArgumentOutOfRangeException(nameof(group), group, "未知台湾麻将台种分类");
            }
            if (string.IsNullOrWhiteSpace(id)) {
                throw new ArgumentException("台湾麻将台种 ID 不能为空", nameof(id));
            }
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("台湾麻将台种名称不能为空", nameof(name));
            }
            if (fan < 1 || fan > 64) {
                throw new ArgumentOutOfRangeException(nameof(fan), fan, "台湾麻将基础台值必须在 1 至 64 之间");
            }
            if (!Enum.IsDefined(typeof(TaiwanFanCountMode), unit)) {
                throw new ArgumentOutOfRangeException(nameof(unit), unit, "未知台湾麻将台种计数方式");
            }
            Group = group;
            Id = id;
            Name = name;
            Fan = fan;
            Unit = unit;
        }
    }

    /// <summary>
    /// 台湾麻将稳定台种目录与完整基础台表。
    /// fan_id 会写入馆规及牌谱，不得复用或随显示名调整。
    /// </summary>
    public static class TaiwanFanCatalog {
        public static readonly IReadOnlyList<TaiwanFanDefinition> Definitions =
            new List<TaiwanFanDefinition> {
                new TaiwanFanDefinition(TaiwanFanCategory.Basic, "concealed_hand", "门清", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Basic, "self_draw", "自摸", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Basic, "single_wait", "独听", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Basic, "fully_concealed_hand", "不求人", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Basic, "all_begging", "全求人", 2, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Basic, "half_begging", "半求人", 1, TaiwanFanCountMode.Once),

                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "all_chows", "平胡", 2, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "three_concealed_pungs", "三暗刻", 2, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "four_concealed_pungs", "四暗刻", 5, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "five_concealed_pungs", "五暗刻", 8, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "all_pungs", "碰碰胡", 4, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "half_flush", "混一色", 4, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "full_flush", "清一色", 8, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "no_flowers_or_honors", "无字无花", 2, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.HandPattern, "eight_and_a_half_pairs", "八对半", 8, TaiwanFanCountMode.Once),

                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "seat_wind_pung", "门风刻", 1, TaiwanFanCountMode.PerGroup),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "prevalent_wind_pung", "圈风刻", 1, TaiwanFanCountMode.PerGroup),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "wind_pung", "见字", 1, TaiwanFanCountMode.PerGroup),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "dragon_pung", "三元牌", 1, TaiwanFanCountMode.PerGroup),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "little_three_dragons", "小三元", 4, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "big_three_dragons", "大三元", 8, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "little_four_winds", "小四喜", 8, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "big_four_winds", "大四喜", 16, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Honors, "all_honors", "字一色", 16, TaiwanFanCountMode.Once),

                new TaiwanFanDefinition(TaiwanFanCategory.Flowers, "flower_tile", "正花", 1, TaiwanFanCountMode.PerTile),
                new TaiwanFanDefinition(TaiwanFanCategory.Flowers, "flower_kong", "花杠", 1, TaiwanFanCountMode.PerGroup),
                new TaiwanFanDefinition(TaiwanFanCategory.Flowers, "no_flowers", "无花", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Flowers, "initial_flower_bonus", "配牌花胡", 4, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Flowers, "eight_flowers_and_seasons", "八仙过海", 8, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Flowers, "seven_flowers_steal_eighth", "七抢一", 8, TaiwanFanCountMode.Once),

                new TaiwanFanDefinition(TaiwanFanCategory.Kongs, "melded_kong", "明杠", 1, TaiwanFanCountMode.PerGroup),
                new TaiwanFanDefinition(TaiwanFanCategory.Kongs, "concealed_kong", "暗杠", 2, TaiwanFanCountMode.PerGroup),

                new TaiwanFanDefinition(TaiwanFanCategory.Ready, "declared_ready", "报听", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Ready, "heavenly_ready", "天听", 16, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.Ready, "earthly_ready", "地听", 8, TaiwanFanCountMode.Once),

                new TaiwanFanDefinition(TaiwanFanCategory.WinEvent, "robbing_kong", "抢杠", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.WinEvent, "out_with_replacement_tile", "杠上开花", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.WinEvent, "last_tile_draw", "海底捞月", 1, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.WinEvent, "last_tile_claim", "河底捞鱼", 1, TaiwanFanCountMode.Once),

                new TaiwanFanDefinition(TaiwanFanCategory.SpecialWin, "heavenly_win", "天胡", 24, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.SpecialWin, "earthly_win", "地胡", 16, TaiwanFanCountMode.Once),
                new TaiwanFanDefinition(TaiwanFanCategory.SpecialWin, "human_win", "人胡", 16, TaiwanFanCountMode.Once),
            }.AsReadOnly();

        private static readonly Dictionary<string, TaiwanFanDefinition> ById =
            Definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> IdByName = BuildIdByName();
        private static readonly Dictionary<string, Dictionary<string, int>> PresetTables =
            BuildPresetTables();

        public static bool TryGetDefinition(string fanId, out TaiwanFanDefinition definition) {
            return ById.TryGetValue(fanId ?? "", out definition);
        }

        public static bool TryResolveFanId(string fanIdOrName, out string fanId) {
            if (ById.ContainsKey(fanIdOrName ?? "")) {
                fanId = fanIdOrName;
                return true;
            }
            return IdByName.TryGetValue(fanIdOrName ?? "", out fanId);
        }

        public static int GetPresetTai(string scoringPreset, string fanId) {
            if (!PresetTables.TryGetValue(scoringPreset ?? "", out Dictionary<string, int> table)) {
                throw new ArgumentException(
                    $"未知台湾麻将基础台表：{scoringPreset}",
                    nameof(scoringPreset));
            }
            if (!table.TryGetValue(fanId ?? "", out int value)) {
                throw new ArgumentException(
                    $"未知台湾麻将台种：{fanId}",
                    nameof(fanId));
            }
            return value;
        }

        private static Dictionary<string, string> BuildIdByName() {
            var result = Definitions.ToDictionary(
                definition => definition.Name,
                definition => definition.Id,
                StringComparer.Ordinal);
            result["见花"] = "flower_tile";
            return result;
        }

        private static Dictionary<string, Dictionary<string, int>> BuildPresetTables() {
            var sml = Definitions.ToDictionary(
                definition => definition.Id,
                definition => definition.Fan,
                StringComparer.Ordinal);
            var cml = new Dictionary<string, int>(sml, StringComparer.Ordinal);
            var star31 = new Dictionary<string, int>(sml, StringComparer.Ordinal) {
                ["flower_kong"] = 2,
                ["heavenly_ready"] = 8,
                ["earthly_ready"] = 4,
                ["all_honors"] = 8,
            };
            var shenlaiye = new Dictionary<string, int>(sml, StringComparer.Ordinal) {
                ["flower_kong"] = 2,
                ["human_win"] = 8,
                ["earthly_ready"] = 4,
                ["all_honors"] = 8,
            };
            return new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal) {
                ["sml"] = sml,
                ["cml"] = cml,
                ["star31"] = star31,
                ["shenlaiye"] = shenlaiye,
            };
        }
    }

    /// <summary>
    /// 客户端提示所需的台湾馆规子集。
    /// </summary>
    public sealed class TaiwanRuleConfig {
        public bool EightAndAHalfPairsEnabled { get; private set; }
        public string FlowerScoringMode { get; private set; } = "seat_flowers_only";
        public bool NoFlowersEnabled { get; private set; }
        public string ScoringPreset { get; private set; } = "sml";
        public string AllChowsDefinition { get; private set; } = "relaxed";
        public bool LittleFourWindsAddWindPungs { get; private set; }
        public bool AllHonorsAddAllPungs { get; private set; } = true;
        public bool PreferTripletDecompositionOnDiscardWin { get; private set; }
        public bool EarthlyReadyExcludesConcealedAndDeclaredReady { get; private set; }
        public bool HalfBeggingEnabled { get; private set; }
        public bool AllWindPungsEnabled { get; private set; }
        public bool NoFlowersOrHonorsEnabled { get; private set; }
        public bool MeldedKongEnabled { get; private set; }
        public bool ConcealedKongEnabled { get; private set; }
        public string ReadyQualificationMode { get; private set; } = "standard_with_dealer_heavenly_ready";
        public bool PublicReadyEnabled { get; private set; }
        public string EightFlowersMode { get; private set; } = "optional_standalone";
        public int? TaiCap { get; private set; }
        private Dictionary<string, int> FanTaiOverrides { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public static TaiwanRuleConfig FromDictionary(IDictionary<string, object> values) {
            var result = new TaiwanRuleConfig();
            if (values == null) return result;
            result.EightAndAHalfPairsEnabled = ReadBool(values, "eight_and_a_half_pairs_enabled", false);
            result.FlowerScoringMode = ReadString(values, "flower_scoring_mode", "seat_flowers_only");
            result.NoFlowersEnabled = ReadBool(values, "no_flowers_enabled", false);
            result.ScoringPreset = ReadString(values, "scoring_preset", "sml");
            result.AllChowsDefinition = ReadString(
                values,
                "all_chows_definition",
                "relaxed");
            result.LittleFourWindsAddWindPungs = ReadBool(
                values,
                "little_four_winds_add_wind_pungs",
                false);
            result.AllHonorsAddAllPungs = ReadBool(
                values,
                "all_honors_add_all_pungs",
                true);
            result.PreferTripletDecompositionOnDiscardWin = ReadBool(
                values,
                "prefer_triplet_decomposition_on_discard_win",
                false);
            result.EarthlyReadyExcludesConcealedAndDeclaredReady = ReadBool(
                values,
                "earthly_ready_excludes_concealed_and_declared_ready",
                false);
            result.HalfBeggingEnabled = ReadBool(values, "half_begging_enabled", false);
            result.AllWindPungsEnabled = ReadBool(values, "all_wind_pungs_enabled", false);
            result.NoFlowersOrHonorsEnabled = ReadBool(values, "no_flowers_or_honors_enabled", false);
            result.MeldedKongEnabled = ReadBool(values, "melded_kong_enabled", false);
            result.ConcealedKongEnabled = ReadBool(values, "concealed_kong_enabled", false);
            result.ReadyQualificationMode = ReadString(values, "ready_qualification_mode", "standard_with_dealer_heavenly_ready");
            result.PublicReadyEnabled = ReadBool(values, "public_ready_enabled", false);
            result.EightFlowersMode = ReadString(values, "eight_flowers_mode", "optional_standalone");
            result.TaiCap = ReadNullableInt(values, "tai_cap");
            foreach (KeyValuePair<string, int> entry in ReadFanTaiOverrides(values)) {
                if (TaiwanFanCatalog.TryGetDefinition(entry.Key, out _)
                    && entry.Value >= 1
                    && entry.Value <= 64) {
                    result.FanTaiOverrides[entry.Key] = entry.Value;
                }
            }
            return result;
        }

        public int ResolveFanTai(string fanIdOrName, int fallback) {
            if (!TaiwanFanCatalog.TryResolveFanId(fanIdOrName, out string fanId)) {
                return fallback;
            }
            if (FanTaiOverrides.TryGetValue(fanId, out int customTai)) {
                return customTai;
            }
            int presetTai = TaiwanFanCatalog.GetPresetTai(ScoringPreset, fanId);
            return presetTai > 0 ? presetTai : fallback;
        }

        internal void ApplyScoringTable(IEnumerable<TaiwanFan> fans) {
            foreach (TaiwanFan fan in fans) {
                fan.SetTai(ResolveFanTai(fan.FanId, fan.Tai));
            }
        }

        private static Dictionary<string, int> ReadFanTaiOverrides(
            IDictionary<string, object> values) {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!values.TryGetValue("fan_tai_overrides", out object raw) || raw == null) {
                return result;
            }
            if (raw is JObject jsonObject) {
                foreach (JProperty property in jsonObject.Properties()) {
                    if (int.TryParse(property.Value.ToString(), out int tai)) {
                        result[property.Name] = tai;
                    }
                }
                return result;
            }
            if (raw is IDictionary<string, int> intValues) {
                foreach (KeyValuePair<string, int> entry in intValues) {
                    result[entry.Key] = entry.Value;
                }
                return result;
            }
            if (raw is IDictionary<string, object> objectValues) {
                foreach (KeyValuePair<string, object> entry in objectValues) {
                    if (entry.Value != null
                        && int.TryParse(entry.Value.ToString(), out int tai)) {
                        result[entry.Key] = tai;
                    }
                }
                return result;
            }
            if (raw is IDictionary dictionary) {
                foreach (DictionaryEntry entry in dictionary) {
                    if (entry.Key != null
                        && entry.Value != null
                        && int.TryParse(entry.Value.ToString(), out int tai)) {
                        result[entry.Key.ToString()] = tai;
                    }
                }
            }
            return result;
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
            Dictionary<int, int> physicalCounts = CloneCounts(counts);
            foreach (TaiwanMeld meld in external) {
                if (meld.Kind == TaiwanMeldKind.Sequence) {
                    IncrementCount(physicalCounts, meld.Tile - 1);
                    IncrementCount(physicalCounts, meld.Tile);
                    IncrementCount(physicalCounts, meld.Tile + 1);
                } else {
                    int count = meld.Kind == TaiwanMeldKind.Kong ? 4 : 3;
                    physicalCounts[meld.Tile] = physicalCounts.TryGetValue(
                        meld.Tile,
                        out int existing)
                        ? existing + count
                        : count;
                }
            }
            if (physicalCounts.Values.Any(count => count > 4)) {
                return waits;
            }
            string cacheKey = BuildCountKey(counts, concealedNeeded)
                + "|eight_and_a_half_pairs_enabled=" + (rules.EightAndAHalfPairsEnabled ? "1" : "0");
            if (TryGetCachedWaits(cacheKey, out HashSet<int> cachedWaits)) {
                return new HashSet<int>(cachedWaits.Where(tile =>
                    !physicalCounts.TryGetValue(tile, out int physicalCount)
                    || physicalCount < 4));
            }

            var meldShapeCache = new Dictionary<string, bool>();
            foreach (int tile in StructureTiles) {
                if (physicalCounts.TryGetValue(tile, out int physicalCount)
                    && physicalCount >= 4) {
                    continue;
                }
                int count = counts.TryGetValue(tile, out int existingCount)
                    ? existingCount
                    : 0;

                counts[tile] = count + 1;
                bool isEightPairsHalfWait = rules.EightAndAHalfPairsEnabled
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
            bool eightPairsHalf = rules.EightAndAHalfPairsEnabled && IsEightPairsHalf(handTiles, external);
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
                rules.ApplyScoringTable(fans);
                int tai = fans.Sum(fan => fan.Tai * fan.Count);
                int interpretationPriority = rules.PreferTripletDecompositionOnDiscardWin
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
                rules.ApplyScoringTable(fans);
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
            if (readyQualification == "heavenly" && rules.ReadyQualificationMode != "disabled") {
                AddFan(fans, "天听", rules.ResolveFanTai("天听", 16));
            } else if (readyQualification == "earthly" && rules.ReadyQualificationMode != "disabled") {
                if (rules.EarthlyReadyExcludesConcealedAndDeclaredReady) {
                    fans.RemoveAll(fan => fan.Name == "门清" || fan.Name == "报听");
                }
                AddFan(fans, "地听", rules.ResolveFanTai("地听", 8));
            } else if (readyQualification == "public" && rules.PublicReadyEnabled) {
                AddFan(fans, "报听", rules.ResolveFanTai("报听", 1));
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

            bool concealed_hand = melds.All(meld => meld.Concealed);
            bool allExposed = melds.Count == Structure.MeldCount
                && melds.All(meld => meld.External && !meld.Concealed);
            bool allSequences = melds.All(meld => meld.Kind == TaiwanMeldKind.Sequence);
            bool allTriplets = melds.All(
                meld => meld.Kind == TaiwanMeldKind.Triplet || meld.Kind == TaiwanMeldKind.Kong);

            bool all_chows = allSequences
                && !concealed_hand
                && !isSelfDraw
                && waits.Count >= 2;
            if (rules.AllChowsDefinition == "strict") {
                all_chows = all_chows
                    && flowers.Count == 0
                    && structure.All(tile => tile < 40);
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
            if (concealed_hand) AddFan(fans, "门清", 1);
            if (concealed_hand && isSelfDraw) AddFan(fans, "不求人", 1);
            if (isSelfDraw) AddFan(fans, "自摸", 1);

            bool smallWindsKeepsWindFans = smallWinds
                && rules.LittleFourWindsAddWindPungs;
            if (!bigWinds
                && (!smallWinds || smallWindsKeepsWindFans)
                && !rules.AllWindPungsEnabled) {
                if (tripletTiles.Contains(seatWind)) AddFan(fans, "门风刻", 1);
                if (tripletTiles.Contains(roundWind)) AddFan(fans, "圈风刻", 1);
            }

            AddFlowerFans(fans, flowers, seatWind, rules);

            if (!(bigDragons || smallDragons) && dragonTripletCount > 0) {
                AddFan(fans, "三元牌", 1, dragonTripletCount);
            }
            if (singleWait) AddFan(fans, "独听", 1);
            if (all_chows) AddFan(fans, "平胡", 2);

            int concealedTriplets = CountConcealedTriplets(decomposition, isSelfDraw);
            if (concealedTriplets >= Structure.MeldCount) AddFan(fans, "五暗刻", 8);
            else if (concealedTriplets >= 4) AddFan(fans, "四暗刻", 5);
            else if (concealedTriplets >= 3) AddFan(fans, "三暗刻", 2);

            if (allExposed && !isSelfDraw) {
                AddFan(fans, "全求人", rules.ResolveFanTai("全求人", 2));
            }
            if (allTriplets && (rules.AllHonorsAddAllPungs || !allHonors)) {
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
            ApplyEightFlowersToNormalHand(fans, flowers, rules);
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
            if (!rules.AllWindPungsEnabled) {
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

            ApplyEightFlowersToNormalHand(fans, flowers, rules);
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
            if (rules.HalfBeggingEnabled && allExposed && isSelfDraw) {
                AddFan(fans, "半求人", 1);
            }
            if (rules.AllWindPungsEnabled) {
                int count = Winds.Count(tripletTiles.Contains);
                if (count > 0) AddFan(fans, "见字", 1, count);
            }
            if (rules.NoFlowersOrHonorsEnabled
                && flowers.Count == 0
                && structure.All(tile => tile < 40)) {
                fans.RemoveAll(fan => fan.Name == "无花");
                AddFan(fans, "无字无花", 2);
            }
            if (rules.MeldedKongEnabled) {
                int count = melds.Count(meld => meld.Kind == TaiwanMeldKind.Kong && !meld.Concealed);
                if (count > 0) AddFan(fans, "明杠", 1, count);
            }
            if (rules.ConcealedKongEnabled) {
                int count = melds.Count(meld => meld.Kind == TaiwanMeldKind.Kong && meld.Concealed);
                if (count > 0) AddFan(fans, "暗杠", 2, count);
            }
        }

        private static void ApplyEightFlowersToNormalHand(
            List<TaiwanFan> fans,
            IList<int> flowers,
            TaiwanRuleConfig rules) {
            if (flowers.Distinct().Count() != 8
                || (rules.EightFlowersMode != "additive"
                    && rules.EightFlowersMode != "compound")) {
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

            if (rules.FlowerScoringMode == "all_flowers") {
                if (flowers.Count > 0) AddFan(fans, "见花", 1, flowers.Count);
            } else {
                int firstFlower = 51 + Math.Max(0, Math.Min(3, seatWind - 41));
                int secondFlower = firstFlower + 4;
                int correctCount = flowers.Count(tile =>
                    (tile == firstFlower || tile == secondFlower)
                    && !flowerKongTiles.Contains(tile));
                if (correctCount > 0) AddFan(fans, "正花", 1, correctCount);
            }

            if (flowerKongCount > 0) AddFan(fans, "花杠", 1, flowerKongCount);
            if (flowers.Count == 0 && rules.NoFlowersEnabled) {
                AddFan(fans, "无花", 1);
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
            Dictionary<int, int> physicalCounts = CloneCounts(counts);
            foreach (TaiwanMeld meld in external) {
                if (meld.Kind == TaiwanMeldKind.Sequence) {
                    IncrementCount(physicalCounts, meld.Tile - 1);
                    IncrementCount(physicalCounts, meld.Tile);
                    IncrementCount(physicalCounts, meld.Tile + 1);
                } else {
                    int count = meld.Kind == TaiwanMeldKind.Kong ? 4 : 3;
                    physicalCounts[meld.Tile] = physicalCounts.TryGetValue(
                        meld.Tile,
                        out int existing)
                        ? existing + count
                        : count;
                }
            }
            if (physicalCounts.Values.Any(count => count > 4)) {
                return new List<TaiwanDecomposition>();
            }

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

        private static void IncrementCount(Dictionary<int, int> counts, int tile) {
            counts[tile] = counts.TryGetValue(tile, out int count) ? count + 1 : 1;
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
        public string FanId { get; }
        public string Name { get; }
        public int Tai { get; private set; }
        public int Count { get; }

        public TaiwanFan(string name, int tai, int count) {
            TaiwanFanCatalog.TryResolveFanId(name, out string fanId);
            FanId = fanId ?? name;
            Name = name;
            Tai = tai;
            Count = count;
        }

        public void SetTai(int tai) {
            Tai = tai;
        }

        public string DisplayName => Count == 1 ? Name : Name + "*" + Count;
    }
}
