using System.Collections.Generic;

/// <summary>
/// 统一维护各规则的局数文本映射，供牌谱局按钮与当前局显示共用。
/// </summary>
public static class RoundTextDictionary {
    public static readonly Dictionary<int, string> CurrentRoundTextGB = new Dictionary<int, string>() {
        {1, "东风东"}, {2, "东风南"}, {3, "东风西"}, {4, "东风北"},
        {5, "南风东"}, {6, "南风南"}, {7, "南风西"}, {8, "南风北"},
        {9, "西风东"}, {10, "西风南"}, {11, "西风西"}, {12, "西风北"},
        {13, "北风东"}, {14, "北风南"}, {15, "北风西"}, {16, "北风北"},
    };

    public static readonly Dictionary<int, string> CurrentRoundTextQingque = new Dictionary<int, string>() {
        {1, "东一局"}, {2, "东二局"}, {3, "东三局"}, {4, "东四局"},
        {5, "南一局"}, {6, "南二局"}, {7, "南三局"}, {8, "南四局"},
        {9, "西一局"}, {10, "西二局"}, {11, "西三局"}, {12, "西四局"},
        {13, "北一局"}, {14, "北二局"}, {15, "北三局"}, {16, "北四局"},
    };

    public static readonly Dictionary<int, string> CurrentRoundTextRiichi = new Dictionary<int, string>() {
        {1, "东一局"}, {2, "东二局"}, {3, "东三局"}, {4, "东四局"},
        {5, "南一局"}, {6, "南二局"}, {7, "南三局"}, {8, "南四局"},
        {9, "西一局"}, {10, "西二局"}, {11, "西三局"}, {12, "西四局"},
        {13, "北一局"}, {14, "北二局"}, {15, "北三局"}, {16, "北四局"},
    };

    public static readonly Dictionary<int, string> CurrentRoundTextClassical = new Dictionary<int, string>() {
        {1, "东一局"}, {2, "东二局"}, {3, "东三局"}, {4, "东四局"},
        {5, "南一局"}, {6, "南二局"}, {7, "南三局"}, {8, "南四局"},
        {9, "西一局"}, {10, "西二局"}, {11, "西三局"}, {12, "西四局"},
        {13, "北一局"}, {14, "北二局"}, {15, "北三局"}, {16, "北四局"},
    };

    /// <summary>局数（当前第几局）显示名</summary>
    public static string GetRoundName(string rule, int currentRound) {
        Dictionary<int, string> roundMap = null;
        if (rule == "guobiao") roundMap = CurrentRoundTextGB;
        else if (rule == "qingque") roundMap = CurrentRoundTextQingque;
        else if (rule == "riichi") roundMap = CurrentRoundTextRiichi;
        else if (rule == "classical") roundMap = CurrentRoundTextClassical;
        if (roundMap != null && roundMap.TryGetValue(currentRound, out string roundName)) {
            return roundName;
        }
        return $"第{currentRound}局";
    }

    /// <summary>圈数（东风战/东南战等）显示名，供房间配置等使用。</summary>
    public static readonly Dictionary<int, string> MaxRoundText = new Dictionary<int, string> {
        { 1, "东风战" },
        { 2, "东南战" },
        { 3, "东西战" },
        { 4, "全庄战" },
    };

    public static string GetMaxRoundText(int game_round) {
        return MaxRoundText.TryGetValue(game_round, out string text) ? text : $"未知({game_round})";
    }

    /// <summary>根据服务端 match_type（如 1/4、2/4、4/4）返回局数显示名（东风战/东南战/全庄战等）</summary>
    public static string GetMatchTypeDisplay(string match_type) {
        if (string.IsNullOrEmpty(match_type)) return "";
        int slash = match_type.IndexOf('/');
        if (slash < 0 || !int.TryParse(match_type.Substring(0, slash), out int rounds))
            return "";
        return GetMaxRoundText(rounds);
    }
}
