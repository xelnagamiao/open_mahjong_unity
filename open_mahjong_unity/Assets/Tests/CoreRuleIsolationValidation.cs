#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 架构边界守卫 + 全路径回归验证：
/// 1) 静态扫描核心 UI / 牌谱解码 / 计分板提取文件，确保零规则名硬编码泄漏；
/// 2) 覆盖 ActionButton/KongOptions、TipsContainer 契约、GameRecordJsonDecoder、
///    ScoreHistoryRecordSettlementExtractor 以及全部牌谱 Fixture 与规则 ReplayModule。
/// </summary>
public static class CoreRuleIsolationValidation {
    private static readonly string[] GuardedCoreFiles = {
        "Assets/Scripts/GameScene/CanvasManager/CanvasControl/ActionButton.cs",
        "Assets/Scripts/GameScene/GameSceneUI/TipsContainer.cs",
        "Assets/Scripts/GameScene/GameSceneUI/TipsBlock.cs",
        "Assets/Scripts/GameScene/Record/GameRecordJsonDecoder.cs",
        "Assets/Scripts/GameScene/GameSceneUI/ScoreHistoryRecordSettlementExtractor.cs",
    };

    private static readonly Regex ForbiddenRuleBranchRegex = new Regex(
        @"(RuleId\s*[!=]=\s*""|roomRule\s*[!=]=\s*""|subRule\s*[!=]=\s*""|(Guobiao|Sichuan|Duocai|Wenzhou|Riichi|Free|Hongque|Beijing|Changsha|Guangdong|Shangrao|Nanchang|Fuzhou|Nanjing|Malaysia|Yixing|HongKong|Singapore|Shenyang|Changchun|Hongzhong|Vietnamese)GameState\.|GuangdongMilRules\.|HuahunRules\.|FourJokersTips\.|DuocaiHintPresentation\.)",
        RegexOptions.Compiled);

    public static object Run() {
        var checks = new List<string>();
        void Check(string name, bool condition) {
            if (!condition) throw new InvalidOperationException("CoreRuleIsolationValidation FAILED: " + name);
            checks.Add(name);
        }

        // 0. 确保所有规则 Bootstrap 已注册
        RuleRecordCodecRegistry.EnsureInitialized();

        // 1. 静态边界守卫：核心文件零规则硬编码
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (string relPath in GuardedCoreFiles) {
            string fullPath = Path.Combine(projectRoot, relPath);
            Check("guarded file exists: " + relPath, File.Exists(fullPath));
            string[] lines = File.ReadAllLines(fullPath);
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///")) continue;
                Match m = ForbiddenRuleBranchRegex.Match(trimmed);
                Check($"{relPath}:{i + 1} no rule leak ({m.Value})", !m.Success);
            }
        }

        // 2. ActionButton & GameState 契约验证
        var freeManifest = RuleRegistry.Resolve("free");
        Check("free SkipActionCandidateExpansion", freeManifest != null && freeManifest.SkipActionCandidateExpansion);

        var gbState = new GuobiaoGameState();
        GameSession.Current.SubRule = GuobiaoGameState.HuahunSubRule;
        var flowerHand = new List<int> { 51, 52, 53, 54, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41 };
        Check("huahun TryCollectKongOptions handles angang",
            gbState.TryCollectKongOptions("angang", flowerHand, new List<string>(), out var huahunOpts)
            && huahunOpts != null && huahunOpts.Any(o => o.targetTile == 51 && o.displayTiles.SequenceEqual(new[] { 51, 52, 53, 54 })));

        GameSession.Current.SubRule = "guobiao/standard";
        Check("guobiao/standard TryCollectKongOptions falls back",
            !gbState.TryCollectKongOptions("angang", flowerHand, new List<string>(), out _));

        // 3. TipsContainer / RuleManifest 提示契约验证
        var gbManifest = RuleRegistry.Resolve("guobiao");
        Check("guobiao DeferWaitingTileHints", gbManifest.DeferWaitingTileHints);
        Check("guobiao DeferInvisibleHintCells", gbManifest.DeferInvisibleHintCells != null
            && gbManifest.DeferInvisibleHintCells(GuobiaoGameState.HuahunSubRule)
            && !gbManifest.DeferInvisibleHintCells("guobiao/standard"));
        Check("guobiao TryDescribeWaitingTileCached", gbManifest.TryDescribeWaitingTileCached != null);
        Check("guobiao DescribeWaitingTileAsync", gbManifest.DescribeWaitingTileAsync != null);
        Check("guobiao TileCopiesForSubRule", gbManifest.TileCopiesForSubRule != null
            && gbManifest.TileCopiesForSubRule(GuobiaoGameState.HuahunSubRule, 51) == 1
            && gbManifest.TileCopiesForSubRule("guobiao/standard", 11) == 4);
        Check("guobiao FullHandTileCount", gbManifest.FullHandTileCount != null
            && gbManifest.FullHandTileCount(GuobiaoGameState.SixteenSubRule) == 17
            && gbManifest.FullHandTileCount("guobiao/standard") == 14);

