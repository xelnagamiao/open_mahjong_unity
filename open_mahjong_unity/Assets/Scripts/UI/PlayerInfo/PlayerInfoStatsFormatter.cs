using System;
using System.Collections.Generic;

/// <summary>局制、统计明细和番种文案；只处理既有统计协议，不依赖 UI 对象。</summary>
public static class PlayerInfoStatsFormatter {
    public static string WinCaption(GuobiaoBigWin win) {
        if (win == null) return string.Empty;
        var names = new List<string>();
        if (win.fans != null) {
            foreach (string fan in win.fans)
                if (!string.IsNullOrWhiteSpace(fan)) names.Add(fan.Trim());
        }
        // 兼容尚未带完整番种列表的旧快照。
        if (names.Count == 0 && !string.IsNullOrWhiteSpace(win.fan_name)) names.Add(win.fan_name);
        return string.Join(" · ", names) + $"  {win.total_fan} 番";
    }

    public static string RuleName(string rule) {
        var manifest = RuleRegistry.Resolve(rule, rule);
        return manifest?.LobbyName ?? manifest?.DisplayName ?? rule;
    }

    public static string[] Modes(string rule, bool ranked) {
        var manifest = RuleRegistry.Resolve(rule, rule);
        if (manifest?.CreateRoomDefaults != null && !manifest.CreateRoomDefaults.ContainsKey(CreateRoomKeys.GameRound))
            return Array.Empty<string>();
        int[] rounds = rule == "riichi" ? new[] { 2, 1 }
            : manifest?.MaxRoundIsHandCount == true ? new[] { 16, 8, 4 } : new[] { 4, 3, 2, 1 };
        var modes = new List<string>();
        foreach (int round in rounds) {
            if (RoundTextDictionary.GetMaxRoundText(rule, round).StartsWith("未知")) continue;
            modes.Add(round + "/4" + (rule == "guobiao" && ranked ? "_rank" : ""));
        }
        return modes.ToArray();
    }

    public static string ModeCaption(string rule, string mode, bool ranked) {
        string match = rule == "riichi" && mode == "2/4" ? "半庄（东南战）"
            : RoundTextDictionary.GetMatchTypeDisplay(rule, mode);
        return RuleName(rule) + match + CategorySuffix(rule, ranked);
    }
    public static string CategorySuffix(string rule, bool ranked) => rule == "guobiao" ? (ranked ? "（天梯）" : "（自定义）") : "";
    public static string FanCaption(string rule, bool ranked) => RuleName(rule).Replace("麻将", "") + "番数总计" + CategorySuffix(rule, ranked);

    public static PlayerStatsInfo Find(RuleStatsResponse response, string rule, string mode) {
        if (response?.history_stats == null) return null;
        foreach (var row in response.history_stats) {
            if (row == null || row.mode != mode) continue;
            if (!string.IsNullOrEmpty(row.rule) && row.rule.Split('/')[0] != rule) continue;
            return row;
        }
        return null;
    }

    public static PlayerStatsInfo Aggregate(RuleStatsResponse response, string rule, string[] modes) {
        var rows = new List<PlayerStatsInfo>();
        foreach (string mode in modes) {
            var row = Find(response, rule, mode);
            if (row != null) rows.Add(row);
        }
        if (rows.Count == 0) return null;
        int Sum(Func<PlayerStatsInfo, int?> field) {
            int total = 0;
            foreach (var row in rows) total += field(row) ?? 0;
            return total;
        }
        return new PlayerStatsInfo {
            rule = rule, total_games = Sum(r => r.total_games), total_rounds = Sum(r => r.total_rounds),
            win_count = Sum(r => r.win_count), self_draw_count = Sum(r => r.self_draw_count),
            deal_in_count = Sum(r => r.deal_in_count), total_fan_score = Sum(r => r.total_fan_score),
            total_win_turn = Sum(r => r.total_win_turn), total_fangchong_score = Sum(r => r.total_fangchong_score),
            first_place_count = Sum(r => r.first_place_count), second_place_count = Sum(r => r.second_place_count),
            third_place_count = Sum(r => r.third_place_count), fourth_place_count = Sum(r => r.fourth_place_count),
            fulu_round_count = Sum(r => r.fulu_round_count), cuohe_count = Sum(r => r.cuohe_count),
            total_round_score = Sum(r => r.total_round_score)
        };
    }

    public static List<KeyValuePair<string, string>> GameDetails(PlayerStatsInfo stats, string rule = null) {
        var fields = new List<KeyValuePair<string, string>>();
        void Add(string name, string value) => fields.Add(new KeyValuePair<string, string>(name, value));
        if (stats == null) { Add("暂无数据", ""); return fields; }
        rule = rule ?? stats.rule;
        int games = stats.total_games ?? 0, rounds = stats.total_rounds ?? 0, wins = stats.win_count ?? 0;
        int dealIns = stats.deal_in_count ?? 0;
        string Ratio(int? numerator, int denominator, bool percent = false) =>
            ((denominator > 0 ? (numerator ?? 0) / (float)denominator : 0) * (percent ? 100 : 1)).ToString("F2") + (percent ? "%" : "");
        Add("总对局数", games.ToString());
        Add("累计回合数", rounds.ToString());
        Add("平均顺位", Ratio((stats.first_place_count ?? 0) + (stats.second_place_count ?? 0) * 2 + (stats.third_place_count ?? 0) * 3 + (stats.fourth_place_count ?? 0) * 4, games));
        Add("副露率", Ratio(stats.fulu_round_count, rounds, true));
        Add("和牌率", Ratio(stats.win_count, rounds, true));
        if (rule == "guobiao") Add("错和率", Ratio(stats.cuohe_count, rounds, true));
        Add("自摸率", Ratio(stats.self_draw_count, wins, true));
        Add("放铳率", Ratio(stats.deal_in_count, rounds, true));
        Add("平均和番", Ratio(stats.total_fan_score, wins));
        Add("平均和巡", Ratio(stats.total_win_turn, wins));
        Add("平均铳番", Ratio(stats.total_fangchong_score, dealIns));
        if (rule == "guobiao") Add("局均点", Ratio(stats.total_round_score, games));
        Add("一位率", Ratio(stats.first_place_count, games, true));
        Add("二位率", Ratio(stats.second_place_count, games, true));
        Add("三位率", Ratio(stats.third_place_count, games, true));
        Add("四位率", Ratio(stats.fourth_place_count, games, true));
        return fields;
    }

    public static List<KeyValuePair<string, string>> FanDetails(string rule, IDictionary<string, int> counts) {
        var fields = new List<KeyValuePair<string, string>>();
        if (counts == null) { fields.Add(new KeyValuePair<string, string>("暂无数据", "")); return fields; }
        var manifest = RuleRegistry.Resolve(rule, rule);
        var seen = new HashSet<string>();
        if (manifest?.StatsFanNames != null) {
            foreach (var fan in manifest.StatsFanNames) {
                counts.TryGetValue(fan.Key, out int count);
                fields.Add(new KeyValuePair<string, string>(fan.Value, count.ToString()));
                seen.Add(fan.Key);
            }
        }
        foreach (var fan in counts) {
            if (seen.Contains(fan.Key)) continue;
            string name = manifest?.FanNameText?.Invoke(manifest.DefaultSubRule ?? rule, fan.Key) ?? fan.Key;
            fields.Add(new KeyValuePair<string, string>(name, fan.Value.ToString()));
        }
        if (fields.Count == 0) fields.Add(new KeyValuePair<string, string>("暂无数据", ""));
        return fields;
    }
}
