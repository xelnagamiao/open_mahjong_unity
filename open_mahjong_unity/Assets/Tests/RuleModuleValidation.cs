#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Registration, form-to-request and shared replay dispatch regressions.</summary>
public static class RuleModuleValidation {
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Hidden).Invoke(target, args);

    private static void RegisterRules() {
        foreach (var type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
            type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
    }

    private static RoomCreationInput Input(RuleManifest manifest, string subRule) => new RoomCreationInput {
        Rule = manifest.RuleId, SubRule = subRule, RoomName = "模块回归", Password = "", RandomSeed = "",
        GameRound = 1, RoundTimer = 60, StepTimer = 10, StartingScoreText = "35000", HepaiLimitText = "8",
        Tips = true, CountTips = true, PointerTips = true, AllowSpectator = true,
        UseFlowers = true, GuangdongMinimumScore = true, OpenKongCount = 2, BirdCount = 2, SmallHuScore = 2, BigHuScore = 8,
        WallWan = true, WallTong = true, WallSuo = true, WallWinds = true, WallDragons = true, WallFlowers = true,
    };

    public static object RunRoomBuilders() {
        RegisterRules();
        int checks = 0, variants = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        foreach (var manifest in RuleRegistry.All) {
            Check(manifest.BuildRoom != null, manifest.RuleId + " room module registered");
            Check(manifest.ReplayFactory != null, manifest.RuleId + " replay module registered");
            if (manifest.HideFromLobby) {
                Check(!manifest.BuildRoom(Input(manifest, manifest.DefaultSubRule)).IsValid, "historical alias cannot create a new room");
                continue;
            }
            foreach (string subRule in (manifest.LobbySubRules ?? Array.Empty<RuleLobbySubRule>()).Select(s => s.Key).DefaultIfEmpty(manifest.DefaultSubRule)) {
                var input = Input(manifest, subRule);
                var request = manifest.BuildRoom(input);
                Check(request.IsValid, subRule + " valid defaults: " + request.Error);
                var config = JObject.FromObject(request.Configuration);
                Check((string)config["RoomName"] == input.RoomName, subRule + " room name preserved");
                if (config["SubRule"] != null) Check((string)config["SubRule"] == subRule, subRule + " identity preserved");
                input.RoomName = "";
                Check(!manifest.BuildRoom(input).IsValid, subRule + " rejects empty name");
                input.RoomName = "模块回归"; input.PasswordEnabled = true;
                Check(!manifest.BuildRoom(input).IsValid, subRule + " rejects empty enabled password");
                input.PasswordEnabled = false; input.RandomSeedEnabled = true; input.RandomSeed = "not-hex";
                Check(!manifest.BuildRoom(input).IsValid, subRule + " rejects invalid seed");
                variants++;
            }
        }
        var gb = RuleRegistry.Resolve("guobiao");
        var g = Input(gb, "guobiao/lanshi"); g.HepaiLimitEnabled = true; g.HepaiLimitText = "12";
        Check(((GB_Create_RoomConfig)gb.BuildRoom(g).Configuration).HepaiLimit == 5, "Lanshi fixed minimum survives migration");
        g.SubRule = GuobiaoGameState.BloodBattleSubRule;
        var blood = (GB_Create_RoomConfig)gb.BuildRoom(g).Configuration;
        Check(blood.HepaiLimit == 8 && !blood.CuoHe && !blood.TacticalCall, "GB blood battle forced options");
        g.SubRule = "guobiao/standard"; g.DuplicateEnabled = true;
        Check(!gb.BuildRoom(g).IsValid, "duplicate wall requires key");
        g.DuplicateEnabled = false; g.HepaiLimitVisible = true; g.HepaiLimitText = "0";
        Check(gb.BuildRoom(g).FocusHepaiLimit, "invalid visible minimum requests focus");
        var sc = RuleRegistry.Resolve("sichuan"); var s = Input(sc, "sichuan/xueliu");
        s.HepaiLimitEnabled = true; s.HepaiLimitVisible = true; s.HepaiLimitText = "0"; s.BloodBattle = true;
        var xueliu = sc.BuildRoom(s);
        Check(xueliu.IsValid && !((Sichuan_Create_RoomConfig)xueliu.Configuration).BloodBattle, "Xueliu accepts zero and overrides blood battle");
        var riichi = RuleRegistry.Resolve("riichi"); var r = Input(riichi, riichi.DefaultSubRule);
        foreach (var entry in new[] { (0, "multi_ron"), (1, "three_ron_abort"), (2, "head_bump") }) {
            r.HepaiWayIndex = entry.Item1;
            Check(((Riichi_Create_RoomConfig)riichi.BuildRoom(r).Configuration).HepaiWay == entry.Item2, "Riichi win policy " + entry.Item2);
        }
        r.StartingScoreText = "1001";
        Check(!riichi.BuildRoom(r).IsValid, "Riichi starting score validation retained");
        return new { passed = checks, variants };
    }

    public static object RunRoomForm() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        RegisterRules();
        var source = Resources.FindObjectsOfTypeAll<CreatePanel>().First(p => p.gameObject.scene.IsValid()
            && !EditorSceneManager.IsPreviewScene(p.gameObject.scene));
        var scene = EditorSceneManager.NewPreviewScene();
        int variants = 0, restricted = 0;
        try {
            var root = UnityEngine.Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(false);
            var panel = root.GetComponent<CreatePanel>();
            Call(panel, "BindFixedCreateControls");
            foreach (string rule in new[] { "taiwan", "hongkong", "riichi" }) Call(panel, "BindDetailedConfigControls", rule);
            Call(panel, "EnsureRuleDropdownOptions");
            Call(panel, "InitializeRoomPresentation");
            // Presets look up their ScrollRect through active parents. This is a
            // preview scene in Edit Mode, so activating it cannot start gameplay.
            root.SetActive(true);
            var name = (TMPro.TMP_InputField)typeof(CreatePanel).GetField("roomNameInput", Hidden).GetValue(panel);
            var dropdown = (TMPro.TMP_Dropdown)typeof(CreatePanel).GetField("SubRuleDropdown", Hidden).GetValue(panel);
            var catalog = CreateRoomRuleTextConfigCatalog.Rules;
            for (int ruleIndex = 0; ruleIndex < catalog.Count; ruleIndex++) {
                Call(panel, "OnRuleDropdownChanged", ruleIndex);
                for (int sub = 0; sub < dropdown.options.Count; sub++) {
                    dropdown.SetValueWithoutNotify(sub); Call(panel, "OnSubRuleChanged", sub);
                    name.text = "模块回归";
                    var request = panel.BuildRoomRequest();
                    string subRule = (string)Call(panel, "GetSelectedSubRule");
                    if (SichuanLobby.IsDuplicate(subRule)) {
                        if (request.IsValid || request.Error != SichuanLobby.DuplicateCreationMessage)
                            throw new Exception(subRule + ": 普通创房面板必须明确限制复式创建入口");
                        restricted++;
                        continue;
                    }
                    if (!request.IsValid) throw new Exception(catalog[ruleIndex].Rule + "/" + sub + ": " + request.Error);
                    var config = JObject.FromObject(request.Configuration);
                    if ((string)config["RoomName"] != "模块回归") throw new Exception("form snapshot lost name");
                    variants++;
                }
            }
            return new { passed = variants + restricted, variants, restricted };
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static object RunRegistrationDispatch() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        RegisterRules();
        var registry = (Dictionary<string, RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var saved = new Dictionary<string, RuleManifest>(registry);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Rule dispatch isolation"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var manager = root.AddComponent<GameRecordManager>();
        var player = new GameRecordManager.RecordPlayer { playerIndex = 0, originalPlayerIndex = 0, userId = 1 };
        manager.indexToPosition[0] = "self"; manager.recordPlayer_to_info["self"] = player;
        typeof(GameRecordManager).GetField("recordPlayerList", Hidden).SetValue(manager, new List<GameRecordManager.RecordPlayer> { player });
        int checks = 0, cleared = 0, sent = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        try {
            for (int i = 0; i < 300; i++) {
                string id = "validation_rule_" + i;
                int mutations = 0, presentations = 0;
                var manifest = new RuleManifest {
                    RuleId = id,
                    BuildRoom = input => RoomCreationRequest.Valid(input.SubRule, network => sent++),
                    ReplayFactory = record => new RuleReplayModule("module_checkpoint") {
                        Reset = () => mutations = 0, Clear = () => cleared++,
                        ApplyBeforeMutation = tick => { if (tick[0] != "module_checkpoint") return false; mutations++; return true; },
                        AfterSilentMutation = tick => presentations++,
                    },
                };
                RuleRegistry.Register(manifest);
                var request = RuleRegistry.Resolve(id).BuildRoom(new RoomCreationInput { Rule = id, SubRule = id + "/v1" });
                Check(request.IsValid && (string)request.Configuration == id + "/v1", id + " room routed");
                request.Send(null);
                typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager,
                    new GameRecord { gameTitle = new Dictionary<string, object> { { "rule", id }, { "sub_rule", id + "/v1" } } });
                Call(manager, "ResetRecordRuleState");
                var tick = new List<string> { "module_checkpoint" };
                Check((bool)Call(manager, "IsRecordSilentTick", tick), id + " custom tick classified");
                Call(manager, "ApplyRecordSilentTick", tick);
                Check(mutations == 1 && presentations == 1, id + " sequential mutation/presentation");
                Call(manager, "ResetRecordRuleState");
                Call(manager, "ApplyActionToRecordState", tick);
                Check(mutations == 1 && presentations == 1, id + " seek shares interpreter and stays silent");
            }
            Check(cleared == 299, "previous module released at every rule switch");
            Check(sent == 300, "all registered room transports called");
            bool rejected = false;
            try { RoomCreationRequest.Invalid("invalid").Send(null); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "invalid request cannot send");
            return new { passed = checks, rules = 300 };
        } finally {
            registry.Clear(); foreach (var pair in saved) registry[pair.Key] = pair.Value;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static object RunReplayWallIsolation() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        RegisterRules();
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Replay wall module isolation"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var recordObject = new GameObject("Record"); recordObject.transform.SetParent(root.transform, false);
        // activeSelf enables record configuration reads; the inactive parent
        // prevents lifecycle callbacks and presentation from running.
        var manager = recordObject.AddComponent<GameRecordManager>();
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        void Set(string name, object value) => typeof(GameRecordManager).GetField(name, Hidden).SetValue(manager, value);
        int Remaining() => (int)Call(manager, "GetRecordRemainTiles");
        Round Load(string rule, string sub) {
            var record = new GameRecord { gameTitle = new Dictionary<string, object> { { "rule", rule }, { "sub_rule", sub } } };
            var round = new Round { roundIndex = 1, tilesList = Enumerable.Repeat(11, 60).ToList() };
            record.gameRound.rounds[1] = round;
            typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager, record);
            manager.currentRoundIndex = 1;
            Set("currentTilesList", new List<int>(round.tilesList)); Set("currentOriginalIndices", Enumerable.Range(0, 60).ToList());
            Set("consumedBackIndices", new HashSet<int>()); Set("consumedFromFront", 0);
            Call(manager, "ResetRecordRuleState"); return round;
        }
        try {
            Load("zhongyong", "zhongyong/standard"); Check(Remaining() == 46, "Zhongyong reserves fourteen");
            Load("zhongyong", "zhongyong/nanque"); Check(Remaining() == 60, "Nanque does not inherit Zhongyong's tail");
            Load("guangdong", "guangdong/tuidao"); Check(Remaining() == 47, "Tuidao reserves thirteen");
            Load("guangdong", GuangdongMilRules.SubRule); Check(Remaining() == 60, "MIL does not inherit Tuidao's tail");
            Load("shanxi", "shanxi/mil2025");
            Check((bool)Call(manager, "TryConsumeRecordRuleWallTile", "gd") && Remaining() == 44, "Shanxi first replacement changes reserve");
            Check((bool)Call(manager, "TryConsumeRecordRuleWallTile", "gd") && Remaining() == 44, "Shanxi paired replacement restores reserve");
            Load("hangzhou", "hangzhou/mil2025");
            Check(Remaining() == 40, "Hangzhou reserves twenty");
            Call(manager, "ApplyRecordRuleActionBeforeMutation", new List<string> { "hangzhou", "tail_burn", "0" });
            Call(manager, "TryConsumeRecordRuleWallTile", "gd");
            Check(Remaining() == 38, "Hangzhou burn and replacement each remove one tile");
            var taiwan = Load("taiwan", "taiwan/standard");
            Check(Remaining() == 44, "Taiwan fixed tail remains sixteen");
            taiwan.detailedConfig = new Dictionary<string, object> { { "dead_wall_count", 18 }, { "dead_wall_mode", "kong_expands_tail" } };
            Call(manager, "ResetRecordRuleState");
            Check(Remaining() == 42, "Taiwan custom tail loaded by module");
            Call(manager, "ApplyRecordRuleActionBeforeMutation", new List<string> { "g" });
            Check(Remaining() == 41, "Taiwan kong expands its configured tail");
            Load("guobiao", "guobiao/standard"); Check(Remaining() == 60, "switching rule releases custom tail state");
            return new { passed = checks };
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
