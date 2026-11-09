#if UNITY_EDITOR
using System.Linq;
using Mahjong.SceneSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;

public partial class SceneConfigPanel
{
    public void BakeCardSettingsPolish()
    {
        var root = transform.Find("Card3DDesignPanel") as RectTransform;
        if (root == null) return;
        BakeInlineCardEdges(root);
        var surface = root.GetComponent<CardDesignSurfaceLayout>();
        BakeSurfaceActions(surface.background);
        BakeSurfaceActions(surface.back);
        cardPresets.BakeInPageRotation(this, root);
        cardPresets.BakePresetChoices(this, root);
        var face = GetComponentInChildren<CardFaceLayoutPresentation>(true);
        face.RefreshLayout();

        var gallery = (RectTransform)root.Find("CardTableFacePreviews");
        var display = (RectTransform)root.Find("Actual3DPreview");
        var area = (RectTransform)display.Find("CardsArea");
        float areaHeight = area.rect.height;
        float bottom = -display.anchoredPosition.y + display.rect.height;
        float top = -gallery.anchoredPosition.y + gallery.rect.height + 44;
        CardFaceLayoutPresentation.Place(display, display.anchoredPosition.x, top, display.rect.width, bottom - top);
        // Preserve the three cards' display size while opening the gap above them.
        CardPreviewStretch(area, 16, 52, 16, display.rect.height - 52 - areaHeight);
        BakeCardDesignButtonColors();
        SaveCardLayoutPoses(root, face.standard);
        EditorUtility.SetDirty(surface);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private void BakeSurfaceActions(CardDesignImagePicker picker)
    {
        var legacy = picker.transform.Find("LegacySurfaceActions");
        if (legacy == null) legacy = CardPreviewNode(picker.transform, "LegacySurfaceActions", -5000, 0, 0, 0);
        legacy.gameObject.SetActive(false);
        picker.adjustColorButton = picker.colors.buttons[picker.back ? 1 : 0];
        foreach (Transform child in picker.actions.Cast<Transform>().ToArray())
            if (child != picker.renameButton.transform && child != picker.deleteButton.transform
                && (picker.restoreImageButton == null || child != picker.restoreImageButton.transform)
                && (picker.restoreColorButton == null || child != picker.restoreColorButton.transform)
                && child != picker.adjustColorButton.transform) child.SetParent(legacy, false);
        if (picker.restoreImageButton == null)
            picker.restoreImageButton = CardPreviewAction(picker.actions, "RestoreSurfaceImage", picker.back ? "还原牌背" : "还原背景", 0, 0, 198, 56);
        if (picker.restoreColorButton == null)
            picker.restoreColorButton = CardPreviewAction(picker.actions, "RestoreSurfaceColor", "还原颜色", 0, 0, 198, 56);
        picker.adjustColorButton.transform.SetParent(picker.actions, false);
        var buttons = new[] { picker.renameButton, picker.deleteButton, picker.restoreImageButton, picker.restoreColorButton, picker.adjustColorButton };
        foreach (var button in buttons)
        {
            SceneConfigUi.ConfigureCardDesignButton(button, SceneConfigUi.UnselectedBlueGray);
            var text = button.GetComponentInChildren<TMP_Text>(true);
            text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
            text.enableAutoSizing = true; text.fontSizeMin = 16; text.fontSizeMax = 22;
            text.alignment = TextAlignmentOptions.Center;
            CardPreviewStretch(text.rectTransform, button == picker.adjustColorButton ? 52 : 8, 2, 8, 2);
        }
        var swatch = picker.colors.swatches[picker.back ? 1 : 0];
        CardFaceLayoutPresentation.Place(swatch.rectTransform, 12, 12, 32, 32);
        picker.options.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CardFaceLayoutPresentation.ChoiceGridHeight(1, true));
        picker.LayoutActions(true, true);
        EditorUtility.SetDirty(picker);
    }

    public void BakeCardDesignButtonColors()
    {
        var root = transform.Find("Card3DDesignPanel");
        if (root == null) return;
        var surface = root.GetComponent<CardDesignSurfaceLayout>();
        var controls = root.GetComponent<CardDesignColorControls>();
        var buttons = surface.background.actions.GetComponentsInChildren<UnityEngine.UI.Button>(true)
            .Concat(surface.back.actions.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            .Concat(controls.buttons)
            .Concat(new[] {
                CardDesignRef<UnityEngine.UI.Button>(cardEdgePanel, "restoreFrontEdgeButton"),
                CardDesignRef<UnityEngine.UI.Button>(cardEdgePanel, "restoreBackEdgeButton")
            }).Distinct();
        foreach (var button in buttons)
        {
            SceneConfigUi.ConfigureCardDesignButton(button, SceneConfigUi.UnselectedBlueGray);
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(button.targetGraphic);
            EditorUtility.SetDirty(button.GetComponentInChildren<TMP_Text>(true));
        }
        root.GetComponent<CardDesignEdgeSettings>().RefreshButtons();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private void SaveCardLayoutPoses(RectTransform cards, RectTransform faces)
    {
        var style = GetComponentInChildren<SceneSettingsSidebarStyle>(true);
        if (style == null) return;
        var data = new SerializedObject(style);
        var layouts = data.FindProperty("layouts");
        for (int i = 0; i < layouts.arraySize; i++)
        {
            var poses = layouts.GetArrayElementAtIndex(i).FindPropertyRelative("poses");
            for (int j = 0; j < poses.arraySize; j++)
            {
                var pose = poses.GetArrayElementAtIndex(j);
                var target = pose.FindPropertyRelative("target").objectReferenceValue as RectTransform;
                if (target == null || !(target == cards || target.IsChildOf(cards) || target == faces || target.IsChildOf(faces))) continue;
                pose.FindPropertyRelative("anchorMin").vector2Value = target.anchorMin;
                pose.FindPropertyRelative("anchorMax").vector2Value = target.anchorMax;
                pose.FindPropertyRelative("pivot").vector2Value = target.pivot;
                pose.FindPropertyRelative("position").vector2Value = target.anchoredPosition;
                pose.FindPropertyRelative("size").vector2Value = target.sizeDelta;
                pose.FindPropertyRelative("active").boolValue = target.gameObject.activeSelf;
                pose.FindPropertyRelative("preserveActive").boolValue = true;
            }
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(style);
    }
}
#endif
