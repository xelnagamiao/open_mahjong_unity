using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>虹雀组形状分类结果（对应服务端 rules.MeldShape）。</summary>
public sealed class HongqueMeldShape {
    public string Kind;       // "sequence" / "triplet" / "rainbow"
    public string BaseKind;   // "sequence" / "triplet"
    public List<int> Tiles;
    public int ColourStep;
    public int NumberStep;
    public bool IsRainbow;
}

/// <summary>虹雀和牌分解：暗组列表 + 雀头（规则书 5.1.1 不允许雀头，恒为空）。</summary>
public sealed class HongqueWinDecomposition {
    public List<List<int>> Groups;
    public List<int> Pair;
}

/// <summary>虹雀计分结果（对应服务端 scoring.best_win_result）。</summary>
public sealed class HongqueWinScore {
    public int Points;
    public int Base;
    public int FanTotal;
}

/// <summary>
/// 虹雀本地计分：移植服务端 rules/win_check/scoring，
/// 供切牌预测提示展示每张和牌张的直接分值。
/// </summary>
public static class HongqueScoring {
    // 切牌悬停预览：同一手牌+和牌张的计分结果只算一次，避免每悬停一张牌都重算分区/番种。
    private static readonly Dictionary<string, HongqueWinScore> BestWinResultCache =
        new Dictionary<string, HongqueWinScore>();
    private const int BestWinResultCacheLimit = 1024;

    /// <summary>新对局/新牌局开始时清空计分缓存。</summary>
    public static void ClearCaches() {
        BestWinResultCache.Clear();
    }

    private static int ColourOf(int tileId) { return (tileId - HongqueTileVisual.BaseId) / 10; }
    private static int NumberOf(int tileId) { return (tileId - HongqueTileVisual.BaseId) % 10; }

    private static HashSet<int> PrimaryColours(int colour) {
        int baseColour = colour / 2;
        if (colour % 2 == 0) return new HashSet<int> { baseColour };
        return new HashSet<int> { baseColour, (baseColour + 1) % 7 };
    }

    private static bool CyclicProgression(List<int> values, int step) {
        if (values.Count == 0) return false;
        if (values.GroupBy(v => v).Any(g => g.Count() > 1)) return false;
        foreach (int start in values) {
            HashSet<int> expected = new HashSet<int>();
            for (int offset = 0; offset < values.Count; offset++) {
                expected.Add(((start + step * offset) % 14 + 14) % 14);
            }
            if (expected.SetEquals(values)) return true;
        }
        return false;
    }

    private static bool OrderedColourProgression(List<int> tiles, int step, int numberStep) {
        List<int> ordered = tiles
            .OrderBy(t => NumberOf(t))
            .ThenBy(t => ColourOf(t))
            .ToList();
        if (numberStep < 0) ordered.Reverse();
        for (int i = 0; i + 1 < ordered.Count; i++) {
            int diff = (ColourOf(ordered[i + 1]) - ColourOf(ordered[i])) % 14;
            if (diff < 0) diff += 14;
            if (diff != step) return false;
        }
        return true;
    }

