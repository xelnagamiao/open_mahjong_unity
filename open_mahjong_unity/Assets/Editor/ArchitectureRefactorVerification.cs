using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 四大核心宿主重构全路径自动化验证套件（Unity Batchmode / Editor 可直接运行）。
/// 覆盖：静态架构约束、MainScene 序列化完整性、全规则注册与扩展生命周期、回放上下文与处理器、建房 Schema 与 BuildRoom 全路径。
/// </summary>
public static class ArchitectureRefactorVerification {
    private delegate void AssertFn(bool condition, string name, string detail = "");

    private static readonly string[] LifecycleNames = {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy"
    };

    [MenuItem("Tools/Verify/Run Architecture Refactor Full-Path Verification")]
    public static void RunFromMenu() {
        RunInternal(exitOnComplete: false);
    }

    public static void RunBatchmode() {
        RunInternal(exitOnComplete: true);
    }

    private static void RunInternal(bool exitOnComplete) {
        var failures = new List<string>();
        var passed = new List<string>();

        void Assert(bool condition, string name, string detail = "") {
            if (condition) {
                passed.Add(name);
            } else {
                string msg = string.IsNullOrEmpty(detail) ? name : $"{name}: {detail}";
                failures.Add(msg);
                Debug.LogError("[ARCH-VERIFY FAIL] " + msg);
            }
        }

        try {
            VerifyStaticLifecycleAndPiggybackRules(Assert);
            VerifyMainSceneSerializationAndHostInterfaces(Assert);
            VerifyAllRulesRegistryAndExtensions(Assert);
            VerifyReplayContextAndHandlers(Assert);
            VerifyRoomOptionFormAndAllBuilders(Assert);
        } catch (Exception ex) {
            failures.Add("Unhandled exception: " + ex);
            Debug.LogException(ex);
        }

        Debug.Log($"[ARCH-VERIFY SUMMARY] Passed={passed.Count}, Failed={failures.Count}");
        if (failures.Count > 0) {
            foreach (string f in failures) Debug.LogError("  FAIL -> " + f);
            if (exitOnComplete) EditorApplication.Exit(1);
            return;
        }

        Debug.Log("[ARCH-VERIFY] ALL PATHS PASSED!");
        if (exitOnComplete) EditorApplication.Exit(0);
    }

