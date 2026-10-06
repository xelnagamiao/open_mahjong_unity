#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>日麻计分板供托回归；只验证内存数据，镜像字段在 finally 中恢复。</summary>
public static class RiichiScoreHistoryValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool condition, string label) {
            if (!condition) throw new Exception("Riichi scoreboard: " + label);
            checks.Add(label);
        }
        GameRecord Record(params string[][] ticks) {
            var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = "riichi", ["sub_rule"] = "riichi/standard", ["starting_score"] = 25000,
            }};
            record.gameRound.rounds[1] = new Round {
                roundIndex = 1, currentRound = 1, seats = new List<int> {0,1,2,3},
                actionTicks = ticks.Select(tick => tick.ToList()).ToList(),
            };
            return record;
        }
        string[] Hu(int winner, string changes, string yaku = "[]", int sticks = 1) =>
            new[] {"hu_riichi", winner.ToString(), "hu_first", "2", "30", yaku, changes,
                "[]", "[]", "0", "0", sticks.ToString(), "2000"};
        void Verify(GameRecord record, int[][] expectedRows, string label) {
            var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
            Check(rows.Count == expectedRows.Length, label + " row count");
            for (int i = 0; i < rows.Count; i++)
                Check(rows[i].scoreChangesByOriginal.SequenceEqual(expectedRows[i]), label + " row " + i);
            var totals = new int[4];
            foreach (var round in record.gameRound.GetRoundsList()) {
                foreach (var tick in round.actionTicks) GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round, tick);
                if (round.scoreChanges != null)
                    for (int i = 0; i < 4; i++) totals[i] += round.scoreChanges[i];
            }
            Check(Enumerable.Range(0, 4).All(player => rows.Sum(row => row.scoreChangesByOriginal[player]) == totals[player]),
                label + " matches replay ledger");
        }
        foreach (var seats in new[] {new[] {0,1,2,3}, new[] {3,0,1,2}, new[] {2,3,0,1}, new[] {1,2,3,0}}) {
            var record = Record(new[] {"riichi","0","0"}, Hu(1,"[-2000,3000,0,0]"), new[] {"end"});
            record.gameRound.rounds[1].seats = seats.ToList();
            Verify(record, new[] {GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(
                new[] {-3000,3000,0,0}, seats.ToList())}, "deposit with seats " + string.Join(",",seats));
        }
        Verify(Record(new[] {"riichi","0","1"}, Hu(0,"[4000,-1000,-1000,-1000]")),
            new[] {new[] {3000,-1000,-1000,-1000}}, "winner's own double-riichi deposit");
        Verify(Record(new[] {"c","47","T","H"}, Hu(1,"[-2000,2000,0,0]", sticks:0)),
            new[] {new[] {-2000,2000,0,0}}, "declaration discard ronned before payment");
        Verify(Record(new[] {"riichi","2","0"}, new[] {"ryuukyoku","[0,0,1,0]","[-1000,-1000,3000,-1000]","exhaustive"}),
            new[] {new[] {-1000,-1000,2000,-1000}}, "draw keeps deposit on table");
        Verify(Record(new[] {"riichi","0","0"}, new[] {"riichi","1","0"}, new[] {"riichi","2","0"},
            new[] {"riichi","3","0"}, new[] {"liuju"}), new[] {new[] {-1000,-1000,-1000,-1000}}, "four-riichi abort");
        Verify(Record(new[] {"riichi","2","0"}, Hu(1,"[-8000,9000,0,0]"), Hu(2,"[-8000,0,8000,0]", sticks:0)),
            new[] {new[] {-8000,9000,-1000,0}, new[] {-8000,0,8000,0}}, "multiple ron charges deposit once");
        Verify(Record(new[] {"riichi","0","0"}, Hu(0,"[-8000,3000,3000,3000]","[\"错和\"]",0)),
            new[] {new[] {-9000,3000,3000,3000}}, "chombo refund cancels deposit");
        var carry = Record(new[] {"riichi","2","0"}, new[] {"ryuukyoku","[0,0,1,0]","[-1000,-1000,3000,-1000]"});
        carry.gameRound.rounds[2] = new Round {roundIndex=2,currentRound=2,seats=new List<int>{0,1,2,3},
            actionTicks=new List<List<string>> {Hu(1,"[-2000,3000,0,0]").ToList()}};
        Verify(carry, new[] {new[] {-1000,-1000,2000,-1000},new[] {-2000,3000,0,0}}, "carry-over deposit awarded next hand");
        var finalDraw = Record(new[] {"riichi","2","0"}, new[] {"ryuukyoku","[0,0,1,0]","[-1000,-1000,3000,-1000]"},new[] {"end"});
        finalDraw.gameTitle["riichi_final_scores"] = new[] {24000,24000,28000,24000};
        Check(ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(finalDraw)[0].scoreChangesByOriginal.SequenceEqual(
            new[] {-1000,-1000,3000,-1000}), "record final deposit distribution included in last row");

        var response = JsonConvert.DeserializeObject<ShowResultInfo>(
            "{\"hu_class\":\"four_riichi_abort\",\"score_history_changes\":{\"0\":-1000,\"1\":-1000,\"2\":-1000,\"3\":-1000}}");
        Check(RiichiGameState.BuildExtras(response)?.ScoreHistoryChanges?[2] == -1000, "abort result protocol includes history changes");
        var mirror = TableMirror.Current;
        var previousPlayers = mirror.PlayerToInfo;
        var previousPositions = mirror.IndexToPosition;
        int previousRound = mirror.CurrentRound;
        try {
            mirror.IndexToPosition = new Dictionary<int,string> {[0]="self",[1]="right",[2]="top",[3]="left"};
            mirror.PlayerToInfo = new Dictionary<string,PlayerInfoClass>();
            mirror.CurrentRound = 3;
            foreach (var pair in mirror.IndexToPosition) mirror.PlayerToInfo[pair.Value] = new PlayerInfoClass {
                original_player_index = (pair.Key + 2) % 4, score = pair.Key == 0 ? 24000 : 25000,
            };
            // Reconnect already reflects the -1000 deposit; history must still record it.
            var changes = new Dictionary<int,int> {[2]=-3000,[3]=3000,[0]=0,[1]=0};
            var scores = new Dictionary<int,int> {[0]=22000,[1]=28000,[2]=25000,[3]=25000};
            SettlementPresenter.Current.ApplyLocalScoreHistory(new RoundSettlementSnapshot(),changes,scores);
            Check(mirror.PlayerToInfo["self"].score == 22000, "reconnect does not deduct deposit twice");
            Check(mirror.PlayerToInfo["self"].score_history.SequenceEqual(new[] {"-3000"}), "reconnect records full hand delta");
            Check(mirror.PlayerToInfo["right"].score_history.SequenceEqual(new[] {"+3000"}), "live history follows original seat after rotation");
            Check(mirror.PlayerToInfo.Values.Sum(p => p.score) == 100000, "live scoreboard conserves player points after win");
            Check(mirror.PlayerToInfo.Values.All(p => p.round_number_history.SequenceEqual(new[] {3})), "live labels remain aligned");
            SettlementPresenter.Current.ApplyLocalScoreHistory(new RoundSettlementSnapshot(), new Dictionary<int,int> {[2]=-100,[3]=100});
            Check(mirror.PlayerToInfo["self"].score == 21900 && mirror.PlayerToInfo["right"].score == 28100,
                "legacy delta-only settlement remains supported");
            int[] drawScores = {24000,24000,27000,24000};
            foreach (var pair in mirror.IndexToPosition) {
                var player = mirror.PlayerToInfo[pair.Value];
                player.score = drawScores[pair.Key];
                player.score_history = new List<string> {(drawScores[pair.Key]-25000).ToString()};
            }
            var finalScores = new Dictionary<int,int> {[2]=24000,[3]=24000,[0]=28000,[1]=24000};
            Check(SettlementPresenter.Current.ApplyFinalScoreHistory(finalScores), "live final deposit distribution applied");
            Check(mirror.PlayerToInfo["top"].score_history.SequenceEqual(new[] {"+3000"}), "live final deposit merged into last row");
            Check(mirror.PlayerToInfo.Values.Sum(p => p.score) == 100000, "final player totals match result panel");
            Check(!SettlementPresenter.Current.ApplyFinalScoreHistory(finalScores), "final deposit distribution is idempotent");
        } finally {
            mirror.PlayerToInfo = previousPlayers;
            mirror.IndexToPosition = previousPositions;
            mirror.CurrentRound = previousRound;
        }
        return JsonConvert.SerializeObject(new {passed=checks.Count,checks},Formatting.Indented);
    }
}
#endif
