#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SichuanDuplicateCreationValidation {
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(CreatePanel p, string key) => (T)typeof(CreatePanel).GetField(key, Flags).GetValue(p);
    static object Call(CreatePanel p, string key, params object[] args) => typeof(CreatePanel).GetMethod(key, Flags).Invoke(p, args);

    public static object RunRealTime() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        foreach (var type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
            type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        var source = Resources.FindObjectsOfTypeAll<CreatePanel>().Single(p => p.gameObject.scene.IsValid()
            && !EditorSceneManager.IsPreviewScene(p.gameObject.scene));
        var preview = EditorSceneManager.NewPreviewScene();
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        try {
            var root = UnityEngine.Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false);
            var p = root.GetComponent<CreatePanel>();
            foreach (string name in new[] { "roomPageParent", "venueOverlayParent" }) {
                var parent = new GameObject(name, typeof(RectTransform));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent, preview);
                typeof(CreatePanel).GetField(name, Flags).SetValue(p, parent.GetComponent<RectTransform>());
            }
            p.PrepareForRoom();
            var catalog = CreateRoomRuleTextConfigCatalog.Rules;
            int ruleIndex = Enumerable.Range(0, catalog.Count).Single(i => catalog[i].Rule == "sichuan");
            Field<TMP_Dropdown>(p, "chooseRule").SetValueWithoutNotify(ruleIndex);
            Call(p, "OnRuleDropdownChanged", ruleIndex);
            void SelectSub(string sub) {
                int index = catalog[ruleIndex].SubRules.Select((s, i) => new { s.Key, i }).Single(s => s.Key == sub).i;
                Field<TMP_Dropdown>(p, "SubRuleDropdown").SetValueWithoutNotify(index);
                Call(p, "OnSubRuleChanged", index);
            }
            var toggle = Field<UnityEngine.UI.Toggle>(p, "SichuanRealTimeToggle");
            var seconds = Field<TMP_Dropdown>(p, "SichuanSecondWaitDropdown");
            var tactical = Field<UnityEngine.UI.Toggle>(p, "TacticalCallToggle");
            Check(toggle && seconds, "real-time controls serialized in MainScene");
            Check(seconds.options.Select(o => o.text).SequenceEqual(new[] { "3秒", "5秒", "8秒", "10秒", "15秒", "20秒" }), "six exact second-wait choices");
            foreach (string sub in new[] { SichuanLobby.DoublesDuplicateSubRule, SichuanLobby.XueliuDuplicateSubRule }) {
                SelectSub(sub);
                Call(p, "ResetRoomDefaults");
                Field<TMP_InputField>(p, "roomNameInput").text = "真实时间验证";
                Check(toggle.gameObject.activeSelf && !toggle.isOn && seconds.value == 1 && !seconds.transform.parent.gameObject.activeSelf, sub + " default off, five seconds hidden");
                foreach (bool enabled in new[] { true, false }) foreach (bool tactics in new[] { false, true }) foreach (int value in SichuanLobby.SecondWaitSeconds) {
                    seconds.SetValueWithoutNotify(Array.IndexOf(SichuanLobby.SecondWaitSeconds, value));
                    tactical.isOn = tactics;
                    toggle.isOn = enabled;
                    Check(seconds.transform.parent.gameObject.activeSelf == enabled, "dropdown follows real-time only");
                    var request = p.BuildRoomRequest();
                    Check(request.IsValid, sub + ": " + request.Error);
                    var config = (Sichuan_Create_RoomConfig)request.Configuration;
                    Check(config.RealTime == enabled && config.TacticalGraceSeconds == value && config.TacticalCall == tactics, "independent switches retain shared seconds");
                    var rows = RoomConfigContainer.BuildDisplayFields(new RoomInfo { room_rule = "sichuan", sub_rule = sub,
                        real_time = enabled, tactical_grace_seconds = value, tactical_call = tactics });
                    Check(rows.Any(row => row.Key == "真实时间" && row.Value == (enabled ? "开" : "关")), "room summary shows real-time setting");
                    Check(rows.Any(row => row.Key == "第二等待时间" && row.Value == value + "秒") == (enabled || tactics), "room summary shows active shared time");
                }
                toggle.isOn = true;
                seconds.SetValueWithoutNotify(5);
                Call(p, "ResetRoomDefaults");
                Check(!toggle.isOn && seconds.value == 1, "restore defaults resets real-time setting");
                var invalid = (Sichuan_Create_RoomConfig)p.BuildRoomRequest().Configuration;
                invalid.TacticalGraceSeconds = 7;
                Check(!invalid.Validate(out _, false, false), "unsupported second-wait rejected");
                toggle.isOn = true;
                seconds.SetValueWithoutNotify(5);
            }
            foreach (string sub in new[] { "sichuan/standard", SichuanLobby.DoublesSubRule, SichuanLobby.XueliuDoublesSubRule }) {
                SelectSub(sub);
                var config = (Sichuan_Create_RoomConfig)p.BuildRoomRequest().Configuration;
                Check(!toggle.gameObject.activeSelf && !seconds.transform.parent.gameObject.activeSelf
                    && !config.RealTime && config.TacticalGraceSeconds == 5, "ordinary Sichuan ignores hidden real-time draft");
                Check(!RoomConfigContainer.BuildDisplayFields(new RoomInfo { room_rule = "sichuan", sub_rule = sub, real_time = true })
                    .Any(row => row.Key == "真实时间" || row.Key == "第二等待时间"), "ordinary Sichuan summary unchanged");
            }
            var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<RoomInfo>("{\"room_rule\":\"sichuan\",\"sub_rule\":\"sichuan/doubles_duplicate\"}");
            Check(!legacy.real_time && legacy.tactical_grace_seconds == 5, "legacy room defaults remain off and five seconds");
            var legacyRequest = Newtonsoft.Json.Linq.JObject.FromObject(new CreateSichuanRoomRequest());
            Check(legacyRequest["real_time"] == null && legacyRequest["tactical_grace_seconds"] == null, "non-duplicate request omits new fields");
            foreach (var text in toggle.GetComponentsInChildren<TMP_Text>(true).Concat(seconds.transform.parent.GetComponentsInChildren<TMP_Text>(true)))
                Check((text.fontStyle & FontStyles.Bold) == 0 && text.fontWeight == FontWeight.Regular, "new Chinese labels use regular weight");
            return new { passed = checks.Count, checks };
        } finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        foreach (var type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
            type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        var source = Resources.FindObjectsOfTypeAll<CreatePanel>().Single(p => p.gameObject.scene.IsValid()
            && !EditorSceneManager.IsPreviewScene(p.gameObject.scene));
        var preview = EditorSceneManager.NewPreviewScene();
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        try {
            var root = UnityEngine.Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false);
            var p = root.GetComponent<CreatePanel>();
            foreach (string name in new[] { "roomPageParent", "venueOverlayParent" }) {
                var parent = new GameObject(name, typeof(RectTransform));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent, preview);
                typeof(CreatePanel).GetField(name, Flags).SetValue(p, parent.GetComponent<RectTransform>());
            }
            p.PrepareForRoom();
            var header = Field<UnityEngine.UI.Image>(p, "creationHeader");
            var footer = Field<UnityEngine.UI.Image>(p, "creationFooter");
            Color blue = header.color;
            var key = Field<TMP_InputField>(p, "DuplicateKeyInput");
            foreach (string venue in new[] { "", "evt_validation", "base_validation" }) {
                if (venue == "") p.PrepareForRoom(); else p.OpenForVenue(venue);
                bool green = venue != "";
                Check(green ? header.color.g > header.color.b && footer.color.g > footer.color.b : header.color == blue,
                    venue + " context color before showing");
                var rules = CreateRoomRuleTextConfigCatalog.Rules;
                int index = Enumerable.Range(0, rules.Count).Single(i => rules[i].Rule == "sichuan");
                Field<TMP_Dropdown>(p, "chooseRule").SetValueWithoutNotify(index);
                Call(p, "OnRuleDropdownChanged", index);
                foreach (string sub in new[] { SichuanLobby.DoublesDuplicateSubRule, SichuanLobby.XueliuDuplicateSubRule }) {
                    int subIndex = rules[index].SubRules.Select((s, i) => new { s.Key, i }).Single(s => s.Key == sub).i;
                    Field<TMP_Dropdown>(p, "SubRuleDropdown").SetValueWithoutNotify(subIndex);
                    Call(p, "OnSubRuleChanged", subIndex);
                    Field<TMP_InputField>(p, "roomNameInput").text = "八人复式验证";
                    Check(!Field<UnityEngine.UI.Toggle>(p, "DuplicateWallToggle").gameObject.activeSelf, "no redundant duplicate toggle");
                    Check(!Field<UnityEngine.UI.Toggle>(p, "SetRandomSeedToggle").gameObject.activeSelf, "no scene seed in paired form");
                    Check(Field<TMP_Dropdown>(p, "SichuanDuplicateRounds").gameObject.activeSelf, "board count field itself visible");
                    key.text = "";
                    Check(key.placeholder.gameObject.activeSelf, "empty key explains automatic generation");
                    foreach (int count in new[] { 1, 4, 8, 12, 16 }) foreach (bool sync in new[] { false, true }) {
                        var rounds = Field<TMP_Dropdown>(p, "SichuanDuplicateRounds");
                        rounds.SetValueWithoutNotify(Array.IndexOf(new[] { 1, 4, 8, 12, 16 }, count));
                        Field<UnityEngine.UI.Toggle>(p, "SichuanDuplicateSyncToggle").SetIsOnWithoutNotify(sync);
                        key.text = "";
                        var request = p.BuildRoomRequest();
                        Check(request.IsValid, sub + ": " + request.Error);
                        var config = (Sichuan_Create_RoomConfig)request.Configuration;
                        Check(config.DuplicateRoundCount == count && config.DuplicateSyncMode == sync
                            && config.EventId == (green ? venue : null) && !config.AllowSpectator, "paired settings preserved");
                    }
                    key.text = "dup_" + new string('b', 32);
                    Check(((Sichuan_Create_RoomConfig)p.BuildRoomRequest().Configuration).DuplicateKey == key.text, "key forwarded");
                    Check(!Field<TMP_Dropdown>(p, "SichuanDuplicateRounds").interactable, "existing key owns board count");
                    var sources = Field<UnityEngine.UI.Toggle[]>(p, "SichuanWallSourceToggles");
                    Check(sources.Length == 4 && Field<GameObject>(p, "SichuanWallSourcePanel").activeSelf, "four explicit wall sources visible");
                    sources[0].isOn = true;
                    var seed = Field<TMP_InputField>(p, "SichuanTrainingSeedInput");
                    var spectators = Field<UnityEngine.UI.Toggle>(p, "AllowSpectatorToggle");
                    Check(spectators.isOn && spectators.interactable, "training defaults to optional spectators");
                    Check(!Field<GameObject>(p, "DuplicateWallPanel").activeSelf && Field<GameObject>(p, "SichuanTrainingSeedPanel").activeSelf,
                        "training has seed input and no key input");
                    seed.text = "";
                    Check(!p.BuildRoomRequest().IsValid, "training requires explicit seed");
                    Field<UnityEngine.UI.Button>(p, "SichuanTrainingGenerateButton").onClick.Invoke();
                    string generated = seed.text;
                    Check(generated.Length == 64 && generated.All(c => "0123456789abcdef".Contains(c)) && generated.Any(c => c != '0'), "generate valid training seed");
                    spectators.isOn = false;
                    Call(p, "RefreshRoomPresentation", false);
                    var training = (Sichuan_Create_RoomConfig)p.BuildRoomRequest().Configuration;
                    Check(training.WallSource == "training" && training.RandomSeed == generated && string.IsNullOrEmpty(training.DuplicateKey)
                        && !training.AllowSpectator && seed.text == generated, "training preserves seed and manual spectator choice; stale key omitted");
                    seed.text = new string('0', 64);
                    Check(!p.BuildRoomRequest().IsValid, "training rejects disabled all-zero seed");
                    seed.text = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
                    Check(p.BuildRoomRequest().IsValid, "training accepts pasted hexadecimal seed");
                    foreach (int sourceIndex in new[] { 2, 3, 1 }) {
                        sources[sourceIndex].isOn = true;
                        Check(!spectators.isOn && !spectators.interactable, "key-backed wall disables spectators");
                        key.text = "";
                        Check(p.BuildRoomRequest().IsValid == (sourceIndex == 1), "only key source can auto-generate a key");
                        key.text = "dup_" + new string('b', 32);
                        var keyed = (Sichuan_Create_RoomConfig)p.BuildRoomRequest().Configuration;
                        Check(keyed.WallSource == new[] { "training", "key", "seed", "manual" }[sourceIndex]
                            && keyed.DuplicateKey == key.text && keyed.RandomSeed == "", "selected source serializes without stale training seed");
                    }
                }
            }
            key.text = "dup_" + new string('c', 32);
            p.OpenForVenue("another_venue");
            Check(key.text == "", "key does not cross venue boundaries");
            p.PrepareForRoom();
            Check(header.color == blue, "ordinary room color restored");
            Call(p, "ResetRoomDefaults");
            Check(Field<TMP_Dropdown>(p, "SichuanDuplicateRounds").value == 2
                && !Field<UnityEngine.UI.Toggle>(p, "SichuanDuplicateSyncToggle").isOn && key.text == "", "paired defaults reset with existing form action");
            Check(p.GetComponentsInChildren<TMP_Text>(true).Where(t => t.gameObject.name == "Label").All(t => (t.fontStyle & FontStyles.Bold) == 0), "normal Chinese labels");
            return new { passed = checks.Count, checks };
        } finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
}
#endif
