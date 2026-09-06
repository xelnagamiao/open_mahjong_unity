using System.Collections.Generic;

/// <summary>古典番/副文本：副表 + 翻表；"满贯"用 -1 表示。</summary>
internal static class ClassicalFanText {
    /// <summary>
    /// 古典规则副数说明（Fu）
    /// </summary>
    public static readonly Dictionary<string, string> FuToDisplayClassical = new Dictionary<string, string> {
        {"和牌", "10副"}, {"自摸", "2副"}, {"边嵌吊", "2副"},
        {"刻子", "2副"}, {"暗刻", "4副"}, {"明杠", "8副"}, {"暗杠", "16副"},
        {"幺九刻", "4副"}, {"幺九暗刻", "8副"}, {"幺九明杠", "16副"}, {"幺九暗杠", "32副"},
        {"番牌刻", "8副"}, {"番牌暗刻", "16副"}, {"番牌明杠", "32副"}, {"番牌暗杠", "64副"},
        {"番牌对", "2副"}, {"番牌对*2", "4副"}, {"番牌对*3", "6副"}, {"番牌对*4", "8副"},
        {"番牌对*5", "10副"}, {"番牌对*6", "12副"}, {"番牌对*7", "14副"},
        {"满贯", "300副"},

        {"刻子*2", "4副"}, {"刻子*3", "6副"}, {"刻子*4", "8副"},
        {"暗刻*2", "8副"}, {"暗刻*3", "12副"}, {"暗刻*4", "16副"},
        {"明杠*2", "16副"}, {"明杠*3", "24副"}, {"明杠*4", "32副"},
        {"暗杠*2", "32副"}, {"暗杠*3", "48副"}, {"暗杠*4", "64副"},

        {"幺九刻*2", "8副"}, {"幺九刻*3", "12副"}, {"幺九刻*4", "16副"},
        {"幺九暗刻*2", "16副"}, {"幺九暗刻*3", "24副"}, {"幺九暗刻*4", "32副"},
        {"幺九明杠*2", "32副"}, {"幺九明杠*3", "48副"}, {"幺九明杠*4", "64副"},
        {"幺九暗杠*2", "64副"}, {"幺九暗杠*3", "96副"}, {"幺九暗杠*4", "128副"},

        {"番牌刻*2", "16副"}, {"番牌刻*3", "24副"}, {"番牌刻*4", "32副"},
        {"番牌暗刻*2", "32副"}, {"番牌暗刻*3", "48副"}, {"番牌暗刻*4", "64副"},
        {"番牌明杠*2", "64副"}, {"番牌明杠*3", "96副"}, {"番牌明杠*4", "128副"},
        {"番牌暗杠*2", "128副"}, {"番牌暗杠*3", "192副"}, {"番牌暗杠*4", "256副"}
    };

    /// <summary>
    /// 古典规则番数说明（Fan）
    /// </summary>
    public static readonly Dictionary<string, string> FanToDisplayClassical = new Dictionary<string, string> {
        {"混一色", "1翻"},
        {"小三元", "2翻"},
        {"清一色", "3翻"},
        {"字一色", "3翻"},
        {"鸾凤和鸣", "1翻"},
        {"岭上开花", "1翻"},
        {"海底捞月", "1翻"},
        {"金鸡夺食", "1翻"},
        {"大三元", "满贯"},
        {"大四喜", "满贯"},
        {"小四喜", "满贯"},
        {"天和", "满贯"},
        {"地和", "满贯"},
        {"九莲宝灯", "满贯"},
        {"国士无双", "满贯"},
    };

    public static string FanValue(string subRule, string fanName) {
        return FanToDisplayClassical.TryGetValue(fanName, out string display) ? display : "0番";
    }

    /// <summary>副种名称 → 副数显示（"10副"），未命中 "0副"。</summary>
    public static string FuValue(string fuName) {
        return FuToDisplayClassical.TryGetValue(fuName, out string display) ? display : "0副";
    }

    /// <summary>副种展示名（"刻子*2" → "数牌刻*2"）。</summary>
    public static string FuName(string fuName) {
        if (fuName == "刻子") return "数牌刻";
        if (fuName.StartsWith("刻子*")) return fuName.Replace("刻子*", "数牌刻*");
        return fuName;
    }

    /// <summary>总计栏：副 + 翻（满贯）+ 点，300 点以上标满贯。</summary>
    public static SettlementTotalDisplay SettlementTotal(SettlementTotalQuery q) {
        int fanTotal = FanTotal(q.Rule, q.HuFan);
        return new SettlementTotalDisplay {
            FuText = q.BaseFu.HasValue ? $"{q.BaseFu.Value}副" : null,
            FanText = fanTotal >= 0 ? $"{fanTotal}番" : "满贯",
            ScoreText = $"{q.HuScore}点",
            LimitText = q.HuScore >= 300 ? "满贯" : null,
        };
    }

    /// <summary>翻数总和；含满贯级役种返回 -1。</summary>
    public static int FanTotal(string subRule, string[] huFan) {
        if (huFan == null) return 0;
        int total = 0;
        foreach (string fan in huFan) {
            string display = FanValue(subRule, fan);
            if (display == "满贯") return -1;
            if (display.EndsWith("翻") && int.TryParse(display.Replace("翻", ""), out int val)) total += val;
        }
        return total;
    }

    /// <summary>计分板摘要：翻数总和或"满贯"。</summary>
    public static string ScoreboardFanText(SettlementTotalQuery q) {
        int fanTotal = FanTotal(q.Rule, q.HuFan);
        return fanTotal >= 0 ? $"{fanTotal}番" : "满贯";
    }
}
