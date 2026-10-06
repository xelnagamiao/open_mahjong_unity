using System;
using System.Collections.Generic;

/// <summary>Shared presentation contract for the server's independent rating pools.</summary>
public static class RankedRules {
    public const string XueliuExchangeRule = "sichuan_xueliu_exchange";
    public static readonly string[] Ids = { "guobiao", "riichi", "qingque", "sichuan", "riichi_sanma", XueliuExchangeRule };
    public static readonly string[] Names = { "国标麻将", "立直麻将", "青雀", "川麻血战", "立直三麻", "川麻血流换三张" };
    // Navigation groups preserve the six independent rating and queue identities.
    public static readonly string[] FamilyNames = { "国标麻将", "立直麻将", "青雀", "川麻" };
    public static readonly int[][] FamilyRuleIndices = { new[] { 0 }, new[] { 1, 4 }, new[] { 2 }, new[] { 3, 5 } };
    public static int FamilyIndex(int ruleIndex) {
        for(int i=0;i<FamilyRuleIndices.Length;i++) if(Array.IndexOf(FamilyRuleIndices[i],ruleIndex)>=0)return i;
        return -1;
    }
    public static string VariantName(int ruleIndex) => ruleIndex==1?"四人":ruleIndex==4?"三人":ruleIndex==3?"血战到底":ruleIndex==5?"血流换三张":Names[ruleIndex];
    public const string Policy = "为适配不同人数的游玩规则的匹配机制，平台目前提供以下两种段位设计\n1.对于每日对于超过100局的麻将规则，使用以级段制为主的段位系统\n2.对于每日对局低于100局的麻将规则，使用以elo为主的匹配系统进行对局，待人数提升后，按照elo排名设计级段制段位，并通过elo对玩家的段位进行赋分\n3.对于娱乐、开发中或不需添加段位机制的规则，不开设段位匹配场";
    public const string EloDescription = "Elo 匹配\n\n青雀、川麻血战、血流换三张独立计分，仅全庄战。按加入顺序匹配，满四人立即进入对局。\n初始 R = 1500，分母 2400，K = 32；按最终名次比较三个对手，并列算平局。桌均 R 低于 1500 时追加保护，不设个人保底。\n\n问号内显示算法。国标、立直仅结算段位/PT；自定义对局不计分。";
    public const string EloAlgorithm = "<b>Elo 算法 · 低分保护</b>\n青雀、川麻血战、血流换三张独立计分；初始 R = 1500，K = 32。\n普通预期 E = 1 / (1 + 10^((对手 R - 自己 R) / 2400))\n保护偏移 T = max(0, 1500 - 四人平均 R)\n保护预期 E' = 1 / (1 + 10^((对手 R + T - 自己 R) / 2400))\n两种预期的指数均限制在 -16 至 16。\n\n实际 S：胜 1，并列 0.5，负 0，只计最终名次。\n普通变化 = (32 / 3) × 合计(S - E)\n保护奖励 = (32 / 3) × 合计(E - E')\n合计为三个对手之和；新 R = 原 R + 普通变化 + 保护奖励。\n普通变化保留两位，舍入余差由末位玩家吸收。\n保护奖励单独保留两位；低分桌总变化可大于 0。\n四人均为 1500：+16、+5.33、-5.33、-16。\n四人均为 1000：+19.77、+9.10、-1.56、-12.23。\n不设个人保底分；国标、立直不计算 R。";
    public static bool Supports(string rule) => Array.IndexOf(Ids, rule) >= 0;
    public static bool IsGrade(string rule) => rule == "guobiao" || rule == "riichi" || rule == RiichiSanmaRankConfig.Rule;
    public static string QueueRule(string queue) {
        if (queue != null && queue.StartsWith(XueliuExchangeRule + "_", StringComparison.Ordinal)) return XueliuExchangeRule;
        if (queue != null && queue.StartsWith(RiichiSanmaRankConfig.Rule + "_", StringComparison.Ordinal)) return RiichiSanmaRankConfig.Rule;
        foreach (string rule in Ids) if (queue != null && queue.StartsWith(rule + "_", StringComparison.Ordinal)) return rule;
        return "guobiao";
    }
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
