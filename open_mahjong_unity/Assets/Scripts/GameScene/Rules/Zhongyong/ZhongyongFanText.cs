using System.Collections.Generic;

internal static class ZhongyongFanText {
    private static readonly Dictionary<string, (string name, int value)> Fans = new Dictionary<string, (string, int)> {
        { "pinfu", ("平和", 5) },
        { "closed_hand", ("门前清", 5) },
        { "all_simples", ("断幺九", 5) },
        { "half_flush", ("混一色", 40) },
        { "full_flush", ("清一色", 80) },
        { "nine_gates", ("九莲宝灯", 480) },
        { "value_honor", ("番牌", 10) },
        { "small_three_dragons", ("小三元", 40) },
        { "big_three_dragons", ("大三元", 130) },
        { "small_three_winds", ("小三风", 30) },
        { "big_three_winds", ("大三风", 120) },
        { "small_four_winds", ("小四喜", 320) },
        { "big_four_winds", ("大四喜", 400) },
        { "all_honors", ("字一色", 320) },
        { "all_triplets", ("对对和", 30) },
        { "two_concealed_triplets", ("二暗刻", 5) },
        { "three_concealed_triplets", ("三暗刻", 30) },
        { "four_concealed_triplets", ("四暗刻", 125) },
        { "one_kong", ("一杠", 5) },
        { "two_kongs", ("二杠", 20) },
        { "three_kongs", ("三杠", 120) },
        { "four_kongs", ("四杠", 480) },
        { "pure_double_chow", ("一般高", 10) },
        { "twice_pure_double_chow", ("两般高", 60) },
        { "pure_triple_chow", ("一色三同顺", 120) },
        { "pure_quadruple_chow", ("一色四同顺", 480) },
        { "mixed_triple_chow", ("三色同顺", 35) },
        { "small_three_suit_triplets", ("三色小同刻", 30) },
        { "three_suit_triplets", ("三色同刻", 120) },
        { "full_straight", ("一气通贯", 40) },
        { "three_consecutive_triplets", ("三连刻", 100) },
        { "four_consecutive_triplets", ("四连刻", 200) },
        { "mixed_outside_hand", ("混全带幺", 40) },
        { "pure_outside_hand", ("纯全带幺", 50) },
        { "mixed_terminals", ("混幺九", 100) },
        { "pure_terminals", ("清幺九", 400) },
        { "haitei", ("海底捞月", 10) },
        { "houtei", ("河底捞鱼", 10) },
        { "rinshan", ("岭上开花", 10) },
        { "chankan", ("抢杠", 10) },
        { "heavenly_win", ("天和", 155) },
        { "earthly_win", ("地和", 155) },
        { "thirteen_orphans", ("十三幺九", 160) },
        { "seven_pairs", ("七对子", 30) },
        { "chicken_hand", ("鸡和", 1) },
    };
    public static bool IsNanque(string subRule) => subRule == "zhongyong/nanque";
    public static string Name(string rule, string fan) => IsNanque(rule) ? JiandanFanText.FanName(rule, fan)
        : Fans.TryGetValue(fan, out var item) ? item.name : fan;
    public static string Value(string rule, string fan) => IsNanque(rule) ? JiandanFanText.FanValue(rule, fan)
        : Fans.TryGetValue(fan, out var item) ? $"{item.value}分" : "";
}
