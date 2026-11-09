#if UNITY_EDITOR
using System.Linq;
using Mahjong.SceneSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;

public partial class SceneConfigPanel
{
    /// <summary>Author the rotation sidebar page and uploaded-pack buttons in the existing UI.</summary>
    public void BakeCardDesignUsability()
    {
        var cardRoot = transform.Find("Card3DDesignPanel") as RectTransform;
        if (cardRoot == null || cardFacePanel == null || cardFaceBgPanel == null) return;

        // Keep the controller alive when switching between design and rotation pages.
        var previous = cardRoot.GetComponent<Card3DPresetPanel>();
        var persistent = GetComponent<Card3DPresetPanel>();
        if (persistent == null) persistent = gameObject.AddComponent<Card3DPresetPanel>();
        if (previous != null && previous != persistent)
        {
            EditorUtility.CopySerialized(previous, persistent);
            DestroyImmediate(previous);
        }
        cardPresets = persistent;
        BakeCardRotationInPage();

        var presentation = GetComponentInChildren<CardFaceLayoutPresentation>(true);
        if (presentation == null) return;
        var grid = presentation.faceSection.Find("UploadedPackButtons") as RectTransform;
        if (grid == null)
        {
            var node = new GameObject("UploadedPackButtons", typeof(RectTransform));
            node.layer = presentation.gameObject.layer;
            grid = (RectTransform)node.transform;
            grid.SetParent(presentation.faceSection, false);
        }
        CardFaceLayoutPresentation.Place(grid, 0, 146, 610, 0);
        var picker = grid.GetComponent<CardFaceUploadedPackPicker>() ?? grid.gameObject.AddComponent<CardFaceUploadedPackPicker>();
        picker.owner = cardFacePanel;
        picker.presentation = presentation;
        picker.optionTemplate = CardDesignRef<UnityEngine.UI.Button>(cardFacePanel, "restoreButton");
        picker.renameButton = CreateResetButton(presentation.faceSection, "RenameUploadedPackButton", "重命名", null, picker.optionTemplate);
        picker.deleteButton = CreateResetButton(presentation.faceSection, "DeleteUploadedPackButton", "删除", null, picker.optionTemplate);
        CardFaceLayoutPresentation.Place((RectTransform)picker.renameButton.transform, 430, 4, 92, 34);
        CardFaceLayoutPresentation.Place((RectTransform)picker.deleteButton.transform, 534, 4, 76, 34);
        picker.renameButton.gameObject.SetActive(false);
        picker.deleteButton.gameObject.SetActive(false);
        grid.gameObject.SetActive(false);
        CardDesignSetRef(cardFacePanel, "uploadedPackPicker", picker);
        presentation.uploadedPacks = picker;
        presentation.faceLibrary.gameObject.SetActive(false);

        SquareCardUpload(CardDesignRef<UnityEngine.UI.Button>(cardFacePanel, "uploadButton"));
        SquareCardUpload(CardDesignRef<UnityEngine.UI.Button>(cardFaceBgPanel, "uploadHandBgButton"));
        SquareCardUpload(CardDesignRef<UnityEngine.UI.Button>(cardFaceBgPanel, "uploadCardBackButton"));
        var follow = CardDesignRef<UnityEngine.UI.Button>(cardFaceBgPanel, "handBackFollowButton");
        follow.transform.SetParent(presentation.backSection, false);
        CardFaceLayoutPresentation.Place((RectTransform)follow.transform, 362, 1, 248, 40);
        var label = follow.GetComponentInChildren<TMP_Text>(true);
        label.text = "自动同步手牌背景";
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.enableAutoSizing = true;
        label.fontSizeMax = 23;
        label.fontSizeMin = 18;
        SceneConfigUi.SetButtonSelected(follow, true);
        follow.gameObject.SetActive(true);
        presentation.RefreshLayout();
        BakeCardDesignPreviewLayout();
        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(cardPresets);
        EditorUtility.SetDirty(cardFacePanel);
        EditorUtility.SetDirty(presentation);
        EditorUtility.SetDirty(picker);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private static void SquareCardUpload(UnityEngine.UI.Button button)
    {
        var rect = (RectTransform)button.transform;
        float x = rect.anchoredPosition.x;
        float y = -rect.anchoredPosition.y + (rect.rect.height - 62) * .5f;
        CardFaceLayoutPresentation.Place(rect, x, y, 62, 62);
        var text = button.GetComponentInChildren<TMP_Text>(true);
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
    }

    private static T CardDesignRef<T>(UnityEngine.Object target, string field) where T : UnityEngine.Object
        => (T)new SerializedObject(target).FindProperty(field).objectReferenceValue;

    private static void CardDesignSetRef(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