    /// <summary>分类一组牌（对应 rules.classify_meld）。返回 null 表示非法组。</summary>
    public static HongqueMeldShape ClassifyMeld(List<int> tileIds) {
        if (tileIds == null || tileIds.Count < 3) return null;
        List<int> normalized = tileIds.Distinct().OrderBy(t => NumberOf(t)).ThenBy(t => ColourOf(t)).ToList();
        if (normalized.Count != tileIds.Count) return null;

        List<int> numbers = normalized.Select(NumberOf).ToList();
        List<int> colours = normalized.Select(ColourOf).ToList();
        HashSet<int> primary = new HashSet<int>();
        foreach (int colour in colours) primary.UnionWith(PrimaryColours(colour));
        bool rainbow = primary.Count == 7;

        if (numbers.Distinct().Count() == 1) {
            foreach (int colourStep in new[] { 1, 2 }) {
                if (CyclicProgression(colours, colourStep)) {
                    return new HongqueMeldShape {
                        Kind = rainbow ? "rainbow" : "triplet",
                        BaseKind = "triplet",
                        Tiles = normalized,
                        ColourStep = colourStep,
                        NumberStep = 0,
                        IsRainbow = rainbow,
                    };
                }
            }
        }

        for (int numberStep = -4; numberStep <= 4; numberStep++) {
            if (numberStep == 0) continue;
            List<int> orderedNumbers = numbers.OrderBy(n => n).ToList();
            if (numberStep < 0) orderedNumbers.Reverse();
            bool stepOk = true;
            for (int i = 0; i + 1 < orderedNumbers.Count; i++) {
                if (orderedNumbers[i + 1] - orderedNumbers[i] != numberStep) { stepOk = false; break; }
            }
            if (!stepOk) continue;
            foreach (int colourStep in new[] { 0, 1, 2 }) {
                if (colourStep == 0) {
                    if (colours.Distinct().Count() == 1) {
                        return new HongqueMeldShape {
                            Kind = rainbow ? "rainbow" : "sequence",
                            BaseKind = "sequence",
                            Tiles = normalized,
                            ColourStep = 0,
                            NumberStep = numberStep,
                            IsRainbow = rainbow,
                        };
                    }
                } else if (OrderedColourProgression(normalized, colourStep, numberStep)) {
                    return new HongqueMeldShape {
                        Kind = rainbow ? "rainbow" : "sequence",
                        BaseKind = "sequence",
                        Tiles = normalized,
                        ColourStep = colourStep,
                        NumberStep = numberStep,
                        IsRainbow = rainbow,
                    };
                }
            }
        }
        return null;
    }

    /// <summary>手牌的全部和牌分解（对应 win_check.winning_decompositions）。</summary>
    public static List<HongqueWinDecomposition> WinningDecompositions(
        List<int> handTileIds,
        bool hasOpenMelds) {
        List<HongqueWinDecomposition> results = new List<HongqueWinDecomposition>();
        if (handTileIds == null) return results;
        // 与服务端 win_check 一致：空手牌 + 有明牌 = 和牌型（杠和把最后一张
        // 牌杠进明牌后的空手状态），此时空分组即为一组合法分解。
        if (handTileIds.Count == 0) {
            if (hasOpenMelds) {
                results.Add(new HongqueWinDecomposition {
                    Groups = new List<List<int>>(),
                    Pair = new List<int>(),
                });
            }
            return results;
        }
        if (handTileIds.Distinct().Count() != handTileIds.Count) return results;

        HashSet<string> seen = new HashSet<string>();
        void Append(List<List<int>> groups, List<int> pair) {
            if (pair.Count > 0 && groups.Count == 0 && !hasOpenMelds) return;
            string key = string.Join("|", groups
                    .Select(g => string.Join(",", g.OrderBy(t => t)))
                    .OrderBy(s => s, StringComparer.Ordinal))
                + ";;" + string.Join(",", pair.OrderBy(t => t));
            if (!seen.Add(key)) return;
            results.Add(new HongqueWinDecomposition {
                Groups = groups,
                Pair = pair,
            });
        }

        foreach (List<List<int>> partition in HongqueTenpai.PartitionsFromCodes(handTileIds)) {
            if (partition.Count > 0 || hasOpenMelds) Append(partition, new List<int>());
        }
        return results;
    }

    private sealed class FanEntry {
        public string Name;
        public int Value;
        public int Count;
        public int Total => Value * Count;
    }

    private static bool Arithmetic(List<int> values) {
        List<int> ordered = values.Distinct().OrderBy(v => v).ToList();
        if (ordered.Count != values.Count || ordered.Count < 2) return false;
        int step = ordered[1] - ordered[0];
        if (step <= 0) return false;
        for (int i = 0; i + 1 < ordered.Count; i++) {
            if (ordered[i + 1] - ordered[i] != step) return false;
        }
        return true;
    }

