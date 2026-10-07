using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>清混碰听牌与花数提示；支持不成四面子一将的乱风向。</summary>
internal static class QinghunpengHandCalculator {
    private static readonly int[] Tiles = Enumerable.Range(1, 3)
        .SelectMany(suit => Enumerable.Range(1, 9).Select(rank => suit * 10 + rank))
        .Concat(new[] { 41, 42, 43, 44, 45, 46, 47 }).ToArray();

    public static HashSet<int> Waits(List<int> hand, List<string> melds) {
        var result = new HashSet<int>();
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + melds.Count * 3 != 13) return result;
        foreach (int tile in Tiles) {
            var candidate = new List<int>(hand) { tile };
            // Keep structural waits visible even before a starting pattern is satisfied.
            if (Decompositions(candidate, melds).Count > 0 || Score(candidate, melds, 0, true) > 0) result.Add(tile);
        }
        return result;
    }

    private static List<int> MeldTiles(string meld) {
        int tile = int.Parse(meld.Substring(1));
        return meld[0] == 's' ? new List<int> { tile - 1, tile, tile + 1 }
            : Enumerable.Repeat(tile, meld[0] == 'g' || meld[0] == 'G' ? 4 : 3).ToList();
    }

    private static List<List<string>> Decompositions(List<int> hand, List<string> melds) {
        var result = new List<List<string>>();
        if (hand.Count + melds.Count * 3 != 14 || hand.Any(tile => !Tiles.Contains(tile))) return result;
        var all = hand.Concat(melds.SelectMany(MeldTiles)).ToList();
        if (all.Any(tile => !Tiles.Contains(tile)) || all.GroupBy(tile => tile).Any(group => group.Count() > 4)) return result;
        foreach (int pair in hand.Distinct()) {
            if (hand.Count(tile => tile == pair) < 2) continue;
            var remaining = hand.OrderBy(tile => tile).ToList();
            remaining.Remove(pair);
            remaining.Remove(pair);
            SplitSets(remaining, new List<string>(), result);
        }
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

    public static int Score(List<int> hand, List<string> melds, int flowers, bool selfDraw,
                            bool replacement = false, bool lastTile = false, bool robKong = false) {
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + melds.Count * 3 != 14 || melds.Count > 4) return 0;
        var all = hand.Concat(melds.SelectMany(MeldTiles)).ToList();
        if (all.Any(tile => !Tiles.Contains(tile)) || all.GroupBy(tile => tile).Any(group => group.Count() > 4)) return 0;
        bool honors = all.All(tile => tile >= 41);
        int suits = all.Where(tile => tile < 40).Select(tile => tile / 10).Distinct().Count();
        bool extra = (melds.Count == 4 && melds.All(code => code[0] != 'G'))
            || (selfDraw && replacement) || lastTile || robKong;
        int best = honors ? 20 : 0;
        foreach (List<string> sets in Decompositions(hand, melds)) {
            bool pungs = sets.Concat(melds).All(code => code[0] != 's');
            bool mixed = all.Any(tile => tile >= 41);
            int basic = honors ? (pungs ? 40 : 20)
                : suits == 1 ? (pungs ? (mixed ? 10 : 20) : (mixed ? 1 : 10))
                : pungs ? 1 : 0;
            if (basic == 0) continue;
            int physical = flowers;
            foreach (string code in sets.Concat(melds)) {
                int tile = int.Parse(code.Substring(1));
                if (code[0] == 's') continue;
                physical += tile >= 45 ? 2 : tile >= 41 ? 1 : 0;
                physical += code[0] == 'G' ? 2 : code[0] == 'g' ? 1 : 0;
            }
            int points = extra ? Math.Max(basic, 10) : basic;
            if (points < 10) points = Math.Min(10, points + physical);
            if (points == 1 && !selfDraw) continue;
            best = Math.Max(best, points);
        }
        return best;
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        int flowers = query.SelfFlowers?.Count ?? query.HuapaiCount;
        int multiplier = query.DetailedConfig != null && query.DetailedConfig.TryGetValue("huangfan_count", out object debt)
            && Convert.ToInt32(debt) > 0 ? 2 : 1;
        int ron = Score(query.HandWithWin, query.Melds, flowers, false);
        if (ron > 0) return WaitTileHint.Ron($"{ron}花（{ron * multiplier}分）" + (multiplier == 2 ? " 荒番" : ""));
        int tsumo = Score(query.HandWithWin, query.Melds, flowers, true);
        return tsumo > 0 ? WaitTileHint.TsumoOnly($"仅自摸 {tsumo}花（{tsumo * multiplier}分）" + (multiplier == 2 ? " 荒番" : "")) : WaitTileHint.None("未满足");
    }
}
