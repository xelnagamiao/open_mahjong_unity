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
