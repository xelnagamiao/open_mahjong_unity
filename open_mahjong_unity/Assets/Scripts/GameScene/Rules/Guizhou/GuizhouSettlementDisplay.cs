using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>将服务端逐家收付投影为和家结算条目；展示层不重新结算鸡杠。</summary>
public sealed class GuizhouSettlementDisplay {
    public int Indicator, ChickenTile, HandScore, KongScore, ChickenScore;
    public string[] Labels;
    public int Total => HandScore + KongScore + ChickenScore;

    // 与番种一样经 GuizhouFanText / FanCount 预制体显示，数值单位统一为分。
    private static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
        { "鸡牌", "鸡牌" }, { "金鸡", "金鸡" }, { "冲锋鸡加分", "冲锋鸡" },
        { "责任鸡加分", "责任鸡" }, { "包鸡", "包鸡" },
        { "暗杠", "暗杠" }, { "加杠", "加杠" }, { "点杠", "点杠" }, { "包杠", "包杠" },
    };
    public static GuizhouSettlementDisplay Build(GuizhouInfo info, int winner) {
        if (info?.ledger?.transfers == null) return null;
        var result = new GuizhouSettlementDisplay {
            Indicator = info.ledger.indicator ?? 0, ChickenTile = info.ledger.chicken_tile ?? 0,
        };
        var rows = new List<string>();
        var transfers = info.ledger.transfers.Where(t => t.payer == winner || t.payee == winner).ToArray();
        int Signed(GuizhouTransfer t) => t.payee == winner ? t.points : -t.points;
        result.HandScore = transfers.Where(t => t.category == "hand").Sum(Signed);
        result.KongScore = transfers.Where(t => t.category == "kong").Sum(Signed);
        result.ChickenScore = transfers.Where(t => t.category == "chicken").Sum(Signed);
        foreach (var group in transfers.Where(t => t.category == "kong" || t.category == "chicken")
            .GroupBy(t => new { t.category, t.reason, t.tile, t.unit_points, Incoming = t.payee == winner,
                Owner = t.reason == "包鸡" || t.reason == "包杠" ? t.payer : t.payee })
            .OrderBy(g => g.Key.category == "kong" ? 0 : 1).ThenBy(g => g.Key.tile).ThenBy(g => g.Key.reason)) {
            int points = group.Sum(Signed);
            string name = Names.TryGetValue(group.Key.reason ?? "", out string mapped) ? mapped : group.Key.reason;
            if (!group.Key.Incoming) name = "付" + name;
            // 同一张鸡/同一杠由三家付款时，数量仍只计一次。
            int? count = group.All(t => t.count.HasValue) ? group.Max(t => t.count.Value) : LegacyCount(group);
            if (group.Key.tile.HasValue) name += TileName(group.Key.tile.Value);
            if (count.HasValue) name += "*" + count.Value;
            rows.Add($"GZ|settlement_{group.Key.category}|{points:+#;-#;0}|{name}");
        }
        result.Labels = rows.ToArray();
        return result;
    }
    private static int? LegacyCount(IEnumerable<GuizhouTransfer> entries) {
        var t = entries.First();
        if (t.category == "kong" || t.reason == "冲锋鸡加分" || t.reason == "责任鸡加分") return 1;
        if (t.reason == "鸡牌" || t.reason == "金鸡") return entries.Max(e => e.points) / (t.reason == "金鸡" ? 2 : 1);
        // 旧牌谱的包鸡收付合并了普通鸡和责任加分，没有张数时只显示真实分数。
        return null;
    }
    private static string TileName(int tile) => tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9
        ? "一二三四五六七八九"[tile % 10 - 1] + new[] { "", "万", "筒", "条" }[tile / 10] : "";

    public SettlementTotalDisplay TotalDisplay() => new SettlementTotalDisplay {
        Layout = SettlementTotalDisplay.LayoutKind.Breakdown,
        FanText = $"和牌得分 {HandScore}分",
        FuText = $"杠{KongScore:+#;-#;0}分  捉鸡{ChickenScore:+#;-#;0}分",
        ScoreText = $"{Total:+#;-#;0}分",
    };
}
