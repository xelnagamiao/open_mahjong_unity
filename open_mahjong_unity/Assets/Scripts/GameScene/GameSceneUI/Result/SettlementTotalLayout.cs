using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 总计栏唯一布局所有者：文字共享基线、禁止换行，空间不足时整组等比缩小。
/// 不能按各列的 preferredHeight 顶端对齐：小数点的回退字体会改变 ascender，导致数字上下错位。
/// </summary>
[DisallowMultipleComponent]
public sealed class SettlementTotalLayout : UnityEngine.EventSystems.UIBehaviour, UnityEngine.UI.ILayoutGroup {
    private readonly List<TMP_Text> columns = new List<TMP_Text>(4);
    private readonly List<Metrics> metrics = new List<Metrics>(4);
    private readonly Dictionary<TMP_Text, string> sourceTexts = new Dictionary<TMP_Text, string>();
    private DrivenRectTransformTracker tracker;
    private RectTransform Rect => (RectTransform)transform;
    private TMP_FontAsset font;
    private float fontSize;
    private float spacing;
    private RectOffset padding;
    private Vector2 standardAnchorMin, standardAnchorMax, standardPivot, standardPosition, standardSize;
    private SettlementTotalDisplay.LayoutKind layoutKind;
    private TMP_Text detail, score;
    private bool initialized;

    private struct Metrics {
        public TMP_Text Text;
        public float Width, Ascender, Descender;
    }

    public static SettlementTotalLayout Ensure(GameObject container) {
        var layout = container.GetComponent<SettlementTotalLayout>();
        return layout != null ? layout : container.AddComponent<SettlementTotalLayout>();
    }

    public void Show(SettlementTotalDisplay display, TMP_Text fan, TMP_Text fu, TMP_Text points, TMP_Text limit) {
        if (!initialized) {
            var oldLayout = GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            spacing = oldLayout != null ? oldLayout.spacing : 20f;
            padding = oldLayout != null
                ? new RectOffset(oldLayout.padding.left, oldLayout.padding.right, oldLayout.padding.top, oldLayout.padding.bottom)
                : new RectOffset();
            if (oldLayout != null) oldLayout.enabled = false;
            var fitter = GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            TMP_Text style = fan != null ? fan : points;
            var typography = style.GetComponent<PlatformFontBinding>();
            font = typography && typography.authoredFont ? typography.authoredFont : style.font;
            fontSize = style.fontSize;
            standardAnchorMin = Rect.anchorMin; standardAnchorMax = Rect.anchorMax; standardPivot = Rect.pivot;
            standardPosition = Rect.anchoredPosition; standardSize = Rect.sizeDelta;
            initialized = true;
        }

        // 明细样式保留原来的展示区域，切回普通总计时由同一组件恢复，不依赖规则自己的快照。
        if (display.Layout != layoutKind) {
            if (display.Layout == SettlementTotalDisplay.LayoutKind.Breakdown) {
                Rect.anchorMin = Rect.anchorMax = new Vector2(.5f, .5f);
                Rect.pivot = new Vector2(0, 1);
                Rect.anchoredPosition = new Vector2(20, -55);
                Rect.sizeDelta = new Vector2(840, 160);
            } else if (layoutKind == SettlementTotalDisplay.LayoutKind.Breakdown) {
                Rect.anchorMin = standardAnchorMin; Rect.anchorMax = standardAnchorMax; Rect.pivot = standardPivot;
                Rect.anchoredPosition = standardPosition; Rect.sizeDelta = standardSize;
            }
        }
        layoutKind = display.Layout;
        detail = fu;
        score = points;
        columns.Clear();
        sourceTexts.Clear();
        if (layoutKind == SettlementTotalDisplay.LayoutKind.FuFanScore) {
            Add(fu, display.FuText);
            Add(fan, display.FanText);
        } else {
            Add(fan, display.FanText);
            Add(fu, display.FuText);
        }
        Add(points, display.ScoreText);
        Add(limit, display.LimitText);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
    }

    private void Add(TMP_Text label, string text) {
        if (label == null) return;
        sourceTexts[label] = text;
        TableText.Set(label, label == detail ? TableText.FuValue(text) : text);
        label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        // 顺序属于显示模型，不再依赖 MainScene 中历史遗留的 sibling 顺序。
        label.transform.SetAsLastSibling();
        if (label.gameObject.activeSelf) columns.Add(label);
        var fitter = label.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
        var typography = label.GetComponent<PlatformFontBinding>();
        if (typography) { typography.preserveAuthoredTypeface = true; typography.Apply(); }
        else label.font = font;
        label.fontSize = layoutKind == SettlementTotalDisplay.LayoutKind.Breakdown
            ? (label == detail ? 34f : label == score ? 54f : 42f) : fontSize;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.BaselineLeft;
        label.margin = Vector4.zero;
    }

