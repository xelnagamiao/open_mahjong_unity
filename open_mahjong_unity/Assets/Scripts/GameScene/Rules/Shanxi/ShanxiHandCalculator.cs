using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>MIL 山西十三张：结构听牌与和牌基本分（不含庄家差额及杠账）。</summary>
internal static class ShanxiHandCalculator {
    private static readonly int[] Tiles = Enumerable.Range(1, 3)
        .SelectMany(s => Enumerable.Range(1, 9).Select(n => s * 10 + n))
        .Concat(Enumerable.Range(41, 7)).ToArray();
    private static readonly int[] Orphans = {11,19,21,29,31,39,41,42,43,44,45,46,47};

    public static int TilePoints(int tile) => Tiles.Contains(tile) ? (tile >= 41 ? 10 : tile % 10) : 0;

    private static List<int> MeldTiles(string code) {
        if (string.IsNullOrEmpty(code) || code.Length != 3 || !"kgG".Contains(code[0])
            || !int.TryParse(code.Substring(1), out int tile) || !Tiles.Contains(tile)) return null;
        return Enumerable.Repeat(tile, code[0] == 'k' ? 3 : 4).ToList();
    }

    private static List<List<string>> Shapes(List<int> hand, List<string> melds) {
        var result = new List<List<string>>();
        if (hand == null || melds.Count > 4 || hand.Count + melds.Count * 3 != 14
            || hand.Any(t => !Tiles.Contains(t))) return result;
        var all = new List<int>(hand);
        foreach (string code in melds) {
            var tiles = MeldTiles(code);
            if (tiles == null) return result;
            all.AddRange(tiles);
        }
        if (all.GroupBy(t => t).Any(g => g.Count() > 4)) return result;
        if (melds.Count == 0) {
            if (hand.GroupBy(t => t).All(g => g.Count() % 2 == 0)) result.Add(new List<string> { "seven" });
            if (hand.Distinct().Count() == 13 && Orphans.All(hand.Contains)) result.Add(new List<string> { "orphans" });
        }
        var decomps = StandardMahjongSolver.StandardDecompositions(hand, allowHonors: true);
        for (int i = 0; i < decomps.Count; i++) {
            var d = decomps[i];
            var sets = new List<string>(d.Melds.Length);
            for (int j = 0; j < d.Melds.Length; j++) {
                var u = d.Melds[j];
                sets.Add((u.Kind == 1 ? "s" : "k") + u.TileId);
            }
            result.Add(sets);
        }
        return result;
    }

    public static HashSet<int> Waits(List<int> hand, List<string> melds) {
        var result = new HashSet<int>();
        melds = melds ?? new List<string>();
        if (hand == null || melds.Count > 4 || hand.Count + 3 * melds.Count != 13) return result;
        int[] phys = new int[48];
        int[] hc = new int[48];
        for (int i = 0; i < hand.Count; i++) {
            int t = hand[i];
            if ((uint)t >= 48 || !Tiles.Contains(t) || ++phys[t] > 4) return result;
            hc[t]++;
        }
        for (int i = 0; i < melds.Count; i++) {
            var mt = MeldTiles(melds[i]);
            if (mt == null) return result;
            for (int j = 0; j < mt.Count; j++) {
                if (++phys[mt[j]] > 4) return result;
            }
        }
        var std = StandardMahjongSolver.StandardWaitingTiles(hand, allowHonors: true, allowFifthCopy: false);
        foreach (int t in std) if ((uint)t < 48 && phys[t] < 4) result.Add(t);
        if (melds.Count == 0) {
            int oddCount = 0, oddTile = -1;
            for (int i = 0; i < Tiles.Length; i++) {
                int t = Tiles[i];
                if ((hc[t] & 1) != 0) { oddCount++; oddTile = t; }
            }
            if (oddCount == 1 && phys[oddTile] < 4) result.Add(oddTile);

            bool allOrphans = true;
            for (int i = 0; i < hand.Count; i++) {
                int t = hand[i];
                if (t < 41 && t % 10 != 1 && t % 10 != 9) { allOrphans = false; break; }
            }
            if (allOrphans) {
                int missing = 0, missingTile = -1, pairCount = 0;
                for (int i = 0; i < Orphans.Length; i++) {
                    int o = Orphans[i];
                    if (hc[o] == 0) { missing++; missingTile = o; }
                    else if (hc[o] == 2) pairCount++;
                    else if (hc[o] > 2) { missing = 99; break; }
                }
                if (missing == 0) {
                    for (int i = 0; i < Orphans.Length; i++) if (phys[Orphans[i]] < 4) result.Add(Orphans[i]);
                } else if (missing == 1 && pairCount == 1 && phys[missingTile] < 4) {
                    result.Add(missingTile);
                }
            }
        }
        return result;
    }

    public static int Score(List<int> hand, List<string> melds, int winningTile, bool selfDraw) {
        melds = melds ?? new List<string>();
        if (hand == null || !hand.Contains(winningTile) || TilePoints(winningTile) < (selfDraw ? 3 : 6)) return 0;
        var shapes = Shapes(hand, melds);
        if (shapes.Count == 0) return 0;
        int best = 0;
        foreach (var shape in shapes) {
            if (shape.Contains("orphans")) best = Math.Max(best, 60);
            else if (shape.Contains("seven")) best = Math.Max(best, hand.Count(t => t == winningTile) == 4 ? 40 : 20);
            else if (Enumerable.Range(1, 3).Any(s => new[] { "s"+s+"2", "s"+s+"5", "s"+s+"8" }.All(shape.Contains))) best = Math.Max(best, 20);
        }
        bool flush = hand.Concat(melds.SelectMany(MeldTiles)).Select(t => t / 10).Distinct().Count() == 1;
        return best + (flush ? 20 : 0) + TilePoints(winningTile);
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        int ron = Score(query.HandWithWin, query.Melds, query.HepaiTile, false);
        if (ron > 0) return WaitTileHint.Ron($"报听后基本分 {ron}分");
        int tsumo = Score(query.HandWithWin, query.Melds, query.HepaiTile, true);
        if (tsumo > 0) {
            var hand = new List<int>(query.HandWithWin);
            hand.Remove(query.HepaiTile);
            // 基本听牌型仍显示；只有低点听口时不能满足六点报听的基本条件。
            // 分值按报听后估计，不按过水或临时状态隐藏听牌。
            if (Waits(hand, query.Melds).Any(tile => TilePoints(tile) >= 6))
                return WaitTileHint.TsumoOnly($"报听后仅自摸 基本分 {tsumo}分");
            return WaitTileHint.None("未满足");
        }
        return WaitTileHint.None("未起和");
    }
}
