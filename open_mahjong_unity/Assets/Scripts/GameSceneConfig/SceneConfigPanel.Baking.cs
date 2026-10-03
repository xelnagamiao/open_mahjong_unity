#if UNITY_EDITOR
using Mahjong.SceneSettingsUI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public partial class SceneConfigPanel
{
    public void BakeButtonFeedback()
    {
        // Style authored action backgrounds, retaining each swatch's own base color.
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            if (button.GetComponent<SceneSettingsSidebarRow>() != null || !(button.targetGraphic is Image image)) continue;
            Color normal = image.color;
            if (button.transition == Selectable.Transition.ColorTint) normal *= button.colors.normalColor;
            if (!Near(normal, SceneConfigUi.TabOff) && !Near(normal, SceneConfigUi.TabOn)
                && !Near(normal, SceneConfigUi.UnselectedBlueGray) && !button.name.Contains("Swatch")) continue;
            SceneConfigUi.ConfigureButtonFeedback(button, normal);
        }
    }

    private static bool Near(Color a, Color b)
        => Mathf.Abs(a.r-b.r) < .02f && Mathf.Abs(a.g-b.g) < .02f && Mathf.Abs(a.b-b.b) < .02f && a.a > .9f;

    public void BakeFixedUi()
    {
        if (HasBakedFixedUi) return;
        BuildSurfaceFooter(tableClothPanel, tableClothPanel.contentParent, "UpdateButton", RestoreTablecloth);
        BuildSurfaceFooter(tableEdgePanel, tableEdgePanel.contentParent, "UploadButton", RestoreTableFrame);
        if (centerDisplayPanel != null)
        {
            AddHeaderReset(centerDisplayPanel.transform, "RestoreDefaultButton", "恢复默认", RestoreCenterDisplay, 174);
            var help = centerDisplayPanel.transform.Find("HelpText");
            if (help != null) help.gameObject.SetActive(false);
        }
        var cardRoot = transform.Find("Card3DDesignPanel");
        if (cardRoot != null) {
            AddHeaderReset(cardRoot, "Restore3DDefaultButton", "恢复3D默认", Restore3DCards, 200);
            cardPresets = cardRoot.GetComponent<Card3DPresetPanel>() ?? cardRoot.gameObject.AddComponent<Card3DPresetPanel>();
            cardPresets.BakeLayout(this);
        }
        BuildGlobalResetButton();
        GetComponentInChildren<SceneSettingsSidebar>(true)?.ApplyCompactLayout();
        var seams = tableClothPanel.GetComponent<TableSeamSelector>() ?? tableClothPanel.gameObject.AddComponent<TableSeamSelector>();
        seams.BakeLayout(tableClothPanel);
        TableSurfaceColorEditor.Ensure(tableClothPanel).BakePalette();
        TableSurfaceColorEditor.Ensure(tableEdgePanel).BakePalette();
        tableClothPanel.ConfigureGalleryScrolling(); tableEdgePanel.ConfigureGalleryScrolling();
        clothReset = tableClothPanel.transform.Find("RestoreDefaultButton").GetComponent<Button>();
        frameReset = tableEdgePanel.transform.Find("RestoreDefaultButton").GetComponent<Button>();
        centerReset = centerDisplayPanel.transform.Find("RestoreDefaultButton").GetComponent<Button>();
        cardsReset = cardRoot.Find("Restore3DDefaultButton").GetComponent<Button>();
        allReset = HideAllPanelButton.transform.parent.Find("RestoreAllDefaultsButton").GetComponent<Button>();
        newCloth = tableClothPanel.transform.Find("NewSurfaceColorButton").GetComponent<Button>();
        newFrame = tableEdgePanel.transform.Find("NewSurfaceColorButton").GetComponent<Button>();
        fixedUiVersion = 1;
    }

    private void BuildSurfaceFooter(Component panel, Transform content, string uploadName, UnityAction restore)
    {
        if (panel == null) return;
        Transform root = panel.transform;
        var upload = root.Find(uploadName)?.GetComponent<Button>();
        var remove = root.Find("RemoveButton")?.GetComponent<Button>();
        // Only these direct-child legacy descriptions belong to the upload footer.
        foreach (Transform child in root)
        {
            var text = child.GetComponent<TMP_Text>();
            if (text != null && text.text.Contains("PlayerPrefs") && text.text.Contains("1MB"))
            {
                text.text = "";
                child.gameObject.SetActive(false);
            }
        }
        var gallery = content != null ? content.GetComponentInParent<ScrollRect>(true) : null;
        if (gallery != null)
        {
            var rect = (RectTransform)gallery.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(0, 72);
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<TableSeamSelector>()?.RefreshGalleryBounds();
            if (panel is TableEdgePanel)
            {
                var header = root.Find("SurfaceHeader") as RectTransform;
                if (header == null)
                {
                    var go = new GameObject("SurfaceHeader", typeof(RectTransform), typeof(Image));
                    go.layer = root.gameObject.layer;
                    header = (RectTransform)go.transform;
                    header.SetParent(root, false);
                    var background = go.GetComponent<Image>();
                    background.color = SceneConfigUi.SurfaceHeaderBackground;
                    background.raycastTarget = false;
                    var fontSource = (upload != null ? upload : HideAllPanelButton).GetComponentInChildren<TMP_Text>(true);
                    SceneConfigUi.CreateSurfaceHeaderTitle(header, "边框", fontSource.font);
                }
                header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one;
                header.offsetMin = new Vector2(0, -SceneConfigUi.SurfaceHeaderHeight);
                header.offsetMax = Vector2.zero;
                rect.offsetMax = new Vector2(0, -SceneConfigUi.SurfaceHeaderHeight);
                var frameHeader = panel.GetComponent<TableFrameHeader>() ?? panel.gameObject.AddComponent<TableFrameHeader>();
                frameHeader.BakeLayout(header, rect, header.GetComponentInChildren<TMP_Text>().font);
            }
        }
        PlaceFooterButton(upload, true, 24);
        var surface = panel as TableSurfacePanel;
        if (surface != null) {
            var create = CreateResetButton(root, "NewSurfaceColorButton", surface.IsClothSurface ? "新建桌布" : "新建边框", surface.BeginNewColor, upload);
            PlaceFooterButton(create, true, 196);
        }
        PlaceFooterButton(remove, true, 368);
        var reset = CreateResetButton(root, "RestoreDefaultButton", "恢复默认", restore, upload);
        PlaceFooterButton(reset, false, 24);
    }

    private static void PlaceFooterButton(Button button, bool right, float inset)
    {
        if (button == null) return;
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1 : 0, 0);
        rect.anchoredPosition = new Vector2(right ? -inset : inset, 14);
        rect.sizeDelta = new Vector2(160, 44);
        var text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null) FitResetButtonText(text);
    }

    private void AddHeaderReset(Transform root, string name, string label, UnityAction action, float width)
    {
        var button = CreateResetButton(root, name, label, action);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-30, -24);
        rect.sizeDelta = new Vector2(width, 42);
        var title = root.Find("Title") as RectTransform;
        if (title != null)
        {
            title.anchorMin = new Vector2(0, 1);
            title.anchorMax = Vector2.one;
            title.pivot = new Vector2(0, 1);
            title.offsetMin = new Vector2(30, title.offsetMin.y);
            title.offsetMax = new Vector2(-width - 48, title.offsetMax.y);
        }
    }

    private Button CreateResetButton(Transform parent, string name, string caption, UnityAction action, Button fontTemplate = null)
    {
        var existing = parent.Find(name)?.GetComponent<Button>();
        if (existing != null) return existing;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.targetGraphic.color = Color.white;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = SceneConfigUi.TabOff;
        colors.highlightedColor = new Color(.25f, .31f, .42f, 1);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.12f, .16f, .24f, 1);
        colors.disabledColor = new Color(.35f, .38f, .43f, 1);
        button.colors = colors;
        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.layer = go.layer;
        textObject.transform.SetParent(go.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        var source = (fontTemplate != null ? fontTemplate : HideAllPanelButton)?.GetComponentInChildren<TMP_Text>(true);
        if (source != null) text.font = source.font;
        text.text = caption;
        text.color = new Color32(238, 243, 250, 255);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(10, 4);
        text.rectTransform.offsetMax = new Vector2(-10, -4);
        FitResetButtonText(text);
        return button;
    }

    private static void FitResetButtonText(TMP_Text text)
    {
        text.enableAutoSizing = true;
        text.fontSize = text.fontSizeMax = 22;
        text.fontSizeMin = 16;
        text.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void BuildGlobalResetButton()
    {
        if (HideAllPanelButton == null) return;
        Transform parent = HideAllPanelButton.transform.parent;
        if (parent.Find("RestoreAllDefaultsButton") != null) return;
        var reset = Instantiate(HideAllPanelButton, parent, false);
        reset.name = "RestoreAllDefaultsButton";
        // A cloned action must never retain the visibility button's callbacks.
        reset.onClick = new Button.ButtonClickedEvent();
        var label = reset.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "恢复默认";
        var row = reset.GetComponent<SceneSettingsSidebarRow>();
        if (row != null)
        {
            row.selected = false;
            row.open = true;
            if (row.band != null) row.band.resetAction = true;
            if (row.icon != null) { row.icon.kind = 9; row.icon.SetVerticesDirty(); }
            row.Draw(true);
        }
        reset.gameObject.SetActive(true);
    }
}
#endif