    private static List<int> OrderedTiles(HongqueMeldShape shape) {
        if (shape.BaseKind == "sequence") {
            List<int> ordered = shape.Tiles
                .OrderBy(t => NumberOf(t))
                .ThenBy(t => ColourOf(t))
                .ToList();
            // 与服务端 scoring._ordered_tiles 一致：递减顺子按数字降序读，
            // 保证“同花（长度+花色对应）”的匹配方向一致——
            // 递增与递减的同花色顺子可以互为双同花，漏掉反转会少算同花。
            if (shape.NumberStep < 0) ordered.Reverse();
            return ordered;
        }
        return shape.Tiles.OrderBy(t => ColourOf(t)).ToList();
    }

    private static List<FanEntry> MergeEntries(List<FanEntry> entries) {
        Dictionary<string, FanEntry> merged = new Dictionary<string, FanEntry>();
        foreach (FanEntry item in entries) {
            if (merged.TryGetValue(item.Name, out FanEntry existing)) {
                existing.Count += 1;
            } else {
                merged[item.Name] = new FanEntry { Name = item.Name, Value = item.Value, Count = 1 };
            }
        }
        return merged.Values.ToList();
    }

    private static List<FanEntry> SameShapeFans(List<HongqueMeldShape> shapes, string baseKind,
        Dictionary<int, (string, int)> names) {
        // 同刻/同顺系列：按“数字集合”分组，组内取最高档（双/三/四）。
        // 同刻不要求各组张数相等：只看同数字刻子的个数（3/3/4/4 也算四同刻）；
        // 不同数字可复计；同组的牌不会同时计入两档。
        Dictionary<string, int> buckets = new Dictionary<string, int>();
        foreach (HongqueMeldShape shape in shapes) {
            if (shape.BaseKind != baseKind) continue;
            string key = string.Join(",", shape.Tiles.Select(NumberOf).Distinct().OrderBy(n => n));
            buckets.TryGetValue(key, out int count);
            buckets[key] = count + 1;
        }
        List<FanEntry> entries = new List<FanEntry>();
        foreach (int count in buckets.Values) {
            int eligible = names.Keys.Where(n => count >= n).DefaultIfEmpty(0).Max();
            if (eligible == 0) continue;
            entries.Add(new FanEntry { Name = names[eligible].Item1, Value = names[eligible].Item2 });
        }
        return MergeEntries(entries);
    }

    private static List<FanEntry> SameColourLayoutFans(List<HongqueMeldShape> shapes) {
        // 同花系列：按（长度，花色对应）分组，组内取最高档，不同分组可复计。
        Dictionary<string, int> buckets = new Dictionary<string, int>();
        foreach (HongqueMeldShape shape in shapes) {
            List<int> ordered = OrderedTiles(shape);
            string key = ordered.Count + "|" + string.Join(",", ordered.Select(ColourOf));
            buckets.TryGetValue(key, out int count);
            buckets[key] = count + 1;
        }
        List<FanEntry> entries = new List<FanEntry>();
        foreach (int count in buckets.Values) {
            if (count >= 4) entries.Add(new FanEntry { Name = "四同花", Value = 12 });
            else if (count >= 3) entries.Add(new FanEntry { Name = "三同花", Value = 6 });
            else if (count >= 2) entries.Add(new FanEntry { Name = "双同花", Value = 2 });
        }
        return MergeEntries(entries);
    }

    private static FanEntry ConsecutiveSequenceFan(List<HongqueMeldShape> shapes) {
        List<HongqueMeldShape> sequences = shapes.Where(s => s.BaseKind == "sequence").ToList();
        int best = 0;
        foreach (int count in new[] { 4, 3 }) {
            foreach (var selected in Combinations(sequences, count)) {
                if (selected.Select(s => s.Tiles.Count).Distinct().Count() != 1) continue;
                if (selected.Select(s => Math.Abs(s.NumberStep)).Distinct().Count() != 1) continue;
                List<int> starts = selected
                    .Select(s => s.Tiles.Min(t => NumberOf(t)))
                    .ToList();
                if (Arithmetic(starts)) { best = count; break; }
            }
            if (best != 0) break;
        }
        if (best == 4) return new FanEntry { Name = "四连顺", Value = 6 };
        if (best == 3) return new FanEntry { Name = "三连顺", Value = 3 };
        return null;
    }

