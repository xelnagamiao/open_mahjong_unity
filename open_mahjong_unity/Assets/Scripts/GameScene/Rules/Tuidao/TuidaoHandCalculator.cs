using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>MIL 推倒和结构听口与基础牌型计分；声明、过水和临时加番由服务端裁定。</summary>
internal static class TuidaoHandCalculator {
    internal static readonly IReadOnlyDictionary<string, int> FanValues = new Dictionary<string, int> {
        { "门清", 2 }, { "平和", 2 }, { "断幺", 2 }, { "报听", 2 },
        { "碰碰和", 6 }, { "全带幺", 6 }, { "大吊车", 6 }, { "混一色", 6 },
        { "抢杠", 8 }, { "海底", 8 }, { "杠上开花", 8 }, { "全不靠", 8 }, { "清龙", 8 },
        { "天听", 16 }, { "七对", 16 }, { "清一色", 16 }, { "豪华七对", 24 },
        { "天和", 32 }, { "十三幺", 32 }, { "大三元", 32 }, { "字一色", 32 },
        { "四暗刻", 32 }, { "大四喜", 32 },
    };

    private static readonly int[] Tiles = Enumerable.Range(1, 3)
        .SelectMany(suit => Enumerable.Range(1, 9).Select(rank => suit * 10 + rank))
        .Concat(Enumerable.Range(41, 7)).ToArray();
    private static readonly HashSet<int> TileSet = new HashSet<int>(Tiles);
    private static readonly HashSet<int> Orphans = new HashSet<int> {
        11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
    };

