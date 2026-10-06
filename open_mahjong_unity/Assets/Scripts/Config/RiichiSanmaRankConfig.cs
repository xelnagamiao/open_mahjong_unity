using System;

/// <summary>Placement-only PT in the independent three-player Riichi pool.</summary>
public static class RiichiSanmaRankConfig {
    public const string Rule = "riichi_sanma";
    public const string Algorithm = "riichi_sanma_place_pt_v1";
    public static readonly int[] EastPoints = { 30, 50, 70 };

    public static string DescribeQueue(string queue) {
        string prefix = Rule + "_";
        if (queue == null || !queue.StartsWith(prefix, StringComparison.Ordinal)) return "此规则暂未开放匹配场";
        string[] parts = queue.Substring(prefix.Length).Split('_');
        if (parts.Length != 2) return "此规则暂未开放匹配场";
        int tier = Array.IndexOf(RiichiRankConfig.Tiers, parts[0]);
        bool east = parts[1] == "dongfeng";
        if (tier < 0 || (!east && parts[1] != "banzhuang")) return "此规则暂未开放匹配场";
        float points = EastPoints[tier] * (east ? 1f : 1.5f);
        string admission = tier == 0 ? "无" : tier == 1 ? "三人立直 1级至六段；七段及以上不可进入" : "三人立直四段及以上";
        return $"立直三麻 · 天凤规则 · {RiichiRankConfig.TierNames[tier]} · {(east ? "东风战" : "半庄战")}\n入场门槛：{admission}\n"
            + $"段位 PT：第一名 +{points:0}，第二名 0，第三名 -{points:0}。只按最终名次计分。\n"
            + "三人和四人立直的段位、PT 与场次独立累计。升降段沿用平台段位表；低段位不能降段时 PT 封底为 0。\n"
            + "达到升段门槛时升段；可掉段的段位仅在 PT 小于 0 时降段。十段为永久称号，固定 100/100 PT，每场变动 0。\n"
            + "同点按起始风位决定名次。特许资格可突破最低段位；七段及以上仍不可进入中级场。\n"
            + "对局设置：35000 点起始，万子只保留 1、9；筒索赤五各一张，可拔北，自摸损。\n"
            + $"提示：{(tier == 0 ? "仅枚数提示" : "无番数／枚数提示")}；时间限制：{(tier == 1 ? "20+8" : "20+5")}。\n自定义房间不计段位。";
    }
}
