using System.Collections.Generic;

/// <summary>
/// 牌 id 排序工具。
/// 牌 id 使用 11-19 万 / 21-29 饼 / 31-39 条 / 41-47 字牌；立直规则额外使用 105/205/305 表示赤 5m/5p/5s。
/// 默认 <c>Array.Sort</c> / <c>List.Sort</c> 按数值升序会把 105/205/305 甩到全部普通牌之后，
/// 导致赤 5 出现在手牌最右侧，与常规 5 不相邻。此工具按"归一化值"排序，并让赤 5 稳定紧跟同色普通 5。
/// </summary>
public static class TileIdOrder {
    public static readonly Comparer<int> Comparer = Comparer<int>.Create(Compare);

    public static int Compare(int a, int b) {
        int ka = NormalizeKey(a);
        int kb = NormalizeKey(b);
        if (ka != kb) return ka.CompareTo(kb);
        // 归一化值相同时（普通 5 与赤 5 同色）按原 id 升序：普通 5(=15/25/35) 在前，赤 5(=105/205/305) 在后
        return a.CompareTo(b);
    }

    private static int NormalizeKey(int tile) {
        if (tile == 105) return 15;
        if (tile == 205) return 25;
        if (tile == 305) return 35;
        return tile;
    }
}
