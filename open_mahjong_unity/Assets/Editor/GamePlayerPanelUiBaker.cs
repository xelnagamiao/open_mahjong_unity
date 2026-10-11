using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>玩家面板固定内容只在编辑器烘焙；运行时沿用场景中的布局和引用。</summary>
public static class GamePlayerPanelUiBaker {
    private const float AvatarScale = 1.08f;
    private static readonly string[] Keys = {
        GamePlayerPanel.VariableLangyong, GamePlayerPanel.VariableHandCount,
        GamePlayerPanel.VariableRemainingTiles, GamePlayerPanel.VariableDeclaredReady,
        GamePlayerPanel.VariableChengbao, GamePlayerPanel.VariableFlowers, GamePlayerPanel.VariableGhosts,
        GamePlayerPanel.VariableHangzhouDealer, GamePlayerPanel.VariableHangzhouForcedCut,
    };
    private static readonly string[] Names = {
        "WavePoint", "FreeHandCount", "DuplicateRemainingTiles", "DeclaredReady", "Chengbao", "YixingFlowers", "GuangdongGhosts",
        "HangzhouDealer", "HangzhouForcedCut",
    };
    private static readonly string[] Examples = {
        "浪涌点数*1", "手牌 13", "余牌 18", "已报听", "承包东南", "花数 3", "打鬼 2",
        "3老庄 ×8", "仅摸切",
    };

    [MenuItem("Tools/Mahjong/Main Scene/Bake Player Panels")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        var panels = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GamePlayerPanel>(true)).ToArray();
        if (panels.Length != 4) throw new InvalidOperationException("主场景必须包含四家玩家面板。");
        foreach (var panel in panels) Bake(panel);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void Bake(GamePlayerPanel panel) {
        Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Bake player panel variables");
        var serialized = new SerializedObject(panel);
        var group = panel.transform.Find("PlayerVariables") as RectTransform;
        bool migrating = group == null;
        RectTransform wave = group != null ? group.Find("WavePoint") as RectTransform
            : panel.transform.Cast<Transform>().FirstOrDefault(child => child.name.StartsWith("WavePoint")) as RectTransform;
        if (wave == null) throw new InvalidOperationException(panel.name + " 缺少现有浪涌标记，不能推断布局基准。");
        bool downward = panel.name == "#TopPlayer";
        Vector2 rowSize = wave.rect.size;
        ScalePanel(panel, serialized, downward);

        if (migrating) {
            group = CreateRect("PlayerVariables", panel.transform);
            group.anchorMin = group.anchorMax = new Vector2(.5f, .5f);
            group.pivot = new Vector2(downward ? 0f : .5f, downward ? 1f : 0f);
            group.anchoredPosition = wave.anchoredPosition + new Vector2(downward ? -rowSize.x * .5f : 0f,
                rowSize.y * (downward ? .5f : -.5f));
            group.sizeDelta = rowSize;
        }
        if (downward) {
            float textEdge = Mathf.Max(
                RectTransformUtility.CalculateRelativeRectTransformBounds(panel.transform, panel.transform.Find("Username")).max.x,
                RectTransformUtility.CalculateRelativeRectTransformBounds(panel.transform, panel.transform.Find("Title")).max.x);
            group.anchoredPosition = new Vector2(Mathf.Max(group.anchoredPosition.x, textEdge + 4f), group.anchoredPosition.y);
        }
        var layout = group.GetComponent<UnityEngine.UI.VerticalLayoutGroup>()
            ?? Undo.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(group.gameObject);
        layout.childAlignment = downward ? TextAnchor.UpperLeft : TextAnchor.LowerCenter;
        layout.reverseArrangement = !downward;
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = group.GetComponent<UnityEngine.UI.ContentSizeFitter>()
            ?? Undo.AddComponent<UnityEngine.UI.ContentSizeFitter>(group.gameObject);
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        var variables = serialized.FindProperty("playerVariables");
        // 已有自定义变量保留；新增玩家变量也必须作为本布局组的场景子项绑定。
        for (int i = 0; i < Keys.Length; i++) {
            RectTransform row = i == 0 ? wave : group.Find(Names[i]) as RectTransform;
            if (row == null) row = panel.transform.Find(Names[i]) as RectTransform;
            bool created = row == null;
            if (created) {
                row = UnityEngine.Object.Instantiate(wave, group, false);
                Undo.RegisterCreatedObjectUndo(row.gameObject, "Bake player variable");
            }
            if (row.parent != group) Undo.SetTransformParent(row, group, "Group player variable");
            row.name = Names[i];
            row.SetSiblingIndex(i);
            if (migrating || created) {
                row.localScale = Vector3.one;
                row.sizeDelta = rowSize;
                var label = row.GetComponentInChildren<TMP_Text>(true);
                TMP_Text source = wave.GetComponentInChildren<TMP_Text>(true);
                label.font = source.font;
                label.fontSize = label.fontSizeMax = source.fontSize;
                label.fontSizeMin = 14f;
                label.enableAutoSizing = true;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.alignment = source.alignment;
                label.color = source.color;
                label.text = Examples[i];
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                row.GetComponent<UnityEngine.UI.Image>().color = wave.GetComponent<UnityEngine.UI.Image>().color;
                row.gameObject.SetActive(i <= 1);
            }
            DisableRaycasts(row);
            int entryIndex = -1;
            for (int j = 0; j < variables.arraySize; j++)
                if (variables.GetArrayElementAtIndex(j).FindPropertyRelative("key").stringValue == Keys[i]) entryIndex = j;
            if (entryIndex < 0) entryIndex = variables.arraySize++;
            var entry = variables.GetArrayElementAtIndex(entryIndex);
            entry.FindPropertyRelative("key").stringValue = Keys[i];
            entry.FindPropertyRelative("text").objectReferenceValue = row.GetComponentInChildren<TMP_Text>(true);
        }
        serialized.FindProperty("playerVariablesLayout").objectReferenceValue = layout;
        var waits = serialized.FindProperty("recordWaits").objectReferenceValue as RecordPlayerWaits;
        if (waits != null && waits.transform.parent != group) {
            Undo.SetTransformParent(waits.transform, group, "Group record waits");
            waits.transform.SetAsLastSibling();
            waits.gameObject.SetActive(false);
        }

        BakeSticker(panel, serialized);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        panel.BakeActionMenu();
        // 头像与状态区域放大时，头像快捷菜单仍保持原尺寸。
        panel.transform.Find("PlayerActionMenu").localScale = Vector3.one / AvatarScale;
        foreach (TMP_Text label in panel.GetComponentsInChildren<TMP_Text>(true)) DisableRaycasts(label.transform);
        DisableRaycasts(panel.transform.Find("Username"));
        DisableRaycasts(panel.transform.Find("Title"));
        DisableRaycasts(panel.transform.Find("DinqueImage"));
        foreach (Component component in panel.GetComponentsInChildren<Component>(true))
            if (component != null) EditorUtility.SetDirty(component);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(group);
        Validate(panel);
    }

