#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Both added rules must coexist in the actual normal/event room forms.</summary>
public static class VariantLobbyValidation {
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(CreatePanel panel, string method, params object[] args) =>
        typeof(CreatePanel).GetMethod(method, Hidden).Invoke(panel, args);
    private static T Field<T>(CreatePanel panel, string name) =>
        (T)typeof(CreatePanel).GetField(name, Hidden).GetValue(panel);

    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        foreach (var type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
            type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity", OpenSceneMode.Single);
        var panels = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CreatePanel>(true)).ToArray();
        var checks = new List<string>();
        void Check(string name, bool ok) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        Check("normal and event room forms exist", panels.Length == 2);
        foreach (var panel in panels) {
            panel.BakeFilipinoDetailsPanel();
            panel.BakeVietnameseDetailsPanel();
        }
        // Tests share Assembly-CSharp with the forms; the baker belongs to the editor assembly.
        var baker = Type.GetType("RuleSupportTableBaker, Assembly-CSharp-Editor", true);
        baker.GetMethod("Bake").Invoke(null, new object[] { scene });
        baker.GetMethod("Validate").Invoke(null, new object[] { scene });
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        var table = baker.GetMethod("ReadWorkbook").Invoke(null, null);
        var rows = (string[][])table.GetType().GetField("Rows").GetValue(table);
        foreach (var identity in new[] { "filipino/classic16", "vietnamese/classic160" })
            Check(identity + " support row", rows.Count(row => row[5] == identity && !row[10].Contains("未合并")) == 1);
        var catalog = CreateRoomRuleTextConfigCatalog.Rules;
        foreach (var source in panels) {
            var preview = EditorSceneManager.NewPreviewScene();
            try {
                var root = UnityEngine.Object.Instantiate(source.gameObject);
                SceneManager.MoveGameObjectToScene(root, preview);
                root.SetActive(false);
                var panel = root.GetComponent<CreatePanel>();
                Call(panel, "BindFixedCreateControls");
                foreach (var rule in new[] { "taiwan", "hongkong", "riichi", "filipino", "vietnamese" })
                    Call(panel, "BindDetailedConfigControls", rule);
                Call(panel, "EnsureRuleDropdownOptions");
                Call(panel, "InitializeRoomPresentation");
                root.SetActive(true);
                var dropdown = Field<TMP_Dropdown>(panel, "chooseRule");
                foreach (var entry in new[] {
                    (rule: "filipino", sub: "filipino/classic16", name: "菲律宾麻将", type: typeof(Filipino_Create_RoomConfig)),
                    (rule: "vietnamese", sub: "vietnamese/classic160", name: "越南麻将", type: typeof(Vietnamese_Create_RoomConfig))
                }) {
                    string label = source.name + ": " + entry.rule;
                    int index = Enumerable.Range(0, catalog.Count).Single(i => catalog[i].Rule == entry.rule);
                    Check(label + " visible once", dropdown.options.Count(option => option.text == entry.name) == 1);
                    Check(label + " dropdown maps to registered rule", dropdown.options[index].text == entry.name);
                    dropdown.SetValueWithoutNotify(index);
                    Call(panel, "OnRuleDropdownChanged", index);
                    Check(label + " selects classic subrule", (string)Call(panel, "GetSelectedSubRule") == entry.sub);
                    Field<TMP_InputField>(panel, "roomNameInput").text = "规则合并验证";
                    var request = panel.BuildRoomRequest();
                    Check(label + " creates the right request", request.IsValid && request.Configuration.GetType() == entry.type);
                    Check(label + " opens advanced settings", (bool)Call(panel, "ShowDetailedConfigPanel", entry.rule));
                    Check(label + " advanced panel active", panel.transform.Find("DetailedConfigPanel_" + entry.rule).gameObject.activeSelf);
                }
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        return new { passed = checks.Count, forms = panels.Length, checks };
    }

    public static void RunBatch() {
        string output = Environment.GetEnvironmentVariable("OPEN_MAHJONG_VALIDATION_OUTPUT") ?? ".om_workspace/validation";
        Directory.CreateDirectory(output);
        try {
            var result = Run();
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(output, "variant-lobby.json"), JsonConvert.SerializeObject(new { passed = true, result }, Formatting.Indented));
            EditorApplication.Exit(0);
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(output, "variant-lobby.json"), JsonConvert.SerializeObject(new { passed = false, error = error.ToString() }, Formatting.Indented));
            EditorApplication.Exit(2);
        }
    }
}
#endif
