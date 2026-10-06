#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>三人国标入口、牌池、风圈、提示和牌谱的客户端回归。</summary>
public static class GuobiaoSanmaValidation {
    public static object Run() {
        // Edit Mode has not run RuntimeInitializeOnLoadMethod yet.
        typeof(GuobiaoRuleBootstrap).GetMethod("Register", System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
        var checks = new List<string>();
        void Check(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("Guobiao sanma: " + name);
            checks.Add(name);
        }
        const string sub = "guobiao/sanma";
        Check(GuobiaoLobby.SubRules.Length == 6 && GuobiaoLobby.SubRules[5].Key == sub, "subrule registered last");
        Check(MahjongPlayerCount.ForSubRule(sub) == 3 && MahjongPlayerCount.ForSubRule("guobiao/standard") == 4
            && MahjongPlayerCount.ForSubRule("riichi/sanma") == 3, "capacities isolated");
        for (int seat = 0; seat < 3; seat++) {
            var mapping = SwitchSeatPanel.ThreePlayerPositionMapping(seat);
            string[] positions = { "self", "right", "left" };
            Check(mapping.Count == 3 && !mapping.ContainsKey("top")
                && mapping[positions[(3 - seat) % 3]] == positions[(4 - seat) % 3]
                && mapping[positions[(4 - seat) % 3]] == positions[(3 - seat) % 3]
                && mapping[positions[(5 - seat) % 3]] == positions[(5 - seat) % 3], "wind switch map " + seat);
        }
        foreach (int count in new[] { 3, 4 }) {
            foreach (int circles in new[] { 1, 2, 4 }) {
                for (int wind = 1; wind <= 3; wind++) {
                    int nextRound = count * wind + 1;
                    Check(SwitchSeatPanel.IsWindChange(nextRound, circles, count) == (wind < circles),
                        "wind switch limit " + count + "/" + circles + "/" + wind);
                }
            }
        }
        for (int round = 1; round <= 12; round++) {
            string name = "东南西北"[(round - 1) / 3] + "风" + "东南西"[(round - 1) % 3];
            Check(RoundTextDictionary.GetRoundName(sub, round) == name, "round " + round);
            Check(ScoreHistoryPanel.ScoreboardRoundName("guobiao", sub, round) == name, "scoreboard round " + round);
            var ctx = new RecordTipsContext { RoomRule = "guobiao", SubRule = sub,
                CurrentRound = round, SelfPlayerIndex = 2, HepaiLimit = 8 };
            var queries = RecordWaitHintCalculator.BuildQueries(ctx, new List<int>(), new List<int> {44});
            Check(queries[0].WayToHepai.Contains("场风" + "东南西北"[(round - 1) / 3])
                && queries[0].WayToHepai.Contains("自风西"), "record wind " + round);
        }
        Check(ScoreHistoryPanel.ScoreboardRoundName("guobiao", "guobiao/standard", 4)
            == RoundTextDictionary.WindSeatRoundName(4), "four-player round preserved");
        var mirror = TableMirror.Current;
        var oldMap = mirror.IndexToPosition;
        string oldSub = GameSession.Current.SubRule;
        try {
            GameSession.Current.SubRule = sub;
            Check(ScoreHistoryPanel.ScoreboardRoundName("guobiao", "guobiao/standard", 4) == "东风北",
                "four-player record labels ignore active sanma session");
            mirror.IndexToPosition = new Dictionary<int, string>();
            for (int seat = 0; seat < 3; seat++) {
                mirror.BuildSeatMap(seat, new[] {0, 1, 2});
                Check(mirror.IndexToPosition.Count == 3 && mirror.IndexToPosition[seat] == "self"
                    && mirror.IndexToPosition[(seat + 1) % 3] == "right"
                    && mirror.IndexToPosition[(seat + 2) % 3] == "left", "seat map " + seat);
            }
        } finally { mirror.IndexToPosition = oldMap; GameSession.Current.SubRule = oldSub; }
        var hand = new List<int> {21, 24, 27, 32, 35, 38, 41, 42, 43, 44, 45, 46, 47};
        var standard = GuobiaoTips.Tingpai(new TingpaiQuery {Hand = hand, Melds = new List<string>(), SubRule = "guobiao/standard"});
        Check(standard.SetEquals(new[] {13, 16, 19}), "standard waits unchanged");
        var waiting = GuobiaoTips.Tingpai(new TingpaiQuery {Hand = hand, Melds = new List<string>(), SubRule = sub});
        Check(waiting.SetEquals(new[] {19}), "removed manzu waits filtered");
        waiting.Clear();
        Check(GuobiaoTips.Tingpai(new TingpaiQuery {Hand = hand, Melds = new List<string>(), SubRule = sub}).SetEquals(new[] {19}), "waiting cache independent");
        for (int tile = 12; tile <= 18; tile++) {
            var hint = GuobiaoTips.Describe(new WaitHintQuery {SubRule = sub, HepaiTile = tile,
                HandWithWin = hand, Melds = new List<string>(), WayToHepai = new List<string>(),
                SingleTileWay = new List<string>(), MergedWay = new List<string>(), HepaiLimit = 8});
            Check(hint.Label == "不在牌组内", "removed tile hint " + tile);
        }
        var legalHand = new List<int> {11, 11, 11, 21, 22, 23, 31, 32, 33, 44, 44, 44, 45, 45};
        for (int wind = 0; wind < 4; wind++) {
            foreach (bool zimo in new[] {false, true}) {
                var way = new List<string> {"场风" + "东南西北"[wind], "自风西", "和单张", zimo ? "自摸" : "点和"};
                var result = GBhepai.HepaiCheck(legalHand, new List<string>(), way.ToList(), 45, false);
                var hint = GuobiaoTips.Describe(new WaitHintQuery {SubRule = sub, HepaiTile = 45,
                    HandWithWin = legalHand, Melds = new List<string>(), WayToHepai = way.ToList(),
                    MergedWay = way.ToList(), SingleTileWay = new List<string>(), HepaiLimit = 8});
                Check(result.Item1 >= 8 && hint.Label == result.Item1 + "番", "standard fan table north " + wind + zimo);
            }
        }
        var config = new GB_Create_RoomConfig {RoomName = "三人国标", SubRule = sub, GameRound = 4,
            RoundTimer = 20, StepTimer = 5, HepaiLimit = 8, UseFlowers = true, TianDiRenHe = true};
        Check(config.Validate(out _, false, false), "standard options accepted");
        config.DuplicateKey = "DUP_test";
        Check(!config.Validate(out string error, false, false) && error.Contains("三人国标"), "four-seat duplicate rejected");
        var json = new JObject {
            ["game_title"] = new JObject {["rule"] = "guobiao", ["sub_rule"] = sub, ["max_round"] = 1},
            ["game_round"] = new JObject {
                ["round_index_1"] = new JObject {
                    ["round_index"] = 1, ["current_round"] = 1, ["dealer_index"] = 0,
                    ["start_player_index"] = 0, ["seats"] = new JArray(2, 0, 1),
                    ["p0_tiles"] = new JArray(), ["p1_tiles"] = new JArray(), ["p2_tiles"] = new JArray(),
                    ["tiles_list"] = new JArray(),
                    ["action_ticks"] = new JArray(new JArray("hu_first", 1, 16, new JArray("测试番"), new JArray(-24, 32, -8)), new JArray("end")),
                },
            },
        };
        var record = GameRecordJsonDecoder.ParseGameRecord(json.ToString());
        var data = record.gameRound.rounds[1];
        Check(data.seats.Count == 3 && data.scoreChanges.Take(3).SequenceEqual(new[] {-8, -24, 32}), "three-seat score decoding");
        var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
        Check(rows.Count == 1 && rows[0].scoreChangesByOriginal.Take(3).SequenceEqual(new[] {-8, -24, 32})
            && rows[0].scoreChangesByOriginal[3] == 0, "three-seat score history");
        return new {passed = checks.Count, checks};
    }
}
#endif
