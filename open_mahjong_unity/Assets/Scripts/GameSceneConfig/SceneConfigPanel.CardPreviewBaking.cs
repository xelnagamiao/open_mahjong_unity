#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

public partial class SceneConfigPanel
{
    /// <summary>Keep the real preview models and author the compact 3D surface controls.</summary>
    public void BakeCardDesignPreviewLayout()
    {
        var root = transform.Find("Card3DDesignPanel");
        if (root == null) return;
        if (root.GetComponent<CardDesignSurfaceLayout>() != null) { BakeCardDesignRefinements(); return; }
        var cardLayout = CardDesignRef<CardDesignScrollLayout>(cardDesignTabs, "cardScrollLayout");
        var scroll = CardDesignRef<UnityEngine.UI.ScrollRect>(cardLayout, "settingsScroll");
        var colors = root.GetComponent<CardDesignColorControls>();
        var model = root.Find("Actual3DPreview").GetComponentInChildren<CardDesignModelPreview>(true);
        var gallery = root.Find("CardTableFacePreviews");
        SetCardPreviewCaption(gallery.Find("Title").GetComponent<TMP_Text>(), "3D牌面（选中预览）");
        gallery.Find("Hint").gameObject.SetActive(false);
        var display = root.Find("Actual3DPreview") as RectTransform;
        foreach (Transform child in display)
            if (child.GetComponent<TMP_Text>() != null && child.name != "ModelTitle") child.gameObject.SetActive(false);
        SetCardPreviewCaption(display.Find("ModelTitle").GetComponent<TMP_Text>(), "立体卡牌展示（拖动旋转）");
        var area = CardPreviewNode(display, "CardsArea", 0, 0, 1, 1);
        CardPreviewStretch(area, 16, 52, 16, 16);
        model.transform.SetParent(area, false);
        var modelRect = (RectTransform)model.transform;
        var fit = model.GetComponent<UnityEngine.UI.AspectRatioFitter>() ?? model.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
        fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 1536f / 576f;
        var picture = model.GetComponent<UnityEngine.UI.RawImage>() ?? model.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        picture.raycastTarget = false;
        for (int i = 0; i < 3; i++)
        {
            var hit = CardPreviewNode(modelRect, "CardDrag" + i, 0, 0, 1, 1);
            var graphic = hit.gameObject.AddComponent<UnityEngine.UI.Image>(); graphic.color = Color.clear; graphic.raycastTarget = true;
            float center = .5f + (i - 1) * (2.25f / 9.6f);
            hit.anchorMin = new Vector2(center - .108f, .1f); hit.anchorMax = new Vector2(center + .108f, .9f);
            hit.offsetMin = hit.offsetMax = Vector2.zero;
            var drag = hit.gameObject.AddComponent<CardDesignPreviewDrag>(); drag.preview = model; drag.cardIndex = i;
        }

        var surfaceLayout = root.gameObject.AddComponent<CardDesignSurfaceLayout>(); surfaceLayout.content = scroll.content;
        var backgroundRoot = CardDesignRef<GameObject>(cardDesignTabs, "tableFacePage").transform;
        var backRoot = cardBackPanel.transform;
        surfaceLayout.background = BakeCardSurface(backgroundRoot, false, colors, surfaceLayout);
        surfaceLayout.back = BakeCardSurface(backRoot, true, colors, surfaceLayout);
        CardDesignSetRef(cardFaceBgPanel, "tableImageLibrary", surfaceLayout.background);
        CardDesignSetRef(cardBackPanel, "imageLibrary", surfaceLayout.back);
        BakeCardEdgeDialog(root, backgroundRoot, backRoot, colors);
        surfaceLayout.RefreshLayout();
        BakeCardDesignRefinements();
        EditorUtility.SetDirty(cardFaceBgPanel); EditorUtility.SetDirty(cardBackPanel);
        EditorUtility.SetDirty(surfaceLayout); EditorUtility.SetDirty(model);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private CardDesignImagePicker BakeCardSurface(Transform section, bool back, CardDesignColorControls colors, CardDesignSurfaceLayout layout)
    {
        var old = CardPreviewNode(section, "LegacyControls", 0, 0, 0, 0);
        var children = new System.Collections.Generic.List<Transform>(); foreach (Transform child in section) children.Add(child);
        foreach (var child in children)
        {
            if (child == old || child.name == "SectionTitle" || child.name == "SectionTitleBackground") continue;
            child.SetParent(old, false);
        }
        old.gameObject.SetActive(false);
        var title = section.Find("SectionTitle").GetComponent<TMP_Text>();
        SetCardPreviewCaption(title, back ? "牌背" : "3D牌面背景");
        CardFaceLayoutPresentation.Place(title.rectTransform, 12, 0, 310, 42);
        var picker = section.gameObject.AddComponent<CardDesignImagePicker>();
        picker.back = back; picker.colors = colors; picker.layout = layout;
        picker.options = CardPreviewNode(section, "ImageOptions", 0, 54, 610, 86);
        picker.optionTemplate = BakeCardSurfaceOption(picker.options, "ImageOptionTemplate", ""); picker.optionTemplate.gameObject.SetActive(false);
        picker.solidButton = Instantiate(picker.optionTemplate, picker.options, false); picker.solidButton.name = "SolidColor";
        picker.solidButton.gameObject.SetActive(true);
        picker.solidButton.GetComponent<CardDesignImageOption>().SetSolid(back ? ConfigManager.DefaultCardBackColor : ConfigManager.DefaultTableFaceColor);
        CardFaceLayoutPresentation.Place((RectTransform)picker.solidButton.transform, 0, 0, 198, 80);
        var uploadTemplate = CardDesignRef<UnityEngine.UI.Button>(cardFacePanel, "uploadButton");
        picker.uploadButton = Instantiate(uploadTemplate, picker.options, false); picker.uploadButton.name = "UploadImage";
        picker.uploadButton.onClick = new UnityEngine.UI.Button.ButtonClickedEvent(); picker.uploadButton.gameObject.SetActive(true);
        CardFaceLayoutPresentation.Place((RectTransform)picker.uploadButton.transform, 206, 9, 62, 62);
        CardDesignSetRef(back ? (UnityEngine.Object)cardBackPanel : cardFaceBgPanel, back ? "pickImageButton" : "uploadTableBgButton", picker.uploadButton);
        picker.actions = CardPreviewNode(section, "ImageActions", 0, 148, 610, 38);
        picker.renameButton = CardPreviewAction(picker.actions, "RenameImage", "重命名", 0, 0, back ? 172 : 134, 38);
        picker.deleteButton = CardPreviewAction(picker.actions, "RemoveImage", "移除", back ? 184 : 146, 0, back ? 172 : 134, 38);
        if (back)
        {
            var restore = CardDesignRef<UnityEngine.UI.Button>(cardBackPanel, "restoreButton");
            restore.transform.SetParent(picker.actions, false); restore.gameObject.SetActive(true);
            CardFaceLayoutPresentation.Place((RectTransform)restore.transform, 444, 0, 166, 38);
            SetCardPreviewCaption(restore.GetComponentInChildren<TMP_Text>(true), "还原牌背");
        }
        else
        {
            var restore = CardDesignRef<UnityEngine.UI.Button>(cardFaceBgPanel, "restoreTableBgButton");
            restore.transform.SetParent(picker.actions, false); restore.gameObject.SetActive(true);
            CardFaceLayoutPresentation.Place((RectTransform)restore.transform, 292, 0, 152, 38);
            SetCardPreviewCaption(restore.GetComponentInChildren<TMP_Text>(true), "还原背景");
            var colorReset = CardDesignRef<UnityEngine.UI.Button>(cardFaceBgPanel, "restoreTableFaceColorButton");
            colorReset.transform.SetParent(picker.actions, false); colorReset.gameObject.SetActive(true);
            CardFaceLayoutPresentation.Place((RectTransform)colorReset.transform, 456, 0, 154, 38);
        }
        picker.colorControl = (RectTransform)colors.buttons[back ? 1 : 0].transform;
        picker.colorControl.SetParent(section, false); picker.colorControl.gameObject.SetActive(true);
        CardFaceLayoutPresentation.Place(picker.colorControl, 0, 198, 610, 44);
        EditorUtility.SetDirty(picker);
        return picker;
    }

    private UnityEngine.UI.Button BakeCardSurfaceOption(Transform parent, string name, string caption)
    {
        var template = CardDesignRef<UnityEngine.UI.Button>(cardFacePanel, "restoreButton");
        var button = Instantiate(template, parent, false); button.name = name;
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        var face = button.GetComponent<CardFacePackThumbnail>();
        var option = button.gameObject.AddComponent<CardDesignImageOption>();
        option.surface = face.background; option.picture = face.artwork;
        option.caption = button.GetComponentInChildren<TMP_Text>(true); option.selection = button.GetComponent<CardSurfaceOptionView>();
        option.surface.transform.SetParent(button.transform, false);
        CardFaceLayoutPresentation.Place(option.surface.rectTransform, 12, 8, 42, 64);
        option.picture.transform.SetParent(option.surface.transform, false); CardPreviewStretch(option.picture.rectTransform, 0, 0, 0, 0);
        option.surface.sprite = null; option.surface.color = Color.white; option.picture.enabled = false;
        DestroyImmediate(face);
        SetCardPreviewCaption(option.caption, caption); option.caption.richText = false;
        option.caption.enableAutoSizing = true; option.caption.fontSizeMax = 23; option.caption.fontSizeMin = 16;
        option.caption.textWrappingMode = TextWrappingModes.Normal;
        CardFaceLayoutPresentation.Place(option.caption.rectTransform, 62, 4, 128, 72);
        button.targetGraphic = option.selection.frame; option.selection.frame.raycastTarget = true;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        var image = button.GetComponent<UnityEngine.UI.Image>(); if (image != null) image.enabled = false;
        CardFaceLayoutPresentation.Place((RectTransform)button.transform, 0, 0, 198, 80);
        return button;
    }

    private void BakeCardEdgeDialog(Transform root, Transform background, Transform back, CardDesignColorControls colors)
    {
        var controls = root.gameObject.AddComponent<CardDesignEdgeSettings>(); controls.colorPicker = colors.picker;
        controls.frontButton = CardPreviewAction(background, "OpenFaceEdge", "设置牌面边缘颜色", 328, 1, 282, 40);
        controls.backButton = CardPreviewAction(back, "OpenBackEdge", "设置牌背边缘颜色", 328, 1, 282, 40);
        var overlay = CardPreviewNode(root, "EdgeSettings", 0, 0, 1608, 930); CardPreviewStretch(overlay, 0, 0, 0, 0);
        controls.overlay = overlay.gameObject;
        var shade = CardPreviewNode(overlay, "Backdrop", 0, 0, 1, 1); CardPreviewStretch(shade, 0, 0, 0, 0);
        var shadeImage = shade.gameObject.AddComponent<UnityEngine.UI.Image>(); shadeImage.color = new Color(0, 0, 0, .55f);
        controls.backdrop = shade.gameObject.AddComponent<UnityEngine.UI.Button>(); controls.backdrop.targetGraphic = shadeImage;
        controls.backdrop.transition = UnityEngine.UI.Selectable.Transition.None;
        var panel = CardPreviewNode(overlay, "Panel", 0, 0, 666, 220);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f,.5f); panel.anchoredPosition = Vector2.zero;
        var panelImage = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        EditorUtility.CopySerialized(colors.picker.transform.Find("Panel").GetComponent<UnityEngine.UI.Image>(), panelImage); panelImage.raycastTarget = true;
        var titleObject = Instantiate(background.Find("SectionTitle").GetComponent<TMP_Text>(), panel, false);
        controls.title = titleObject; SetCardPreviewCaption(titleObject, "牌面边缘设置");
        CardFaceLayoutPresentation.Place(titleObject.rectTransform, 24, 18, 520, 42);
        controls.closeButton = CardPreviewAction(panel, "Close", "×", 598, 20, 44, 36);
        var front = cardEdgePanel.transform.Find("FrontEdgeDesign"); var rear = cardEdgePanel.transform.Find("BackEdgeDesign");
        controls.frontControls = front.gameObject; controls.backControls = rear.gameObject;
        for (int i = 0; i < 2; i++)
        {
            var section = i == 0 ? front : rear; section.SetParent(panel, false);
            CardFaceLayoutPresentation.Place((RectTransform)section, 24, 78, 610, 120);
            section.Find("SectionTitle").gameObject.SetActive(false); section.Find("SectionTitleBackground").gameObject.SetActive(false);
            var mode = CardDesignRef<UnityEngine.UI.Toggle>(cardEdgePanel, i == 0 ? "frontEdgeModeIndependent" : "backEdgeModeIndependent").transform.parent;
            CardFaceLayoutPresentation.Place((RectTransform)mode, 0, 0, 610, 42);
            CardFaceLayoutPresentation.Place((RectTransform)colors.buttons[i + 2].transform, 0, 62, 430, 44);
            var restore = CardDesignRef<UnityEngine.UI.Button>(cardEdgePanel, i == 0 ? "restoreFrontEdgeButton" : "restoreBackEdgeButton");
            CardFaceLayoutPresentation.Place((RectTransform)restore.transform, 444, 62, 166, 44);
            section.gameObject.SetActive(i == 0);
        }
        cardEdgePanel.transform.SetParent(root, false);
        CardFaceLayoutPresentation.Place((RectTransform)cardEdgePanel.transform, -5000, 0, 0, 0);
        SetCardPreviewCaption(CardDesignRef<UnityEngine.UI.Toggle>(cardEdgePanel, "frontEdgeModeFollowBackEdge").GetComponentInChildren<TMP_Text>(true), "跟随牌背边缘");
        SetCardPreviewCaption(CardDesignRef<UnityEngine.UI.Toggle>(cardEdgePanel, "backEdgeModeFollowFront").GetComponentInChildren<TMP_Text>(true), "跟随牌面边缘");
        overlay.gameObject.SetActive(false);
        EditorUtility.SetDirty(controls);
    }

    private UnityEngine.UI.Button CardPreviewAction(Transform parent, string name, string caption, float x, float y, float width, float height)
    {
        var template = GetComponentInChildren<CardFaceLayoutPresentation>(true).rows[0].reset;
        var button = Instantiate(template, parent, false); button.name = name; button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        SetCardPreviewCaption(button.GetComponentInChildren<TMP_Text>(true), caption);
        SceneConfigUi.ConfigureCardDesignButton(button, SceneConfigUi.UnselectedBlueGray);
        CardFaceLayoutPresentation.Place((RectTransform)button.transform, x, y, width, height); button.gameObject.SetActive(true);
        return button;
    }
    private static RectTransform CardPreviewNode(Transform parent, string name, float x, float y, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
        CardFaceLayoutPresentation.Place((RectTransform)go.transform, x, y, width, height); return (RectTransform)go.transform;
    }
    private static void CardPreviewStretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
    }
    private static void SetCardPreviewCaption(TMP_Text text, string caption)
    {
        text.text = caption; text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
    }
}
#endif
