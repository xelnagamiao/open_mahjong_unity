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
        string name = PlayerInfoRuleCatalog.Name(rule);
        if (name != null) return name;
        if (RankedRules.Supports(rule)) return RankedRules.Name(rule);
        if (rule == RiichiSanmaRankConfig.Rule) return RankedRules.Name(rule);
        var manifest = RuleRegistry.Resolve(PlayerInfoRuleCatalog.SourceRule(rule));
        return manifest?.LobbyName ?? manifest?.DisplayName ?? rule;
    }

    public static string[] Modes(string rule, bool ranked, RuleStatsResponse response = null) {
        var modes = new List<string>(PlayerInfoRuleCatalog.Modes(rule, ranked));
        // 保留本子规则已有的历史局制，默认入口取自统计配表。
        if (response?.history_stats != null) foreach (var row in response.history_stats) {
            if (row == null || string.IsNullOrEmpty(row.mode) || row.rule != rule) continue;
            string mode = NormalizeMode(row.mode, PlayerInfoRuleCatalog.PlayerCount(rule) == 3);
            if (mode.EndsWith("_rank") == ranked && !modes.Contains(mode)) modes.Add(mode);
        }
        // 展示时按局制长度降序：全庄、东西、半庄（东南）、东风，历史局制也参与排序。
        modes.Sort((left, right) => {
            int.TryParse(left.Split('/')[0], out int leftLength);
            int.TryParse(right.Split('/')[0], out int rightLength);
            return rightLength.CompareTo(leftLength);
        });
        return modes.ToArray();
    }

    public static string ModeCaption(string rule, string mode, bool ranked) {
        if (string.IsNullOrEmpty(mode)) return RuleName(rule) + "总计" + CategorySuffix(rule, ranked);
        if (ranked && rule == "hongque") return "虹雀 · " + mode.Split('/')[0] + "局（Elo 匹配）";
        if (rule == RiichiSanmaRankConfig.Rule) return RuleName(rule) + " · " + RiichiModeCaption(mode) + CategorySuffix(rule, ranked);
        if (rule == PlayerInfoRuleCatalog.GuobiaoSanma) return RuleName(rule) + " · "
            + RoundTextDictionary.GetMatchTypeDisplay("guobiao", mode.Replace("_sanma", "")) + CategorySuffix(rule, ranked);
        if(ranked&&(rule=="sichuan" || rule==RankedRules.XueliuExchangeRule))return RankedRules.Name(rule)+(mode=="4/4_rank"?" · 全庄（16局，历史）":mode=="2/4_rank"?" · 半庄（8局）":" · 四局（历史）")+"（Elo 匹配）";
        string match = rule == "riichi" && mode == "2/4" ? "半庄（东南战）"
            : rule == "riichi" && mode == "3/4" ? "东南西战（历史）"
            : rule == "riichi" && mode == "4/4" ? "全庄战（历史）"
            : RoundTextDictionary.GetMatchTypeDisplay(PlayerInfoRuleCatalog.SourceRule(rule), mode);
        return RuleName(rule) + match + CategorySuffix(rule, ranked);
    }
    static string RiichiModeCaption(string mode) => mode.StartsWith("2/") ? "半庄（东南战）"
        : mode.StartsWith("3/") ? "东南西战（历史）" : mode.StartsWith("4/") ? "全庄战（历史）" : "东风战";
    public static string CategorySuffix(string rule, bool ranked) => ranked ? "（匹配）" : "（自定义）";
    public static string FanCaption(string rule, bool ranked) => RuleName(rule).Replace("麻将", "") + "番数总计" + CategorySuffix(rule, ranked);

    public static PlayerStatsInfo Find(RuleStatsResponse response, string rule, string mode) {
        if (response?.history_stats == null) return null;
        foreach (var row in response.history_stats) {
            if (row == null || NormalizeMode(row.mode, PlayerInfoRuleCatalog.PlayerCount(rule) == 3) != mode) continue;
            string source = PlayerInfoRuleCatalog.SourceRule(rule);
            if (!string.IsNullOrEmpty(row.rule) && row.rule != rule) continue;
            return row;
        }
        return null;
    }

    static string NormalizeMode(string mode, bool sanma) {
        if (mode == null) return null;
        // 保留旧三人数据读取兼容，界面统一展示实际人数 /3。
        if (sanma) return mode.Replace("/4_sanma", "/3").Replace("/3_sanma", "/3");
        return mode;
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
        Dictionary<string, int> details = null;
        if (PlayerInfoRuleCatalog.IsRiichi(rule) && rows.TrueForAll(r => r.riichi_details != null)) {
            details = new Dictionary<string, int>();
            foreach (var row in rows)
                foreach (var field in row.riichi_details) {
                    details.TryGetValue(field.Key, out int old);
                    details[field.Key] = old + field.Value;
                }
        }
        return new PlayerStatsInfo {
            rule = rule, total_games = Sum(r => r.total_games), total_rounds = Sum(r => r.total_rounds),
            win_count = Sum(r => r.win_count), self_draw_count = Sum(r => r.self_draw_count),
            deal_in_count = Sum(r => r.deal_in_count), total_fan_score = Sum(r => r.total_fan_score),
            total_win_turn = Sum(r => r.total_win_turn), total_fangchong_score = Sum(r => r.total_fangchong_score),
            first_place_count = Sum(r => r.first_place_count), second_place_count = Sum(r => r.second_place_count),
            third_place_count = Sum(r => r.third_place_count), fourth_place_count = Sum(r => r.fourth_place_count),
            fulu_round_count = Sum(r => r.fulu_round_count), cuohe_count = Sum(r => r.cuohe_count),
            total_round_score = Sum(r => r.total_round_score), riichi_details = details
        };
    }

    public static List<KeyValuePair<string, string>> GameDetails(PlayerStatsInfo stats, string rule = null) {
        var fields = new List<KeyValuePair<string, string>>();
        void Add(string name, string value) => fields.Add(new KeyValuePair<string, string>(name, value));
        if (stats == null) { Add("暂无数据", ""); return fields; }
        rule = rule ?? stats.rule;
        if (PlayerInfoRuleCatalog.IsRiichi(rule)) return RiichiDetails(stats, PlayerInfoRuleCatalog.PlayerCount(rule) == 3);
        bool guobiao = PlayerInfoRuleCatalog.SourceRule(rule) == "guobiao";
        bool singapore = PlayerInfoRuleCatalog.SourceRule(rule) == "singapore";
        bool malaysia = PlayerInfoRuleCatalog.SourceRule(rule) == "malaysia";
        bool sanma = PlayerInfoRuleCatalog.PlayerCount(rule) == 3;
        int games = stats.total_games ?? 0, rounds = stats.total_rounds ?? 0, wins = stats.win_count ?? 0;
        int dealIns = stats.deal_in_count ?? 0;
        string Ratio(int? numerator, int denominator, bool percent = false) =>
            ((denominator > 0 ? (numerator ?? 0) / (float)denominator : 0) * (percent ? 100 : 1)).ToString("F2") + (percent ? "%" : "");
        Add("总对局数", games.ToString());
        Add("累计回合数", rounds.ToString());
        Add("平均顺位", Ratio((stats.first_place_count ?? 0) + (stats.second_place_count ?? 0) * 2 + (stats.third_place_count ?? 0) * 3 + (sanma ? 0 : stats.fourth_place_count ?? 0) * 4, games));
        Add("副露率", Ratio(stats.fulu_round_count, rounds, true));
        Add("和牌率", Ratio(stats.win_count, rounds, true));
        if (guobiao) Add("错和率", Ratio(stats.cuohe_count, rounds, true));
        Add("自摸率", Ratio(stats.self_draw_count, wins, true));
        Add("放铳率", Ratio(stats.deal_in_count, rounds, true));
        string winValueLabel = PlayerInfoRuleCatalog.WinValueLabel(rule);
        string dealInValueLabel = PlayerInfoRuleCatalog.DealInValueLabel(rule);
        if (winValueLabel != null) Add(winValueLabel, Ratio(stats.total_fan_score, wins));
        Add("平均和巡", Ratio(stats.total_win_turn, wins));
        if (dealInValueLabel != null) Add(dealInValueLabel, Ratio(stats.total_fangchong_score, dealIns));
        if (malaysia) Add("累计净得分", (stats.total_round_score ?? 0).ToString());
        if (guobiao || singapore || malaysia) Add("局均点", Ratio(stats.total_round_score, games));
        Add("一位率", Ratio(stats.first_place_count, games, true));
        Add("二位率", Ratio(stats.second_place_count, games, true));
        Add("三位率", Ratio(stats.third_place_count, games, true));
        if (!sanma) Add("四位率", Ratio(stats.fourth_place_count, games, true));
        return fields;
    }

    private static List<KeyValuePair<string, string>> RiichiDetails(PlayerStatsInfo s, bool sanma) {
        var fields = new List<KeyValuePair<string, string>>();
        void Add(string name, string value) => fields.Add(new KeyValuePair<string, string>(name, value));
        int D(string key) => s.riichi_details != null && s.riichi_details.TryGetValue(key, out int value) ? value : 0;
        string Rate(int n, int d) => (d > 0 ? 100f * n / d : 0).ToString("F2") + "%";
        string Mean(int n, int d) => (d > 0 ? n / (float)d : 0).ToString("F2");
        int games = s.total_games ?? 0, rounds = s.total_rounds ?? 0, wins = s.win_count ?? 0;
        int ranked = (s.first_place_count ?? 0) + (s.second_place_count ?? 0)
            + (s.third_place_count ?? 0) + (sanma ? 0 : s.fourth_place_count ?? 0);
        int riichi = D("riichi_round_count"), calls = s.fulu_round_count ?? 0;
        Add("总对局", games.ToString());
        Add("总回合", rounds.ToString());
        Add("平均顺位", ranked > 0 ? (((s.first_place_count ?? 0) + (s.second_place_count ?? 0) * 2
            + (s.third_place_count ?? 0) * 3 + (sanma ? 0 : s.fourth_place_count ?? 0) * 4) / (float)ranked).ToString("F2") : "0.00");
        Add("局均点", Mean(s.total_round_score ?? 0, games));
        Add("被飞率", Rate(D("busted_count"), D("final_score_count")));
        Add("和牌率", Rate(wins, rounds));
        Add("自摸率", Rate(s.self_draw_count ?? 0, wins));
        Add("放铳率", Rate(s.deal_in_count ?? 0, rounds));
        Add("立直率", Rate(riichi, rounds));
        Add("副露率", Rate(calls, rounds));
        Add("平均和了打点", Mean(D("total_win_points"), wins));
        Add("平均和了巡目", Mean(s.total_win_turn ?? 0, wins));
        Add("一位率", Rate(s.first_place_count ?? 0, ranked));
        Add("二位率", Rate(s.second_place_count ?? 0, ranked));
        Add("三位率", Rate(s.third_place_count ?? 0, ranked));
        if (!sanma) Add("四位率", Rate(s.fourth_place_count ?? 0, ranked));
        return fields;
    }

    public static List<KeyValuePair<string, string>> FanDetails(string rule, IDictionary<string, int> counts) {
        var fields = new List<KeyValuePair<string, string>>();
        if (counts == null) { fields.Add(new KeyValuePair<string, string>("暂无数据", "")); return fields; }
        var manifest = RuleRegistry.Resolve(PlayerInfoRuleCatalog.SourceRule(rule));
        var seen = new HashSet<string>();
        var names = PlayerInfoRuleCatalog.FanNames(rule);
        if (names != null) {
            foreach (var fan in names) {
                counts.TryGetValue(fan.Key, out int count);
                fields.Add(new KeyValuePair<string, string>(fan.Value, count.ToString()));
                seen.Add(fan.Key);
            }
        }
        foreach (var fan in counts) {
            if (seen.Contains(fan.Key)) continue;
            string name = manifest?.FanNameText?.Invoke(PlayerInfoRuleCatalog.SubRule(rule) ?? manifest.DefaultSubRule ?? rule, fan.Key) ?? fan.Key;
            fields.Add(new KeyValuePair<string, string>(name, fan.Value.ToString()));
        }
        if (fields.Count == 0) fields.Add(new KeyValuePair<string, string>("暂无数据", ""));
        return fields;
    }
}
