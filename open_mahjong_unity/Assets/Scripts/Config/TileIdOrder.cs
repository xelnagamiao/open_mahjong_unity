using System.Collections.Generic;

/// <summary>
/// 牌 id 排序工具。
/// 牌 id 使用 11-19 万 / 21-29 饼 / 31-39 条 / 41-47 字牌；立直规则额外使用 105/205/305 表示赤 5m/5p/5s；国标花牌使用 51-58。
/// 默认 <c>Array.Sort</c> / <c>List.Sort</c> 按数值升序会把 105/205/305 甩到全部普通牌之后，
/// 导致赤 5 出现在手牌最右侧，与常规 5 不相邻。此工具按"归一化值"排序，并让赤 5 稳定紧跟同色普通 5。
/// 排序顺序支持自定义：万/饼/条三种花色的相对顺序（SuitOrderOptions 共 6 种全排列），以及字牌插入的位置（HonorOrderOptions 共 4 种）。
/// 自定义规则由 <see cref="ConfigManager"/> 在加载/变更时通过 <see cref="SetSortRule"/> 推入，默认万→饼→条→字牌。
/// </summary>
public static class TileIdOrder {
    public static readonly Comparer<int> Comparer = Comparer<int>.Create(Compare);

    /// <summary>花色顺序下拉选项（索引即 ConfigManager.HandSortSuitOrderMode）。万/饼/条共 3! = 6 种全排列。</summary>
    public static readonly string[] SuitOrderOptions = {
        "万饼条", "万条饼", "饼万条", "饼条万", "条万饼", "条饼万"
    };

    /// <summary>字牌位置下拉选项（索引即 ConfigManager.HandSortHonorOrderMode）。</summary>
    public static readonly string[] HonorOrderOptions = {
        "靠后", "第三", "第二", "最前"
    };

    // 每种花色顺序对应的三花色排列（0=万 1=饼 2=条）。
    private static readonly int[][] SuitPermutations = {
        new[] { 0, 1, 2 }, // 万饼条
        new[] { 0, 2, 1 }, // 万条饼
        new[] { 1, 0, 2 }, // 饼万条
        new[] { 1, 2, 0 }, // 饼条万
        new[] { 2, 0, 1 }, // 条万饼
        new[] { 2, 1, 0 }, // 条饼万
    };

    // suit -> 组排名（越小越靠左）。索引：0 万 1 饼 2 条 3 字牌 4 花牌/其它。
    private static readonly int[] groupRank = { 0, 1, 2, 3, 4 };

    /// <summary>
    /// 设置自定义排序规则。
    /// </summary>
    /// <param name="suitOrderMode">花色顺序，索引对应 <see cref="SuitOrderOptions"/>（0-5）。</param>
    /// <param name="honorOrderMode">字牌位置，索引对应 <see cref="HonorOrderOptions"/>：0 最后 1 第三 2 第二 3 最前。</param>
    public static void SetSortRule(int suitOrderMode, int honorOrderMode) {
        if (suitOrderMode < 0 || suitOrderMode >= SuitPermutations.Length) suitOrderMode = 0;
        if (honorOrderMode < 0 || honorOrderMode > 3) honorOrderMode = 0;

        int[] order = SuitPermutations[suitOrderMode];
        // 4 个分组槽位（3 花色 + 字牌）；字牌插入位置：最后=3 第三=2 第二=1 最前=0。
        int honorSlot = 3 - honorOrderMode;
        for (int k = 0; k < order.Length; k++) {
            int slot = k < honorSlot ? k : k + 1;
            groupRank[order[k]] = slot;
        }
        groupRank[3] = honorSlot; // 字牌
        groupRank[4] = 4;         // 花牌/未知，恒定排在四组之后
    }

    public static int Compare(int a, int b) {
        int ka = SortKey(a);
        int kb = SortKey(b);
        if (ka != kb) return ka.CompareTo(kb);
        // 排序键相同时（普通 5 与赤 5 同色）按原 id 升序：普通 5(=15/25/35) 在前，赤 5(=105/205/305) 在后
        return a.CompareTo(b);
    }

    // 组排名占高位、归一化值占低位，保证先按花色分组、组内按点数升序。归一化值 <1000。
    private static int SortKey(int tile) => groupRank[SuitOf(tile)] * 1000 + Normalize(tile);

    // 0 万 1 饼 2 条 3 字牌 4 花牌/其它。
    private static int SuitOf(int tile) {
        int t = Normalize(tile);
        if (t >= 11 && t <= 19) return 0;
        if (t >= 21 && t <= 29) return 1;
        if (t >= 31 && t <= 39) return 2;
        if (t >= 41 && t <= 47) return 3;
        return 4;
    }

    /// <summary>
    /// 将赤 5（105/205/305）归一化为同色普通 5（15/25/35），其余 id 不变。
    /// </summary>
    public static int Normalize(int tile) {
        if (tile == 105) return 15;
        if (tile == 205) return 25;
        if (tile == 305) return 35;
        return tile;
    }
}