    public static HashSet<int> Waits(List<int> hand, List<string> melds) {
        var waits = new HashSet<int>();
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + melds.Count * 3 != 13) return waits;
        foreach (int tile in Tiles) {
            var completed = new List<int>(hand) { tile };
            if (IsComplete(completed, melds)) waits.Add(tile);
        }
        return waits;
    }

    public static bool IsComplete(List<int> hand, List<string> melds) {
        melds = melds ?? new List<string>();
        if (hand == null || melds.Count > 4 || hand.Count + 3 * melds.Count != 14) return false;
        var all = new List<int>(hand);
        foreach (string code in melds) {
            if (string.IsNullOrEmpty(code) || code.Length < 2 || !int.TryParse(code.Substring(1), out int tile)) return false;
            if (code[0] == 's') {
                if (tile >= 40 || tile % 10 < 2 || tile % 10 > 8) return false;
                all.Add(tile - 1); all.Add(tile); all.Add(tile + 1);
            } else if (code[0] == 'k' || code[0] == 'g' || code[0] == 'G') {
                for (int i = 0; i < (code[0] == 'k' ? 3 : 4); i++) all.Add(tile);
            } else return false;
        }
        if (all.Any(tile => !TileSet.Contains(tile)) || all.GroupBy(tile => tile).Any(group => group.Count() > 4)) return false;
        if (melds.Count == 0) {
            var groups = hand.GroupBy(tile => tile).ToList();
            // 四张相同牌可作两对。
            if (groups.All(group => group.Count() % 2 == 0)) return true;
            if (Orphans.IsSubsetOf(hand) && hand.All(Orphans.Contains)) return true;
            if (hand.Distinct().Count() == 14 && IsKnitted(hand)) return true;
        }
        var counts = new int[48];
        foreach (int tile in hand) counts[tile]++;
        foreach (int pair in hand.Distinct()) {
            if (counts[pair] < 2) continue;
            counts[pair] -= 2;
            bool complete = SplitSets(counts, hand.Count - 2);
            counts[pair] += 2;
            if (complete) return true;
        }
        return false;
    }

    private static bool SplitSets(int[] counts, int remaining) {
        if (remaining == 0) return true;
        int tile = 11;
        while (tile < counts.Length && counts[tile] == 0) tile++;
        if (tile == counts.Length) return false;
        if (counts[tile] >= 3) {
            counts[tile] -= 3;
            bool complete = SplitSets(counts, remaining - 3);
            counts[tile] += 3;
            if (complete) return true;
        }
        if (tile < 40 && tile % 10 <= 7 && counts[tile + 1] > 0 && counts[tile + 2] > 0) {
            counts[tile]--; counts[tile + 1]--; counts[tile + 2]--;
            bool complete = SplitSets(counts, remaining - 3);
            counts[tile]++; counts[tile + 1]++; counts[tile + 2]++;
            if (complete) return true;
        }
        return false;
    }

    private static bool IsKnitted(List<int> hand) {
        // 147/258/369 三组分别归属三门；九张数牌与七张字牌的任意14张不重复牌。
        foreach (int first in new[] { 1, 2, 3 }) {
            foreach (int second in new[] { 1, 2, 3 }) {
                if (first == second) continue;
                int third = 6 - first - second;
                var pool = new HashSet<int>(Enumerable.Range(41, 7));
                foreach (int rank in new[] { 1, 4, 7 }) pool.Add(first * 10 + rank);
                foreach (int rank in new[] { 2, 5, 8 }) pool.Add(second * 10 + rank);
                foreach (int rank in new[] { 3, 6, 9 }) pool.Add(third * 10 + rank);
                if (hand.All(pool.Contains)) return true;
            }
        }
        return false;
    }

    internal sealed class BaseScore {
        public int RawFan { get; }
        public int Fan => Math.Min(32, RawFan);
        public int Points => 2 + Fan;
        public IReadOnlyList<string> FanNames { get; }
        public BaseScore(HashSet<string> fans) {
            FanNames = FanValues.Keys.Where(fans.Contains).ToArray();
            RawFan = FanNames.Sum(name => FanValues[name]);
        }
    }

    private sealed class Meld {
        public char Kind;
        public int Tile;
        public bool Open;
        public int Count;
        public IEnumerable<int> PhysicalTiles => Kind == 's'
            ? new[] { Tile - 1, Tile, Tile + 1 } : Enumerable.Repeat(Tile, Count);
    }

    // 从牌形本身计番，不把报听、天听、抢杠、海底、杠上开花、天和投影到基础提示。
    // 合法零番和必须返回非 null：本规则没有最低番数或强制报听门槛。
    public static BaseScore Score(List<int> hand, List<string> melds, int winningTile, bool selfDraw) {
        melds = melds ?? new List<string>();
        if (hand == null || !hand.Contains(winningTile) || !IsComplete(hand, melds)) return null;
        var calls = melds.Select(code => new Meld {
            Kind = code[0] == 's' ? 's' : 'k', Tile = int.Parse(code.Substring(1)),
            Open = code[0] != 'G', Count = code[0] == 's' || code[0] == 'k' ? 3 : 4,
        }).ToList();
        var physical = hand.Concat(calls.SelectMany(meld => meld.PhysicalTiles)).ToList();
        var basis = new HashSet<string>();
        if (!calls.Any(meld => meld.Open)) basis.Add("门清");
        if (physical.All(tile => tile < 40 && tile % 10 != 1 && tile % 10 != 9)) basis.Add("断幺");
        var suits = new HashSet<int>(physical.Where(tile => tile < 40).Select(tile => tile / 10));
        if (suits.Count == 0) basis.Add("字一色");
        else if (suits.Count == 1) basis.Add(physical.Any(tile => tile >= 40) ? "混一色" : "清一色");

        BaseScore best = null;
        void Consider(HashSet<string> fans) {
            var candidate = new BaseScore(fans);
            if (best == null || candidate.RawFan > best.RawFan
                || candidate.RawFan == best.RawFan && CompareFans(candidate.FanNames, best.FanNames) > 0)
                best = candidate;
        }
        if (melds.Count == 0) {
            var groups = hand.GroupBy(tile => tile).ToList();
            string special = Orphans.IsSubsetOf(hand) && hand.All(Orphans.Contains) ? "十三幺"
                : groups.All(group => group.Count() % 2 == 0) ? groups.Any(group => group.Count() == 4) ? "豪华七对" : "七对"
                : groups.Count == 14 && IsKnitted(hand) ? "全不靠" : null;
            if (special != null) {
                var fans = new HashSet<string>(basis) { special };
                fans.Remove("门清");
                Consider(fans);
            }
        }

        var counts = new int[48];
        foreach (int tile in hand) counts[tile]++;
        foreach (int pair in hand.Distinct().OrderBy(tile => tile)) {
            if (counts[pair] < 2) continue;
            counts[pair] -= 2;
            EnumerateSets(counts, hand.Count - 2, new List<Meld>(), sets => {
                var allSets = calls.Concat(sets).ToList();
                var fans = new HashSet<string>(basis);
                bool allPungs = allSets.All(meld => meld.Kind == 'k');
                if (allPungs) fans.Add("碰碰和");
                if (allSets.All(meld => meld.Kind == 's') && pair < 40) fans.Add("平和");
                if (Orphans.Contains(pair) && allSets.All(meld => meld.PhysicalTiles.Any(Orphans.Contains))) fans.Add("全带幺");
                if (hand.Count == 2) fans.Add("大吊车");
                var sequences = new HashSet<int>(allSets.Where(meld => meld.Kind == 's').Select(meld => meld.Tile));
                if (Enumerable.Range(1, 3).Any(suit => new[] { suit * 10 + 2, suit * 10 + 5, suit * 10 + 8 }.All(sequences.Contains)))
                    fans.Add("清龙");
                var pungs = new HashSet<int>(allSets.Where(meld => meld.Kind == 'k').Select(meld => meld.Tile));
                if (new[] { 45, 46, 47 }.All(pungs.Contains)) fans.Add("大三元");
                if (new[] { 41, 42, 43, 44 }.All(pungs.Contains)) {
                    fans.Add("大四喜");
                    fans.Remove("碰碰和");
                }
                // 点和补成的刻子不能算暗刻；四暗刻点和只能完成将牌。
                if (allPungs && allSets.All(meld => !meld.Open) && (selfDraw || pair == winningTile)) {
                    fans.Add("四暗刻");
                    fans.ExceptWith(new[] { "门清", "碰碰和" });
                }
                if (fans.Contains("字一色")) fans.Remove("全带幺");
                Consider(fans);
            });
            counts[pair] += 2;
        }
        return best;
    }

    private static int CompareFans(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        var a = left.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var b = right.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) {
            int comparison = StringComparer.Ordinal.Compare(a[i], b[i]);
            if (comparison != 0) return comparison;
        }
        return a.Length.CompareTo(b.Length);
    }

    private static void EnumerateSets(int[] counts, int remaining, List<Meld> sets, Action<List<Meld>> accept) {
        if (remaining == 0) { accept(sets); return; }
        int tile = 11;
        while (tile < counts.Length && counts[tile] == 0) tile++;
        if (tile == counts.Length) return;
        if (counts[tile] >= 3) {
            counts[tile] -= 3;
            sets.Add(new Meld { Kind = 'k', Tile = tile, Count = 3 });
            EnumerateSets(counts, remaining - 3, sets, accept);
            sets.RemoveAt(sets.Count - 1);
            counts[tile] += 3;
        }
        if (tile < 40 && tile % 10 <= 7 && counts[tile + 1] > 0 && counts[tile + 2] > 0) {
            counts[tile]--; counts[tile + 1]--; counts[tile + 2]--;
            sets.Add(new Meld { Kind = 's', Tile = tile + 1, Count = 3 });
            EnumerateSets(counts, remaining - 3, sets, accept);
            sets.RemoveAt(sets.Count - 1);
            counts[tile]++; counts[tile + 1]++; counts[tile + 2]++;
        }
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        if (query == null) return WaitTileHint.None("未起和");
        BaseScore ron = Score(query.HandWithWin, query.Melds, query.HepaiTile, false);
        if (ron == null) return WaitTileHint.None("未起和");
        string label = $"基础{ron.Fan}番{ron.Points}分";
        BaseScore tsumo = Score(query.HandWithWin, query.Melds, query.HepaiTile, true);
        if (tsumo != null && tsumo.Fan != ron.Fan) label += $"\n自摸{tsumo.Fan}番{tsumo.Points}分";
        return WaitTileHint.Ron(label);
    }
}
