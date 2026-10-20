#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>牌谱余张固定按各玩家当时已知的信息计数，与观看视角、隐藏手牌及牌山无关。</summary>
public static class RecordWaitPerspectiveValidation {
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;

    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        var checks = new List<string>();
        void Check(bool ok, string label) {
            if (!ok) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry).GetField("ManifestsByRule", StaticHidden).GetValue(null);
        var saved = new Dictionary<string, RuleManifest>(registry);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Record wait perspective validation");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var recordObject = new GameObject("Record");
        recordObject.transform.SetParent(root.transform, false);
        var manager = recordObject.AddComponent<GameRecordManager>();
        object Call(string name, params object[] args) => typeof(GameRecordManager).GetMethod(name, Hidden).Invoke(manager, args);
        RecordTipsContext Context(GameRecordManager.RecordPlayer player) => (RecordTipsContext)Call("BuildRecordTipsContext", player);
        int Remaining(RecordTipsContext ctx, List<int> hand, int tile, int? cut = null) => RecordWaitHintCalculator.Remaining(
            tile, RecordWaitHintCalculator.CountVisibleTiles(ctx, hand, cut), RuleRegistry.Resolve(ctx.RoomRule, ctx.SubRule));
        try {
            foreach (Type type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
                type.GetMethod("Register", StaticHidden | BindingFlags.Public)?.Invoke(null, null);
            var record = new GameRecord { gameTitle = new Dictionary<string, object> {
                ["rule"] = "guobiao", ["sub_rule"] = "guobiao/standard",
            } };
            // Screenshot regression: 6vuva5EjjL, west/south, node 127.
            // One 9m is in our hand and one in the river; another player's 9m and the hidden wall are unknown.
            var round = new Round { roundIndex = 10, currentRound = 10,
                tilesList = new List<int> { 19, 11, 12, 13, 14, 15 } };
            record.gameRound.rounds[10] = round;
            typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager, record);
            manager.currentRoundIndex = 10;
            var players = new[] {
                new GameRecordManager.RecordPlayer { playerIndex = 0, discardTiles = new List<int> {19} },
                new GameRecordManager.RecordPlayer { playerIndex = 1 },
                new GameRecordManager.RecordPlayer { playerIndex = 2,
                    tileList = new List<int> {16,17,18,19,33,34,35,35,36,37},
                    combinationTiles = new List<string> {"s32"} },
                new GameRecordManager.RecordPlayer { playerIndex = 3, tileList = new List<int> {16,19} },
            };
            void Rotate(int viewer) {
                manager.indexToPosition.Clear(); manager.recordPlayer_to_info.Clear();
                foreach (var player in players) {
                    string position = TableMirror.RelativeSeat(viewer, player.playerIndex, 4);
                    manager.indexToPosition[player.playerIndex] = position;
                    manager.recordPlayer_to_info[position] = player;
                }
            }
            Rotate(2);
            Call("ResetRecordRiichiFieldState", round);
            var ctx = Context(players[2]);
            Check(ctx.DoraIndicators.Count == 0, "Guobiao never reveals the sixth tile from the wall end");
            Check(Remaining(ctx, players[2].tileList, 16) == 3 && Remaining(ctx, players[2].tileList, 19) == 2,
                "Screenshot: 6m = 3, 9m = 2");
            foreach (int viewer in Enumerable.Range(0, 4)) {
                Rotate(viewer);
                foreach (var player in players) {
                    ctx = Context(player);
                    int expected = 4 - 1 - player.tileList.Count(t => t == 19);
                    Check(Remaining(ctx, player.tileList, 19) == expected,
                        $"Viewer {viewer}, player {player.playerIndex}: only own hand and public river");
                    Check(ctx.PlayersByPosition["self"].CombinationTiles == player.combinationTiles,
                        $"Viewer {viewer}, player {player.playerIndex}: self always means the calculated player");
                }
                var opponentHand = players[3].tileList;
                players[3].tileList = new List<int> {19,19,19};
                Check(Remaining(Context(players[2]), players[2].tileList, 19) == 2,
                    $"Viewer {viewer}: changing hidden opponent copies does not change our count");
                players[3].tileList = opponentHand;
            }
            foreach (var rule in RuleRegistry.All.Where(r => !r.RecordTracksRiichiField).ToArray()) {
                record.gameTitle["rule"] = rule.RuleId; record.gameTitle["sub_rule"] = rule.DefaultSubRule;
                Call("ResetRecordRiichiFieldState", round);
                var indicators = (List<int>)typeof(GameRecordManager).GetField("recordRiichiDoraIndicators", Hidden).GetValue(manager);
                Check(indicators.Count == 0, rule.RuleId + ": no Riichi fallback indicator");
            }
            record.gameTitle["rule"] = "riichi"; record.gameTitle["sub_rule"] = "riichi/standard";
            Call("ResetRecordRiichiFieldState", round);
            Check(Context(players[2]).DoraIndicators.SequenceEqual(new[] {19}), "Legacy Riichi wall marker remains visible");
            round.riichi = new RiichiRoundExtras { doraMarker = 16, riichiSticks = 2 };
            Call("ResetRecordRiichiFieldState", round);
            Check(Context(players[2]).DoraIndicators.SequenceEqual(new[] {16}), "Explicit Riichi marker overrides wall inference");
            record.gameTitle["sub_rule"] = WashizuTile.SubRule;
            round.washizu = new WashizuRoundExtras { dora_indicators = new[] {2018,2028}, dead_wall_count = 13 };
            Call("ResetRecordRiichiFieldState", round);
            Check(Context(players[2]).DoraIndicators.SequenceEqual(new[] {2018,2028}), "Washizu recorded indicators are preserved");
            record.gameTitle["rule"] = "guobiao"; record.gameTitle["sub_rule"] = "guobiao/standard";
            Call("ResetRecordRiichiFieldState", round);
            Check(Context(players[2]).DoraIndicators.Count == 0, "Switching back to Guobiao clears Riichi markers even with old metadata");

            var other = new RecordTipsPlayerVisible { CombinationTiles = new List<string> {"G19"} };
            ctx = new RecordTipsContext { RoomRule = "guobiao", SubRule = "guobiao/standard",
                PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible> { ["right"] = other } };
            var empty = new List<int>();
            Check(Remaining(ctx, empty, 19) == 4, "An opponent's encoded concealed kong is not public information");
            other.CombinationMasks = new List<int[]> {new[] {2,19,2,19,2,19,2,19}};
            Check(Remaining(ctx, empty, 19) == 4, "Replay knowing all concealed kong faces does not expose them to counts");
            ctx.PlayersByPosition.Clear(); ctx.PlayersByPosition["self"] = other;
            Check(Remaining(ctx, empty, 19) == 0, "Own concealed kong is known");
            ctx.PlayersByPosition.Clear(); ctx.PlayersByPosition["right"] = other;
            other.CombinationTiles[0] = "g19";
            Check(Remaining(ctx, empty, 19) == 0, "Open kong still removes all four copies");
            other.CombinationTiles[0] = "G19";
            ctx.RoomRule = "riichi"; ctx.SubRule = "riichi/standard";
            Check(Remaining(ctx, empty, 19) == 0, "Riichi publicly identifiable concealed kong remains known");
            ctx.RoomRule = "guobiao"; ctx.SubRule = "guobiao/huahun";
            other.CombinationTiles[0] = "G51";
            other.CombinationMasks[0] = new[] {0,51,0,52,0,53,0,54};
            Check(new[] {51,52,53,54}.All(t => Remaining(ctx, empty, t) == 0), "Public flower kong counts each physical flower once");
            other.CombinationTiles.Clear(); other.CombinationMasks.Clear();
            other.PublicHandTiles = new List<int> {16,19,2019};
            ctx.SubRule = "guobiao/standard";
            Check(Remaining(ctx, new List<int> {19}, 19) == 3, "Ordinary replay ignores opponent hand reveal data");
            ctx.SubRule = GuobiaoGameState.TransparentSubRule;
            Check(Remaining(ctx, new List<int> {19}, 19) == 2, "Transparent rule counts only genuinely public glass copies");
            ctx.SubRule = "guobiao/standard";
            Check(Remaining(ctx, new List<int> {19}, 19, 19) == 2, "Discard preview counts the pending own discard once");
            return new { passed = checks.Count, checks = checks.ToArray() };
        } finally {
            registry.Clear(); foreach (var entry in saved) registry.Add(entry.Key, entry.Value);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
