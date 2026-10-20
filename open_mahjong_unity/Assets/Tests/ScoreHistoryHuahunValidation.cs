#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

/// <summary>花混计分板保留真正主番及四季癞子，覆盖实际牌张组件和牌谱提取。</summary>
public static class ScoreHistoryHuahunValidation {
    public static object Run(GameObject cardPrefab) {
        var checks = new List<string>();
        void Check(bool ok, string label) {
            if (!ok) throw new InvalidOperationException("Huahun scoreboard: " + label);
            checks.Add(label);
        }
        const string sub = GuobiaoGameState.HuahunSubRule;
        typeof(GuobiaoRuleBootstrap).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        string Pick(params string[] fans) => ScoreHistorySettlementHelper.PickMainFanName(sub, fans);
        foreach (string[] fans in new[] {
            new[] {"门前清", "清一色", "无癞子翻倍*40"},
            new[] {"无癞子翻倍*40", "清一色", "门前清"},
            new[] {"门前清", "无癞子翻倍*40", "清一色"},
        }) {
            var copy = (string[])fans.Clone();
            var snapshot = new RoundSettlementSnapshot {huFan = fans};
            string details = ScoreHistorySettlementHelper.BuildAllFansText(sub, snapshot);
            Check(ScoreHistorySettlementHelper.GetMainFanColumnLabel(sub, snapshot) == "清一色",
                "doubling never replaces the largest real fan: " + string.Join(",", fans));
            Check(fans.SequenceEqual(copy) && ScoreHistorySettlementHelper.BuildAllFansText(sub, snapshot) == details,
                "picking preserves the complete fan list and its order");
        }
        Check(FanTextDictionary.GetFanDisplayText(sub, "无癞子翻倍*40") == "40番", "doubling detail retains its value");
        Check(ScoreHistorySettlementHelper.BuildAllFansText(sub,
            new RoundSettlementSnapshot {huFan = new[] {"无癞子翻倍*40"}}).Contains("40番"), "doubling remains visible in details");
        Check(Pick("无癞子翻倍", "无癞子翻倍*40") == "", "bonus alone cannot become a main fan");
        Check(Pick("无癞子翻倍*100", "错和", "清一色") == "错和", "false-win priority is preserved");
        Check(Pick("花牌*8", "门前清") == "门前清", "ordinary flower bonus stays excluded");
        Check(Pick("门前清", "一般高*4") == "一般高", "repeated fan ranking is preserved");
        Check(Pick("清一色", "花杠", "无癞子翻倍*100") == "花杠", "flower kong remains a real fan");

        Check(cardPrefab != null && cardPrefab.GetComponent<StaticCard>() != null, "actual settlement tile prefab supplied");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Huahun scoreboard validation");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try {
            int[] Render(RoundSettlementSnapshot snapshot) {
                string before = JsonConvert.SerializeObject(snapshot);
                var fixture = new GameObject("Tooltip", typeof(RectTransform));
                fixture.transform.SetParent(root.transform, false);
                try {
                    var tooltip = fixture.AddComponent<ScoreHistoryFanTooltip>();
                    var row = new GameObject("Tiles", typeof(RectTransform));
                    row.transform.SetParent(fixture.transform, false);
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(ScoreHistoryFanTooltip).GetField("staticCardPrefab", flags).SetValue(tooltip, cardPrefab);
                    typeof(ScoreHistoryFanTooltip).GetField("tilesRoot", flags).SetValue(tooltip, row.transform);
                    typeof(ScoreHistoryFanTooltip).GetMethod("PopulateTiles", flags).Invoke(tooltip, new object[] {snapshot});
                    var cards = row.GetComponentsInChildren<StaticCard>(true);
                    Check(cards.All(card => card.GetComponentsInChildren<Image>(true).Any(image => image.sprite != null)),
                        snapshot.subRule + " preview tiles have visible sprites");
                    Check(JsonConvert.SerializeObject(snapshot) == before, "preview preserves original hand, fans and melds");
                    return cards.Select(card => card.TileId).ToArray();
                } finally { UnityEngine.Object.DestroyImmediate(fixture); }
            }
            RoundSettlementSnapshot Snapshot(string rule, int[] hand, int[] jokers) => new RoundSettlementSnapshot {
                subRule = rule, hasWin = true, huFan = new[] {"门前清"}, hepaiPlayerHand = hand, handJokerTiles = jokers,
            };
            int[] handWithFlowers = {11,12,13,21,22,23,31,32,33,51,52,53,54,55,56,57,58,41};
            int[] jokers = {51,52,53,54};
            int[] expected = {51,52,53,54,11,12,13,21,22,23,31,32,33,41};
            var live = Snapshot(sub, handWithFlowers, jokers);
            int[] visible = Render(live);
            Check(visible.Length == 14 && visible.OrderBy(t => t).SequenceEqual(expected.OrderBy(t => t)),
                "all four concealed season jokers render; ordinary flowers are filtered");
            Check(visible.Take(4).All(jokers.Contains) && visible.Last() == 41, "jokers sort first and winning tile stays last");
            Check(Render(Snapshot("guobiao/standard", handWithFlowers, null)).SequenceEqual(new[] {11,12,13,21,22,23,31,32,33,41}),
                "ordinary Guobiao still filters all eight flower IDs");
            Check(Render(Snapshot(sub, new[] {11,51,52,53,54}, jokers)).Last() == 54, "a winning joker stays in the winning slot");
            Check(Render(Snapshot(GuangdongMilRules.SubRule, handWithFlowers, null)).Length == handWithFlowers.Length,
                "Guangdong's existing flower hand handling is preserved");
            var kong = Snapshot(sub, new[] {11,11}, jokers);
            kong.combinationMask = new[] {HuahunRules.ReplayKongMask(sub, jokers)};
            Check(Render(kong).SequenceEqual(new[] {11,51,52,53,54,11}), "flower kong remains exposed between hand and winning tile");

            var record = new GameRecord {gameTitle = new Dictionary<string, object> { ["rule"] = "guobiao", ["sub_rule"] = sub }};
            record.gameRound.rounds[1] = new Round {roundIndex = 1, currentRound = 1, seats = new List<int> {0,1,2,3},
                p0Tiles = expected.Take(expected.Length - 1).ToList(), actionTicks = new List<List<string>> {
                    new List<string> {"hu_first", "0", "26", "[\"门前清\",\"清一色\"]", "[24,-8,-8,-8]", "41"},
                }};
            string original = JsonConvert.SerializeObject(record);
            var replay = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record).Single().snapshot;
            Check(replay.handJokerTiles.SequenceEqual(jokers), "replay extraction captures Huahun joker identities");
            int[] replayTiles = Render(replay);
            Check(replayTiles.Length == 14 && replayTiles.OrderBy(t => t).SequenceEqual(expected.OrderBy(t => t)) && replayTiles.Last() == 41,
                "replay scoreboard shows the complete winning hand including all four jokers");
            Check(JsonConvert.SerializeObject(record) == original, "existing records are read without mutation or migration");
        } finally {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
        return new {count = checks.Count, checks};
    }
}
#endif
