using System;
using System.Collections.Generic;

/// <summary>Shared presentation contract for the server's independent rating pools.</summary>
public static class RankedRules {
    public const string DoublesRule = "sichuan_doubles";
    public const string DoublesDescription = "川麻血战 · 2V2\n\n双人组队，队友对坐，半庄 8 副。两队按现有血战双打规则判定胜负，任意队员均可开始或取消匹配。\n独立双打 Elo，初始 1500；队伍实力取两人均分。按团队胜负计算，两名队友获得相同涨跌，换队友保留个人双打分。\nE = 1 / (1 + 10^((对手队均分 - 本队均分) / 2400))\n每人变化 = 32 × (团队结果 - E)\n胜 1、平 0.5、负 0；严格零和，不设低分奖励。两队均分相同，获胜每人 +16，失败每人 -16。\n队内分差最多 500。匹配范围随双方等待时间扩大：30 秒内 ±100，60 秒内 ±200，120 秒内 ±350，之后 ±500。";
    public const string XueliuExchangeRule = "sichuan_xueliu_exchange";
    public static readonly string[] Ids = { "guobiao", "riichi", "qingque", "sichuan", "riichi_sanma", XueliuExchangeRule, "hongque", DoublesRule };
    public static readonly string[] Names = { "国标麻将", "立直麻将", "青雀", "川麻血战", "立直三麻", "川麻血流换三张", "虹雀", "川麻血战·2V2" };
    // Navigation groups preserve the independent rating and queue identities.
    public static readonly string[] FamilyNames = { "国标麻将", "立直麻将", "青雀", "川麻", "虹雀" };
    public static readonly int[][] FamilyRuleIndices = { new[] { 0 }, new[] { 1, 4 }, new[] { 2 }, new[] { 3, 5, 7 }, new[] { 6 } };
    public static int FamilyIndex(int ruleIndex) {
        for(int i=0;i<FamilyRuleIndices.Length;i++) if(Array.IndexOf(FamilyRuleIndices[i],ruleIndex)>=0)return i;
        return -1;
    }
    public static string VariantName(int ruleIndex) => ruleIndex==7?"血战 2V2":ruleIndex==1?"四人":ruleIndex==4?"三人":ruleIndex==3?"血战到底":ruleIndex==5?"血流换三张":Names[ruleIndex];
    public const string Policy = "为适配不同人数的游玩规则的匹配机制，平台目前提供以下两种段位设计\n1.对于每日对于超过100局的麻将规则，使用以级段制为主的段位系统\n2.对于每日对局低于100局的麻将规则，使用以elo为主的匹配系统进行对局，待人数提升后，按照elo排名设计级段制段位，并通过elo对玩家的段位进行赋分\n3.对于娱乐、开发中或不需添加段位机制的规则，不开设段位匹配场";
    public const string EloDescription = "Elo 匹配\n\n各规则独立计分。虹雀可选半庄8局或全庄16局，川麻血战和血流换三张为半庄战（8副）。按加入顺序匹配，满四人立即进入对局。\n初始 R = 1500，分母 2400。虹雀半庄 K=16，全庄 K=32；川麻 K=32。按最终名次比较三个对手，并列算平局。桌均 R 低于 1500 时追加保护，不设个人保底。\n\n青雀使用独立的局均素点算法，详见青雀页面。国标、立直仅结算段位/PT；自定义对局不计分。";
    public const string QingqueEloDescription = "青雀 · 素点 Elo\n\n全庄战（16局）和无固定时限共用青雀积分，初始 R = 1500。满四人开始，按完成各局的净素点、实际局数和同桌实力结算；最终名次不直接决定 Elo。\n\n同为1500分，16局净赢800素点约加16 Elo，净赢1600素点约加32 Elo。大幅领先可获得更多积分，不设单场涨跌上限。低分桌保留原有保护。\n\n无固定时限：随时申请结束，本局继续打完；局间申请则立即结束，不再开始下一局。实际完成局数包含流局。结算冻结前可撤回申请。\n\n自定义对局不计 Elo。问号内显示完整算法。";
    public const string QingqueEloAlgorithm = "<b>青雀 · 局均素点 Elo</b>\n初始 R = 1500；全庄与无固定时限共用积分。\nN = 实际完成局数（包含流局），Y = 累计净素点。\n局均 A = Y / N；桌均 M = 四人 R 的平均值。\n预期局均 = (自己 R - M) / 32\n普通变化 = 32N / (N + 1584) × (A - 预期局均)\n不按名次计分，不设涨跌硬上限。\n保护系数 K = 3200N / (N + 1584)\nT = max(0, 1500 - M)\nE = 1 / (1 + 10^((对手 R - 自己 R) / 2400))\nE' = 1 / (1 + 10^((对手 R + T - 自己 R) / 2400))\n指数限制在 -16 至 16；合计为三个对手之和。\n保护奖励 = (K / 3) × 合计(E - E')\n新 R = 原 R + 普通变化 + 保护奖励。\n普通变化按分币分配余差保持零和，保护另取两位。\n同为1500分、16局：净赢800加16，1600加32。\n未完成局不计入；零完成局不结算。";
    public const string EloAlgorithm = "<b>Elo 算法 · 低分保护</b>\n各规则独立计分，初始 R = 1500。\n虹雀半庄 K=16；虹雀全庄及其他 Elo 规则 K=32。\n普通预期 E = 1 / (1 + 10^((对手 R - 自己 R) / 2400))\n保护偏移 T = max(0, 1500 - 四人平均 R)\n保护预期 E' = 1 / (1 + 10^((对手 R + T - 自己 R) / 2400))\n两种预期的指数均限制在 -16 至 16。\n实际 S：胜 1，并列 0.5，负 0，只计最终名次。\n普通变化 = (K / 3) × 合计(S - E)\n保护奖励 = (K / 3) × 合计(E - E')\n合计为三个对手之和；新 R = 原 R + 普通变化 + 保护奖励。\n普通变化保留两位，舍入余差由末位玩家吸收。\n保护奖励单独保留两位；低分桌总变化可大于 0。\n1500 同分桌，K=16：+8、+2.67、-2.67、-8。\n1500 同分桌，K=32：+16、+5.33、-5.33、-16。\n1000 同分桌，K=32：+19.77、+9.10、-1.56、-12.23。\n不设个人保底分；国标、立直不计算 R。";
    public static bool Supports(string rule) => Array.IndexOf(Ids, rule) >= 0;
    public static bool IsGrade(string rule) => rule == "guobiao" || rule == "riichi" || rule == RiichiSanmaRankConfig.Rule;
    public static string QueueRule(string queue) {
        if (queue == PartyNetworkManager.Queue) return DoublesRule;
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
