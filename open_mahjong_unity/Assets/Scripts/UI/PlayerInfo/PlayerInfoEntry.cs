using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>可整行展开的局制条目，明细使用两列名称/数值布局并复用已有单元格。</summary>
public sealed class PlayerInfoEntry : MonoBehaviour {
    public const float HeaderHeight = 56;
    private const float DetailRowHeight = 44;
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private Button expandButton;
    [SerializeField] private Image expandArrow;
    [SerializeField] private Image expandedAccent;
    [SerializeField] private Image detailsBackground;
    [SerializeField] private Image[] arrowStrokes;
    [SerializeField] private TMP_FontAsset headingMedium;
    [SerializeField] private RectTransform detailsRoot;
    [SerializeField] private PlayerInfoStatField fieldPrefab;
    [SerializeField] private List<PlayerInfoStatField> cells = new List<PlayerInfoStatField>();
    private LayoutElement layout;
    private GridLayoutGroup grid;
    private bool initialized;
    private int detailCount;
    private PlayerInfoAppearance.Palette palette;
    public bool IsExpanded { get; private set; }
    public float PreferredHeight => layout != null ? layout.preferredHeight : HeaderHeight;
    public event Action<PlayerInfoEntry> ExpansionChanged;

    public void Bind(string caption, IList<KeyValuePair<string, string>> fields, bool keepExpanded = false) {
        Initialize();
        modeText.text = caption;
        detailCount = fields.Count;
        for (int i = 0; i < fields.Count; i++) {
            if (i == cells.Count) {
                var cell = Instantiate(fieldPrefab, detailsRoot);
                foreach (var child in cell.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = gameObject.layer;
                cells.Add(cell);
            }
            cells[i].gameObject.SetActive(true);
            cells[i].Bind(fields[i].Key, fields[i].Value);
            if (palette != null) cells[i].ApplyAppearance(palette);
        }
        for (int i = fields.Count; i < cells.Count; i++) cells[i].gameObject.SetActive(false);
        SetExpanded(keepExpanded && IsExpanded);
    }

    private void Initialize() {
        if (initialized) return;
        initialized = true;
        layout = GetComponent<LayoutElement>();
        grid = detailsRoot.GetComponent<GridLayoutGroup>();
        // 明细底色内收 8，字段文字再内收 10，与标题的 18 单位左边距对齐。
        detailsRoot.sizeDelta = new Vector2(-16, detailsRoot.sizeDelta.y);
        expandButton.onClick.AddListener(() => SetExpanded(!IsExpanded));
    }

    public void ApplyAppearance(PlayerInfoAppearance.Palette value) {
        palette = value;
        Initialize();
        modeText.color = value[PlayerInfoAppearance.Role.Ink];
        var font = headingMedium;
        if (font) {
            modeText.font = font; modeText.fontSharedMaterial = font.material;
            var binding = modeText.GetComponent<PlatformFontBinding>();
            if (binding) {
                binding.authoredFont = font; binding.authoredMaterial = font.material;
                binding.preserveAuthoredTypeface = true; binding.preserveAuthoredChineseFace = true;
                binding.authoredStyle = FontStyles.Normal; binding.authoredWeight = FontWeight.Regular; binding.Apply();
            }
        }
        modeText.fontStyle = FontStyles.Normal; modeText.fontWeight = FontWeight.Regular;
        modeText.fontSize = modeText.fontSizeMax = 28;
        modeText.enableAutoSizing = false;
        modeText.alignment = TextAlignmentOptions.MidlineLeft;
        var localized = modeText.GetComponent<LocalizedPlatformLabel>();
        if (localized) { localized.authoredSize = modeText.fontSize; localized.authoredAutoSize = false; }
        var header = expandButton.targetGraphic.rectTransform;
        header.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, HeaderHeight);
        ((RectTransform)expandButton.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, HeaderHeight);
        var title = modeText.rectTransform;
        title.anchorMin = new Vector2(0, 1); title.anchorMax = new Vector2(1, 1); title.pivot = new Vector2(.5f, 1);
        title.anchoredPosition = new Vector2(-13, 0); title.sizeDelta = new Vector2(-62, HeaderHeight);
        detailsRoot.anchoredPosition = new Vector2(0, -HeaderHeight - 12);
        expandedAccent.rectTransform.anchoredPosition = new Vector2(0, -HeaderHeight + 2);
        expandedAccent.color = value[PlayerInfoAppearance.Role.Accent];
        if (detailsBackground) detailsBackground.color = value[PlayerInfoAppearance.Role.Detail];
        PlayerInfoAppearance.PaintControl(expandButton, value[PlayerInfoAppearance.Role.Section]);
        foreach (var stroke in arrowStrokes ?? Array.Empty<Image>())
            if (stroke) stroke.color = value[PlayerInfoAppearance.Role.Ink];
        foreach (var cell in cells) if (cell) cell.ApplyAppearance(value);
        UpdateLayout();
    }

    public void SetExpanded(bool expanded) {
        Initialize();
        bool changed = IsExpanded != expanded;
        IsExpanded = expanded;
        detailsRoot.gameObject.SetActive(expanded);
        expandedAccent.gameObject.SetActive(expanded);
        expandArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, expanded ? 180 : 0);
        UpdateLayout();
        if (changed) ExpansionChanged?.Invoke(this);
    }

    private void OnRectTransformDimensionsChange() {
        if (initialized) UpdateLayout();
    }

    private void UpdateLayout() {
        float width = detailsRoot.rect.width - grid.padding.horizontal - grid.spacing.x;
        grid.cellSize = new Vector2(Mathf.Max(1, width / 2), DetailRowHeight);
        float height = Mathf.Ceil(detailCount / 2f) * DetailRowHeight;
        detailsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        layout.minHeight = layout.preferredHeight = IsExpanded ? HeaderHeight + 24 + height : HeaderHeight;
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
    }
}
