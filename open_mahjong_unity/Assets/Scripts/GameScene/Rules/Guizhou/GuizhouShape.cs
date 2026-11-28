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
        var physical = new int[48];
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
        var roHand = hand as IReadOnlyList<int> ?? new List<int>(hand);
        var std = StandardMahjongSolver.StandardWaitingTiles(roHand, allowHonors: false, allowFifthCopy: false);
        foreach (int tile in std) if (Valid(tile) && physical[tile] < 4) result.Add(tile);
        if (melds.Count == 0) {
            int oddCount = 0, oddTile = -1;
            for (int tile = 11; tile <= 39; tile++) {
                if (Valid(tile) && (hidden[tile] & 1) != 0) { oddCount++; oddTile = tile; }
            }
            if (oddCount == 1 && physical[oddTile] < 4) result.Add(oddTile);
        }
        return result;
    }
    private static bool Valid(int tile) => tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9;
    internal static bool Standard(int[] counts, int groups) {
        var hand = new List<int>(groups * 3 + 2);
        for (int tile = 11; tile <= 39; tile++) {
            if (!Valid(tile)) continue;
            for (int k = 0; k < counts[tile]; k++) hand.Add(tile);
        }
        if (hand.Count != groups * 3 + 2) return false;
        return StandardMahjongSolver.CanStandardWin(hand, allowHonors: false);
    }
}