        var duocaiManifest = RuleRegistry.Resolve("duocai");
        Check("duocai deferred & display hooks",
            duocaiManifest.DeferWaitingTileHints && duocaiManifest.RetryNullDeferredHint
            && duocaiManifest.SelectDisplayWaitingTiles != null && duocaiManifest.ResolveWaitingVariants != null
            && duocaiManifest.CollectLivePlayerVisibleTiles != null);

        var beijingManifest = RuleRegistry.Resolve("beijing");
        Check("beijing deferred & retry hooks",
            beijingManifest.DeferWaitingTileHints && beijingManifest.RetryNullDeferredHint);

        var gdManifest = RuleRegistry.Resolve("guangdong");
        Check("guangdong HintCellWidth & TileCopiesForSubRule",
            gdManifest.HintCellWidth != null
            && Mathf.Approximately(gdManifest.HintCellWidth(GuangdongMilRules.SubRule), 100f)
            && Mathf.Approximately(gdManifest.HintCellWidth("guangdong/tuidao_mil2024"), 84f)
            && gdManifest.TileCopiesForSubRule != null
            && gdManifest.TileCopiesForSubRule(GuangdongMilRules.SubRule, 55) == 1
            && gdManifest.TileCopiesForSubRule("guangdong/tuidao_mil2024", 11) == 4);

        var wzManifest = RuleRegistry.Resolve("wenzhou");
        Check("wenzhou visible indicators & melds", wzManifest.LiveVisibleIndicators != null && wzManifest.CollectLivePlayerVisibleTiles != null);
        var riichiManifest = RuleRegistry.Resolve("riichi");
        Check("riichi visible indicators & melds", riichiManifest.LiveVisibleIndicators != null && riichiManifest.CollectLivePlayerVisibleTiles != null);
        var vnManifest = RuleRegistry.Resolve("vietnamese");
        Check("vietnamese visible melds", vnManifest.CollectLivePlayerVisibleTiles != null);

