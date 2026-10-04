using System;
using System.Collections.Generic;

/// <summary>Shared presentation contract for the server's four independent rating pools.</summary>
public static class RankedRules {
    public static readonly string[] Ids = { "guobiao", "riichi", "qingque", "sichuan" };
    public static readonly string[] Names = { "国标麻将", "立直麻将", "青雀", "川麻血战" };
    public const string Policy = "为适配不同人数的游玩规则的匹配机制，平台目前提供以下两种段位设计\n\n1. 对于每日对局超过 100 局的麻将规则，使用以级段制为主的段位系统。\n\n2. 对于每日对局低于 100 局的麻将规则，使用以 Elo 为主的匹配系统进行对局。待人数提升后，按照 Elo 排名设计级段制段位，并通过 Elo 对玩家的段位进行赋分。\n\n3. 对于娱乐、开发中或不需添加段位机制的规则，不开设段位匹配场。";
    public const string EloDescription = "Elo 匹配\n\n同一场次按加入顺序匹配，满四人立即进入对局。青雀和川麻血战仅开放全庄战。\n\n初始 R 为 1500，采用四人两两比较的 Elo 算法，预期映射分母为 2000，K = 32，按三个对手取平均；同名次按平局计算。鼠标指向匹配页 R 值旁的问号可查看完整算法。\n\n国标和立直同时更新本规则的 R 值及段位 PT；青雀和川麻血战仅更新 R 值。自定义对局不影响评分。";
    public const string EloAlgorithm = "<b>Elo 算法</b>\n初始 R = 1500；各规则独立计分，K = 32。\n\n对每位对手，预期 E =\n1 / (1 + 10^((对手 R - 自己 R) / 2000))\n指数范围限制在 -16 至 16。\n\n实际 S：名次高于对手为 1，并列为 0.5，低于为 0。\nR 变化 = (32 / 3) × 三位对手的 (S - E) 之和\n新 R = 原 R + R 变化；只计最终名次，不计点差。\n\n四人 R 相同时，一至四名分别变化\n+16、+5.33、-5.33、-16；并列按平局计算。\n\n保留两位小数，舍入余差由末位玩家吸收，四人合计为 0。仅匹配对局计分，自定义对局不计分。";
    public static bool Supports(string rule) => Array.IndexOf(Ids, rule) >= 0;
    public static bool IsGrade(string rule) => rule == "guobiao" || rule == "riichi";
    public static string Name(string rule) { int i=Array.IndexOf(Ids,rule); return i<0?rule:Names[i]; }
    public static string Next(string rule) => Ids[(Array.IndexOf(Ids,rule)+1)%Ids.Length];
    public static RuleRating Get(IDictionary<string,RuleRating> ratings, string rule) =>
        ratings != null && ratings.TryGetValue(rule,out var rating) && rating != null ? rating :
        new RuleRating { rule=rule, system=IsGrade(rule)?"grade":"elo", rank_name=IsGrade(rule)?"10级":"", elo=1500 };
    public static string RankCaption(RuleRating r) => Name(r.rule)+" · "+(IsGrade(r.rule)?r.rank_name:"Elo 匹配");
    public static string ScoreCaption(RuleRating r) => IsGrade(r.rule)
        ? $"{r.rank_score:0.##} / {RankConfig.RankTable[RankConfig.GetRankIndex(r.rank_name)].promoteScore} PT"
        : $"R {r.elo:0.##}  ·  {r.games} 场";
    public static float Progress(RuleRating r) => IsGrade(r.rule)
        ? UnityEngine.Mathf.Clamp01(r.rank_score/RankConfig.RankTable[RankConfig.GetRankIndex(r.rank_name)].promoteScore) : 0;
}
