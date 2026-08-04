#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一次性烘焙工具：把「加宽 TMP 牌背设置面板」真正写进当前场景（替换旧的烘焙面板）。
/// 运行时不再绘制任何 UI，因此面板必须是场景对象；执行后请 Ctrl+S 保存场景。
/// 菜单：Tools/牌背设置/烘焙加宽TMP面板到场景
/// </summary>
public static class CardBackPanelBaker
{
    private static readonly Color[] PresetColors =
    {
        new Color(0.218f, 0.372f, 0.66f, 1f),  // 默认蓝
        new Color(0.72f, 0.10f, 0.14f, 1f),
        new Color(0.95f, 0.55f, 0.10f, 1f),
        new Color(0.93f, 0.80f, 0.20f, 1f),
        new Color(0.12f, 0.55f, 0.25f, 1f),
        new Color(0.10f, 0.65f, 0.65f, 1f),
        new Color(0.45f, 0.25f, 0.70f, 1f),
        new Color(0.80f, 0.30f, 0.55f, 1f),
        new Color(0.15f, 0.15f, 0.18f, 1f),
        new Color(0.92f, 0.92f, 0.92f, 1f),
    };

    private static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.10f, 0.97f);
    private static readonly Color ButtonBg = new Color(0.17f, 0.21f, 0.30f, 1f);
    private static readonly Color Accent = new Color(0.28f, 0.48f, 0.92f, 1f);
    private static readonly Color LabelColor = new Color(0.82f, 0.85f, 0.90f, 1f);

    private static Sprite _whiteSprite;
    private static TMP_FontAsset _tmpFont;

    [MenuItem("Tools/牌背设置/烘焙加宽TMP面板到场景")]
    public static void Bake()
    {
        SceneConfigPanel scp = Object.FindObjectOfType<SceneConfigPanel>();
        if (scp == null)
        {
            EditorUtility.DisplayDialog("牌背面板烘焙", "当前场景找不到 SceneConfigPanel，无法烘焙。", "好的");
            return;
        }

        Transform root = scp.transform;

        // 删除旧的烘焙面板（含旧尺寸/旧文本）
        Transform oldPanel = root.Find("CardBackPanel");
        if (oldPanel != null)
        {
            Object.DestroyImmediate(oldPanel.gameObject);
        }

        // ===== 主面板 =====
        RectTransform panelRt = NewRect("CardBackPanel", root);
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = new Vector2(1169f, -490f);
        panelRt.sizeDelta = new Vector2(900f, 820f);
        Image bg = panelRt.gameObject.AddComponent<Image>();
        bg.color = PanelBg;

        // 标题 + 关闭
        TMP_Text title = NewText(panelRt, "Title", "牌背设置", 22, Color.white, TextAnchor.MiddleCenter);
        StretchTop(title.rectTransform, 0f, 40f, 0f);

        Button closeBtn = NewButton(panelRt, "CloseButton", "关闭", ButtonBg, Color.white);
        RectTransform closeRt = (RectTransform)closeBtn.transform;
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.anchoredPosition = new Vector2(-8f, -6f);
        closeRt.sizeDelta = new Vector2(56f, 30f);

        // 当前预览
        TMP_Text previewLabel = NewText(panelRt, "PreviewLabel", "当前牌背", 15, LabelColor, TextAnchor.MiddleLeft);
        StretchTop(previewLabel.rectTransform, 10f, 18f, 56f);
        Image previewImage = NewImage(panelRt, "PreviewImage", new Color(0.218f, 0.372f, 0.66f, 1f));
        RectTransform previewRt = (RectTransform)previewImage.transform;
        previewRt.anchorMin = new Vector2(0.5f, 1f);
        previewRt.anchorMax = new Vector2(0.5f, 1f);
        previewRt.pivot = new Vector2(0.5f, 1f);
        previewRt.anchoredPosition = new Vector2(0f, -88f);
        previewRt.sizeDelta = new Vector2(280f, 150f);

        // 颜色区
        TMP_Text colorLabel = NewText(panelRt, "ColorLabel", "颜色", 17, Color.white, TextAnchor.MiddleLeft);
        StretchTop(colorLabel.rectTransform, 10f, 22f, 208f);

        int swatchCols = 5;
        float swatchSize = 62f;
        float swatchGap = 76f;
        float startX = -((swatchCols - 1) * swatchGap) * 0.5f;
        for (int i = 0; i < PresetColors.Length; i++)
        {
            int row = i / swatchCols;
            int col = i % swatchCols;
            Button swatch = NewButton(panelRt, "Swatch" + i, "", PresetColors[i], Color.white);
            RectTransform swRt = (RectTransform)swatch.transform;
            swRt.anchorMin = new Vector2(0.5f, 1f);
            swRt.anchorMax = new Vector2(0.5f, 1f);
            swRt.pivot = new Vector2(0.5f, 1f);
            swRt.anchoredPosition = new Vector2(startX + col * swatchGap, -238f - row * (swatchSize + 10f));
            swRt.sizeDelta = new Vector2(swatchSize, swatchSize);
        }

        // RGB 滑条
        CreateSliderRow(panelRt, "R", -366f, Color.red);
        CreateSliderRow(panelRt, "G", -400f, Color.green);
        CreateSliderRow(panelRt, "B", -434f, Color.blue);

        // HEX
        TMP_Text hexLabel = NewText(panelRt, "HexLabel", "HEX", 14, LabelColor, TextAnchor.MiddleRight);
        RectTransform hexLabelRt = hexLabel.rectTransform;
        hexLabelRt.anchorMin = new Vector2(0f, 1f);
        hexLabelRt.anchorMax = new Vector2(0f, 1f);
        hexLabelRt.pivot = new Vector2(0f, 1f);
        hexLabelRt.anchoredPosition = new Vector2(16f, -470f);
        hexLabelRt.sizeDelta = new Vector2(48f, 24f);

        TMP_InputField hexInput = CreateInput(panelRt, "HexInput", "RRGGBB 或 RRGGBBAA");
        RectTransform hexRt = (RectTransform)hexInput.transform;
        hexRt.anchorMin = new Vector2(0f, 1f);
        hexRt.anchorMax = new Vector2(0f, 1f);
        hexRt.pivot = new Vector2(0f, 1f);
        hexRt.anchoredPosition = new Vector2(72f, -470f);
        hexRt.sizeDelta = new Vector2(360f, 28f);

        Button hexApply = NewButton(panelRt, "HexApply", "应用", Accent, Color.white);
        RectTransform hexApplyRt = (RectTransform)hexApply.transform;
        hexApplyRt.anchorMin = new Vector2(0f, 1f);
        hexApplyRt.anchorMax = new Vector2(0f, 1f);
        hexApplyRt.pivot = new Vector2(0f, 1f);
        hexApplyRt.anchoredPosition = new Vector2(448f, -470f);
        hexApplyRt.sizeDelta = new Vector2(76f, 28f);

        // 恢复默认
        Button restore = NewButton(panelRt, "RestoreButton", "恢复默认颜色并清除图片", ButtonBg, Color.white);
        RectTransform restoreRt = (RectTransform)restore.transform;
        restoreRt.anchorMin = new Vector2(0.5f, 1f);
        restoreRt.anchorMax = new Vector2(0.5f, 1f);
        restoreRt.pivot = new Vector2(0.5f, 1f);
        restoreRt.anchoredPosition = new Vector2(0f, -512f);
        restoreRt.sizeDelta = new Vector2(440f, 36f);

        // 图片区
        TMP_Text imageLabel = NewText(panelRt, "ImageLabel", "牌背图片", 17, Color.white, TextAnchor.MiddleLeft);
        StretchTop(imageLabel.rectTransform, 10f, 22f, 566f);

        Button pickButton = NewButton(panelRt, "PickImageButton", "选择图片", Accent, Color.white);
        RectTransform pickRt = (RectTransform)pickButton.transform;
        pickRt.anchorMin = new Vector2(0.5f, 1f);
        pickRt.anchorMax = new Vector2(0.5f, 1f);
        pickRt.pivot = new Vector2(0.5f, 1f);
        pickRt.anchoredPosition = new Vector2(0f, -602f);
        pickRt.sizeDelta = new Vector2(440f, 38f);

        Button dropZone = NewButton(panelRt, "DropZone", "", new Color(0.10f, 0.12f, 0.16f, 1f), Color.white);
        RectTransform dropRt = (RectTransform)dropZone.transform;
        dropRt.anchorMin = new Vector2(0.5f, 1f);
        dropRt.anchorMax = new Vector2(0.5f, 1f);
        dropRt.pivot = new Vector2(0.5f, 1f);
        dropRt.anchoredPosition = new Vector2(0f, -652f);
        dropRt.sizeDelta = new Vector2(440f, 96f);
        TMP_Text dropText = NewText(dropRt, "DropText", "把图片文件拖到这里\n(WebGL 浏览器 / 编辑器 Project 资源)", 13, LabelColor, TextAnchor.MiddleCenter);
        Stretch(dropText.rectTransform);

        Button clearButton = NewButton(panelRt, "ClearImageButton", "清除图片（恢复纯色）", ButtonBg, Color.white);
        RectTransform clearRt = (RectTransform)clearButton.transform;
        clearRt.anchorMin = new Vector2(0.5f, 1f);
        clearRt.anchorMax = new Vector2(0.5f, 1f);
        clearRt.pivot = new Vector2(0.5f, 1f);
        clearRt.anchoredPosition = new Vector2(0f, -764f);
        clearRt.sizeDelta = new Vector2(440f, 36f);

        // 逻辑组件（运行时会按名字自动挂接，也可以在 Inspector 里拖引用）
        panelRt.gameObject.AddComponent<CardBackConfigPanel>();
        CardBackEditorDragReceiver.EnsureOnRoot(scp.gameObject);

        EditorSceneManager.MarkSceneDirty(scp.gameObject.scene);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "牌背面板烘焙",
            "已重建加宽 TMP 面板（900×820）。\n请按 Ctrl+S 保存场景，然后进 Play 测试。",
            "好的");
    }

    // ==================== 组件工厂（编辑模式可用）====================

    private static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            _whiteSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }
        return _whiteSprite;
    }

    private static TMP_FontAsset TmpFont()
    {
        if (_tmpFont != null) return _tmpFont;
        _tmpFont = Resources.Load<TMP_FontAsset>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF");
        if (_tmpFont == null) _tmpFont = TMP_Settings.defaultFontAsset;
        if (_tmpFont == null) _tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return _tmpFont;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static Image NewImage(RectTransform parent, string name, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TMP_Text NewText(RectTransform parent, string name, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = NewRect(name, parent);
        TMP_Text text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TmpFont();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TmpAlignment(anchor);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static TextAlignmentOptions TmpAlignment(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.MidlineLeft;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.MidlineRight;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
            default: return TextAlignmentOptions.Midline;
        }
    }

    private static Button NewButton(RectTransform parent, string name, string label, Color bg, Color textColor)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        img.sprite = WhiteSprite();
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;
        if (!string.IsNullOrEmpty(label))
        {
            TMP_Text text = NewText(rt, "Label", label, 15, textColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
        }
        return button;
    }

    private static void CreateSliderRow(RectTransform parent, string name, float y, Color accent)
    {
        TMP_Text label = NewText(parent, name + "Label", name, 14, LabelColor, TextAnchor.MiddleRight);
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = new Vector2(0f, 1f);
        labelRt.anchorMax = new Vector2(0f, 1f);
        labelRt.pivot = new Vector2(0f, 1f);
        labelRt.anchoredPosition = new Vector2(16f, y);
        labelRt.sizeDelta = new Vector2(44f, 20f);

        Slider slider = CreateSlider(parent, name + "Slider", 0f, 255f, 128f);
        RectTransform sliderRt = (RectTransform)slider.transform;
        sliderRt.anchorMin = new Vector2(0f, 1f);
        sliderRt.anchorMax = new Vector2(0f, 1f);
        sliderRt.pivot = new Vector2(0f, 1f);
        sliderRt.anchoredPosition = new Vector2(66f, y + 8f);
        sliderRt.sizeDelta = new Vector2(560f, 16f);
        slider.fillRect.GetComponent<Image>().color = accent;

        TMP_Text value = NewText(parent, name + "Value", "0", 14, Color.white, TextAnchor.MiddleLeft);
        RectTransform valueRt = value.rectTransform;
        valueRt.anchorMin = new Vector2(0f, 1f);
        valueRt.anchorMax = new Vector2(0f, 1f);
        valueRt.pivot = new Vector2(0f, 1f);
        valueRt.anchoredPosition = new Vector2(640f, y);
        valueRt.sizeDelta = new Vector2(64f, 20f);
    }

    private static Slider CreateSlider(RectTransform parent, string name, float min, float max, float value)
    {
        RectTransform rt = NewRect(name, parent);
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.20f, 0.22f, 0.28f, 1f);

        RectTransform fillRt = NewRect("Fill", rt);
        Stretch(fillRt);
        Image fill = fillRt.gameObject.AddComponent<Image>();
        fill.sprite = WhiteSprite();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.color = Accent;

        RectTransform handleRt = NewRect("Handle", rt);
        handleRt.anchorMin = new Vector2(0f, 0.5f);
        handleRt.anchorMax = new Vector2(0f, 0.5f);
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(16f, 20f);
        Image handle = handleRt.gameObject.AddComponent<Image>();
        handle.sprite = WhiteSprite();
        handle.color = Color.white;

        Slider slider = rt.gameObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static TMP_InputField CreateInput(RectTransform parent, string name, string placeholderText)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.13f, 0.15f, 0.20f, 1f);

        TMP_Text text = NewText(rt, "Text", "", 14, Color.white, TextAnchor.MiddleLeft);
        RectTransform textRt = text.rectTransform;
        Stretch(textRt);
        textRt.offsetMin = new Vector2(8f, 0f);
        textRt.offsetMax = new Vector2(-8f, 0f);

        TMP_Text ph = NewText(rt, "Placeholder", placeholderText, 14, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
        RectTransform phRt = ph.rectTransform;
        Stretch(phRt);
        phRt.offsetMin = new Vector2(8f, 0f);
        phRt.offsetMax = new Vector2(-8f, 0f);

        TMP_InputField input = rt.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = img;
        input.textComponent = text;
        input.placeholder = ph;
        return input;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void StretchTop(RectTransform rt, float left, float height, float y)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(-left * 2f, height);
    }
}
#endif
