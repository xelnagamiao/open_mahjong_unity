using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

/// <summary>Round Panel reads settled team totals; personal live scores remain independent.</summary>
public sealed class SichuanRoundScoreState {
    public bool Enabled { get; private set; }
    public bool Paired { get; private set; }
    public bool ShowScores { get; private set; } = true;
    public string Unit { get; private set; } = "队分";
    public string Caption { get; private set; } = "";
    public long? Blue { get; private set; }
    public long? Red { get; private set; }
    private int[] teams;
    private string matchId;

    public void Initialize(GameInfo info, string subRule) {
        Enabled = SichuanLobby.IsTeamMode(subRule);
        Paired = Enabled && (SichuanLobby.IsDuplicate(subRule) || info?.duplicate_match != null);
        teams = info?.sichuan_teams?.team_by_player_index;
        teams = teams != null ? (int[])teams.Clone() : null;
        matchId = info?.duplicate_match?.id;
        Blue = Red = null;
        ShowScores = true;
        Unit = Paired ? (SichuanLobby.IsXueliu(subRule) ? "副差分" : "IMP") : "队分";
        Caption = Paired ? $"总 {Unit} · 未公开" : "";
        if (!Enabled) return;
        if (Paired) { UpdateDuplicate(info?.duplicate_match); return; }
        var scores = info?.sichuan_teams?.settled_scores;
        if (scores != null && scores.TryGetValue(0, out int blue) && scores.TryGetValue(1, out int red)) {
            Blue = blue;
            Red = red;
        }
    }

    public void UpdateDuplicate(DuplicateMatchInfo info) {
        if (!Enabled || !Paired || info == null) return;
        if (!string.IsNullOrEmpty(matchId) && !string.IsNullOrEmpty(info.id) && matchId != info.id) return;
        if (!string.IsNullOrEmpty(info.id)) matchId = info.id;
        if (info.sync_mode == false && info.status != "completed") {
            ShowScores = false;
            Blue = Red = null;
            Caption = info.waiting_for == "match" ? "等待另一桌结束" : "非同步模式 · 全场结束后公布";
            return;
        }
        ShowScores = true;
        if (!string.IsNullOrEmpty(info.scoring)) Unit = info.sync_mode == true || info.scoring == "imp" ? "IMP" : "副差分";
        // Missing cross-table scores are not zero and must never fall back to this table's raw scores.
        int[] scores = Unit == "IMP" ? info.imp_scores ?? (info.scoring == "imp" ? info.scores : null) : info.small_scores ?? info.scores;
        Blue = scores?.Length == 2 ? scores[0] : (long?)null;
        Red = scores?.Length == 2 ? scores[1] : (long?)null;
        string status = info.status == "completed" ? "全场结束"
            : info.status == "interrupted" ? "等待赛务"
            : info.waiting ? "等待对室"
            : info.status == "replay" && !Blue.HasValue ? "牌谱未附对室分"
            : Blue.HasValue ? "已结算" : "未公开";
        Caption = $"总 {Unit} · {status}";
    }

    public void ApplySettlement(ShowResultInfo info) {
        if (!Enabled || Paired || info == null || info.round_continues == true || info.defer_score_settlement == true) return;
        if (info.liuju_status_final != true && info.next_status != "round_end_by_ready" && info.next_status != "match_end") return;
        if (teams?.Length != 4 || info.player_to_score?.Count != 4) return;
        long blue = 0, red = 0;
        int blueCount = 0, redCount = 0;
        for (int i = 0; i < 4; i++) {
            if (!info.player_to_score.TryGetValue(i, out int score)) return;
            if (teams[i] == 0) { blue += score; blueCount++; }
            else if (teams[i] == 1) { red += score; redCount++; }
            else return;
        }
        if (blueCount != 2 || redCount != 2) return;
        Blue = blue;
        Red = red;
    }

    public string Format(long? value) => value.HasValue
        ? (!Paired && value.Value > 0 ? "+" : "") + value.Value.ToString(CultureInfo.InvariantCulture)
        : "—";

    /// <summary>Replay totals at this board's start, using original-player score history only.</summary>
    public static SichuanTeamsInfo RecordSnapshot(GameRecord record, int roundIndex) {
        if (record?.gameTitle == null || record.gameRound?.rounds == null
            || !record.gameTitle.TryGetValue("sub_rule", out object rule)
            || !SichuanLobby.IsTeamMode(rule?.ToString())
            || !record.gameTitle.TryGetValue("team_by_player_index", out object value) || value == null) return null;
        int[] originalTeams = JArray.FromObject(value).ToObject<int[]>();
        if (originalTeams?.Length != 4) return null;
        var totals = new int[4];
        if (record.gameTitle.TryGetValue("starting_scores", out object start) && start != null) {
            int[] initial = JArray.FromObject(start).ToObject<int[]>();
            if (initial?.Length == 4) totals = initial;
        }
        foreach (var entry in record.gameRound.rounds) {
            if (entry.Key >= roundIndex || entry.Value?.scoreChanges?.Count != 4) continue;
            for (int i = 0; i < 4; i++) totals[i] += entry.Value.scoreChanges[i];
        }
        var scores = new Dictionary<int, int> { {0, 0}, {1, 0} };
        for (int i = 0; i < 4; i++) {
            if (originalTeams[i] != 0 && originalTeams[i] != 1) return null;
            scores[originalTeams[i]] += totals[i];
        }
        var currentTeams = (int[])originalTeams.Clone();
        if (record.gameRound.rounds.TryGetValue(roundIndex, out Round current) && current.seats?.Count == 4) {
            for (int i = 0; i < 4; i++) currentTeams[current.seats[i]] = originalTeams[i];
        }
        return new SichuanTeamsInfo { team_by_player_index = currentTeams, settled_scores = scores };
    }
}
