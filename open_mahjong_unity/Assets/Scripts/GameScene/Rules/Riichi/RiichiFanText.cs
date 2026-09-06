using System.Collections.Generic;

/// <summary>日麻役文本：役名去掉门清/食下后缀展示，番数按完整 key 查表。</summary>
internal static class RiichiFanText {
    /// <summary>
    /// 立直麻将役种番（han）数显示映射，役名与 mahjong 库中文映射保持一致。
    /// 满贯及以上为“役满/累计役满”。
    /// </summary>
    public static readonly Dictionary<string, string> FanToDisplayRiichi = new Dictionary<string, string> {
        // 1番
        {"立直", "1番"}, {"门前清自摸和", "1番"}, {"平和", "1番"}, {"断幺九", "1番"},
        {"一杯口", "1番"}, {"役牌·白", "1番"}, {"役牌·发", "1番"}, {"役牌·中", "1番"},
        {"自风·东", "1番"}, {"自风·南", "1番"}, {"自风·西", "1番"}, {"自风·北", "1番"},
        {"场风·东", "1番"}, {"场风·南", "1番"}, {"场风·西", "1番"}, {"场风·北", "1番"},
        {"岭上开花", "1番"}, {"枪杠", "1番"}, {"海底捞月", "1番"}, {"河底捞鱼", "1番"},
        {"一发", "1番"}, {"宝牌", "1番"}, {"赤宝牌", "1番"}, {"里宝牌", "1番"},
        {"错和", "1番"},
        {"赤宝牌*1", "1番"}, {"赤宝牌*2", "2番"}, {"赤宝牌*3", "3番"},
        {"宝牌*1", "1番"}, {"宝牌*2", "2番"}, {"宝牌*3", "3番"}, {"宝牌*4", "4番"}, {"宝牌*5", "5番"},
        {"宝牌*6", "6番"}, {"宝牌*7", "7番"}, {"宝牌*8", "8番"}, {"宝牌*9", "9番"}, {"宝牌*10", "10番"},
        {"宝牌*11", "11番"}, {"宝牌*12", "12番"}, {"宝牌*13", "13番"}, {"宝牌*14", "14番"}, {"宝牌*15", "15番"},
        {"宝牌*16", "16番"}, {"宝牌*17", "17番"}, {"宝牌*18", "18番"}, {"宝牌*19", "19番"}, {"宝牌*20", "20番"},
        {"里宝牌*1", "1番"}, {"里宝牌*2", "2番"}, {"里宝牌*3", "3番"}, {"里宝牌*4", "4番"}, {"里宝牌*5", "5番"},
        {"里宝牌*6", "6番"}, {"里宝牌*7", "7番"}, {"里宝牌*8", "8番"}, {"里宝牌*9", "9番"}, {"里宝牌*10", "10番"},
        {"里宝牌*11", "11番"}, {"里宝牌*12", "12番"}, {"里宝牌*13", "13番"}, {"里宝牌*14", "14番"}, {"里宝牌*15", "15番"},
        {"里宝牌*16", "16番"}, {"里宝牌*17", "17番"}, {"里宝牌*18", "18番"}, {"里宝牌*19", "19番"}, {"里宝牌*20", "20番"},
        // 2番
        {"双立直", "2番"}, {"三色同刻", "2番"}, {"三杠子", "2番"}, {"对对和", "2番"},
        {"三暗刻", "2番"}, {"小三元", "2番"}, {"混老头", "2番"}, {"七对子", "2番"},
        {"混全带幺九", "2番"}, {"一气通贯", "2番"}, {"三色同顺", "2番"},

        {"一气通贯（门清）", "2番"}, {"一气通贯（食下）", "1番"},
        {"三色同顺（门清）", "2番"}, {"三色同顺（食下）", "1番"},
        {"混全带幺九（门清）", "2番"}, {"混全带幺九（食下）", "1番"},
        {"纯全带幺九（门清）", "3番"}, {"纯全带幺九（食下）", "2番"},
        {"混一色（门清）", "3番"}, {"混一色（食下）", "2番"},
        {"清一色（门清）", "6番"}, {"清一色（食下）", "5番"},
        // 3番
        {"二杯口", "3番"}, {"纯全带幺九", "3番"}, {"混一色", "3番"},
        // 6番
        {"清一色", "6番"},
        // 役满（13番计）
        {"天和", "役满"}, {"地和", "役满"}, {"大三元", "役满"}, {"四暗刻", "役满"},
        {"字一色", "役满"}, {"绿一色", "役满"}, {"清老头", "役满"}, {"国士无双", "役满"},
        {"小四喜", "役满"}, {"四杠子", "役满"}, {"九莲宝灯", "役满"},
        // 双倍役满
        {"四暗刻单骑", "双倍役满"}, {"国士无双十三面", "双倍役满"},
        {"纯正九莲宝灯", "双倍役满"}, {"大四喜", "双倍役满"},
    };

    /// <summary>
    /// 立直麻将中当前未在对局启用的役名与番数显示对照；牌谱或服务端仍可能下发这些名称。
    /// </summary>
    public static readonly Dictionary<string, string> FanToDisplayRiichiInactive = new Dictionary<string, string> {
        {"开立直", "2番"},
        {"双倍开立直", "3番"},
        {"人和", "5番"},
        {"流局满贯", "满贯"},
        {"大七星", "役满"},
        {"大车轮", "役满"},
        {"八连庄", "役满"},
        {"包牌", "役满"},
    };

    /// <summary>去掉服务端用于区分门清/食下番数的后缀。</summary>
    public static string YakuName(string fanName) {
        const string menqing = "（门清）";
        const string shixia = "（食下）";
        if (fanName.EndsWith(menqing)) return fanName.Substring(0, fanName.Length - menqing.Length);
        if (fanName.EndsWith(shixia)) return fanName.Substring(0, fanName.Length - shixia.Length);
        return fanName;
    }

    public static string FanName(string subRule, string fanName) => YakuName(fanName);

    public static string FanValue(string subRule, string fanName) {
        if (FanToDisplayRiichi.TryGetValue(fanName, out string display)) return display;
        if (FanToDisplayRiichiInactive.TryGetValue(fanName, out display)) return display;
        return "0番";
    }

    /// <summary>总计栏：符 + 番 + 点（浪涌带倍数）。无扩展时退回通用显示。</summary>
    public static SettlementTotalDisplay SettlementTotal(SettlementTotalQuery q) {
        RiichiEndResultExtras extras = q.ExtrasAs<RiichiEndResultExtras>();
        if (extras == null) return q.DefaultDisplay();
        return new SettlementTotalDisplay {
            FuText = $"{extras.Fu}符",
            FanText = $"{extras.Han}番",
            ScoreText = extras.LangyongMultiplier > 1
                ? $"{q.HuScore}点*{extras.LangyongMultiplier}"
                : $"{q.HuScore}点",
        };
    }

    /// <summary>含役满播放 Gong_hu。</summary>
    public static bool PlaysGongHu(string subRule, string[] huFan, int huScore) {
        if (huFan == null) return false;
        foreach (string fanKey in huFan) {
            if (FanValue(subRule, fanKey).Contains("役满")) return true;
        }
        return false;
    }

    /// <summary>计分板摘要：快照里的 han，没有则退回和牌分。</summary>
    public static string ScoreboardFanText(SettlementTotalQuery q) {
        int? han = q.Han ?? q.ExtrasAs<RiichiEndResultExtras>()?.Han;
        return $"{(han ?? q.HuScore)}番";
    }
}
