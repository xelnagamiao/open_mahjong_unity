/// <summary>虹雀番文本：服务端番名形如 "名称|番数"。</summary>
internal static class HongqueFanText {
    public static string FanName(string subRule, string fanName) {
        int separator = fanName.LastIndexOf('|');
        return separator > 0 ? fanName.Substring(0, separator) : fanName;
    }

    public static string FanValue(string subRule, string fanName) {
        int separator = fanName?.LastIndexOf('|') ?? -1;
        if (separator > 0 && int.TryParse(fanName.Substring(separator + 1), out int fan)) return $"{fan}番";
        return "0番";
    }

    /// <summary>和牌总分达到 30 播放 Gong_hu。</summary>
    public static bool PlaysGongHu(string subRule, string[] huFan, int huScore) => huScore >= 30;

    /// <summary>总计栏：底 + 累加番 + 点。</summary>
    public static SettlementTotalDisplay SettlementTotal(SettlementTotalQuery q) {
        return new SettlementTotalDisplay {
            FuText = q.BaseFu.HasValue ? $"{q.BaseFu.Value}底" : null,
            FanText = $"{FanTotal(q.HuFan)}番",
            ScoreText = $"{q.HuScore}点",
        };
    }

    public static int FanTotal(string[] huFan) {
        int total = 0;
        foreach (string fan in huFan ?? System.Array.Empty<string>()) {
            int separator = fan?.LastIndexOf('|') ?? -1;
            if (separator > 0 && int.TryParse(fan.Substring(separator + 1), out int value)) total += value;
        }
        return total;
    }

    public static string ScoreboardFanText(SettlementTotalQuery q) => $"{q.HuScore}分";

    private static readonly string[] CnNumbers = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "十二", "十三", "十四", "十五", "十六" };

    public static string RoundName(int round) {
        return round >= 1 && round < CnNumbers.Length ? $"第{CnNumbers[round]}局" : $"第{round}局";
    }

    /// <summary>虹雀 max_round 就是实际局数（4/8/16）。</summary>
    public static string MaxRoundText(int gameRound) {
        switch (gameRound) {
            case 4: return "四局";
            case 8: return "八局";
            case 16: return "十六局";
            default: return $"共{gameRound}局";
        }
    }
}
