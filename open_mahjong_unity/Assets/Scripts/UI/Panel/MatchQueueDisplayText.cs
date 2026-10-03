using System.Collections.Generic;

/// <summary>
/// 匹配队列在前端的展示文案（与服务器 message 解耦）。默认按 queue_type 拼成「国标麻将-场次-局制」；可在 <see cref="ExplicitTitles"/> 中按 key 覆盖。
/// </summary>
public static class MatchQueueDisplayText {
    /// <summary>
    /// 与 <see cref="MatchButton"/> 的 QueueType 一致，例如 beginner_banzhuang。在此写死即可单独覆盖展示，不写则走默认拼接。
    /// </summary>
    public static readonly Dictionary<string, string> ExplicitTitles = new Dictionary<string, string> {
        // The legacy queue ID now represents a full-length Sichuan match.
        { "sichuan_elo_xuezhan", "川麻血战-Elo 匹配-全庄战" },
    };

    private const string RuleTitle = "国标麻将";

    private static readonly Dictionary<string, string> TierTitles = new Dictionary<string, string> {
        { "beginner", "初级场" },
        { "intermediate", "中级场" },
        { "advanced", "高级场" },
        { "mcrpl", "MCRPL" },
        { "elo", "Elo 匹配" },
    };

    private static readonly Dictionary<string, string> GameTypeTitles = new Dictionary<string, string> {
        { "dongfeng", "东风战" },
        { "banzhuang", "半庄战" },
        { "quanzhuang", "全庄战" },
        { "xuezhan", "血战到底" },
    };

    /// <summary>
    /// 从 queue_type（如 beginner_quanzhuang）解析场次名（初级场等）。
    /// </summary>
    public static string GetTierTitle(string queueType) {
        if (string.IsNullOrEmpty(queueType)) {
            return "排位";
        }
        string ruleTitle=RuleTitle;
        foreach(string rule in RankedRules.Ids)if(queueType.StartsWith(rule+"_")){ruleTitle=RankedRules.Name(rule);queueType=queueType.Substring(rule.Length+1);break;}
        int u = queueType.LastIndexOf('_');
        string tierKey = u > 0 ? queueType.Substring(0, u) : queueType;
        return TierTitles.TryGetValue(tierKey, out string t) ? t : tierKey;
    }

    /// <summary>
    /// 匹配中 / 匹配成功面板上方的规则描述一行。
    /// </summary>
    public static string GetQueueTitle(string queueType) {
        if (string.IsNullOrEmpty(queueType)) {
            return RuleTitle;
        }
        if (ExplicitTitles.TryGetValue(queueType, out string custom)) {
            return custom;
        }
        string ruleTitle=RuleTitle;
        foreach(string rule in RankedRules.Ids)if(queueType.StartsWith(rule+"_")){ruleTitle=RankedRules.Name(rule);queueType=queueType.Substring(rule.Length+1);break;}
        int u = queueType.LastIndexOf('_');
        if (u <= 0 || u >= queueType.Length - 1) {
            return $"{RuleTitle}-{queueType}";
        }
        string tierKey = queueType.Substring(0, u);
        string gameKey = queueType.Substring(u + 1);
        string tier = TierTitles.TryGetValue(tierKey, out string t) ? t : tierKey;
        string game = GameTypeTitles.TryGetValue(gameKey, out string g) ? g : gameKey;
        return $"{ruleTitle}-{tier}-{game}";
    }
}
