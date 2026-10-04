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
        foreach (int pair in hand.Distinct()) {
            if (hand.Count(t => t == pair) < 2) continue;
            var rest = hand.OrderBy(t => t).ToList();
            rest.Remove(pair); rest.Remove(pair);
            SplitSets(rest, new List<string>(), result);
        }
        return result;
    }

    public static HashSet<int> Waits(List<int> hand, List<string> melds) {
        var result = new HashSet<int>();
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + 3 * melds.Count != 13) return result;
        foreach (int tile in Tiles) if (Shapes(new List<int>(hand) { tile }, melds).Count > 0) result.Add(tile);
        return result;
    }
    private static void SplitSets(List<int> hand, List<string> sets, List<List<string>> result) {
        if (hand.Count == 0) { result.Add(new List<string>(sets)); return; }
        int tile = hand[0];
        if (hand.Count(value => value == tile) >= 3) {
            var rest = new List<int>(hand);
            for (int i = 0; i < 3; i++) rest.Remove(tile);
            sets.Add("k" + tile);
            SplitSets(rest, sets, result);
            sets.RemoveAt(sets.Count - 1);
        }
        if (tile < 40 && tile % 10 <= 7 && hand.Contains(tile + 1) && hand.Contains(tile + 2)) {
            var rest = new List<int>(hand);
            rest.Remove(tile); rest.Remove(tile + 1); rest.Remove(tile + 2);
            sets.Add("s" + (tile + 1));
            SplitSets(rest, sets, result);
            sets.RemoveAt(sets.Count - 1);
        }
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
        return tsumo > 0 ? WaitTileHint.TsumoOnly($"报听后仅自摸 基本分 {tsumo}分") : WaitTileHint.None("和牌张不足三点");
    }
}