    protected override void OnEnable() {
        base.OnEnable();
        ConfigManager.OnLanguageChanged += RefreshLanguage;
        RefreshLanguage();
    }
    protected override void OnDisable() {
        ConfigManager.OnLanguageChanged -= RefreshLanguage;
        tracker.Clear();
        base.OnDisable();
    }
    private void RefreshLanguage() {
        foreach (var pair in sourceTexts) if (pair.Key)
            TableText.Set(pair.Key, pair.Key == detail ? TableText.FuValue(pair.Value) : pair.Value);
        MarkDirty();
    }
    protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); MarkDirty(); }
    private void MarkDirty() { UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(Rect); }
    public void SetLayoutHorizontal() { Arrange(); }
    public void SetLayoutVertical() { Arrange(); }

    private void Arrange() {
        if (!initialized) return;
        tracker.Clear();
        metrics.Clear();
        foreach (TMP_Text label in columns) {
            if (label == null || !label.gameObject.activeSelf) continue;
            RectTransform rect = label.rectTransform;
            tracker.Add(this, rect, DrivenTransformProperties.Anchors | DrivenTransformProperties.Pivot
                | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta | DrivenTransformProperties.Scale);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, .5f);
            rect.localScale = Vector3.one;
            Vector2 size = label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity);
            rect.sizeDelta = new Vector2(Mathf.Ceil(size.x), Mathf.Ceil(size.y));
            // BaselineLeft + 中心 pivot 使本地基线恒为 0，实际 ascender 包含所有回退字体。
            label.ForceMeshUpdate(true);
            float ascent = label.font.faceInfo.ascentLine * label.fontSize / label.font.faceInfo.pointSize;
            float descent = -label.font.faceInfo.descentLine * label.fontSize / label.font.faceInfo.pointSize;
            if (label.textInfo.characterCount > 0) {
                ascent = label.textInfo.lineInfo[0].ascender;
                descent = -label.textInfo.lineInfo[0].descender;
                float inkTop = float.NegativeInfinity, inkBottom = float.PositiveInfinity;
                // Align visible glyphs without the fallback font's unused ascender space.
                for (int i = 0; i < label.textInfo.characterCount; i++) {
                    TMP_CharacterInfo character = label.textInfo.characterInfo[i];
                    if (!character.isVisible) continue;
                    inkTop = Mathf.Max(inkTop, character.topLeft.y, character.topRight.y);
                    inkBottom = Mathf.Min(inkBottom, character.bottomLeft.y, character.bottomRight.y);
                }
                if (!float.IsNegativeInfinity(inkTop)) { ascent = Mathf.Max(0, inkTop); descent = Mathf.Max(0, -inkBottom); }
            }
            metrics.Add(new Metrics { Text = label, Width = rect.sizeDelta.x, Ascender = ascent, Descender = descent });
        }
        if (metrics.Count == 0) return;
        if (layoutKind == SettlementTotalDisplay.LayoutKind.Breakdown) ArrangeBreakdown();
        else ArrangeInline();
    }

    private void ArrangeInline() {
        float width = spacing * (metrics.Count - 1), ascent = 0, descent = 0;
        foreach (Metrics item in metrics) {
            width += item.Width;
            ascent = Mathf.Max(ascent, item.Ascender);
            descent = Mathf.Max(descent, item.Descender);
        }
        float scale = Fit(width, ascent + descent, Rect.rect.width - padding.horizontal, Rect.rect.height - padding.vertical);
        float x = padding.left, baseline = -padding.top - ascent * scale;
        foreach (Metrics item in metrics) {
            Place(item, x, baseline, scale);
            x += (item.Width + spacing) * scale;
        }
    }

    private void ArrangeBreakdown() {
        float bodyWidth = 0, scoreWidth = 0, ascent = 0, descent = 0, detailAscent = 0, detailDescent = 0;
        foreach (Metrics item in metrics) {
            if (item.Text == score) scoreWidth = item.Width;
            else bodyWidth = Mathf.Max(bodyWidth, item.Width);
            if (item.Text == detail) { detailAscent = item.Ascender; detailDescent = item.Descender; }
            else { ascent = Mathf.Max(ascent, item.Ascender); descent = Mathf.Max(descent, item.Descender); }
        }
        const float detailGap = 10f;
        float height = ascent + descent + detailGap + detailAscent + detailDescent;
        float scale = Fit(bodyWidth + spacing + scoreWidth, height, Rect.rect.width, Rect.rect.height);
        foreach (Metrics item in metrics) {
            float x = item.Text == score ? Rect.rect.width - scoreWidth * scale : 0f;
            float baseline = item.Text == detail ? -(ascent + descent + detailGap + detailAscent) * scale : -ascent * scale;
            Place(item, x, baseline, scale);
        }
    }

    private static float Fit(float width, float height, float availableWidth, float availableHeight) {
        return Mathf.Clamp01(Mathf.Min(Mathf.Max(0, availableWidth) / Mathf.Max(1, width),
            Mathf.Max(0, availableHeight) / Mathf.Max(1, height)));
    }

    private static void Place(Metrics item, float x, float baseline, float scale) {
        item.Text.rectTransform.localScale = Vector3.one * scale;
        item.Text.rectTransform.anchoredPosition = new Vector2(x, baseline);
    }
}