    private static void ScalePanel(GamePlayerPanel panel, SerializedObject serialized, bool downward) {
        var edge = (UnityEngine.UI.Image)serialized.FindProperty("playerProfileEdgePicture").objectReferenceValue;
        var corners = new Vector3[4];
        edge.rectTransform.GetWorldCorners(corners);
        Vector3 oldTop = corners[1];
        panel.transform.localScale = Vector3.one * AvatarScale;
        // 对家贴近屏幕上缘，保持头像上边缘位置，防止放大后裁切。
        if (downward) {
            edge.rectTransform.GetWorldCorners(corners);
            panel.transform.position += new Vector3(0f, oldTop.y - corners[1].y, 0f);
        }
        panel.transform.Find("Username").localScale = Vector3.one / AvatarScale;
        panel.transform.Find("Title").localScale = Vector3.one / AvatarScale;
    }

    private static void BakeSticker(GamePlayerPanel panel, SerializedObject serialized) {
        Transform anchor = serialized.FindProperty("showStickerPos").objectReferenceValue as Transform;
        if (anchor == null) throw new InvalidOperationException(panel.name + " 缺少表情锚点。");
        var display = anchor.Find("StickerDisplay") as RectTransform;
        if (display == null) {
            display = CreateRect("StickerDisplay", anchor);
            display.anchorMin = display.anchorMax = display.pivot = new Vector2(.5f, .5f);
            display.sizeDelta = new Vector2(140f, 140f);
            var image = Undo.AddComponent<UnityEngine.UI.Image>(display.gameObject);
            image.sprite = Resources.Load<Sprite>("image/sticker/turtle/3");
            image.raycastTarget = false;
            display.gameObject.SetActive(false);
        }
        serialized.FindProperty("stickerDisplay").objectReferenceValue = display.GetComponent<UnityEngine.UI.Image>();
    }

    private static RectTransform CreateRect(string name, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Bake player panel UI");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void DisableRaycasts(Transform root) {
        if (root == null) return;
        foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
    }

    public static void Validate(GamePlayerPanel panel) {
        var serialized = new SerializedObject(panel);
        var layout = serialized.FindProperty("playerVariablesLayout").objectReferenceValue as UnityEngine.UI.VerticalLayoutGroup;
        if (layout == null) throw new InvalidOperationException(panel.name + " 缺少统一玩家变量布局组。");
        var variables = serialized.FindProperty("playerVariables");
        var keys = new HashSet<string>();
        for (int i = 0; i < variables.arraySize; i++) {
            var entry = variables.GetArrayElementAtIndex(i);
            string key = entry.FindPropertyRelative("key").stringValue;
            var label = entry.FindPropertyRelative("text").objectReferenceValue as TMP_Text;
            if (string.IsNullOrEmpty(key) || !keys.Add(key) || label == null || label.transform.parent.parent != layout.transform)
                throw new InvalidOperationException(panel.name + " 玩家变量引用重复、缺失或未放入统一布局组：" + key);
            if (label.transform.parent.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Any(graphic => graphic.raycastTarget))
                throw new InvalidOperationException(panel.name + " 玩家变量仍拦截射线：" + key);
        }
        if (Keys.Any(key => !keys.Contains(key))) throw new InvalidOperationException(panel.name + " 玩家变量示例未全部烘焙。");
        foreach (string field in new[] { "actionMenu", "infoButton", "muteButton", "recordPerspectiveButton", "showStickerPos", "stickerDisplay" })
            if (serialized.FindProperty(field).objectReferenceValue == null)
                throw new InvalidOperationException(panel.name + " 固定 UI 未烘焙：" + field);
        if (panel.GetComponentsInChildren<TMP_Text>(true).Any(label => label.raycastTarget))
            throw new InvalidOperationException(panel.name + " 文本仍拦截射线。");
    }
}
