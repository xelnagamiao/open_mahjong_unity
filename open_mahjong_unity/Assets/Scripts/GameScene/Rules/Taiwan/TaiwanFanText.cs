using System.Collections.Generic;

/// <summary>台湾台数文本：基础台数表 + 房间细则（detailed_config）调整。</summary>
internal static class TaiwanFanText {
    public static readonly Dictionary<string, int> TaiwanBaseTai = new Dictionary<string, int> {
        {"门清", 1}, {"不求人", 1}, {"自摸", 1}, {"门风刻", 1}, {"圈风刻", 1},
        {"正花", 1}, {"花牌", 1}, {"三元牌", 1}, {"独听", 1}, {"抢杠", 1}, {"杠上开花", 1},
        {"连庄拉庄", 1},
        {"海底捞月", 1}, {"花杠", 1},
        {"平胡", 2}, {"三暗刻", 2}, {"全求人", 2},
        {"碰碰胡", 4}, {"小三元", 4}, {"混一色", 4},
        {"四暗刻", 5},
        {"地听", 8}, {"五暗刻", 8}, {"大三元", 8}, {"小四喜", 8}, {"清一色", 8},
        {"八仙过海", 8}, {"七抢一", 8}, {"八对半", 8}, {"四杠子", 8},
        {"天听", 16}, {"地胡", 16}, {"人胡", 16}, {"字一色", 16}, {"大四喜", 16}, {"五杠子", 16},
        {"天胡", 24},
        {"报听", 1}, {"无花", 1}, {"半求人", 1}, {"河底捞鱼", 1}, {"风刻", 1},
        {"无字无花", 2}, {"明杠", 1}, {"暗杠", 2}, {"配牌花胡", 4},
    };

    /// <summary>总计栏：隐藏番栏，点栏写"x台  y点"。</summary>
    public static SettlementTotalDisplay SettlementTotal(SettlementTotalQuery q) {
        string pointText = q.WinnerPointDelta.HasValue ? q.WinnerPointDelta.Value.ToString() : "—";
        return new SettlementTotalDisplay { FanText = null, ScoreText = $"{q.HuScore}台  {pointText}点" };
    }

    public static string FanValue(string subRule, string fanName) {
        if (string.IsNullOrEmpty(fanName)) return "0台";
        string baseName = fanName;
        int count = 1;
        int starIndex = fanName.LastIndexOf('*');
        if (starIndex > 0
            && int.TryParse(fanName.Substring(starIndex + 1), out int parsedCount)
            && parsedCount > 0) {
            baseName = fanName.Substring(0, starIndex);
            count = parsedCount;
        }
        if (!TaiwanBaseTai.TryGetValue(baseName, out int tai)) return "0台";
        IDictionary<string, object> ruleValues = null;
        GameRecordManager record = GameRecordManager.Instance;
        if (record != null && record.gameObject.activeSelf) {
            ruleValues = record.GetDetailedConfigSnapshot();
        }
        if (ruleValues == null && TaiwanGameState.Active != null) {
            ruleValues = GameSession.Current.DetailedConfig;
        }
        tai = TaiwanExternal.ResolveFanTai(baseName, tai, ruleValues);
        return $"{tai * count}台";
    }

    public static string ScoreboardFanText(SettlementTotalQuery q) => $"{q.HuScore}台";
}
