using System.Collections.Generic;

/// <summary>四川番文本：累加制，3 番封顶。</summary>
internal static class SichuanFanText {
    /// <summary>
    /// 四川麻将（血战到底）番数说明：番数为累加制，3番封顶（基本分=2^min(总番,3)）。
    /// </summary>
    public static readonly Dictionary<string, string> FanToDisplaySichuan = new Dictionary<string, string> {
        {"平和", "0番"},
        {"杠", "1番"},
        {"根", "1番"},
        {"大对子", "1番"},
        {"金钩钓", "1番"},
        {"清一色", "2番"},
        {"七对", "2番"},
        {"杠上花", "1番"},
        {"杠上炮", "1番"},
        {"抢杠", "1番"},
        {"海底", "1番"},
    };

    public static string FanValue(string subRule, string fanName) {
        if (subRule == "sichuan/xueliu_exchange" && UsesModernExchangeDisplay()) {
            switch (fanName) {
                case "基本胡": return "2番";
                case "碰碰胡": return "4番";
                case "清一色": case "七对": return "6番";
                case "金钩钓": return "8番";
                case "清碰": return "10番";
                case "清七对": return "12番";
                case "清金钩钓": return "16番";
                case "门清": case "断幺九": return "1番";
                case "根": case "杠上花": case "杠上炮": case "抢杠": case "海底": return "+1倍";
                default: return "0番";
            }
        }
        if (SichuanLobby.IsXueliu(subRule)) {
            switch (fanName) {
                case "基本胡": case "门清自摸": case "明杠": return "1番";
                case "暗杠": return "2番";
                case "碰碰胡": return "4番";
                case "清一色": return "6番";
                case "清碰": return "12番";
                default: return "0番";
            }
        }
        return FanToDisplaySichuan.TryGetValue(fanName, out string display) ? display : "0番";
    }

    private static bool UsesModernExchangeDisplay() {
        var record = GameRecordManager.Instance;
        Dictionary<string, object> config = record != null && record.gameObject.activeSelf
            ? record.GetDetailedConfigSnapshot() : GameSession.Current.DetailedConfig;
        return SichuanTips.UsesExchangeScoring("sichuan/xueliu_exchange", config);
    }

    /// <summary>总计栏：累加番 + 点。</summary>
    public static SettlementTotalDisplay SettlementTotal(SettlementTotalQuery q) {
        return new SettlementTotalDisplay {
            FanText = $"{FanTotal(q.Rule, q.HuFan)}番",
            ScoreText = $"{q.HuScore}点",
        };
    }

    /// <summary>结算总计 / 计分板摘要：按番表累加总番。</summary>
    public static int FanTotal(string subRule, string[] huFan) {
        if (huFan == null) return 0;
        int total = 0;
        int extraRoots = 0;
        foreach (string fan in huFan) {
            string display = FanValue(subRule, fan);
            if (display == "+1倍") extraRoots++;
            if (display.EndsWith("番") && int.TryParse(display.Replace("番", ""), out int val)) total += val;
        }
        return total * (1 + extraRoots);
    }

    public static string ScoreboardFanText(SettlementTotalQuery q) => $"{FanTotal(q.Rule, q.HuFan)}番";

    private static readonly string[] CnNumbers = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "十二", "十三", "十四", "十五", "十六" };

    /// <summary>川麻按"副"计局：第一副 … 第十六副。</summary>
    public static string RoundName(int round) {
        return round >= 1 && round < CnNumbers.Length ? $"第{CnNumbers[round]}副" : $"第{round}副";
    }
}
