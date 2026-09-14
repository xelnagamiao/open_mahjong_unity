using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>可整行展开的局制条目，明细使用两列名称/数值布局并复用已有单元格。</summary>
public sealed class PlayerInfoEntry : MonoBehaviour {
    public const float HeaderHeight = 64;
    private const float DetailRowHeight = 44;
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private TMP_Text expandText;
    [SerializeField] private Button expandButton;
    [SerializeField] private Image expandArrow;
    [SerializeField] private Image expandedAccent;
    [SerializeField] private RectTransform detailsRoot;
    [SerializeField] private PlayerInfoStatField fieldPrefab;
    [SerializeField] private List<PlayerInfoStatField> cells = new List<PlayerInfoStatField>();
    private LayoutElement layout;
    private GridLayoutGroup grid;
    private bool initialized;
    private int detailCount;
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
        }
        for (int i = fields.Count; i < cells.Count; i++) cells[i].gameObject.SetActive(false);
        SetExpanded(keepExpanded && IsExpanded);
    }

    private void Initialize() {
        if (initialized) return;
        initialized = true;
        layout = GetComponent<LayoutElement>();
        grid = detailsRoot.GetComponent<GridLayoutGroup>();
        // 同时修正场景中已有的条目与动态生成的预制体条目。
        expandedAccent.rectTransform.anchoredPosition = new Vector2(4, -8);
        expandedAccent.rectTransform.sizeDelta = new Vector2(3, HeaderHeight - 16);
        detailsRoot.sizeDelta = new Vector2(-64, detailsRoot.sizeDelta.y);
        expandButton.onClick.AddListener(() => SetExpanded(!IsExpanded));
    }

    public void SetExpanded(bool expanded) {
        Initialize();
        bool changed = IsExpanded != expanded;
        IsExpanded = expanded;
        detailsRoot.gameObject.SetActive(expanded);
        expandedAccent.gameObject.SetActive(expanded);
        expandText.text = expanded ? "收起" : "展开";
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