        // 4. RuleRecordCodecRegistry 解码与分值累加全规则路径覆盖
        var round = new Round { seats = new List<int> { 0, 1, 2, 3 } };
        var headerJson = JObject.Parse(@"{
            ""round_index"": 1, ""current_round"": 1, ""seats"": [0,1,2,3],
            ""dealer_index"": 0, ""start_player_index"": 0,
            ""riichi"": {""honba"": 2, ""riichi_sticks"": 1, ""dora_marker"": 11},
            ""wenzhou"": {""start_scores"": [100,100,100,100], ""caishen"": 11},
            ""nanchang"": {""start_scores"": [10,10,10,10]},
            ""shangrao"": {""start_scores"": [20,20,20,20]},
            ""beijing"": {""start_scores"": [30,30,30,30]},
            ""duocai"": {""start_scores"": [40,40,40,40]},
            ""nanjing"": {""start_scores"": [50,50,50,50]},
            ""dingque_suits"": {""0"": 1, ""1"": 2, ""2"": 3, ""3"": 1}
        }");
        GameRecordJsonDecoder.ApplyRoundHeader(round, headerJson, 1, 4);
        Check("ApplyRoundHeader decoded riichi", round.riichi != null && round.riichi.honba == 2 && round.riichi.riichiSticks == 1);
        Check("ApplyRoundHeader decoded wenzhou", round.wenzhou != null && round.wenzhou.start_scores[0] == 100);
        Check("ApplyRoundHeader decoded nanchang/shangrao/beijing/duocai/nanjing",
            round.nanchang != null && round.shangrao != null && round.beijing != null && round.duocai != null && round.nanjing != null);
        Check("ApplyRoundHeader decoded dingque_suits", round.dingqueSuits != null && round.dingqueSuits[1] == 2);

        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round, new List<string> { "wenzhou", "state", "{}", "[112,96,96,96]" });
        Check("wenzhou state score diff", round.scoreChanges != null && round.scoreChanges.SequenceEqual(new[] { 12, -4, -4, -4 }));

        var gdRound = new Round { seats = new List<int> { 0, 1, 2, 3 } };
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(gdRound, new List<string> { "guangdong", "kong_score", "[3,-1,-1,-1]" });
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(gdRound, new List<string> { "guangdong", "refund_kongs", "[-3,1,1,1]" });
        Check("guangdong kong+refund nets to 0", gdRound.scoreChanges != null && gdRound.scoreChanges.SequenceEqual(new[] { 0, 0, 0, 0 }));

        var riichiRound = new Round { seats = new List<int> { 0, 1, 2, 3 } };
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(riichiRound, new List<string> { "riichi", "0" });
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(riichiRound, new List<string> { "hu_riichi", "0", "hu_self", "1", "30", "[]", "[4000,-1000,-1000,-1000]" });
        Check("riichi + hu_riichi score accumulation", riichiRound.scoreChanges != null && riichiRound.scoreChanges.SequenceEqual(new[] { 3000, -1000, -1000, -1000 }));

        var shRound = new Round { seats = new List<int> { 0, 1, 2, 3 } };
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(shRound, new List<string> { "lajihe_eight_flowers", "1", "30", "[]", "[-30,90,-30,-30]" });
        Check("lajihe_eight_flowers score accumulation", shRound.scoreChanges != null && shRound.scoreChanges.SequenceEqual(new[] { -30, 90, -30, -30 }));

        // 5. 遍历 Assets/Tests/Fixtures 下全部牌谱 JSON，全量跑通解码 + 计分板提取
        string fixturesDir = Path.Combine(Application.dataPath, "Tests", "Fixtures");
        int replayFixturesTested = 0;
        if (Directory.Exists(fixturesDir)) {
            foreach (string file in Directory.GetFiles(fixturesDir, "*Replay*.json", SearchOption.TopDirectoryOnly)) {
                string json = File.ReadAllText(file);
                JObject root = JObject.Parse(json);
                JToken recordToken = root["game_round"] != null ? root : root["record"];
                if (recordToken?["game_round"] == null) continue;
                GameRecord rec = GameRecordJsonDecoder.ParseGameRecord(recordToken.ToString());
                var rows = ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(rec, includeUnsettledRounds: true);
                Check($"fixture {Path.GetFileName(file)} decoded & extracted ({rows.Count} rows)",
                    rec?.gameRound?.rounds != null && rec.gameRound.rounds.Count > 0 && rows != null);
                replayFixturesTested++;
            }
        }
        Check("at least 1 replay fixture tested", replayFixturesTested > 0);

        Debug.Log($"[CoreRuleIsolationValidation] PASS ({checks.Count} checks, {replayFixturesTested} replay fixtures)");
        return new { passed = true, checks = checks.Count, replayFixturesTested };
    }

    /// <summary>Unity batchmode 批量入口：打开 MainScene 后运行架构守卫 + 全量 EditMode 回归测试集。</summary>
    public static void RunBatchSuite() {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Single);
        var failures = new List<string>();
        void RunOne(string name, Action action) {
            try {
                RuleRegistry.ClearCurrent();
                action();
                Debug.Log($"[BatchSuite] PASS: {name}");
            } catch (Exception e) {
                Debug.LogError($"[BatchSuite] FAIL: {name}: {e}");
                failures.Add($"{name}: {e.Message}");
            }
        }

        RunOne("CoreRuleIsolationValidation", () => Run());
        RunOne("CountTipsValidation", () => CountTipsValidation.Run());
        RunOne("RiichiScoreHistoryValidation", () => RiichiScoreHistoryValidation.Run());
        RunOne("SichuanScoreHistoryValidation", () => SichuanScoreHistoryValidation.Run());
        RunOne("GuangdongClientValidation", () => GuangdongClientValidation.Run());
        RunOne("ChangchunClientValidation", () => ChangchunClientValidation.Run());
        RunOne("ShenyangClientValidation", () => ShenyangClientValidation.Run());
        RunOne("NanchangClientValidation", () => NanchangClientValidation.Run());
        RunOne("ShangraoClientValidation", () => ShangraoClientValidation.Run());
        RunOne("DuocaiHintsValidation", () => DuocaiHintsValidation.Run());
        RunOne("GuobiaoHuahunValidation", () => GuobiaoHuahunValidation.Run());
        RunOne("SingaporeValidation", () => SingaporeValidation.Run());
        RunOne("WuhanValidation", () => WuhanValidation.Run());
        RunOne("LajiheValidation", () => LajiheValidation.Run());

        // 其余全规则 & 回放 & 提示 & 按钮 EditMode 验证全量补跑
        RunOne("ActionButtonOrderValidation", () => ActionButtonOrderValidation.Run());
        RunOne("ActionChoicePanelValidation", () => ActionChoicePanelValidation.Run());
        RunOne("FilipinoValidation", () => FilipinoValidation.Run());
        RunOne("FilipinoReplayValidation", () => FilipinoReplayValidation.Run());
        RunOne("GuobiaoFourJokersValidation", () => GuobiaoFourJokersValidation.Run());
        RunOne("GuobiaoHintValidation", () => GuobiaoHintValidation.Run());
        RunOne("GuobiaoSixteenValidation", () => GuobiaoSixteenValidation.Run());
        RunOne("GuobiaoTransparentValidation", () => GuobiaoTransparentValidation.Run());
        RunOne("GuobiaoSanmaValidation", () => GuobiaoSanmaValidation.Run());
        RunOne("HangzhouClientContractValidation", () => HangzhouClientContractValidation.Run());
        RunOne("HepaiRiverSourceValidation", () => HepaiRiverSourceValidation.Run());
        RunOne("HongqueRecordTipsValidation", () => HongqueRecordTipsValidation.Run());
        RunOne("HongqueReplayLayoutValidation", () => HongqueReplayLayoutValidation.Run());
        RunOne("HongqueSessionValidation", () => HongqueSessionValidation.Run());
        RunOne("HongqueWinChoiceValidation", () => HongqueWinChoiceValidation.Run());
        RunOne("HongqueDisplayOrderValidation", () => HongqueDisplayOrderValidation.Run());
        RunOne("JiujiangValidation", () => JiujiangValidation.Run());
        RunOne("LocalHintValidation", () => LocalHintValidation.Run());
        RunOne("MalaysiaClaimOptionsValidation", () => MalaysiaClaimOptionsValidation.Run());
        RunOne("NanjingValidation", () => NanjingValidation.Run());
        RunOne("RecordRuleDisplayValidation", () => {
            var res = RecordRuleDisplayValidation.Run();
            if (res.failures != null && res.failures.Count > 0)
                throw new InvalidOperationException(string.Join("; ", res.failures));
        });
        RunOne("RecordRuleMetadataValidation", () => RecordRuleMetadataValidation.Run());
        RunOne("RecordTacticalClaimValidation", () => RecordTacticalClaimValidation.Run());
        RunOne("RecordWaitPerspectiveValidation", () => RecordWaitPerspectiveValidation.Run());
        RunOne("RecordWinningHandValidation", () => RecordWinningHandValidation.Run());
        RunOne("ReplayWallPolicyValidation", () => ReplayWallPolicyValidation.Run());
        RunOne("RiichiFieldStateValidation", () => RiichiFieldStateValidation.Run());
        RunOne("RiichiSanmaValidation", () => RiichiSanmaValidation.Run());
        RunOne("RiichiWinRequirementsValidation", () => RiichiWinRequirementsValidation.Run());
        RunOne("RonOnlyTipsValidation", () => RonOnlyTipsValidation.Run());
        RunOne("RuleActionVoiceValidation", () => RuleActionVoiceValidation.Run());
        RunOne("SbrExchangeValidation", () => SbrExchangeValidation.Run());
        RunOne("ScoreHistoryMainFanValidation", () => ScoreHistoryMainFanValidation.Run());
        RunOne("ScoreHistoryPresentationValidation", () => ScoreHistoryPresentationValidation.Run());
        RunOne("BackgroundResyncValidation", () => BackgroundResyncValidation.Run());
        RunOne("SettlementSyncValidation", () => SettlementSyncValidation.Run());
        RunOne("ShanghaiBloodflowTipsValidation", () => ShanghaiBloodflowTipsValidation.Run());
        RunOne("ShanghaiTipsValidation", () => ShanghaiTipsValidation.Run());
        RunOne("SichuanDoublesValidation", () => SichuanDoublesValidation.Run());
        RunOne("SichuanRoundScoreValidation", () => SichuanRoundScoreValidation.Run());
        RunOne("SichuanTipsValidation", () => SichuanTipsValidation.Run());
        RunOne("XueliuDoublesValidation", () => XueliuDoublesValidation.Run());
        RunOne("VietnameseValidation", () => VietnameseValidation.Run());
        RunOne("VietnameseReplayValidation", () => VietnameseReplayValidation.Run());
        RunOne("WashizuPresentationValidation", () => WashizuPresentationValidation.Run());
        RunOne("WashizuRealtimeValidation", () => WashizuRealtimeValidation.Run());
        RunOne("WashizuValidation", () => WashizuValidation.Run());

        if (failures.Count > 0) {
            throw new InvalidOperationException($"BatchSuite failed ({failures.Count}):\n" + string.Join("\n", failures));
        }
        Debug.Log("[BatchSuite] ALL PASSED!");
    }

    private const string PlayModeSmokeFlag = "CoreRuleIsolation_PlayModeSmoke";
    private const string PlayModeSmokeOutKey = "CoreRuleIsolation_PlayModeOutDir";
    private const string PlayModeSmokeStartedKey = "CoreRuleIsolation_PlayModeStarted";
    private const string PlayModeSmokeDeadlineKey = "CoreRuleIsolation_PlayModeDeadline";

    [InitializeOnLoadMethod]
    private static void ResumePlayModeSmokeAfterDomainReload() {
        if (!SessionState.GetBool(PlayModeSmokeFlag, false)) return;
        EditorApplication.update -= PollPlayModeSmoke;
        EditorApplication.update += PollPlayModeSmoke;
    }

    /// <summary>
    /// Batchmode Play Mode 冒烟验证入口（运行时不带 -quit，完成后自行调用 EditorApplication.Exit）：
    /// 打开 MainScene.unity → 进入 Play Mode → 运行 LocalHintRuntimeValidation（真实异步提示协程 + 虹雀提示渲染）
    /// 与全规则 ReplayFactory 实例化验证。
    /// </summary>
    public static void RunPlayModeSmoke() {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".om_workspace", "rule-isolation-test", "playmode"));
        Directory.CreateDirectory(outDir);
        string resultFile = Path.Combine(outDir, "unity-runtime-result.json");
        if (File.Exists(resultFile)) File.Delete(resultFile);

        SessionState.SetBool(PlayModeSmokeFlag, true);
        SessionState.SetString(PlayModeSmokeOutKey, outDir);
        SessionState.SetBool(PlayModeSmokeStartedKey, false);
        SessionState.SetFloat(PlayModeSmokeDeadlineKey, (float)(EditorApplication.timeSinceStartup + 120.0));

        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity",
            UnityEditor.SceneManagement.OpenSceneMode.Single);
        EditorApplication.update -= PollPlayModeSmoke;
        EditorApplication.update += PollPlayModeSmoke;
        EditorApplication.EnterPlaymode();
    }

    private static void FinishPlayModeSmoke(int exitCode) {
        SessionState.SetBool(PlayModeSmokeFlag, false);
        EditorApplication.update -= PollPlayModeSmoke;
        EditorApplication.Exit(exitCode);
    }

    private static void PollPlayModeSmoke() {
        try {
            float deadline = SessionState.GetFloat(PlayModeSmokeDeadlineKey, 0f);
            if (deadline > 0f && EditorApplication.timeSinceStartup > deadline) {
                Debug.LogError("[PlayModeSmoke] TIMEOUT");
                FinishPlayModeSmoke(1);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            string outDir = SessionState.GetString(PlayModeSmokeOutKey, "");
            if (!SessionState.GetBool(PlayModeSmokeStartedKey, false)) {
                SessionState.SetBool(PlayModeSmokeStartedKey, true);
                var grm = UnityEngine.Object.FindFirstObjectByType<GameRecordManager>(FindObjectsInactive.Include);
                if (grm == null) throw new InvalidOperationException("GameRecordManager not found in MainScene");
                int replayCount = 0;
                foreach (var m in RuleRegistry.All) {
                    if (m.ReplayFactory == null) continue;
                    var mod = m.ReplayFactory(grm);
                    if (mod == null) throw new InvalidOperationException($"ReplayFactory returned null for {m.RuleId}");
                    replayCount++;
                }
                Debug.Log($"[PlayModeSmoke] Verified {replayCount} RuleReplayModules in PlayMode MainScene");
                LocalHintRuntimeValidation.Begin(outDir);
                return;
            }
            string resultFile = Path.Combine(outDir, "unity-runtime-result.json");
            if (!File.Exists(resultFile)) return;
            string json = File.ReadAllText(resultFile);
            JObject obj = JObject.Parse(json);
            bool passed = obj["passed"]?.Value<bool>() == true;
            Debug.Log($"[PlayModeSmoke] LocalHintRuntimeValidation finished: passed={passed}\n{json}");
            FinishPlayModeSmoke(passed ? 0 : 1);
        } catch (Exception e) {
            Debug.LogError("[PlayModeSmoke] EXCEPTION: " + e);
            FinishPlayModeSmoke(1);
        }
    }
}
#endif