    private static void VerifyStaticLifecycleAndPiggybackRules(AssertFn assert) {
        string scriptsRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "Scripts"));
        string[] csFiles = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories);

        var targets = new Dictionary<string, string> {
            { "GameCanvas", "GameCanvas.cs" },
            { "Game3DManager", "Game3DManager.cs" },
            { "GameRecordManager", "GameRecordManager.cs" },
            { "CreatePanel", "CreatePanel.cs" },
        };

        foreach (var kv in targets) {
            string className = kv.Key;
            string primaryFile = kv.Value;
            var partialDeclRegex = new Regex($@"(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed|abstract)?\s*partial\s+class\s+{className}\b");
            var leakedLifecycles = new List<string>();

            foreach (string file in csFiles) {
                string raw = File.ReadAllText(file);
                Match m = partialDeclRegex.Match(raw);
                if (!m.Success) continue;
                string fileName = Path.GetFileName(file);
                if (string.Equals(fileName, primaryFile, StringComparison.OrdinalIgnoreCase)) continue;

                // 精确提取该 partial class 的大括号类体，避免误匹配同文件中后续其他独立类
                string body = ExtractClassBody(raw, m.Index);
                foreach (string lc in LifecycleNames) {
                    if (Regex.IsMatch(body, $@"(?m)^\s*(?:public|internal|private|protected)?\s*void\s+{lc}\s*\(")) {
                        leakedLifecycles.Add($"{fileName}::{lc}");
                    }
                }
            }

            assert(leakedLifecycles.Count == 0,
                $"StaticLifecycle[{className}]",
                "Leaked Unity lifecycle methods outside primary file: " + string.Join(", ", leakedLifecycles));
        }

        // GameCanvas 跨文件注入必须归零（只匹配真实类声明行，排除注释）
        var canvasDeclRegex = new Regex(@"(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed|abstract)?\s*partial\s+class\s+GameCanvas\b");
        var canvasPiggybacks = csFiles.Where(f => {
            string name = Path.GetFileNameWithoutExtension(f);
            if (name == "GameCanvas" || name.StartsWith("GameCanvas.") || name.StartsWith("GameCanvas_")) return false;
            return canvasDeclRegex.IsMatch(File.ReadAllText(f));
        }).Select(Path.GetFileName).ToList();

        assert(canvasPiggybacks.Count == 0,
            "StaticPiggyback[GameCanvas]",
            "Unexpected piggyback partial files: " + string.Join(", ", canvasPiggybacks));

        // ZhongyongReplay 必须彻底脱离 partial class GameRecordManager
        string zhongyongReplay = Path.Combine(scriptsRoot, "GameScene", "Rules", "Zhongyong", "ZhongyongReplay.cs");
        var recordDeclRegex = new Regex(@"(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed|abstract)?\s*partial\s+class\s+GameRecordManager\b");
        assert(File.Exists(zhongyongReplay) && !recordDeclRegex.IsMatch(File.ReadAllText(zhongyongReplay)),
            "StaticDetachedReplay[Zhongyong]",
            "ZhongyongReplay.cs still uses partial class GameRecordManager");
    }

    private static string ExtractClassBody(string source, int declStart) {
        int open = source.IndexOf('{', declStart);
        if (open < 0) return string.Empty;
        int depth = 0;
        for (int i = open; i < source.Length; i++) {
            char c = source[i];
            if (c == '{') depth++;
            else if (c == '}') {
                depth--;
                if (depth == 0) return source.Substring(open + 1, i - open - 1);
            }
        }
        return source.Substring(open + 1);
    }

    private static void VerifyMainSceneSerializationAndHostInterfaces(AssertFn assert) {
        string scenePath = "Assets/Scenes/MainScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        assert(scene.IsValid(), "MainScene.Open", "Failed to open MainScene.unity");

        var roots = scene.GetRootGameObjects();
        int missingScripts = 0;
        foreach (var root in roots) {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            }
        }
        assert(missingScripts == 0, "MainScene.ZeroMissingScripts", $"Found {missingScripts} missing scripts");

        var gameCanvas = UnityEngine.Object.FindFirstObjectByType<GameCanvas>(FindObjectsInactive.Include);
        var game3D = UnityEngine.Object.FindFirstObjectByType<Game3DManager>(FindObjectsInactive.Include);
        var gameRecord = UnityEngine.Object.FindFirstObjectByType<GameRecordManager>(FindObjectsInactive.Include);
        var createPanel = UnityEngine.Object.FindFirstObjectByType<CreatePanel>(FindObjectsInactive.Include);

        assert(gameCanvas != null && gameCanvas is ITableHudHost, "MainScene.GameCanvasHost");
        assert(game3D != null && game3D is ITable3DHost, "MainScene.Game3DManagerHost");
        assert(gameRecord != null && gameRecord is IRecordReplayContext, "MainScene.GameRecordManagerHost");
        assert(createPanel != null && createPanel.OptionFormRenderer != null, "MainScene.CreatePanelHost");

        if (gameCanvas != null) {
            assert(gameCanvas.PlayerSelfPanel != null && gameCanvas.PlayerLeftPanel != null
                && gameCanvas.PlayerTopPanel != null && gameCanvas.PlayerRightPanel != null,
                "MainScene.GameCanvas.PlayerPanels");
            assert(gameCanvas.HandCardsContainer != null && gameCanvas.ActionButtonContainer != null,
                "MainScene.GameCanvas.CoreContainers");
            assert(gameCanvas.GetPlayerPanel("self") == gameCanvas.PlayerSelfPanel
                && gameCanvas.GetPlayerPanel("top") == gameCanvas.PlayerTopPanel,
                "MainScene.GameCanvas.GetPlayerPanel");
        }

        if (game3D != null) {
            assert(game3D.GetPosPanel("self") != null && game3D.GetPosPanel("right") != null
                && game3D.GetPosPanel("top") != null && game3D.GetPosPanel("left") != null,
                "MainScene.Game3DManager.PosPanels");
        }
    }

    private static void EnsureRuleBootstrapsRegistered() {
        // EditMode 下 RuntimeInitializeOnLoadMethod 不会自动触发，这里通过反射安全触发一次所有 RuleBootstrap.Register
        if (RuleRegistry.All.Any()) return;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types) {
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                    if (m.GetParameters().Length != 0) continue;
                    var attr = m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (attr != null && attr.loadType == RuntimeInitializeLoadType.SubsystemRegistration
                        && t.Name.EndsWith("RuleBootstrap", StringComparison.Ordinal)) {
                        try { m.Invoke(null, null); } catch (Exception e) { Debug.LogWarning($"Bootstrap {t.Name}.{m.Name}: {e.Message}"); }
                    }
                }
            }
        }
    }

    private static void VerifyAllRulesRegistryAndExtensions(AssertFn assert) {
        EnsureRuleBootstrapsRegistered();
        var manifests = RuleRegistry.Ordered;
        assert(manifests.Count >= 20, "RuleRegistry.Count", $"Expected >= 20 rules, got {manifests.Count}");

        var gameCanvas = UnityEngine.Object.FindFirstObjectByType<GameCanvas>(FindObjectsInactive.Include);
        var game3D = UnityEngine.Object.FindFirstObjectByType<Game3DManager>(FindObjectsInactive.Include);

        int testedCombinations = 0;
        foreach (var manifest in manifests) {
            var subRules = manifest.LobbySubRules != null && manifest.LobbySubRules.Length > 0
                ? manifest.LobbySubRules.Select(s => s.Key).ToArray()
                : new[] { manifest.DefaultSubRule };

            foreach (string subRule in subRules) {
                RuleRegistry.SetCurrent(manifest.RuleId, subRule);
                gameCanvas?.SyncRuleCanvasExtension();
                game3D?.SyncRule3DExtension();
                testedCombinations++;
            }
        }

        // 测试自定义 Extension 完整 Attach -> OnHostEnabled -> OnLateTick -> OnHostDisabled -> Dispose 路径
        bool attached = false, ticked = false, disabled = false, disposed = false;
        var probeManifest = new RuleManifest {
            RuleId = "__verify_probe__",
            DefaultSubRule = "__verify_probe__/default",
            CreateCanvasExtension = _ => new ProbeCanvasExt(
                () => attached = true, () => ticked = true, () => disabled = true, () => disposed = true),
            Create3DExtension = _ => new Probe3DExt(),
        };
        RuleRegistry.Register(probeManifest);
        RuleRegistry.SetCurrent("__verify_probe__", "__verify_probe__/default");
        gameCanvas.SyncRuleCanvasExtension();
        game3D.SyncRule3DExtension();
        gameCanvas.ActiveRuleExtension?.OnLateTick();
        gameCanvas.ActiveRuleExtension?.OnHostDisabled();
        RuleRegistry.ClearCurrent();
        gameCanvas.SyncRuleCanvasExtension();
        game3D.SyncRule3DExtension();

        assert(testedCombinations >= 20, "RuleRegistry.AllSubRulesSwitched", $"Switched {testedCombinations}");
        assert(attached && ticked && disabled && disposed, "ExtensionLifecycle.FullCycle",
            $"attached={attached}, ticked={ticked}, disabled={disabled}, disposed={disposed}");
    }

    private static void VerifyReplayContextAndHandlers(AssertFn assert) {
        var gameRecord = UnityEngine.Object.FindFirstObjectByType<GameRecordManager>(FindObjectsInactive.Include);
        IRecordReplayContext ctx = gameRecord;
        assert(ctx != null, "ReplayContext.Available");

        // 验证独立 ZhongyongReplayHandler 在标准规(14死墙)与南雀(0死墙)下的路径
        var stdCtx = new StubReplayContext("zhongyong", "zhongyong/standard");
        using (var h1 = new ZhongyongReplayHandler(stdCtx)) {
            var mod1 = h1.BuildModule();
            assert(mod1 != null && mod1.ReservedWallTiles == 14, "ZhongyongReplay.StandardDeadWall", $"got {mod1?.ReservedWallTiles}");
        }
        var nqCtx = new StubReplayContext("zhongyong", "zhongyong/nanque");
        using (var h2 = new ZhongyongReplayHandler(nqCtx)) {
            var mod2 = h2.BuildModule();
            assert(mod2 != null && mod2.ReservedWallTiles == 0, "ZhongyongReplay.NanqueDeadWall", $"got {mod2?.ReservedWallTiles}");
        }

        // 验证所有已注册规则的 ReplayFactory / CreateReplayHandler 均可创建且不抛异常
        int verifiedHandlers = 0;
        foreach (var manifest in RuleRegistry.Ordered) {
            if (manifest.CreateReplayHandler != null) {
                using (var handler = manifest.CreateReplayHandler(new StubReplayContext(manifest.RuleId, manifest.DefaultSubRule))) {
                    var mod = handler?.BuildModule();
                    assert(mod != null, $"ReplayHandler[{manifest.RuleId}]");
                    verifiedHandlers++;
                }
            } else if (manifest.ReplayFactory != null && gameRecord != null) {
                var mod = manifest.ReplayFactory(gameRecord);
                assert(mod != null, $"ReplayFactory[{manifest.RuleId}]");
                verifiedHandlers++;
            }
        }
        assert(verifiedHandlers >= 20, "Replay.AllRegisteredRulesCovered", $"Covered {verifiedHandlers} rules");
    }

    private static void VerifyRoomOptionFormAndAllBuilders(AssertFn assert) {
        var renderer = new RoomOptionFormRenderer();
        var schemaManifest = new RuleManifest {
            RuleId = "__schema_probe__",
            DefaultSubRule = "__schema_probe__/a",
            RoomOptionSchema = new RoomOptionField[] {
                new ToggleOptionField { Key = "enable_extra", Label = "Extra", DefaultValue = false },
                new DropdownOptionField {
                    Key = "extra_mode",
                    Label = "Mode",
                    DefaultValue = 2,
                    OptionLabels = new[] { "A", "B" },
                    OptionValues = new object[] { 1, 2 },
                    VisibleWhen = draft => draft.GetBool("enable_extra"),
                },
            },
        };
        renderer.BindRule(schemaManifest, "__schema_probe__/a");
        assert(renderer.EvaluateVisibleFields().Count == 1, "RoomOptionForm.InitialVisibility");
        renderer.SetValue("enable_extra", true);
        assert(renderer.EvaluateVisibleFields().Count == 2, "RoomOptionForm.DynamicVisibility");
        var exported = new Dictionary<string, object>();
        renderer.ExportVisibleValues(exported);
        assert(exported.ContainsKey("extra_mode") && Convert.ToInt32(exported["extra_mode"]) == 2, "RoomOptionForm.ExportValues");

        // 遍历所有大厅规则，验证 RoomOptionFormRenderer.BindRule 均无异常
        int boundRules = 0;
        foreach (var manifest in RuleRegistry.Ordered) {
            if (manifest.HideFromLobby) continue;
            renderer.BindRule(manifest, manifest.DefaultSubRule);
            boundRules++;
        }
        assert(boundRules >= 15, "RoomOptionForm.AllLobbyRulesBound", $"Bound {boundRules} lobby rules");
    }

    private sealed class ProbeCanvasExt : RuleCanvasExtensionBase {
        private readonly Action _onAttach, _onTick, _onDisable, _onDispose;
        public ProbeCanvasExt(Action a, Action t, Action d, Action disp) { _onAttach = a; _onTick = t; _onDisable = d; _onDispose = disp; }
        public override void Attach(ITableHudHost host) { base.Attach(host); _onAttach(); }
        public override void OnLateTick() => _onTick();
        public override void OnHostDisabled() => _onDisable();
        public override void Dispose() { _onDispose(); base.Dispose(); }
    }

    private sealed class Probe3DExt : Rule3DExtensionBase { }

    private sealed class StubReplayContext : IRecordReplayContext {
        public StubReplayContext(string ruleId, string subRule) { RuleId = ruleId; SubRule = subRule; }
        public GameRecord Record => null;
        public int CurrentRoundIndex => 1;
        public int CurrentNode => 0;
        public string RuleId { get; }
        public string SubRule { get; }
        public bool IsSpectating => false;
        public int SelectedPlayerIndex => 0;
        public int SelectedPlayerOriginalIndex => 0;
        public IReadOnlyDictionary<int, string> IndexToPosition => new Dictionary<int, string>();
        public IReadOnlyList<int> CurrentWallTiles => Array.Empty<int>();
        public IReadOnlyList<int> CurrentWallOriginalIndices => Array.Empty<int>();
        public int DeadWallCount { get; set; }
        public string DeadWallMode { get; set; }
        public int ReadTitleInt(string key, int fallback = 0) => fallback;
        public string ReadTitleString(string key, string fallback = "") => key == "sub_rule" ? SubRule : key == "rule" ? RuleId : fallback;
        public bool ReadTitleBool(string key, bool fallback = false) => fallback;
        public int ReadRoundInt(string key, int fallback = 0) => fallback;
        public string ReadRoundString(string key, string fallback = "") => fallback;
        public bool TryGetCurrentRound(out Round round) { round = null; return false; }
        public bool TryConsumeWallFront(out int tileId) { tileId = 0; return false; }
        public bool TryConsumeWallTail(out int tileId) { tileId = 0; return false; }
        public void SetPlayerTag(int playerIndex, string tag) { }
        public void RemovePlayerTag(int playerIndex, string tag) { }
        public void SetPlayerVariable(int playerIndex, string key, string value) { }
    }
}
