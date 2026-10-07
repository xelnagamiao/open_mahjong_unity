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
        }
        return $"Passed {checks} Sichuan scoreboard checks.";
    }
}
#endif
