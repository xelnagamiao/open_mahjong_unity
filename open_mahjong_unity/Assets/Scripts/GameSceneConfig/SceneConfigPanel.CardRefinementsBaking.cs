#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Mahjong.SceneSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;

public partial class SceneConfigPanel
{
    public void BakeCardDesignRefinements()
    {
        var root = transform.Find("Card3DDesignPanel");
        if (root == null) return;
        BakeInlineCardEdges(root);
        BakeThreeCardSlots(root);
        BakeCardRotationInPage();
        cardPresets.BakePresetChoices(this, (RectTransform)root);
        BakeCardSettingsPolish();
        var follow = CardDesignRef<UnityEngine.UI.Button>(cardFaceBgPanel, "handBackFollowButton");
        SetCardPreviewCaption(follow.GetComponentInChildren<TMP_Text>(true), "自动同步手牌背景");
        var icon = GetComponentInChildren<SceneSettingsSidebar>(true).transform.Find("RestoreAllDefaultsButton").GetComponent<SceneSettingsSidebarRow>().icon;
        icon.SetVerticesDirty();
        // Existing shapes predate their required CanvasRenderer declarations.
        foreach (var shape in GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            if ((shape is CardLayoutRoundedGraphic || shape is RuntimeColorPickerSurface) && shape.GetComponent<CanvasRenderer>() == null)
                shape.gameObject.AddComponent<CanvasRenderer>();
        EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    public void BakeCardEdgeExpansion()
    {
        var root = transform.Find("Card3DDesignPanel");
        if (root == null) return;
        BakeInlineCardEdges(root);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    public void BakeCardRotationScrollSection()
    {
        BakeCardRotationInPage();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    public void BakeCardPresetPreviews()
    {
        var root = transform.Find("Card3DDesignPanel") as RectTransform;
        if (root == null) return;
        BakeThreeCardSlots(root);
        BakeCardRotationInPage();
        cardPresets.BakePresetChoices(this, root);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private void BakeInlineCardEdges(Transform root)
    {
        var controls = root.GetComponent<CardDesignEdgeSettings>();
        var layout = root.GetComponent<CardDesignSurfaceLayout>();
        controls.layout = layout;
        var sections = new[] { controls.frontControls, controls.backControls };
        var pickers = new[] { layout.background, layout.back };
        var colors = root.GetComponent<CardDesignColorControls>();
        for (int i = 0; i < 2; i++)
        {
            var section = (RectTransform)sections[i].transform;
            section.SetParent(pickers[i].transform, false);
            var title = section.Find("SectionTitle").GetComponent<TMP_Text>();
            var backdrop = (RectTransform)section.Find("SectionTitleBackground");
            SetCardPreviewCaption(title, i == 0 ? "牌面边缘颜色" : "牌背边缘颜色");
            title.color = new Color32(242, 246, 253, 255);
            title.enableVertexGradient = false;
            title.gameObject.SetActive(true);
            backdrop.gameObject.SetActive(true);
            CardFaceLayoutPresentation.Place(backdrop, 0, 0, 610, 42);
            CardFaceLayoutPresentation.Place(title.rectTransform, 12, 0, 586, 42);
            backdrop.SetAsFirstSibling();
            title.transform.SetSiblingIndex(1);
            var mode = CardDesignRef<UnityEngine.UI.Toggle>(cardEdgePanel, i == 0 ? "frontEdgeModeIndependent" : "backEdgeModeIndependent").transform.parent;
            CardFaceLayoutPresentation.Place((RectTransform)mode, 0, 54, 610, 42);
            CardFaceLayoutPresentation.Place((RectTransform)colors.buttons[i + 2].transform, 0, 116, 430, 44);
            var restore = CardDesignRef<UnityEngine.UI.Button>(cardEdgePanel, i == 0 ? "restoreFrontEdgeButton" : "restoreBackEdgeButton");
            CardFaceLayoutPresentation.Place((RectTransform)restore.transform, 444, 116, 166, 44);
            CardFaceLayoutPresentation.Place(section, 0, 254, 610, 0);
            var fade = section.GetComponent<CanvasGroup>() ?? section.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0; fade.interactable = fade.blocksRaycasts = false;
            if (section.GetComponent<UnityEngine.UI.RectMask2D>() == null) section.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            pickers[i].edgeControls = section.gameObject;
            pickers[i].EdgeExpansion = 0;
            section.gameObject.SetActive(false);
            EditorUtility.SetDirty(pickers[i]);
        }
        if (controls.overlay != null) controls.overlay.SetActive(false);
        controls.RefreshButtons();
        layout.RefreshLayout();
        EditorUtility.SetDirty(controls);
        EditorUtility.SetDirty(layout);
    }

    private void BakeThreeCardSlots(Transform root)
    {
        var display = root.Find("Actual3DPreview");
        var model = display.GetComponentInChildren<CardDesignModelPreview>(true);
        display.Find("CardsArea").SetAsLastSibling();
        var modelRoot = (RectTransform)model.transform;
        var oldFit = model.GetComponent<UnityEngine.UI.AspectRatioFitter>();
        if (oldFit != null) DestroyImmediate(oldFit);
        CardPreviewStretch(modelRoot, 0, 0, 0, 0);
        var oldImage = model.GetComponent<UnityEngine.UI.RawImage>();
        if (oldImage != null) { oldImage.enabled = false; oldImage.raycastTarget = false; oldImage.texture = null; }
        for (int i = 0; i < 3; i++)
        {
            var oldDrag = modelRoot.Find("CardDrag" + i);
            if (oldDrag != null) DestroyImmediate(oldDrag.gameObject);
        }
        var serialized = new SerializedObject(model);
        // A wider transparent viewport preserves the original world-to-pixel scale and centers.
        serialized.FindProperty("renderWidth").intValue = 768;
        serialized.FindProperty("renderHeight").intValue = 960;
        serialized.FindProperty("orthographicSize").floatValue = 2.25f;
        var views = serialized.FindProperty("views"); views.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            var slot = modelRoot.Find("PreviewSlot" + i) as RectTransform;
            if (slot == null) slot = CardPreviewNode(modelRoot, "PreviewSlot" + i, 0, 0, 1, 1);
            slot.anchorMin = new Vector2(i / 3f, 0); slot.anchorMax = new Vector2((i + 1) / 3f, 1);
            slot.offsetMin = new Vector2(12, 0); slot.offsetMax = new Vector2(-12, 0);
            var view = slot.Find("Card") as RectTransform;
            if (view == null) view = CardPreviewNode(slot, "Card", 0, 0, 1, 1);
            var image = view.GetComponent<UnityEngine.UI.RawImage>() ?? view.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            image.enabled = true; image.raycastTarget = true; image.texture = null; image.color = Color.clear;
            var fit = view.GetComponent<UnityEngine.UI.AspectRatioFitter>() ?? view.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 2f / 3f;
            var drag = view.GetComponent<CardDesignPreviewDrag>() ?? view.gameObject.AddComponent<CardDesignPreviewDrag>();
            drag.preview = model; drag.cardIndex = i;
            var overflow = view.Find("UnclippedCard") as RectTransform;
            if (overflow == null) overflow = CardPreviewNode(view, "UnclippedCard", 0, 0, 1, 1);
            overflow.anchorMin = new Vector2(-.25f, -.125f); overflow.anchorMax = new Vector2(1.25f, 1.125f);
            overflow.offsetMin = overflow.offsetMax = Vector2.zero;
            var picture = overflow.GetComponent<UnityEngine.UI.RawImage>() ?? overflow.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            picture.color = Color.white; picture.raycastTarget = false; picture.maskable = false;
            views.GetArrayElementAtIndex(i).objectReferenceValue = picture;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(model);
    }

    private void BakeCardRotationInPage()
    {
        var root = transform.Find("Card3DDesignPanel") as RectTransform;
        cardPresets.BakeInPageRotation(this, root);
        var oldButton = ShowCardRotationPanelButton;
        if (oldButton == null) oldButton = GetComponentInChildren<SceneSettingsSidebar>(true).transform.Find("CardRotationButton")?.GetComponent<UnityEngine.UI.Button>();
        if (oldButton != null)
        {
            var sidebar = GetComponentInChildren<SceneSettingsSidebar>(true);
            var buttons = new List<UnityEngine.UI.Button>(); var pages = new List<GameObject>(); var rows = new List<SceneSettingsSidebarRow>();
            for (int i = 0; i < sidebar.navigation.Length; i++)
                if (sidebar.navigation[i] != oldButton) { buttons.Add(sidebar.navigation[i]); pages.Add(sidebar.pages[i]); rows.Add(sidebar.rows[i]); }
            sidebar.navigation = buttons.ToArray(); sidebar.pages = pages.ToArray(); sidebar.rows = rows.ToArray();
            sidebar.selectedIndex = Mathf.Clamp(sidebar.selectedIndex, 0, Mathf.Max(0, pages.Count - 1));
            var style = sidebar.GetComponent<SceneSettingsSidebarStyle>();
            if (style != null)
            {
                var value = new SerializedObject(style); var styledRows = value.FindProperty("styledRows");
                var oldRow = oldButton.GetComponent<SceneSettingsSidebarRow>();
                for (int i = styledRows.arraySize - 1; i >= 0; i--)
                    if (styledRows.GetArrayElementAtIndex(i).objectReferenceValue == oldRow) { styledRows.GetArrayElementAtIndex(i).objectReferenceValue = null; styledRows.DeleteArrayElementAtIndex(i); }
                var layouts = value.FindProperty("layouts");
                for (int i = 0; i < layouts.arraySize; i++)
                {
                    var poses = layouts.GetArrayElementAtIndex(i).FindPropertyRelative("poses");
                    for (int n = poses.arraySize - 1; n >= 0; n--)
                    {
                        var target = poses.GetArrayElementAtIndex(n).FindPropertyRelative("target").objectReferenceValue as Transform;
                        if (target != null && (target == oldButton.transform || target.IsChildOf(oldButton.transform))) poses.DeleteArrayElementAtIndex(n);
                    }
                }
                value.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(style);
            }
            ShowCardRotationPanelButton = null;
            DestroyImmediate(oldButton.gameObject);
            EditorUtility.SetDirty(sidebar);
        }
        EditorUtility.SetDirty(cardPresets);
    }
}
#endif
