using System.Collections.Generic;

/// <summary>简单麻将番文本：服务端下发稳定番种 ID，这里映射中文名与番值。</summary>
public static class JiandanFanText {
    /// <summary>简单麻将番表，key 使用服务端稳定番种 ID。</summary>
    public static readonly Dictionary<string, string> FanToDisplayJiandan = new Dictionary<string, string> {
        {"two_wind_triplets", "1番"}, {"small_three_winds", "3番"}, {"big_three_winds", "8番"}, {"small_four_winds", "20番"}, {"big_four_winds", "20番"},
        {"red_dragon", "1番"}, {"green_dragon", "1番"}, {"white_dragon", "1番"}, {"two_dragon_triplets", "3番"}, {"small_three_dragons", "8番"}, {"big_three_dragons", "20番"},
        {"all_simples", "1番"}, {"full_straight", "3番"}, {"mixed_outside_hand", "3番"}, {"mixed_terminals", "8番"}, {"pure_outside_hand", "8番"}, {"pure_terminals", "20番"}, {"thirteen_orphans", "20番"},
        {"half_flush", "3番"}, {"full_flush", "8番"}, {"all_honors", "20番"}, {"nine_gates", "20番"},
        {"two_suit_triplets", "1番"}, {"small_three_suit_triplets", "3番"}, {"mixed_triple_chow", "3番"}, {"three_suit_triplets", "8番"},
        {"closed_hand", "1番"}, {"heavenly_win", "20番"}, {"earthly_win", "20番"},
        {"pinfu", "1番"}, {"all_triplets", "3番"}, {"seven_pairs", "3番"},
        {"haitei", "1番"}, {"houtei", "1番"}, {"rinshan", "1番"}, {"chankan", "1番"},
        {"two_concealed_triplets", "1番"}, {"three_concealed_triplets", "8番"}, {"four_concealed_triplets", "20番"},
        {"two_consecutive_triplets", "1番"}, {"three_consecutive_triplets", "8番"}, {"four_consecutive_triplets", "20番"},
        {"pure_double_chow", "1番"}, {"twice_pure_double_chow", "8番"}, {"pure_triple_chow", "8番"}, {"pure_quadruple_chow", "20番"},
        {"one_kong", "1番"}, {"two_kongs", "3番"}, {"three_kongs", "8番"}, {"four_kongs", "20番"},
    };

    public static readonly Dictionary<string, string> FanNameToDisplayJiandan = new Dictionary<string, string> {
        {"two_wind_triplets", "二风刻"}, {"small_three_winds", "小三风"}, {"big_three_winds", "大三风"}, {"small_four_winds", "小四喜"}, {"big_four_winds", "大四喜"},
        {"red_dragon", "中"}, {"green_dragon", "发"}, {"white_dragon", "白"}, {"two_dragon_triplets", "二元刻"}, {"small_three_dragons", "小三元"}, {"big_three_dragons", "大三元"},
        {"all_simples", "断幺九"}, {"full_straight", "一气通贯"}, {"mixed_outside_hand", "混全带幺"}, {"mixed_terminals", "混幺九"}, {"pure_outside_hand", "纯全带幺"}, {"pure_terminals", "清幺九"}, {"thirteen_orphans", "十三幺九"},
        {"half_flush", "混一色"}, {"full_flush", "清一色"}, {"all_honors", "字一色"}, {"nine_gates", "九莲宝灯"},
        {"two_suit_triplets", "二色同刻"}, {"small_three_suit_triplets", "小三色同刻"}, {"mixed_triple_chow", "三色同顺"}, {"three_suit_triplets", "三色同刻"},
        {"closed_hand", "门前清"}, {"heavenly_win", "天和"}, {"earthly_win", "地和"},
        {"pinfu", "平和"}, {"all_triplets", "对对和"}, {"seven_pairs", "七对子"},
        {"haitei", "海底捞月"}, {"houtei", "河底捞鱼"}, {"rinshan", "杠上开花"}, {"chankan", "抢杠"},
        {"two_concealed_triplets", "二暗刻"}, {"three_concealed_triplets", "三暗刻"}, {"four_concealed_triplets", "四暗刻"},
        {"two_consecutive_triplets", "二连刻"}, {"three_consecutive_triplets", "三连刻"}, {"four_consecutive_triplets", "四连刻"},
        {"pure_double_chow", "一般高"}, {"twice_pure_double_chow", "二般高"}, {"pure_triple_chow", "一色三同顺"}, {"pure_quadruple_chow", "一色四同顺"},
        {"one_kong", "一杠"}, {"two_kongs", "二杠"}, {"three_kongs", "三杠"}, {"four_kongs", "四杠"},
    };

    public static string FanName(string subRule, string fanName) {
        return FanNameToDisplayJiandan.TryGetValue(fanName, out string display) ? display : fanName;
    }

    public static string FanValue(string subRule, string fanName) {
        return FanToDisplayJiandan.TryGetValue(fanName, out string display) ? display : "0番";
    }

    /// <summary>计分板摘要用：按番表累加总番。</summary>
    public static int FanTotal(string subRule, string[] huFan) {
        if (huFan == null) return 0;
        int total = 0;
        foreach (string fan in huFan) {
            string display = FanValue(subRule, fan);
            if (display.EndsWith("番") && int.TryParse(display.Replace("番", ""), out int val)) total += val;
        }
        return total;
    }

    public static string ScoreboardFanText(SettlementTotalQuery q) => $"{FanTotal(q.Rule, q.HuFan)}番";
}
