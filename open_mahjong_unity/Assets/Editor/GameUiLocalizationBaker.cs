#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Localizes authored table UI while leaving player content and protocol data untouched.</summary>
public static class GameUiLocalizationBaker {
    static readonly Dictionary<string, string[]> UserFields = new() {
        { "GamePlayerPanel", new[] { "playerNameText", "playerTitleText" } },
        { "ScoreHistoryPanel", new[] { "player0UserName", "player1UserName", "player2UserName", "player3UserName" } },
        { "EndResultPanel", new[] { "player0UserName", "player1UserName", "player2UserName", "player3UserName" } },
        { "CommitmentSaltDetailPanel", new[] { "commitmentText", "saltText" } }
    };
    static readonly Dictionary<string, string> ControlKeys = new() {
        { "TileList", "table.replay.wall" }, { "LaterXunmu", "table.replay.prev_turn" },
        { "NextXunmu", "table.replay.next_turn" }, { "LaterStep", "table.replay.prev_step" },
        { "NextStep", "table.replay.next_step" }, { "AutoPlay", "table.replay.play" },
        { "ReturnLiveButton", "table.replay.return_live" }, { "SwitchToMeun", "table.utility.lobby" },
        { "OpenRuleBook", "table.utility.rules" }, { "OpenHistoryPanel", "table.utility.score" },
        { "VotePause", "table.utility.pause" }, { "VoteQuit", "table.utility.end" },
        { "UnlimitedEndButton", "table.utility.end" }, { "QuitRecordButton", "table.utility.exit" },
        { "QuitSpectatingButton", "table.utility.exit" }, { "QuitRealTimeSpectatingButton", "table.utility.exit" }
    };
    public static void Bake(Scene scene) {
        var roots = scene.GetRootGameObjects().Where(r => r.name == "#GameCanvas" || r.name == "BoardCanvas").ToList();
        var overlay = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "OverlayCanvas");
        var table = overlay ? overlay.transform.Find("TableOverlay") : null;
        if (table) roots.Add(table.gameObject);
        BakeHierarchy(roots.ToArray());
        foreach (var layout in roots.SelectMany(r => r.GetComponentsInChildren<TableHudUtilityLayout>(true))) {
            RestoreHints(layout);
            layout.RefreshLayout();
            EditorUtility.SetDirty(layout);
        }
        foreach (var theme in roots.SelectMany(r => r.GetComponentsInChildren<TableHudTheme>(true))) theme.Apply();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
    public static void BakePrefabs() {
        foreach (var path in new[] {
            "Assets/Prefabs/Record/RecordNodeItem.prefab", "Assets/Prefabs/Record/RecordRoundItem.prefab",
            "Assets/Prefabs/GameScene/FanCount.prefab", "Assets/Prefabs/HistoryText.prefab"
        }) {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new InvalidOperationException("Missing table prefab: " + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try { BakeHierarchy(new[] { root }); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
    static void BakeHierarchy(GameObject[] roots) {
        var excluded = new HashSet<TMP_Text>();
        foreach (var input in roots.SelectMany(r => r.GetComponentsInChildren<TMP_InputField>(true))) excluded.Add(input.textComponent);
        foreach (var component in roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c)) {
            if (!UserFields.TryGetValue(component.GetType().Name, out var fields)) continue;
            var data = new SerializedObject(component);
            foreach (var field in fields) {
                var property = data.FindProperty(field);
                if (property?.objectReferenceValue is TMP_Text label) excluded.Add(label);
            }
        }
        foreach (var text in roots.SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).Distinct()) {
            string path = Path(text.transform), name = text.name.ToLowerInvariant();
            bool user = excluded.Contains(text) || name.Contains("username") || name.Contains("playername")
                || name.Contains("playertitle") || name == "userid" || name == "nickname" || name == "uid"
                || path.Contains("/Chat") || path.Contains("/Messages/") || path.Contains("/LogText")
                || path.Contains("/CommitmentHash") || path.Contains("/PlayerTitle/");
            var binding = text.GetComponent<LocalizedPlatformLabel>();
            if (user) { if (binding) UnityEngine.Object.DestroyImmediate(binding); continue; }
            bool control = path.Contains("/ControlPanel/") || path.Contains("/GamePageControl/") || path.Contains("/#ExitButtonManager/");
            string key = binding ? binding.key : text.text;
            if (control && text.transform.parent && ControlKeys.TryGetValue(text.transform.parent.name, out var controlKey)) key = controlKey;
            if (path.Contains("/MouseControllerTips/")) key = "table.replay.shortcuts";
            if (!binding && !PlatformText.HasChinese(key) && !PlatformText.Contains(key)) continue;
            bool created = !binding;
            if (created) binding = text.gameObject.AddComponent<LocalizedPlatformLabel>();
            binding.key = key;
            if (!binding.typographyBaked) {
                binding.authoredAutoSize = text.enableAutoSizing;
                binding.authoredSize = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
                binding.minimumSize = Mathf.Min(binding.authoredSize, Mathf.Max(12, binding.authoredSize * .65f));
                binding.typographyBaked = true;
            }
            binding.followSourceChanges = !key.StartsWith("table.", StringComparison.Ordinal);
            bool ownedLayout = control || path.Contains("/MouseControllerTips/") || text.GetComponentInParent<BoardCanvas>(true)
                || text.GetComponentInParent<SimpleRoundPanel>(true) || text.GetComponentInParent<RiichiRoundPanel>(true)
                || text.GetComponentInParent<AutoAction>(true) || text.GetComponentInParent<FanCount>(true)
                || text.GetComponentInParent<TableHudMatchInfo>(true) || text.GetComponentInParent<SettlementTotalLayout>(true)
                || roots.Any(r => r.name == "HistoryText");
            binding.fitTranslation = !ownedLayout && text.rectTransform.rect.height <= 100
                && !text.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (control) binding.fitTranslation = false;
            EditorUtility.SetDirty(binding);
        }
    }
    static void RestoreHints(TableHudUtilityLayout layout) {
        if (!layout.replayTips) return;
        var text = layout.replayTips.GetComponentInChildren<TMP_Text>(true);
        if (text) {
            text.color = Color.white;
            text.fontSize = text.fontSizeMax = 17.4f; text.fontSizeMin = 12;
            text.enableAutoSizing = true;
            var binding = text.GetComponent<LocalizedPlatformLabel>();
            if (binding) { binding.authoredSize = 17.4f; binding.minimumSize = 12; binding.authoredAutoSize = true; binding.fitTranslation = false; EditorUtility.SetDirty(binding); }
            EditorUtility.SetDirty(text);
        }
        var image = layout.replayTips.GetComponent<UnityEngine.UI.Image>();
        if (image) { image.enabled = true; image.color = new Color(0.5415094f, 0.57174706f, 1f, 0.50980395f); EditorUtility.SetDirty(image); }
    }
    static string Path(Transform value) => value.parent ? Path(value.parent) + "/" + value.name : value.name;
}
#endif
