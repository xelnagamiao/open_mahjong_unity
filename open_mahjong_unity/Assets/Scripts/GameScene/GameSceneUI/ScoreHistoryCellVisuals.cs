using UnityEngine;

/// <summary>随单元格 RectTransform 伸缩的原生 uGUI 边线与高亮。</summary>
public static class ScoreHistoryCellVisuals {
    private const float DefaultBorderWidth = 2f;
    private static readonly Color BorderColor = new Color32(128, 128, 128, 255);

    public static void AddBorders(Transform cell, bool left = false, bool top = false) {
        if (left) AddLine(cell, "BorderLeft", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, .5f));
        if (top) AddLine(cell, "BorderTop", new Vector2(0, 1), new Vector2(1, 1), new Vector2(.5f, 1));
        AddLine(cell, "BorderRight", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, .5f));
        AddLine(cell, "BorderBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(.5f, 0));
    }

    private static void AddLine(Transform cell, string name, Vector2 min, Vector2 max, Vector2 pivot) {
        if (cell.Find(name) != null) return;
        var line = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.LayoutElement));
        line.transform.SetParent(cell, false);
        var rect = (RectTransform)line.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        float width = BorderWidth(cell.GetComponentInParent<Canvas>());
        rect.sizeDelta = min.x == max.x ? new Vector2(width, 0) : new Vector2(0, width);
        line.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var image = line.GetComponent<UnityEngine.UI.Image>();
        image.color = BorderColor;
        image.raycastTarget = false;
    }

    public static float BorderWidth(Canvas canvas) {
        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        // 小窗口下仍至少占一个屏幕像素，再由 Canvas 的 Pixel Perfect 对齐边缘。
        return Mathf.Max(DefaultBorderWidth, 1f / Mathf.Max(scale, .001f));
    }

    public static void RefreshBorderWidths(Transform table, float width) {
        foreach (var rect in table.GetComponentsInChildren<RectTransform>(true)) {
            if (rect.name == "BorderLeft" || rect.name == "BorderRight") rect.sizeDelta = new Vector2(width, 0);
            else if (rect.name == "BorderTop" || rect.name == "BorderBottom") rect.sizeDelta = new Vector2(0, width);
        }
    }

    /// <summary>所有计分板选项共用原生 Toggle：ColorTint 处理悬停/按下，graphic 处理持续选中与淡入淡出。</summary>
    public static UnityEngine.UI.Toggle CreateToggle(TMPro.TMP_Text text, UnityEngine.UI.ToggleGroup group = null) {
        var toggle = text.GetComponent<UnityEngine.UI.Toggle>();
        if (toggle != null) return toggle;

        var hover = CreateHighlight(text.transform, "HoverHighlight", Color.white);
        var selected = CreateHighlight(text.transform, "SelectedHighlight", new Color(.12f, .55f, 1f, .38f));
        toggle = text.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        toggle.targetGraphic = hover;
        toggle.graphic = selected;
        toggle.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        toggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.Fade;
        toggle.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Automatic };
        var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
        colors.normalColor = Color.clear;
        colors.highlightedColor = new Color(1f, 1f, 1f, .16f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.10f, .35f, .70f, .55f);
        colors.disabledColor = Color.clear;
        colors.fadeDuration = .12f;
        toggle.colors = colors;
        toggle.SetIsOnWithoutNotify(false);
        selected.canvasRenderer.SetAlpha(0f);
        hover.canvasRenderer.SetColor(Color.clear);
        toggle.group = group;
        text.raycastTarget = true;
        return toggle;
    }

    public static UnityEngine.UI.ToggleGroup EnsureToggleGroup(Transform parent, bool allowSwitchOff) {
        var group = parent.GetComponent<UnityEngine.UI.ToggleGroup>();
        if (group == null) group = parent.gameObject.AddComponent<UnityEngine.UI.ToggleGroup>();
        group.allowSwitchOff = allowSwitchOff;
        return group;
    }

    private static UnityEngine.UI.Image CreateHighlight(Transform cell, string name, Color color) {
        var highlight = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.LayoutElement));
        highlight.layer = cell.gameObject.layer;
        highlight.transform.SetParent(cell, false);
        highlight.transform.SetAsFirstSibling();
        var rect = (RectTransform)highlight.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * DefaultBorderWidth;
        rect.offsetMax = -Vector2.one * DefaultBorderWidth;
        highlight.GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var image = highlight.GetComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