    private static FanEntry DragonFan(List<HongqueMeldShape> shapes) {
        List<HongqueMeldShape> sequences = shapes.Where(s => s.BaseKind == "sequence").ToList();
        foreach (int count in new[] { 1, 2, 3 }) {
            foreach (var selected in Combinations(sequences, count)) {
                if (selected.Select(s => Math.Abs(s.NumberStep)).Distinct().Count() != 1) continue;
                List<int> numbers = selected.SelectMany(s => s.Tiles).Select(NumberOf).ToList();
                List<int> sorted = numbers.OrderBy(n => n).ToList();
                if (sorted.Count == 9 && sorted.SequenceEqual(Enumerable.Range(1, 9))) {
                    return new FanEntry { Name = "一条龙", Value = 3 };
                }
            }
        }
        return null;
    }

    private static IEnumerable<List<T>> Combinations<T>(List<T> items, int count) {
        if (count <= 0 || count > items.Count) yield break;
        int[] indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        while (true) {
            yield return indices.Select(i => items[i]).ToList();
            int pos = count - 1;
            while (pos >= 0 && indices[pos] == items.Count - count + pos) pos--;
            if (pos < 0) yield break;
            indices[pos]++;
            for (int i = pos + 1; i < count; i++) indices[i] = indices[i - 1] + 1;
        }
    }

