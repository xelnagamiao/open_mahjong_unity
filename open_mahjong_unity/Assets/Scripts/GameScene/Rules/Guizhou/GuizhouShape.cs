using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>物理可和的四面子一对 / 七对。流局的理论听牌由服务端独立计算。</summary>
public static class GuizhouShape {
    public static HashSet<int> Waiting(TingpaiQuery query) => Waiting(query.Hand, query.Melds);
    public static HashSet<int> Waiting(IList<int> hand, IList<string> melds) {
        var result = new HashSet<int>();
        melds = melds ?? Array.Empty<string>();
        if (hand == null || melds.Count > 4 || hand.Count != (4 - melds.Count) * 3 + 1) return result;
        var hidden = new int[40];
        var physical = new int[40];
        foreach (int tile in hand) {
            if (!Valid(tile) || ++physical[tile] > 4) return result;
            hidden[tile]++;
        }
        foreach (string code in melds) {
            if (string.IsNullOrEmpty(code) || code.Length != 3 || !"kgG".Contains(code[0]) ||
                !int.TryParse(code.Substring(1), out int tile) || !Valid(tile)) return result;
            physical[tile] += code[0] == 'k' ? 3 : 4;
            if (physical[tile] > 4) return result;
        }
        for (int tile = 11; tile <= 39; tile++) {
            if (!Valid(tile) || physical[tile] >= 4) continue;
            hidden[tile]++;
            if ((melds.Count == 0 && hidden.All(n => n % 2 == 0)) || Standard(hidden, 4 - melds.Count)) result.Add(tile);
            hidden[tile]--;
        }
        return result;
    }
    private static bool Valid(int tile) => tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9;
    private static bool Standard(int[] counts, int groups) {
        for (int tile = 11; tile <= 39; tile++) {
            if (counts[tile] < 2) continue;
            counts[tile] -= 2;
            bool complete = Groups(counts, groups);
            counts[tile] += 2;
            if (complete) return true;
        }
        return false;
    }
    private static bool Groups(int[] counts, int groups) {
        int tile = Array.FindIndex(counts, n => n > 0);
        if (tile < 0) return groups == 0;
        if (groups <= 0) return false;
        if (counts[tile] >= 3) {
            counts[tile] -= 3;
            bool complete = Groups(counts, groups - 1);
            counts[tile] += 3;
            if (complete) return true;
        }
        if (tile % 10 <= 7 && counts[tile + 1] > 0 && counts[tile + 2] > 0) {
            counts[tile]--; counts[tile + 1]--; counts[tile + 2]--;
            bool complete = Groups(counts, groups - 1);
            counts[tile]++; counts[tile + 1]++; counts[tile + 2]++;
            if (complete) return true;
        }
        return false;
    }
}
