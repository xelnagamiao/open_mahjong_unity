using System.Collections.Generic;

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

    public static readonly Dictionary<int, string> CurrentRoundTextRiichi = new Dictionary<int, string>(CurrentRoundTextQingque);
    public static readonly Dictionary<int, string> CurrentRoundTextClassical = new Dictionary<int, string>(CurrentRoundTextQingque);
    public static readonly Dictionary<int, string> CurrentRoundTextJiandan = new Dictionary<int, string>(CurrentRoundTextQingque);

    public static readonly Dictionary<int, string> CurrentRoundTextSichuan = new Dictionary<int, string>() {
        {1, "第一副"}, {2, "第二副"}, {3, "第三副"}, {4, "第四副"},
        {5, "第五副"}, {6, "第六副"}, {7, "第七副"}, {8, "第八副"},
        {9, "第九副"}, {10, "第十副"}, {11, "第十一副"}, {12, "第十二副"},
        {13, "第十三副"}, {14, "第十四副"}, {15, "第十五副"}, {16, "第十六副"},
    };

    public static readonly Dictionary<int, string> CurrentRoundTextChangsha = new Dictionary<int, string>() {
        {1, "第1局"}, {2, "第2局"}, {3, "第3局"}, {4, "第4局"},
        {5, "第5局"}, {6, "第6局"}, {7, "第7局"}, {8, "第8局"},
        {9, "第9局"}, {10, "第10局"}, {11, "第11局"}, {12, "第12局"},
        {13, "第13局"}, {14, "第14局"}, {15, "第15局"}, {16, "第16局"},
    };

    public static readonly Dictionary<int, string> MaxRoundText = new Dictionary<int, string> {
        { 1, "东风战" },
        { 2, "东南战" },
        { 3, "东西战" },
        { 4, "全庄战" },
    };

    public static readonly Dictionary<int, string> MaxRoundTextChangsha = new Dictionary<int, string> {
        { 1, "4局" },
        { 2, "8局" },
        { 4, "16局" },
    };

    public static string GetRoundName(string rule, int currentRound) {
        Dictionary<int, string> roundMap = null;
        if (rule == "guobiao") roundMap = CurrentRoundTextGB;
        else if (rule == "qingque") roundMap = CurrentRoundTextQingque;
        else if (rule == "riichi") roundMap = CurrentRoundTextRiichi;
        else if (rule == "classical") roundMap = CurrentRoundTextClassical;
        else if (rule == "sichuan") roundMap = CurrentRoundTextSichuan;
        else if (rule == "changsha") roundMap = CurrentRoundTextChangsha;
        else if (rule == "jiandan") roundMap = CurrentRoundTextJiandan;
        if (roundMap != null && roundMap.TryGetValue(currentRound, out string roundName)) {
            return roundName;
        }
        return $"第{currentRound}局";
    }

    public static string GetMaxRoundText(int gameRound) {
        return MaxRoundText.TryGetValue(gameRound, out string text) ? text : $"未知({gameRound})";
    }

    public static string GetMaxRoundText(string rule, int gameRound) {
        string baseRule = rule;
        int slash = !string.IsNullOrEmpty(baseRule) ? baseRule.IndexOf('/') : -1;
        if (slash >= 0) baseRule = baseRule.Substring(0, slash);
        if (baseRule == "changsha") {
            return MaxRoundTextChangsha.TryGetValue(gameRound, out string changshaText)
                ? changshaText
                : $"未知({gameRound})";
        }
        return GetMaxRoundText(gameRound);
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
