using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>金锣赛独立和牌资格。与服务端 guobiao_jinluo 保持一致，国标番数和支付不变。</summary>
public static class JinluoRules {
    public const string DefaultWeather = "dragon_pairs";
    public static readonly string[] WeatherKeys = {
        "dragon_pairs", "seat_round_winds", "terminal_pairs", "all_simples",
        "missing_suit", "consecutive_pairs", "same_rank_pairs", "four_of_a_kind"
    };
    public static readonly string[] WeatherLabels = {
        "箭牌对子×2", "门风+场风", "数牌幺九对×3", "断幺", "无字+缺门",
        "连对：两个及以上同花色相邻数牌对子", "同对：两个及以上相同序数数牌对子",
        "四归一：有四张相同牌的七对子"
    };
    public const string Description = "金锣赛是一种制作国标麻将高难度番种的特殊比赛规则，将七对、番数低于等于24且不是凑番的和牌判作错和，从而有充分的时间制作平常对局较为稀有的凑番或者大牌的玩法，其中对某些具体番种有特殊的规定，具体规则可见jmcr.cc.cd，推荐添加q群1101569363游玩。";
    private static readonly HashSet<string> Reduced = new HashSet<string> { "海底捞月", "妙手回春", "杠上开花", "双暗杠", "明暗杠" };
    private static readonly HashSet<string> Unrestricted = new HashSet<string> { "天和", "地和", "人和", "人和I", "人和II", "人和Ⅰ", "人和Ⅱ", "三色双龙会", "三同刻", "全带五" };
    private static readonly HashSet<string> LowFans = new HashSet<string> {
        "一般高", "喜相逢", "连六", "老少副", "幺九刻", "明杠", "缺一门", "无字", "边张", "嵌张", "单钓将", "自摸", "花牌",
        "箭刻", "圈风刻", "门风刻", "门前清", "平和", "四归一", "双同刻", "双暗刻", "暗杠", "断幺"
    };

    public static bool IsWeather(string value) => Array.IndexOf(WeatherKeys, value) >= 0;
    public static string WeatherLabel(string value) {
        int index = Array.IndexOf(WeatherKeys, value);
        return index < 0 ? "未知天气" : WeatherLabels[index];
    }
    private static List<int> PairTiles(List<int> hand, List<string> melds) {
        if (melds.Count != 0 || hand.Count != 14) return new List<int>();
        var groups = hand.GroupBy(tile => tile).ToList();
        if (groups.Any(group => group.Count() % 2 != 0)) return new List<int>();
        return groups.SelectMany(group => Enumerable.Repeat(group.Key, group.Count() / 2)).OrderBy(tile => tile).ToList();
    }
    public static bool MatchesWeather(List<int> pairs, string weather, int seatWind, int roundWind) {
        if (pairs.Count != 7) return false;
        var numbered = pairs.Where(tile => tile < 40).ToList();
        switch (weather) {
            case "dragon_pairs": return pairs.Count(tile => tile >= 45 && tile <= 47) >= 2;
            case "seat_round_winds": return pairs.Contains(seatWind) && pairs.Contains(roundWind);
            case "terminal_pairs": return numbered.Count(tile => tile % 10 == 1 || tile % 10 == 9) >= 3;
            case "all_simples": return numbered.Count == 7 && numbered.All(tile => tile % 10 >= 2 && tile % 10 <= 8);
            case "missing_suit": return numbered.Count == 7 && numbered.Select(tile => tile / 10).Distinct().Count() <= 2;
            case "consecutive_pairs": return numbered.Any(tile => tile % 10 < 9 && numbered.Contains(tile + 1));
            case "same_rank_pairs": return numbered.Any(a => numbered.Any(b => a != b && a % 10 == b % 10));
            case "four_of_a_kind": return pairs.Distinct().Count() < 7;
            default: return false;
        }
    }
    private static bool Run(int a, int b, int c) => a < 40 && a / 10 == c / 10 && b == a + 1 && c == a + 2;
    private static bool SpecialSevenPairs(List<int> pairs) {
        if (pairs.Count != 7) return false;
        bool numbered = pairs.All(tile => tile < 40);
        var ranks = pairs.Select(tile => tile % 10).Distinct().ToList();
        if (numbered && (ranks.Count == 2 || ranks.Count == 3 || ranks.All(rank => rank % 2 == 0))) return true;
        if (numbered && ranks.Count == 7 && ranks.Max() - ranks.Min() == 6) return true;
        if (new[] { 45, 46, 47 }.All(pairs.Contains) || new[] { 41, 42, 43, 44 }.All(pairs.Contains)) return true;
        for (int i = 0; i < 5; i++) for (int j = i + 1; j < 6; j++) for (int k = j + 1; k < 7; k++) {
            if (!Run(pairs[i], pairs[j], pairs[k])) continue;
            var rest = pairs.Where((tile, index) => index != i && index != j && index != k).ToList();
            for (int a = 0; a < 2; a++) for (int b = a + 1; b < 3; b++) for (int c = b + 1; c < 4; c++)
                if (Run(rest[a], rest[b], rest[c])) return true;
        }
        return false;
    }
    public static bool IsAllowed(Tuple<int, List<string>> result, List<int> hand, List<string> melds, string weather, int seatWind, int roundWind) {
        var counts = new Dictionary<string, int>();
        foreach (string label in result.Item2) {
            var parts = label.Split('*');
            int count = parts.Length == 2 ? int.Parse(parts[1]) : 1;
            counts.TryGetValue(parts[0], out int before);
            counts[parts[0]] = before + count;
        }
        int Count(string name) => counts.TryGetValue(name, out int value) ? value : 0;
        int core = result.Item1 - Count("花牌");
        if (counts.Count == 0 || core < 8) return false;
        if (Count("花牌") >= 6 || counts.Keys.Any(Unrestricted.Contains)) return true;
        if (Count("碰碰和") > 0) {
            var tiles = hand.Concat(melds.Select(meld => int.Parse(meld.Substring(1)))).ToList();
            if (tiles.All(tile => tile < 40) && tiles.Select(tile => tile % 10).Distinct().Count() == 2) return true;
        }
        if (Count("七对") > 0 || Count("连七对") > 0) {
            var pairs = PairTiles(hand, melds);
            return core >= 28 || SpecialSevenPairs(pairs) || MatchesWeather(pairs, weather, seatWind, roundWind);
        }
        if (core >= 24 || (core >= 16 && counts.Keys.Any(Reduced.Contains))) return true;
        return counts.Keys.All(LowFans.Contains) && !(Count("门前清") > 0 && Count("平和") > 0);
    }
    public static Tuple<int, List<string>> Check(List<int> hand, List<string> melds, List<string> way, int tile, string weather, int seatWind, int roundWind) {
        return new Chinese_Hepai_Check(false, null).HepaiCheck(hand, melds, way, tile,
            result => IsAllowed(result, hand, melds, weather, seatWind, roundWind));
    }
}
