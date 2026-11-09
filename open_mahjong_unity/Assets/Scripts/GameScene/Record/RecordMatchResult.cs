using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>The persisted match settlement. Replay never recalculates account ratings.</summary>
[Serializable]
public sealed class RecordMatchResult {
    public int version;
    public string rule, sub_rule, room_type, duplicate_table;
    public int player_count;
    public List<RecordMatchPlayerResult> players;
    public SichuanMatchSettlementInfo sichuan_match;
    public DuplicateMatchInfo duplicate_match;

    /// <summary>Map saved match results into the same rows used by live game_end.</summary>
    public Dictionary<string, Dictionary<string, object>> ToFinalPlayerData() {
        var data = new Dictionary<string, Dictionary<string, object>>();
        foreach (var player in players) {
            var row = new Dictionary<string, object> {
                ["user_id"] = player.user_id,
                ["username"] = player.username,
                ["original_player_index"] = player.original_player_index,
                ["rank"] = player.rank,
                ["score"] = player.score,
                ["pt"] = player.rating_pt,
                ["rating_pt"] = player.rating_pt,
                ["rating_system"] = player.rating_system,
                ["rating_status"] = player.rating_status,
                ["elo_delta"] = player.elo_delta,
                ["currency_delta_minor"] = player.currency_delta_minor,
                ["currency_reward_status"] = player.currency_reward_status
            };
            if (player.match_points.HasValue) row["match_points"] = player.match_points.Value;
            if (player.competition_points.HasValue) row["competition_points"] = player.competition_points.Value;
            if (player.team_id.HasValue) {
                row["team_id"] = player.team_id.Value;
                row["team_role"] = player.team_role;
                row["team_result_reason"] = player.team_result_reason;
                row["team_score"] = player.team_score;
                row["team_match_points"] = player.team_match_points;
                row["team_small_score"] = player.team_small_score;
                row["team_imp_score"] = player.team_imp_score;
            }
            data.Add(player.original_player_index.ToString(CultureInfo.InvariantCulture), row);
        }
        return data;
    }

    public void Validate() {
        if (version != 1 || players == null || players.Count != player_count || player_count < 3 || player_count > 4)
            throw new FormatException("牌谱全局结算格式无效");
        if (!players.Select(p => p.original_player_index).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, player_count)))
            throw new FormatException("牌谱全局结算座位不完整");
        foreach (var player in players) {
            if (player.currency_delta_minor < 0) throw new FormatException("牌谱货币奖励不能为负数");
            if (player.rank < 1 || player.rank > player_count || double.IsNaN(player.score) || double.IsInfinity(player.score))
                throw new FormatException("牌谱全局结算名次或分数无效");
            if (player.rating_status == "missing") {
                if (room_type != "match" || player.rating_pt.HasValue || player.elo_delta.HasValue)
                    throw new FormatException("缺失评分标记与结算数据不一致");
            } else if (player.rating_status == "not_applicable") {
                if (room_type == "match" || player.rating_system != "none" || player.rating_pt != 0)
                    throw new FormatException("非计分对局必须保存零 PT");
            } else if (player.rating_status == "saved") {
                if (player.rating_system == "grade" && player.rating_pt.HasValue) continue;
                if (player.rating_system == "elo" && player.elo_before.HasValue && player.elo_after.HasValue && player.elo_delta.HasValue) continue;
                throw new FormatException("牌谱评分结算不完整");
            } else throw new FormatException("牌谱评分状态无效");
        }
    }
}

[Serializable]
public sealed class RecordMatchPlayerResult {
    public int user_id, original_player_index, rank;
    public string username, rating_system, rating_status, team_role, team_result_reason;
    public double score;
    public double? rating_pt, elo_before, elo_after, elo_delta, match_points, competition_points;
    public int? team_id;
    public double? team_score, team_match_points, team_small_score, team_imp_score;
    public long? currency_delta_minor;
    public string currency_reward_status;
    public int currency_reward_version;

    public string RatingLabel => rating_system == "elo" ? "Elo" : "PT";
    public double? RatingDelta => rating_system == "elo" ? elo_delta : rating_pt;
    public string RatingText => rating_status == "missing" ? "-" : Signed(RatingDelta.Value);
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Signed(double value) => value > 0 ? "+" + Number(value) : Number(value);
}
