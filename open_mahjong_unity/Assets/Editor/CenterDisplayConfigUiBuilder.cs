using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 显式增量搭建中心盘设置页；没有导入回调，不会自行打开或保存主场景。
/// 验证副本可调用 BuildAndSaveActiveScene，迁移时只取新增对象和两处绑定。
/// </summary>
public static class CenterDisplayConfigUiBuilder
{
    [MenuItem("Tools/Mahjong/Scene Settings/Add Center Display Page")]
    public static void BuildActiveScene()
    {
        SceneConfigPanel owner = FindPanelInActiveScene();
        Build(owner);
        Selection.activeGameObject = owner.transform.Find("CenterDisplayPanel").gameObject;
    }

    public static void BuildAndSaveActiveScene()
    {
        SceneConfigPanel owner = FindPanelInActiveScene();
        Build(owner);
        if (!EditorSceneManager.SaveScene(owner.gameObject.scene))
            throw new InvalidOperationException("Failed to save the explicitly selected scene.");
        Debug.Log("CENTER_CONFIG_UI_BUILT " + owner.gameObject.scene.path);
    }

    public static CenterDisplayConfigPanel Build(SceneConfigPanel owner)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Build the settings UI outside Play mode.");
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        Transform navigation = owner.transform.Find("NavigateBar");
        if (navigation == null) throw new InvalidOperationException("SceneConfigPanel/NavigateBar is missing.");
        Button template = navigation.Find("TableClothButton")?.GetComponent<Button>();
        if (template == null) throw new InvalidOperationException("The existing tablecloth navigation button is missing.");
        TMP_Text sourceLabel = template.GetComponentInChildren<TMP_Text>(true);
        if (sourceLabel == null || sourceLabel.font == null)
            throw new InvalidOperationException("The existing navigation font is missing.");
        RectTransform sourcePage = owner.transform.Find("CardFacePanel") as RectTransform;
        if (sourcePage == null) throw new InvalidOperationException("The existing card-face content rectangle is missing.");

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add center display settings page");
        Transform existingPage = owner.transform.Find("CenterDisplayPanel");
        CenterDisplayConfigPanel panel;
        if (existingPage != null)
        {
            panel = existingPage.GetComponent<CenterDisplayConfigPanel>();
            if (panel == null) throw new InvalidOperationException("CenterDisplayPanel exists without its settings component.");
            ValidateOptions(panel);
        }
        else
        {
            panel = CreatePage(owner.transform, sourcePage, sourceLabel.font);
            Undo.RegisterCreatedObjectUndo(panel.gameObject, "Create center display page");
        }

