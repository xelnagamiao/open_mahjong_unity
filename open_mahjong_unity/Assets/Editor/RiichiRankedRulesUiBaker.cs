using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Author Riichi descriptions and tables without rebuilding the lobby.</summary>
public static class RiichiRankedRulesUiBaker {
    const string Folder = "Assets/Resources/UI/Description/";
    static readonly float[] ScoreWidths = { .12f, .15f, .12f, .12f, .27f, .22f };
    static readonly float[] RankWidths = { .13f, .13f, .15f, .08f, .17f, .17f, .17f };
    static T Find<T>() where T : Component => Resources.FindObjectsOfTypeAll<T>().First(x => x.gameObject.scene == SceneManager.GetActiveScene());
    static T Ref<T>(Object target, string field) where T : Object => (T)new SerializedObject(target).FindProperty(field).objectReferenceValue;
    static void Set(Object target, string field, Object value) {
        var data = new SerializedObject(target);
        data.FindProperty(field).objectReferenceValue = value;
        data.ApplyModifiedProperties();
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Riichi Ranked Descriptions")]
    public static void Bake() {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/MainScene.unity")
            throw new InvalidOperationException("Open MainScene outside Play Mode first.");
        var panel = Find<MatchDescribePanel>();
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Update Riichi ranked descriptions");
        var content = Ref<TMP_Text>(panel, "contentText").transform.parent;
        var oldScore = content.Find("MatchScoreTable").gameObject;
        var oldRank = content.Find("RankProgressionTable").gameObject;
        var scoreRows = new List<(string kind, string[] text)> {
            ("title", new[] { "立直 · M规计分表" }),
            ("group", new[] { "比赛分 M =（终局点数 - 30000）/1000 + 顺位奖励；一位 +50 已含头名奖" }),
            ("header", new[] { "项目", "一位", "二位", "三位", "四位", "同点处理" }),
            ("data", new[] { "顺位奖励", "+50", "+10", "-10", "-30", "分摊所占名次奖励" }),
            ("data", new[] { "示例点数", "40000", "30000", "20000", "10000", "供托按规则分配" }),
            ("data", new[] { "示例 M", "+60", "+10", "-20", "-50", "比赛分含终局素点" }),
            ("group", new[] { "段位 PT = q ×（k × M - C）；q 为局制系数，k 为场次倍率，C 见段位扣分表" }),
            ("header", new[] { "局制", "场次", "倍率 k", "系数 q", "PT 计算", "普通入场段位" }),
        };
        foreach (bool east in new[] { false, true }) {
            for (int tier = 0; tier < 3; tier++) {
                string k = RiichiRankConfig.Number(RiichiRankConfig.TierMultipliers[tier]);
                string q = east ? "0.7" : "1";
                scoreRows.Add(("data", new[] { east ? "东风战" : "半庄战", RiichiRankConfig.TierNames[tier], k, q,
                    $"{q} ×（{k} × M - C）", tier == 0 ? "无" : tier == 1 ? "1级至六段" : "四段及以上" }));
            }
        }
        scoreRows.Add(("group", new[] { "C 为负数时是保护奖励；PT 保留两位小数。十段固定 100/100 PT，每场变动为 0。" }));
        var rankRows = new List<(string kind, string[] text)> {
            ("title", new[] { "立直 · 段位升降与扣分表" }),
            ("group", new[] { "各名次扣相同的 C；东风整体乘 0.7，负数为保护奖励。特许可突破最低段位；— 为禁入场次。" }),
            ("header", new[] { "段位", "起始 PT", "升段 PT", "掉段", "初级 C", "中级 C", "高级 C" }),
        };
        for (int i = 0; i < RankConfig.RankTable.Length; i++) {
            var rank = RankConfig.RankTable[i];
            rankRows.Add(("data", new[] { rank.name, rank.startScore.ToString(), i == 19 ? "永久称号" : rank.promoteScore.ToString(),
                i >= 7 && i < 19 ? "有" : "无", RiichiRankConfig.Number(RiichiRankConfig.RankCost(i, 0)),
                i >= 16 ? "—" : RiichiRankConfig.Number(RiichiRankConfig.RankCost(i, 1)),
                RiichiRankConfig.Number(RiichiRankConfig.RankCost(i, 2)) }));
        }
        var score = BuildTable("RiichiMatchScoreTable", "MatchScoreTable", scoreRows, ScoreWidths);
        var rankTable = BuildTable("RiichiRankProgressionTable", "RankProgressionTable", rankRows, RankWidths);
        var scoreInstance = PlaceTable(content, "RiichiMatchScoreTable", score, 3);
        var rankInstance = PlaceTable(content, "RiichiRankProgressionTable", rankTable, 4);
        Set(panel, "matchScoreTable", oldScore); Set(panel, "rankProgressionTable", oldRank);
        Set(panel, "riichiScoreTable", scoreInstance); Set(panel, "riichiRankTable", rankInstance);
        oldScore.SetActive(true); oldRank.SetActive(true);

        var policy = Find<RankPolicyPanel>();
        Undo.RegisterFullObjectHierarchyUndo(policy.gameObject, "Update ranked policy");
        var policyText = Ref<GameObject>(policy, "policyContent").GetComponent<TMP_Text>();
        policyText.text = RankedRules.Policy;
        policyText.enableAutoSizing = true; policyText.fontSizeMin = 22; policyText.fontSizeMax = 28;
        policyText.textWrappingMode = TextWrappingModes.Normal;
        AuthorSettlementDetails(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Riichi M-league PT descriptions, tables and settlement breakdown saved.");
    }

    [MenuItem("Tools/Mahjong/Main Scene/Update Ranked Settlement Details")]
    public static void UpdateSettlementDetails() {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/MainScene.unity")
            throw new InvalidOperationException("Open MainScene outside Play Mode first.");
        AuthorSettlementDetails(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static void AuthorSettlementDetails(Scene scene) {
        foreach (var rankPanel in Resources.FindObjectsOfTypeAll<RankChangePanel>().Where(x => x.gameObject.scene == scene)) {
            Undo.RegisterFullObjectHierarchyUndo(rankPanel.gameObject, "Add Riichi rating breakdown");
            var source = Ref<TMP_Text>(rankPanel, "ptChangeText");
            var existing = source.transform.parent.Find("RatingDetails");
            var label = existing != null ? existing.GetComponent<TMP_Text>() : Object.Instantiate(source, source.transform.parent);
            label.name = "RatingDetails";
            label.text = "比赛分 +60 × 场次倍率 1\n段位扣分 5.25 · 局制系数 1";
            label.fontSize = 22; label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.Normal;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.rectTransform.anchoredPosition = new Vector2(-31, -170);
            label.rectTransform.sizeDelta = new Vector2(530, 78);
            label.gameObject.SetActive(false);
            Set(rankPanel, "ratingDetailsText", label);
        }
    }

    static GameObject PlaceTable(Transform parent, string name, GameObject prefab, int index) {
        var existing = parent.Find(name);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(instance, "Place Riichi table");
        instance.name = name; instance.transform.SetSiblingIndex(index); instance.SetActive(false);
        return instance;
    }

    static GameObject BuildTable(string name, string templateName, List<(string kind, string[] text)> rows, float[] widths) {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + templateName + ".prefab"));
        root.name = name;
        if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var staged = new List<GameObject>();
        foreach (var item in rows) {
            string template = item.kind == "title" ? "TableTitle" : item.kind == "header" ? "ColumnHeaders" : item.kind == "group" ? "Group_01" : "DataRow_01";
            var row = Object.Instantiate(root.transform.Find(template).gameObject);
            row.name = item.kind == "title" ? "TableTitle" : item.kind + "_" + staged.Count.ToString("00");
            var labels = row.GetComponentsInChildren<TMP_Text>(true);
            if (labels.Length != item.text.Length) throw new InvalidOperationException("Table template column count changed: " + template);
            var layout = row.GetComponent<LayoutElement>();
            float height = item.kind == "title" ? 48 : item.kind == "header" ? 72 : item.kind == "group" ? 66 : widths.Length == 7 ? 44 : 62;
            layout.minHeight = layout.preferredHeight = height;
            for (int i = 0; i < labels.Length; i++) {
                var label = labels[i]; label.text = item.text[i];
                label.fontSize = item.kind == "title" ? 26 : 22; label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.Normal;
                if (labels.Length > 1) {
                    var cell = (RectTransform)label.transform.parent;
                    float left = widths.Take(i).Sum();
                    cell.anchorMin = new Vector2(left, 0); cell.anchorMax = new Vector2(left + widths[i], 1);
                    cell.offsetMin = new Vector2(1, 1); cell.offsetMax = new Vector2(-1, -1);
                    label.alignment = i == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
                    label.margin = i == 0 ? new Vector4(14, 0, 8, 0) : new Vector4(4, 0, 4, 0);
                }
            }
            staged.Add(row);
        }
        foreach (Transform child in root.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        foreach (var row in staged) row.transform.SetParent(root.transform, false);
        var result = PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
        Object.DestroyImmediate(root);
        return result;
    }
}
