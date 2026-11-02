#if UNITY_EDITOR
using System;
using System.Collections.Generic;

public static class SichuanRoundScoreValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var state = new SichuanRoundScoreState();
        var snapshot = new GameInfo {
            sub_rule = "sichuan/doubles",
            sichuan_teams = new SichuanTeamsInfo {
                team_by_player_index = new[] {0, 1, 0, 1},
                settled_scores = new Dictionary<int, int> {{0, 18}, {1, -18}},
            },
            players_info = new[] { new PlayerInfo { player_index = 0, score = 100 } },
        };
        var final = new ShowResultInfo {
            next_status = "round_end_by_ready",
            player_to_score = new Dictionary<int, int> {{0, 15}, {1, -9}, {2, 11}, {3, -17}},
        };
        foreach (string rule in new[] {"sichuan/doubles", "sichuan/xueliu_doubles"}) {
            state.Initialize(snapshot, rule);
            Check(state.Enabled && !state.Paired && state.Blue == 18 && state.Red == -18, "Use settled snapshot, not live personal totals");
            Check(state.Format(state.Blue) == "+18" && state.Format(state.Red) == "-18", "Signed single-table scores");
            final.round_continues = true;
            state.ApplySettlement(final);
            Check(state.Blue == 18, "Do not settle on a blood-battle/blood-flow mid-hand win");
            final.round_continues = false;
            final.defer_score_settlement = true;
            state.ApplySettlement(final);
            Check(state.Blue == 18, "Deferred settlement cannot update team totals");
            final.defer_score_settlement = false;
            state.ApplySettlement(final);
            Check(state.Blue == 26 && state.Red == -26, "Finished board includes both teammates");
            state.ApplySettlement(final);
            Check(state.Blue == 26, "Repeated final snapshot is idempotent");
            state.Initialize(snapshot, rule);
            Check(state.Blue == 18, "Reconnect restores authoritative totals");
            snapshot.sichuan_teams.team_by_player_index = new[] {1, 0, 1, 0};
            state.Initialize(snapshot, rule);
            state.ApplySettlement(final);
            Check(state.Blue == -26 && state.Red == 26, "Rotating seats does not rotate team identity");
            snapshot.sichuan_teams.team_by_player_index = new[] {0, 1, 0, 1};
        }
        state.Initialize(new GameInfo(), "sichuan/doubles");
        Check(state.Blue == null && state.Format(state.Blue) == "—", "Missing snapshot is not zero or a previous match");
        state.ApplySettlement(final);
        Check(state.Blue == null, "Missing team map cannot assume alternating teams");
        snapshot.sichuan_teams.team_by_player_index = new[] {0, 0, 0, 1};
        state.Initialize(snapshot, "sichuan/doubles");
        state.ApplySettlement(final);
        Check(state.Blue == 18, "Incomplete teams cannot change score");
        snapshot.sichuan_teams.team_by_player_index = new[] {0, 1, 0, 1};
        snapshot.duplicate_match = new DuplicateMatchInfo { id = "pair-a", scoring = "imp", status = "running", table = "closed" };
        state.Initialize(snapshot, "sichuan/doubles_duplicate");
        Check(state.Paired && state.Unit == "IMP" && state.Blue == null, "Cross-table IMP must not use local raw totals");
        state.ApplySettlement(final);
        Check(state.Blue == null, "Local round end cannot reveal or invent IMP");
        state.UpdateDuplicate(new DuplicateMatchInfo { id = "pair-a", scoring = "imp", waiting = true });
        Check(state.Caption.Contains("等待对室") && state.Blue == null, "Waiting remains visible when round label is replaced");
        state.UpdateDuplicate(new DuplicateMatchInfo { id = "pair-b", scoring = "imp", scores = new[] {99, 2} });
        Check(state.Blue == null, "Old match status is ignored");
        var published = new DuplicateMatchInfo { id = "pair-a", scoring = "imp", status = "completed", scores = new[] {7, 4}, imp_scores = new[] {7, 4}, small_scores = new[] {35, 19} };
        state.UpdateDuplicate(published);
        Check(state.Blue == 7 && state.Red == 4 && state.Format(state.Blue) == "7", "SBR uses IMP, not small scores");
        Check(state.Caption.Contains("全场结束"), "Final status is readable");
        published.scoring = "raw_difference";
        state.Initialize(new GameInfo { duplicate_match = published }, "sichuan/xueliu_doubles_duplicate");
        Check(state.Unit == "副差分" && state.Blue == 35 && state.Red == 19, "Blood-flow uses its official scoring unit");
        published.small_scores = null;
        published.scores = new[] {35, 19};
        state.UpdateDuplicate(published);
        Check(state.Blue == 35, "Legacy published scores stay compatible");
        published.scores = new[] {35};
        state.UpdateDuplicate(published);
        Check(state.Blue == null && state.Red == null, "Malformed scores do not retain old public scores");
        foreach (string rule in new[] {"sichuan/standard", "sichuan/xueliu_exchange", "guobiao/standard", "riichi/standard", "riichi/washizu_doubles"}) {
            state.Initialize(snapshot, rule);
            Check(!state.Enabled && state.Blue == null, "Other rules keep their round display: " + rule);
        }
        var record = new GameRecord { gameTitle = new Dictionary<string, object> {
            {"sub_rule", "sichuan/doubles"}, {"team_by_player_index", new[] {0, 1, 0, 1}},
        } };
        record.gameRound.rounds[1] = new Round { scoreChanges = new List<int> {10, -6, 8, -12}, seats = new List<int> {0, 1, 2, 3} };
        record.gameRound.rounds[2] = new Round { scoreChanges = new List<int> {100, -50, 100, -150}, seats = new List<int> {3, 0, 1, 2} };
        var replay = SichuanRoundScoreState.RecordSnapshot(record, 2);
        Check(replay.settled_scores[0] == 18 && replay.settled_scores[1] == -18, "Replay excludes current/future board scores");
        Check(replay.team_by_player_index[0] == 1 && replay.team_by_player_index[3] == 0, "Replay maps original teams through rotated seats");
        Check(SichuanRoundScoreState.RecordSnapshot(record, 1).settled_scores[0] == 0, "Replay first board starts at zero");
        return $"Passed {checks} Sichuan Round Panel score checks.";
    }
}
#endif
