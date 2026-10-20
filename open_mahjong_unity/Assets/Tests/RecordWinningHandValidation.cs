#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>和牌张的归属由回放状态决定，独立于规则张数、牌面重复和展示次数。</summary>
public static class RecordWinningHandValidation {
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run() {
        var checks = new List<string>();
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException("Winning hand: " + label);
            checks.Add(label);
        }
        // 覆盖每一种余数及空暗手；其中 3 张正是玩家截图触发的分支。
        foreach (int length in Enumerable.Range(0, 15)) {
            int[] hand = Enumerable.Range(0, length).Select(i => 1001 + i / 9 * 10 + i % 9).ToArray();
            int[] original = (int[])hand.Clone();
            int[] complete = hand.Concat(new[] { 1078 }).ToArray();
            foreach (string action in new[] { "hu_first", "hu_second", "hu_third" }) {
                int[] built = RecordHuHandBuilder.BuildDisplayHand(hand, action, 1078, 1099, false);
                Check(built.SequenceEqual(complete), $"{length} tiles / {action}: append the declared tile once");
                Check(RecordHuHandBuilder.BuildDisplayHand(built, action, 1078, 1099, true).SequenceEqual(complete),
                    $"{length} tiles / {action}: an already received tile is never appended twice");
            }
            Check(RecordHuHandBuilder.BuildDisplayHand(complete, "hu_self", 1078, 1099, false).SequenceEqual(complete),
                $"{length} tiles: self draw preserves its existing draw");
            Check(hand.SequenceEqual(original), $"{length} tiles: source remains immutable");
        }
        var repeated = new[] { 11, 11, 11, 12 };
        Check(RecordHuHandBuilder.BuildDisplayHand(repeated, "hu_first", 11, 12, false)
            .SequenceEqual(new[] { 11, 11, 11, 12, 11 }), "ordinary mahjong retains same-face physical copies");
        Check(RecordHuHandBuilder.BuildDisplayHand(repeated, "hu_first", 0, 19, false).Last() == 19,
            "a record without explicit tile uses its recorded discard source");

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Winning hand regression");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var saved = new Dictionary<string, RuleManifest>(registry);
        try {
            RuleRegistry.Register(new RuleManifest { RuleId = "hongque", DefaultSubRule = "hongque/v1.6" });
            foreach (string rule in new[] { "guobiao", "qingque", "riichi", "classical" })
                RuleRegistry.Register(new RuleManifest { RuleId = rule, DefaultSubRule = rule + "/standard" });
            var manager = root.AddComponent<GameRecordManager>();
            var receive = typeof(GameRecordManager).GetMethod("ApplyRecordRonHuToHandState", Hidden);
            var seek = typeof(GameRecordManager).GetMethod("ApplyActionToRecordState", Hidden);
            foreach (string rule in new[] { "hongque", "guobiao", "qingque", "riichi", "classical" }) {
                foreach (int seat in Enumerable.Range(0, 4)) {
                    int[] closed = rule == "hongque" ? new[] { 1072, 1076, 1074 } : new[] { 11, 11, 11, 12 };
                    int winningTile = rule == "hongque" ? 1078 : 12;
                    int[] expected = closed.Concat(new[] { winningTile }).ToArray();
                    var tick = new List<string> { "hu_first", seat.ToString(), "30", "[\"平和|1\"]", "[30,0,0,0]", winningTile.ToString() };
                    var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                        ["rule"] = rule, ["sub_rule"] = rule == "hongque" ? "hongque/v1.6" : rule + "/standard" } };
                    record.gameRound.rounds[1] = new Round { roundIndex = 1, currentRound = 1,
                        seats = new List<int> {0,1,2,3}, actionTicks = new List<List<string>> { tick } };
                    typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager, record);
                    manager.currentRoundIndex = 1;
                    manager.currentPlayerIndex = 0;
                    manager.currentNode = 0;
                    manager.indexToPosition.Clear();
                    manager.recordPlayer_to_info.Clear();
                    string[] positions = { "self", "right", "top", "left" };
                    var players = new List<GameRecordManager.RecordPlayer>();
                    for (int i = 0; i < 4; i++) {
                        var player = new GameRecordManager.RecordPlayer { playerIndex = i, originalPlayerIndex = i, userId = 101+i };
                        players.Add(player);
                        manager.indexToPosition[i] = positions[i];
                        manager.recordPlayer_to_info[positions[i]] = player;
                    }
                    typeof(GameRecordManager).GetField("recordPlayerList", Hidden).SetValue(manager, players);
                    var winner = players[seat];
                    foreach (bool jumping in new[] { false, true }) {
                        winner.tileList = closed.ToList();
                        winner.hasRonWinningTile = false;
                        winner.showHandDrawSlotActive = false;
                        if (jumping) seek.Invoke(manager, new object[] { tick });
                        else receive.Invoke(manager, new object[] { tick, "hu_first" });
                        Check(winner.hasRonWinningTile && winner.tileList.SequenceEqual(expected),
                            $"{rule}/{seat}/{jumping}: sequential/delayed and seek state retain every tile");
                        receive.Invoke(manager, new object[] { tick, "hu_first" });
                        Check(winner.tileList.SequenceEqual(expected), $"{rule}/{seat}/{jumping}: repeated projection is idempotent");
                        var display = RecordHuHandBuilder.BuildDisplayHandFromTick(tick, rule, winner.tileList,
                            "hu_first", 0, winner.hasRonWinningTile);
                        Check(display.SequenceEqual(expected), $"{rule}/{seat}/{jumping}: display reads the complete snapshot");
                        var request = HepaiRevealDirector.BuildRecordRequest(positions[seat], "hu_first", display, null, rule, true, winningTile, "left");
                        Check(request.HepaiTile == winningTile && request.HepaiPlayerHand.SequenceEqual(expected),
                            $"{rule}/{seat}/{jumping}: reveal keeps winning identity and original closed hand");
                    }
                }
            }
            // 三个已亮牌组共 10 张，余下 DY2/DY4/DY6；真正点和张 DY8 必须另加。
            var sample = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = "hongque", ["sub_rule"] = "hongque/v1.6" } };
            sample.gameRound.rounds[1] = new Round { roundIndex = 1, currentRound = 1,
                seats = new List<int> {0,1,2,3}, p0Tiles = new List<int> {1072,1076,1074,1038,1039,1094,1096,1026,1034,1042},
                actionTicks = new List<List<string>> {
                    new List<string> {"cm","1037","0","1038","1039"},
                    new List<string> {"cm","1092","0","1094","1096"},
                    new List<string> {"cm","1018","0","1026","1034","1042"},
                    new List<string> {"hu_first","0","30","[\"平和|1\"]","[30,0,0,0]","1078"} } };
            string before = JsonConvert.SerializeObject(sample);
            var row = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(sample).Single();
            Check(row.snapshot.hepaiPlayerHand.SequenceEqual(new[] {1072,1076,1074,1078}),
                "scoreboard and replay use the same complete hand contract");
            Check(row.snapshot.combinationMask.Sum(mask => mask.Length / 2) == 10,
                "variable melds retain ten tiles alongside the four-tile winning hand");
            Check(JsonConvert.SerializeObject(sample) == before, "reading existing records requires no migration and changes no data");
            return JsonConvert.SerializeObject(new { passed = checks.Count, checks }, Formatting.Indented);
        } finally {
            registry.Clear();
            foreach (var entry in saved) registry.Add(entry.Key, entry.Value);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
