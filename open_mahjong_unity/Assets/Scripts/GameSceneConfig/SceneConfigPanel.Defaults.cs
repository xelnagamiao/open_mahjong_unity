using Mahjong.SceneSettingsUI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public partial class SceneConfigPanel
{
    private bool resetControlsReady;
    private MessagePrefab resetConfirmation;

    private void EnsureResetControls()
    {
        if (resetControlsReady) return;
        resetControlsReady = true;
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
            var presets = cardRoot.GetComponent<Card3DPresetPanel>() ?? cardRoot.gameObject.AddComponent<Card3DPresetPanel>();
            presets.Initialize(this);
        }
        BuildGlobalResetButton();
        GetComponentInChildren<SceneSettingsSidebar>(true)?.ApplyCompactLayout();
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
            }
        }
        PlaceFooterButton(upload, true, 24);
        PlaceFooterButton(remove, true, 196);
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
        button.onClick.AddListener(action);
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
        reset.onClick.AddListener(ConfirmRestoreAllDefaults);
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

    private void RestoreTablecloth()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefaultTablecloth();
        Desktop.Instance?.RefreshTablecloth();
        Desktop.Instance?.RefreshEdge();
        tableClothPanel.GetComponent<TableSeamSelector>()?.RefreshSelection();
        RefreshPage();
    }

    private void RestoreTableFrame()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefaultTableFrame();
        Desktop.Instance?.RefreshEdge();
        tableClothPanel.GetComponent<TableSeamSelector>()?.RefreshSelection();
        RefreshPage();
    }

    private void RestoreCenterDisplay()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefaultCenterDisplay();
        centerDisplayPanel.ShowPanel();
    }

    private void Restore3DCards()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreDefault3DCards();
        RefreshDefaultCards(false);
        RefreshPage();
    }

    private void RefreshDefaultCards(bool includeHand)
    {
        CardBackManager.RefreshAfterDefaults(includeHand);
        if (cardBackPanel.isActiveAndEnabled) cardBackPanel.ReloadSaved();
        if (cardEdgePanel.isActiveAndEnabled) cardEdgePanel.ReloadSaved();
        if (cardFacePanel.isActiveAndEnabled) cardFacePanel.RefreshHighlights();
        if (cardFaceBgPanel.isActiveAndEnabled) cardFaceBgPanel.RefreshSolidColorUi();
    }

    private void ConfirmRestoreAllDefaults()
    {
        if (resetConfirmation != null || ConfigManager.Instance == null) return;
        if (NotificationManager.Instance == null) return;
        resetConfirmation = NotificationManager.Instance.ShowConfirmation("恢复全部场景设置",
            "确定将所有场景设置恢复为默认吗？\n桌布、边框、中心盘、牌面及3D卡牌外观将被重置。\n已上传的图片和牌面包会保留。",
            () => { if (this != null) RestoreAllDefaults(); }, "确定恢复", "取消");
    }

    private void RestoreAllDefaults()
    {
        if (ConfigManager.Instance == null) return;
        ConfigManager.Instance.RestoreAllSceneDefaults();
        TileFaceResolver.SelectPack(TilePackIds.PackOfficial);
        RefreshDefaultCards(true);
        Desktop.Instance?.RefreshTablecloth();
        Desktop.Instance?.RefreshEdge();
        tableClothPanel.GetComponent<TableSeamSelector>()?.RefreshSelection();
        if (centerDisplayPanel != null) centerDisplayPanel.ReloadSaved();
        RefreshPage();
        SceneConfigUi.ShowTip("场景设置已恢复默认");
    }
}
