using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>上海敲麻结构听牌与基本分提示，与服务端 qiaoma 的四面子一将规则一致。</summary>
internal static class ShanghaiHandCalculator {
    private static readonly int[] Tiles = Enumerable.Range(1, 3)
        .SelectMany(suit => Enumerable.Range(1, 9).Select(rank => suit * 10 + rank))
        .Concat(new[] { 41, 42, 43, 44 }).ToArray();

    public static HashSet<int> Waits(List<int> hand, List<string> melds) {
        var result = new HashSet<int>();
        melds = melds ?? new List<string>();
        if (hand == null || hand.Count + melds.Count * 3 != 13) return result;
        foreach (int tile in Tiles) {
            var candidate = new List<int>(hand) { tile };
            if (Decompositions(candidate, melds).Count != 0) result.Add(tile);
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

    public static int Score(List<int> hand, List<string> melds, int flowerCount, bool selfDraw, int minFan = 0) {
        melds = melds ?? new List<string>();
        int best = 0;
        var all = hand.Concat(melds.SelectMany(MeldTiles)).ToList();
        foreach (List<string> sets in Decompositions(hand, melds)) {
            int fan = melds.All(code => code[0] == 'G') ? 1 : 0;
            if (hand.Count == 2) fan++;
            if (all.Where(tile => tile < 40).Select(tile => tile / 10).Distinct().Count() == 1)
                fan += all.Any(tile => tile >= 41) ? 1 : 2;
            if (sets.Concat(melds).All(code => code[0] != 's')) fan++;
            int flowers = flowerCount;
            foreach (string code in sets.Concat(melds)) {
                int tile = int.Parse(code.Substring(1));
                if (code[0] == 'g' || code[0] == 'G') flowers += (code[0] == 'G' ? 2 : 1) + (tile >= 41 ? 1 : 0);
                else if (code[0] == 'k' && tile >= 41) flowers++;
            }
            if (flowers == 0) flowers = 10;
            if (fan == 0 && flowers < (selfDraw ? 2 : 3)) continue;
            if (fan < minFan) continue;
            best = Math.Max(best, (1 + flowers) * (1 << Math.Min(fan, 3)));
        }
        return best;
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        int flowers = query.SelfFlowers?.Count ?? query.HuapaiCount;
        int ron = Score(query.HandWithWin, query.Melds, flowers, false, query.HepaiLimit);
        if (ron > 0) return WaitTileHint.Ron($"敲牌后 {ron}分");
        int tsumo = Score(query.HandWithWin, query.Melds, flowers, true, query.HepaiLimit);
        if (tsumo > 0) return WaitTileHint.TsumoOnly($"敲牌后仅自摸 {tsumo}分");
        // 其他普通和牌条件已满足时，才将起和番门槛不足单独标为“未起和”。
        bool belowMinimum = query.HepaiLimit > 0 && Score(query.HandWithWin, query.Melds, flowers, true) > 0;
        return WaitTileHint.None(belowMinimum ? "未起和" : "未满足");
    }
}
