#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>川麻计分表必须与逐帧分数累计一致，含换位、杠分、退税及终局结算。</summary>
public static class SichuanScoreHistoryValidation {
    public static string Run() {
        int checks = 0;
        void Verify(string subRule, bool bloodBattle, int[] seats, params string[][] ticks) {
            var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = "sichuan", ["sub_rule"] = subRule, ["blood_battle"] = bloodBattle,
            }};
            var round = new Round {
                roundIndex = 1, currentRound = 1, seats = seats.ToList(),
                p0Tiles = new List<int> {11,11,11,11,22},
                actionTicks = ticks.Select(t => t.ToList()).ToList(),
            };
            record.gameRound.rounds[1] = round;
            foreach (var tick in round.actionTicks) GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round, tick);
            var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
            var actual = new int[4];
            foreach (var row in rows) {
                if (row.scoreChangesByOriginal == null) continue;
                for (int i = 0; i < 4; i++) actual[i] += row.scoreChangesByOriginal[i];
            }
            if (rows.Count == 0 || !actual.SequenceEqual(round.scoreChanges ?? new List<int> {0,0,0,0}))
                throw new Exception($"Sichuan scoreboard {subRule}, blood={bloodBattle}, seats={string.Join(",",seats)}: "
                    + $"displayed [{string.Join(",",actual)}], replay [{string.Join(",",round.scoreChanges ?? new List<int>())}]");
            if (SichuanLobby.IsXueliu(subRule) && rows.Count != 1)
                throw new Exception("血流同局多次结算必须保持一行");
            checks++;
        }
        foreach (var seats in new[] {new[] {0,1,2,3}, new[] {3,0,1,2}, new[] {2,3,0,1}, new[] {1,2,3,0}}) {
            foreach (bool blood in new[] {true, false}) {
                Verify("sichuan/standard", blood, seats,
                    new[] {"ag","11","F","11","11","11","11","gs","6","-2","-2","-2"},
                    new[] {"hu_first","1","2","[]", blood ? "[0,0,0,0]" : "[-2,2,0,0]","22"},
                    new[] {"liuju","reveal_hu","{}"},
                    new[] {"liuju","settle_hu","hu_first","1","2","[]", blood ? "[-2,2,0,0]" : "[0,0,0,0]","1"},
                    new[] {"end"});
            }
            Verify("sichuan/standard", true, seats,
                new[] {"g","22","1","22","22","22","gs","-2","2","0","0"},
                new[] {"jg","22","T","gs","-1","3","-1","-1"},
                new[] {"gr","gs","1","-3","1","1"},
                new[] {"liuju","reveal_hu","{}"},
                new[] {"liuju","chajiao","1","no_ting","[]","[2,-2,0,0]","1"},
                new[] {"liuju","cha_refund","[0,0,0,0]"},
                new[] {"liuju","final","[100,200,300,400]"}, new[] {"end"});
            Verify("sichuan/xueliu_exchange", false, seats,
                new[] {"ag","11","F","gs","6","-2","-2","-2"},
                new[] {"hu_self","0","1","[]","[3,-1,-1,-1]","22"},
                new[] {"liuju","chajiao","1","no_ting","[]","[-2,2,0,0]","0"},
                new[] {"liuju","chajiao","2","ting","[]","[0,-2,2,0]","1"}, new[] {"end"});
            Verify("sichuan/xueliu", false, seats,
                new[] {"ag","11","F"},
                new[] {"hu_self","0","3","[]","[9,-3,-3,-3]","22"},
                new[] {"liuju","chajiao","1","no_ting","[]","[0,-2,2,0]","1"}, new[] {"end"});
            foreach (string sub in new[] {"sichuan/xueliu", "sichuan/xueliu_exchange", "sichuan/xueliu_doubles"}) {
                Verify(sub, false, seats,
                    new[] {"jg","11","F","gs","3","-1","-1","-1"},
                    new[] {"gr","gs","-3","1","1","1"},
                    new[] {"hu_first","1","2","[]","[-2,2,0,0]","11"},
                    new[] {"hu_second","2","2","[]","[-2,0,2,0]","11"},
                    new[] {"hu_self","1","1","[]","[-1,3,-1,-1]","22"},
                    new[] {"liuju","reveal_hu","{}"},
                    new[] {"liuju","chajiao","3","no_ting","[]","[2,0,0,-2]","1"},
                    new[] {"liuju","cha_refund","[0,-2,0,2]"},
                    new[] {"liuju","final","[-3,3,1,-1]"}, new[] {"end"});
                Verify(sub, false, seats, new[] {"ag","11","F","gs","6","-2","-2","-2"});
                Verify(sub, false, seats, new[] {"liuju","chajiao","0","ting","[]","[0,0,0,0]","1"});
            }
        }
        foreach (string sub in new[] {"sichuan/xueliu", "sichuan/xueliu_exchange", "sichuan/xueliu_doubles"}) {
            var players = Enumerable.Range(0, 4).Select(i => new PlayerInfoClass {original_player_index = i}).ToArray();
            var snapshots = new List<RoundSettlementSnapshot>();
            void AssertHistory(int round, int[][] expected) {
                XueliuScoreHistory.Refresh(sub, round, players, snapshots);
                // 重绘/重复快照不能产生新行或给总分再次加分。
                XueliuScoreHistory.Refresh(sub, round, players, snapshots);
                for (int i = 0; i < 4; i++) {
                    if (!players[i].score_history.Select(int.Parse).SequenceEqual(expected[i])
                        || players[i].score != expected[i].Sum()
                        || !players[i].round_number_history.SequenceEqual(Enumerable.Range(1, round)))
                        throw new Exception($"{sub} live row mismatch, player {i}, round {round}");
                }
                if (snapshots.Count != round) throw new Exception("血流结算快照与计分行不一致");
                checks++;
            }
            AssertHistory(1, new[] {new[] {0}, new[] {0}, new[] {0}, new[] {0}});
            // 杠分、两次和牌、退分：每次用权威总分刷新同一行。
            foreach (int[] totals in new[] {new[] {6,-2,-2,-2},new[] {9,-3,-3,-3},new[] {7,1,-4,-4},new[] {1,3,-2,-2}}) {
                for (int i = 0; i < 4; i++) players[i].score = totals[i];
                AssertHistory(1, totals.Select(v => new[] {v}).ToArray());
            }
            XueliuScoreHistory.Refresh(sub, 1, players, snapshots, SichuanRoundLabel.Chajiao);
            AssertHistory(2, new[] {new[] {1,0},new[] {3,0},new[] {-2,0},new[] {-2,0}});
            players[0].score += 6; players[1].score -= 2; players[2].score -= 2; players[3].score -= 2;
            // 换座与重连只改变排列和快照缓存，不丢失前局，也不重复累计本局。
            Array.Reverse(players);
            snapshots.Clear();
            XueliuScoreHistory.Refresh(sub, 2, players, snapshots);
            Array.Reverse(players);
            AssertHistory(2, new[] {new[] {1,6},new[] {3,-2},new[] {-2,-2},new[] {-2,-2}});
            if (snapshots[0].sichuanRoundLabel != SichuanRoundLabel.Chajiao)
                throw new Exception("血流重连丢失已完成局标记");
            checks++;
        }
        return $"Passed {checks} Sichuan scoreboard checks.";
    }
}
#endif