    private static HongqueWinScore ScorePartition(
        List<List<int>> concealedPartition,
        List<int[]> meldMasks,
        List<int> pair,
        bool selfDraw,
        bool beforeFirstDiscard,
        bool wallEmpty) {
        List<List<int>> groups = new List<List<int>>(concealedPartition);
        foreach (int[] mask in meldMasks) {
            if (mask == null) continue;
            List<int> meldTiles = new List<int>();
            for (int i = 1; i < mask.Length; i += 2) {
                if (mask[i] != 0) meldTiles.Add(mask[i]);
            }
            if (meldTiles.Count > 0) groups.Add(meldTiles);
        }
        List<HongqueMeldShape> shapes = new List<HongqueMeldShape>();
        foreach (List<int> group in groups) {
            HongqueMeldShape shape = ClassifyMeld(group);
            if (shape == null) return null;
            shapes.Add(shape);
        }
        List<int> pairTiles = pair ?? new List<int>();
        if (pairTiles.Count > 0) {
            if (pairTiles.Count != 2 || NumberOf(pairTiles[0]) != NumberOf(pairTiles[1])) return null;
        }

        List<int> allTiles = new List<int>();
        foreach (List<int> group in groups) allTiles.AddRange(group);
        allTiles.AddRange(pairTiles);

        int basePoints = 3 + groups.Sum(g => Math.Max(0, g.Count - 3));
        bool concealed = meldMasks.Count == 0;
        if (concealed) basePoints += 2;

        List<FanEntry> fans = new List<FanEntry>();
        // 清顺：仅“花色相同”（ColourStep 0）且非彩虹的顺子，按组复计。
        int cleanSequences = shapes.Count(s => s.Kind == "sequence" && s.ColourStep == 0);
        int cleanTriplets = shapes.Count(s => s.BaseKind == "triplet" && s.Tiles.Count >= 4);
        if (cleanSequences > 0) fans.Add(new FanEntry { Name = "清顺", Value = 1, Count = cleanSequences });
        if (cleanTriplets > 0) fans.Add(new FanEntry { Name = "清刻", Value = 1, Count = cleanTriplets });

        FanEntry dragon = DragonFan(shapes);
        if (dragon != null) fans.Add(dragon);
        fans.AddRange(SameShapeFans(shapes, "triplet",
            new Dictionary<int, (string, int)> { { 2, ("双同刻", 2) }, { 3, ("三同刻", 6) }, { 4, ("四同刻", 12) } }));
        fans.AddRange(SameShapeFans(shapes, "sequence",
            new Dictionary<int, (string, int)> { { 2, ("双同顺", 2) }, { 3, ("三同顺", 6) }, { 4, ("四同顺", 12) } }));
        fans.AddRange(SameColourLayoutFans(shapes));
        FanEntry consecutive = ConsecutiveSequenceFan(shapes);
        if (consecutive != null) fans.Add(consecutive);

        Dictionary<int, int> colourCounts = allTiles.GroupBy(ColourOf).ToDictionary(g => g.Key, g => g.Count());
        int maxColourCount = colourCounts.Values.DefaultIfEmpty(0).Max();
        if (maxColourCount == 9) fans.Add(new FanEntry { Name = "九归一", Value = 6 });
        else if (maxColourCount == 7 || maxColourCount == 8) fans.Add(new FanEntry { Name = "七归一", Value = 3 });

        int rainbowCount = shapes.Count(s => s.IsRainbow);
        if (rainbowCount >= 2) fans.Add(new FanEntry { Name = "双虹会", Value = 12 });
        else if (rainbowCount == 1) fans.Add(new FanEntry { Name = "彩虹", Value = 6 });

        int distinctColours = colourCounts.Count;
        if (distinctColours == 1) fans.Add(new FanEntry { Name = "清一色", Value = 18 });
        else if (distinctColours == 14) fans.Add(new FanEntry { Name = "全彩", Value = 12 });
        else if (distinctColours == allTiles.Count) fans.Add(new FanEntry { Name = "光谱", Value = 6 });
        else if (distinctColours == 2) fans.Add(new FanEntry { Name = "双色", Value = 12 });
        else if (distinctColours == 3) fans.Add(new FanEntry { Name = "三色", Value = 6 });
        if (allTiles.Count > 0 && allTiles.All(t => ColourOf(t) % 2 == 0)) fans.Add(new FanEntry { Name = "全纯色", Value = 1 });
        if (allTiles.Count > 0 && allTiles.All(t => ColourOf(t) % 2 == 1)) fans.Add(new FanEntry { Name = "全半色", Value = 1 });

        List<int> numbers = allTiles.Select(NumberOf).Distinct().OrderBy(n => n).ToList();
        bool allTriplets = shapes.Count > 0 && shapes.All(s => s.BaseKind == "triplet");
        if (numbers.Count == 1) fans.Add(new FanEntry { Name = "清一数", Value = 18 });
        else if (numbers.Count == 2) fans.Add(new FanEntry { Name = "二数", Value = 12 });
        else if (numbers.Count == 3 && Arithmetic(numbers)) fans.Add(new FanEntry { Name = "三数", Value = 6 });
        else if (numbers.Count == 4 && Arithmetic(numbers)) fans.Add(new FanEntry { Name = "四数", Value = 3 });
        // 全带幺：每组牌均含数字 1 或 9 的牌（按规则书“牌组”判定，而非全体手牌）。
        if (groups.Count > 0
                && groups.All(group => group.Any(tile => {
                    int number = NumberOf(tile);
                    return number == 1 || number == 9;
                }))) {
            fans.Add(new FanEntry { Name = "全带幺", Value = 2 });
        }

        bool heavenly = selfDraw && beforeFirstDiscard && concealed;
        if (heavenly) fans.Add(new FanEntry { Name = "天和", Value = 18 });
        else if (concealed) fans.Add(new FanEntry { Name = "门清", Value = 1 });
        if (selfDraw && wallEmpty) fans.Add(new FanEntry { Name = "海底", Value = 2 });
        // 清一数、二数均不计碰碰和。
        if (allTriplets && numbers.Count > 2) fans.Add(new FanEntry { Name = "碰碰和", Value = 3 });
        // 平和：仅由顺子构成。彩虹组本质也是顺子（长顺子），不影响平和；
        // 因此按 BaseKind 判断，彩虹顺子计入平和，彩虹刻子（刻子类）不计。
        if (shapes.Count > 0 && shapes.All(s => s.BaseKind == "sequence")) fans.Add(new FanEntry { Name = "平和", Value = 1 });
        if (groups.Count == 1) fans.Add(new FanEntry { Name = "金龙", Value = 6 });
        else if (groups.Count == 2) fans.Add(new FanEntry { Name = "二金", Value = 3 });
        else if (groups.Count == 3) fans.Add(new FanEntry { Name = "三金", Value = 1 });

        // 番种从大到小展示。
        fans.Sort((left, right) => right.Value.CompareTo(left.Value));
        int fanTotal = fans.Sum(f => f.Total);
        return new HongqueWinScore {
            Points = fanTotal == 0 ? 1 : basePoints * fanTotal,
            Base = basePoints,
            FanTotal = fanTotal,
        };
    }

