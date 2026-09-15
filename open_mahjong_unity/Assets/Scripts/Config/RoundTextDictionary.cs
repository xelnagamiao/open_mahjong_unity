using System.Collections.Generic;

/// <summary>
/// 局名 / 总局数文案入口。族通过 RuleManifest.RoundName / MaxRoundText 声明自己的写法；
/// 这里只提供两套通用表（风圈+座风、风圈+数字）与缺省回退，不出现任何规则名。
/// </summary>
public static class RoundTextDictionary {
    /// <summary>风圈 + 座风（"东风东"…"北风北"）。</summary>
    public static readonly Dictionary<int, string> WindSeatRoundNames = new Dictionary<int, string>() {
        {1, "东风东"}, {2, "东风南"}, {3, "东风西"}, {4, "东风北"},
        {5, "南风东"}, {6, "南风南"}, {7, "南风西"}, {8, "南风北"},
        {9, "西风东"}, {10, "西风南"}, {11, "西风西"}, {12, "西风北"},
        {13, "北风东"}, {14, "北风南"}, {15, "北风西"}, {16, "北风北"},
    };

    /// <summary>风圈 + 局序（"东一局"…"北四局"）。</summary>
    public static readonly Dictionary<int, string> WindNumberRoundNames = new Dictionary<int, string>() {
        {1, "东一局"}, {2, "东二局"}, {3, "东三局"}, {4, "东四局"},
        {5, "南一局"}, {6, "南二局"}, {7, "南三局"}, {8, "南四局"},
        {9, "西一局"}, {10, "西二局"}, {11, "西三局"}, {12, "西四局"},
        {13, "北一局"}, {14, "北二局"}, {15, "北三局"}, {16, "北四局"},
    };

    /// <summary>通用总局数文案：房间 game_round 为风圈数。</summary>
    public static readonly Dictionary<int, string> MaxRoundText = new Dictionary<int, string> {
        { 1, "东风战" },
        { 2, "东南战" },
        { 3, "东西战" },
        { 4, "全庄战" },
    };

    public static string WindSeatRoundName(int round) {
        return WindSeatRoundNames.TryGetValue(round, out string name) ? name : $"第{round}局";
    }

    public static string WindNumberRoundName(int round) {
        return WindNumberRoundNames.TryGetValue(round, out string name) ? name : $"第{round}局";
    }

    /// <summary>按规则（room_rule 或 sub_rule）给出第 currentRound 局的局名；未声明的族用 "第n局"。</summary>
    public static string GetRoundName(string rule, int currentRound) {
        var hook = RuleRegistry.Resolve(rule, rule)?.RoundName;
        return hook != null ? hook(currentRound) : $"第{currentRound}局";
    }

    public static string GetMaxRoundText(int gameRound) {
        return MaxRoundText.TryGetValue(gameRound, out string text) ? text : $"未知({gameRound})";
    }

    public static string GetMaxRoundText(string rule, int gameRound) {
        var hook = RuleRegistry.Resolve(rule, rule)?.MaxRoundText;
        return hook != null ? hook(gameRound) : GetMaxRoundText(gameRound);
    }

    /// <summary>
    /// 服务端 max_round 换算成实际局数：通用族是风圈数 ×4，声明 MaxRoundIsHandCount 的族（虹雀）本身就是局数。
    /// </summary>
    public static int ToTotalHands(string rule, int maxRound) {
        if (maxRound <= 0) return 0;
        return RuleRegistry.Resolve(rule, rule)?.MaxRoundIsHandCount == true ? maxRound : maxRound * 4;
    }

    public static string GetMatchTypeDisplay(string matchType) {
        return GetMatchTypeDisplay(null, matchType);
    }

    public static string GetMatchTypeDisplay(string rule, string matchType) {
        if (string.IsNullOrEmpty(matchType)) return "";
        string normalized = matchType;
        if (normalized.EndsWith("_rank")) {
            normalized = normalized.Substring(0, normalized.Length - "_rank".Length);
        }
        int slash = normalized.IndexOf('/');
        if (slash < 0 || !int.TryParse(normalized.Substring(0, slash), out int rounds)) {
            return "";
        }
        return GetMaxRoundText(rule, rounds);
    }
}
