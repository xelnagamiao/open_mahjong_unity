using System;
using System.Globalization;

/// <summary>Presentation of the server's riichi_mleague_pt_v1 configuration.</summary>
public static class RiichiRankConfig {
    public const string Algorithm = "riichi_mleague_pt_v1";
    public const string Formula = "PT = 局制系数 ×（场次倍率 × 比赛分 - 段位扣分 C）";
    public static readonly string[] Tiers = { "beginner", "intermediate", "advanced" };
    public static readonly string[] TierNames = { "初级场", "中级场", "高级场" };
    public static readonly decimal[] TierMultipliers = { 0.3m, 0.6m, 1m };
    public static readonly decimal[] TierCostOffsets = { 5.25m, 11.375m, 18.375m };
    public static readonly decimal[] BaseCosts = {
        0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 2.625m, 6.125m,
        7.875m, 9.625m, 11.375m, 16.625m, 18.375m, 21m, 23.625m, 28.875m, 31.5m, 0m,
    };
    public static decimal RankCost(int rankIndex, int tierIndex) => rankIndex == 19 ? 0m : BaseCosts[rankIndex] - TierCostOffsets[tierIndex];
    public static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string DescribeQueue(string queue) {
        string[] parts = queue.Split('_');
        if (parts.Length != 3 || parts[0] != "riichi") return "此规则暂未开放匹配场";
        int tier = Array.IndexOf(Tiers, parts[1]);
        bool east = parts[2] == "dongfeng";
        if (tier < 0 || (!east && parts[2] != "banzhuang")) return "此规则暂未开放匹配场";
        string admission = tier == 0 ? "无" : tier == 1 ? "立直 1级至六段；七段及以上不可进入" : "立直四段及以上";
        string timer = tier == 1 ? "20+8" : "20+5";
        return $"立直麻将 · M规 · {TierNames[tier]} · {(east ? "东风战" : "半庄战")}\n入场门槛：{admission}\n"
            + "比赛分 =（终局点数 - 30000）/1000 + 顺位奖励\n顺位奖励：一位 +50，二位 +10，三位 -10，四位 -30；一位奖励已含头名奖。\n"
            + Formula + $"\n本场倍率 {Number(TierMultipliers[tier])}，局制系数 {(east ? "0.7" : "1")}；C 见下方段位表，负数为保护奖励。\n"
            + "同点分摊顺位奖励；终局供托由头名取得，同点头名分摊。十段固定 100/100 PT，本场变动为 0。\n"
            + "特许资格可突破最低段位；七段及以上仍不可进入中级场。\n"
            + "对局设置：25000点起始，赤牌各一张；头跳，无击飞、无西入、无途中流局、无流满、无和止／听止。\n"
            + $"提示：{(tier == 0 ? "仅枚数提示" : "无番数／枚数提示")}；时间限制：{timer}";
    }
}
