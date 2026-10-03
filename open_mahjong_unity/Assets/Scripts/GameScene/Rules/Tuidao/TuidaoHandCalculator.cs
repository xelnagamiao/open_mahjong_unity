using System.Collections.Generic;
using System.Linq;

/// <summary>仅计算 MIL 推倒和结构听口。实时过水、天听等资格由服务端裁定。</summary>
internal static class TuidaoHandCalculator {
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

    public static WaitTileHint Describe(WaitHintQuery query) {
        return IsComplete(query.HandWithWin, query.Melds)
            ? WaitTileHint.Ron("成和牌形；过水与番数依本局状态") : WaitTileHint.None("未成和");
    }
}