        // This project's font needs 1.4 times its point size for a complete line.
        // Ellipsis otherwise removes an entire short label when its height is too small.
        foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            RectTransform textRect = text.rectTransform;
            float lineRatio = text.font != null
                ? (text.font.faceInfo.ascentLine - text.font.faceInfo.descentLine) / Mathf.Max(1f, text.font.faceInfo.pointSize)
                : 1.4f;
            float requiredHeight = text.fontSize * (lineRatio + .05f);
            if (textRect.rect.height < requiredHeight)
            {
                Undo.RecordObject(textRect, "Fit center display label font metrics");
                textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
            }
        }

        Button button = navigation.Find("CenterDisplayButton")?.GetComponent<Button>();
        if (button == null)
        {
            GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, navigation, false);
            copy.name = "CenterDisplayButton";
            button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            copy.GetComponentInChildren<TMP_Text>(true).text = "中心盘";
            Transform edge = navigation.Find("TableEdgeButton");
            copy.transform.SetSiblingIndex(edge != null ? edge.GetSiblingIndex() + 1 : template.transform.GetSiblingIndex() + 1);
            Undo.RegisterCreatedObjectUndo(copy, "Create center display navigation button");
        }

        Undo.RecordObject(owner, "Bind center display settings page");
        var ownerData = new SerializedObject(owner);
        ownerData.FindProperty("centerDisplayPanel").objectReferenceValue = panel;
        ownerData.FindProperty("ShowCenterDisplayPanelButton").objectReferenceValue = button;
        ownerData.ApplyModifiedProperties();
        EditorUtility.SetDirty(owner);
        EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        return panel;
    }

    private static CenterDisplayConfigPanel CreatePage(Transform parent, RectTransform source, TMP_FontAsset font)
    {
        GameObject page = new GameObject("CenterDisplayPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        page.layer = parent.gameObject.layer;
        page.SetActive(false);
        RectTransform rect = (RectTransform)page.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.anchoredPosition = source.anchoredPosition;
        rect.sizeDelta = new Vector2(900f, 820f);
        page.GetComponent<Image>().color = new Color(0.045f, 0.061f, 0.085f, 0.98f);
        CenterDisplayConfigPanel panel = page.AddComponent<CenterDisplayConfigPanel>();

        MakeText("Title", rect, font, "中心盘", 36f, 30f, 24f, 840f, 48f, TextAlignmentOptions.MidlineLeft);
        MakeText("Description", rect, font, "选择牌桌中央的显示样式", 23f, 30f, 78f, 840f, 30f, TextAlignmentOptions.MidlineLeft);
        TMP_Text status = MakeText("StatusText", rect, font, "当前：项目默认  ·  选择后自动保存", 21f,
            30f, 112f, 840f, 28f, TextAlignmentOptions.MidlineLeft);
        status.color = new Color(0.70f, 0.76f, 0.85f, 1f);

        RectTransform grid = MakeRect("StyleGrid", rect, 30f, 156f, 840f, 592f);
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(266f, 286f);
        layout.spacing = new Vector2(20f, 20f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 3;
        layout.childAlignment = TextAnchor.UpperLeft;

        var data = new SerializedObject(panel);
        SerializedProperty options = data.FindProperty("options");
        options.arraySize = CenterDisplayStyles.All.Count;
        for (int i = 0; i < CenterDisplayStyles.All.Count; i++)
        {
            CenterDisplayStyles.Entry entry = CenterDisplayStyles.All[i];
            RectTransform card = MakeRect("Style_" + entry.Id, grid, 0f, 0f, 266f, 286f);
            Image border = card.gameObject.AddComponent<Image>();
            border.color = entry.Id == "classic" ? SceneConfigUi.SelectedOrange : SceneConfigUi.UnselectedBlueGray;
            Button select = card.gameObject.AddComponent<Button>();
            select.targetGraphic = border;
            select.transition = Selectable.Transition.None;
            Image inset = MakeRect("Surface", card, 2f, 2f, 262f, 282f).gameObject.AddComponent<Image>();
            inset.color = new Color(0.08f, 0.105f, 0.15f, 1f);
            inset.raycastTarget = false;
            RawImage preview = MakeRect("Preview", card, 19f, 12f, 228f, 228f).gameObject.AddComponent<RawImage>();
            preview.texture = CenterDisplayStyles.GetPreview(entry.Id);
            preview.color = Color.white;
            preview.raycastTarget = false;
            preview.gameObject.SetActive(preview.texture != null);
            TMP_Text fallback = MakeText("Fallback", card, font,
                entry.Id == "classic" ? "项目原始样式" : "预览暂未载入", 23f, 19f, 12f, 228f, 228f, TextAlignmentOptions.Center);
            fallback.color = new Color(0.70f, 0.76f, 0.85f, 1f);
            fallback.gameObject.SetActive(preview.texture == null);
            TMP_Text name = MakeText("Name", card, font, entry.Name, 23f, 16f, 247f, 190f, 28f, TextAlignmentOptions.MidlineLeft);
            RectTransform badge = MakeRect("SelectedBadge", card, 213f, 250f, 40f, 23f);
            Image badgeImage = badge.gameObject.AddComponent<Image>();
            badgeImage.color = SceneConfigUi.SelectedOrange;
            badgeImage.raycastTarget = false;
            TMP_Text badgeLabel = MakeText("Label", badge, font, "已选", 16f, 0f, 0f, 40f, 23f, TextAlignmentOptions.Center);
            badgeLabel.color = new Color(0.10f, 0.12f, 0.16f, 1f);
            badge.gameObject.SetActive(entry.Id == "classic");

            SerializedProperty option = options.GetArrayElementAtIndex(i);
            option.FindPropertyRelative("id").stringValue = entry.Id;
            option.FindPropertyRelative("button").objectReferenceValue = select;
            option.FindPropertyRelative("preview").objectReferenceValue = preview;
            option.FindPropertyRelative("nameText").objectReferenceValue = name;
            option.FindPropertyRelative("selectedBadge").objectReferenceValue = badge.gameObject;
            option.FindPropertyRelative("fallback").objectReferenceValue = fallback.gameObject;
        }
        data.FindProperty("statusText").objectReferenceValue = status;
        data.ApplyModifiedPropertiesWithoutUndo();
        MakeText("HelpText", rect, font, "点击卡片即可应用；选择「项目默认」可恢复原始中心盘。", 20f,
            30f, 774f, 840f, 28f, TextAlignmentOptions.MidlineLeft).color = new Color(0.70f, 0.76f, 0.85f, 1f);
        return panel;
    }

    private static RectTransform MakeRect(string name, Transform parent, float x, float top, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static TMP_Text MakeText(string name, Transform parent, TMP_FontAsset font, string value, float size,
        float x, float top, float width, float height, TextAlignmentOptions alignment)
    {
        TMP_Text text = MakeRect(name, parent, x, top, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static void ValidateOptions(CenterDisplayConfigPanel panel)
    {
        SerializedProperty options = new SerializedObject(panel).FindProperty("options");
        int styleCount = CenterDisplayStyles.All.Count;
        // Older scenes keep a complete catalog prefix. Awake appends missing
        // cards and confines any extra rows to the existing grid viewport.
        if (options.arraySize == 0 || options.arraySize > styleCount)
            throw new InvalidOperationException("Existing center display page has incomplete bindings; inspect it before rebuilding.");
        for (int i = 0; i < options.arraySize; i++)
        {
            SerializedProperty option = options.GetArrayElementAtIndex(i);
            if (option.FindPropertyRelative("id").stringValue != CenterDisplayStyles.All[i].Id)
                throw new InvalidOperationException("Existing center display page has an invalid card binding at index " + i);
            foreach (string reference in new[] { "button", "preview", "nameText", "selectedBadge", "fallback" })
                if (option.FindPropertyRelative(reference).objectReferenceValue == null)
                    throw new InvalidOperationException("Existing center display page has a missing " + reference + " binding at index " + i);
        }
    }

    private static SceneConfigPanel FindPanelInActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
            throw new InvalidOperationException("Explicitly open the target saved scene before adding its settings page.");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            SceneConfigPanel panel = root.GetComponentInChildren<SceneConfigPanel>(true);
            if (panel != null) return panel;
        }
        throw new InvalidOperationException("The active scene has no SceneConfigPanel.");
    }
}