    /// <summary>
    /// 最优和牌分值（对应 scoring.best_win_result）。
    /// handTileIds 为暗手；meldMasks 为副露掩码（[flag, tileId] 对）。
    /// </summary>
    public static HongqueWinScore BestWinResult(
        List<int> handTileIds,
        List<int[]> meldMasks,
        bool selfDraw,
        bool beforeFirstDiscard,
        bool wallEmpty) {
        string cacheKey = BuildWinResultCacheKey(
            handTileIds, meldMasks, selfDraw, beforeFirstDiscard, wallEmpty);
        if (BestWinResultCache.TryGetValue(cacheKey, out HongqueWinScore cached)) {
            return cached;
        }
        List<int[]> masks = meldMasks ?? new List<int[]>();
        List<HongqueWinDecomposition> decompositions =
            WinningDecompositions(handTileIds, masks.Count > 0);
        HongqueWinScore best = null;
        foreach (HongqueWinDecomposition decomposition in decompositions) {
            HongqueWinScore score = ScorePartition(
                decomposition.Groups, masks, decomposition.Pair,
                selfDraw, beforeFirstDiscard, wallEmpty);
            if (score == null) continue;
            if (best == null
                || score.Points > best.Points
                || (score.Points == best.Points && score.FanTotal > best.FanTotal)
                || (score.Points == best.Points && score.FanTotal == best.FanTotal && score.Base > best.Base)) {
                best = score;
            }
        }
        if (BestWinResultCache.Count >= BestWinResultCacheLimit) BestWinResultCache.Clear();
        BestWinResultCache[cacheKey] = best;
        return best;
    }

    private static string BuildWinResultCacheKey(
        List<int> handTileIds,
        List<int[]> meldMasks,
        bool selfDraw,
        bool beforeFirstDiscard,
        bool wallEmpty) {
        System.Text.StringBuilder key = new System.Text.StringBuilder(64);
        if (handTileIds != null) {
            List<int> sorted = new List<int>(handTileIds);
            sorted.Sort();
            for (int i = 0; i < sorted.Count; i++) {
                if (i > 0) key.Append(',');
                key.Append(sorted[i]);
            }
        }
        key.Append('|');
        if (meldMasks != null) {
            for (int m = 0; m < meldMasks.Count; m++) {
                int[] mask = meldMasks[m];
                if (mask == null) continue;
                List<int> tiles = new List<int>();
                for (int i = 1; i < mask.Length; i += 2) tiles.Add(mask[i]);
                tiles.Sort();
                for (int i = 0; i < tiles.Count; i++) {
                    key.Append(tiles[i]);
                    key.Append(';');
                }
                key.Append('/');
            }
        }
        key.Append(selfDraw ? 'T' : 'F');
        key.Append(beforeFirstDiscard ? 'T' : 'F');
        key.Append(wallEmpty ? 'T' : 'F');
        return key.ToString();
    }
}
