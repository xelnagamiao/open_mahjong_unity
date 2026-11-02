using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(100)]
public sealed class RuleSupportTableLayout : MonoBehaviour {
    [Serializable] private sealed class Row {
        public string[] texts;
        public Color[] fills, inks;
    }
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform header, dialog, rowTemplate;
    [SerializeField] private Row[] rows = Array.Empty<Row>();
    [SerializeField] private string sourceHash;
    private TMP_Text[][] labels;
    private TMP_Text[] headings, templateLabels;
    private int[] assignedRows;
    private float[] edges, tops, heights;
    private float lastWidth = -1, lastHeight = -1, lastScale = -1, padding, fontSize;
    private static readonly float[] ColumnWeights = { 48, 96, 88, 172, 188, 240, 66, 66, 66, 304 };

    public void Configure(ScrollRect tableScroll, RectTransform tableHeader, RectTransform tableDialog) {
        scroll = tableScroll; header = tableHeader; dialog = tableDialog;
        labels = null; headings = null; lastWidth = lastHeight = lastScale = -1;
#if UNITY_EDITOR
        if (!Application.isPlaying) {
            rows = Array.Empty<Row>();
            if (rowTemplate) DestroyImmediate(rowTemplate.gameObject);
            rowTemplate = null;
        }
#endif
    }

    private void OnEnable() { RefreshLayout(true); }
    private void LateUpdate() { RefreshLayout(); SnapScroll(); UpdateVisibleRows(); }
    private float Pixel(float value) => Mathf.Round(value * lastScale) / lastScale;
    private float CeilPixel(float value) => Mathf.Ceil(value * lastScale) / lastScale;
    private static TMP_Text[] Labels(Transform row) => row.Cast<Transform>().Select(c => c.GetComponentInChildren<TMP_Text>(true)).ToArray();

    public void RefreshLayout(bool force = false) {
        if (!scroll || !header || !dialog) return;
        var canvas = GetComponentInParent<Canvas>(true);
        float scale = canvas ? Mathf.Max(.1f, canvas.rootCanvas.scaleFactor) : 1;
        float width = scroll.viewport.rect.width;
        if (width < 1 || (!force && Mathf.Approximately(width, lastWidth)
            && Mathf.Approximately(scroll.viewport.rect.height, lastHeight) && Mathf.Approximately(scale, lastScale))) return;
        lastScale = scale;
        dialog.offsetMin = new Vector2(Pixel(28), Pixel(24)); dialog.offsetMax = -dialog.offsetMin;
        var title = dialog.Find("Title").GetComponent<TMP_Text>();
        title.fontSize = Pixel(34);
        title.rectTransform.anchoredPosition = new Vector2(Pixel(24), -Pixel(12));
        title.rectTransform.sizeDelta = new Vector2(Pixel(620), Pixel(62));
        var close = (RectTransform)dialog.Find("Close");
        close.anchoredPosition = new Vector2(-Pixel(20), -Pixel(16)); close.sizeDelta = new Vector2(Pixel(112), Pixel(52));
        close.GetComponentInChildren<TMP_Text>(true).fontSize = Pixel(24);
        var headerViewport = (RectTransform)header.parent;
        headerViewport.offsetMin = new Vector2(Pixel(20), headerViewport.offsetMin.y);
        headerViewport.offsetMax = new Vector2(-2 * Pixel(20), headerViewport.offsetMax.y);
        var scrollRoot = (RectTransform)scroll.transform;
        scrollRoot.offsetMin = new Vector2(Pixel(20), Pixel(20));
        scrollRoot.offsetMax = new Vector2(-Pixel(20), scrollRoot.offsetMax.y);
        scroll.viewport.offsetMin = Vector2.zero; scroll.viewport.offsetMax = new Vector2(-Pixel(20), 0);
        width = scroll.viewport.rect.width;
        lastWidth = width; padding = Pixel(6);
        fontSize = Mathf.Clamp(Mathf.Round(21 * scale), 16, 42) / scale;
        headings ??= Labels(header);
        edges = new float[ColumnWeights.Length + 1];
        float total = ColumnWeights.Sum();
        float sequence = Mathf.Max(width * ColumnWeights[0] / total, Mathf.Min(2 * fontSize + 2 * padding + Pixel(2), width * .12f));
        float main = Mathf.Max(width * ColumnWeights[1] / total, Mathf.Min(4 * fontSize + 2 * padding + Pixel(2), width * .12f));
        float cumulative = 0;
        for (int col = 0; col < ColumnWeights.Length; col++) {
            cumulative += col == 0 ? sequence : col == 1 ? main : (width - sequence - main) * ColumnWeights[col] / (total - ColumnWeights[0] - ColumnWeights[1]);
            edges[col + 1] = Pixel(cumulative);
        }
        float headerHeight = Measure(headings, null);
        headerViewport.offsetMin = new Vector2(headerViewport.offsetMin.x, -Pixel(84) - headerHeight);
        headerViewport.offsetMax = new Vector2(headerViewport.offsetMax.x, -Pixel(84));
        scrollRoot.offsetMax = new Vector2(scrollRoot.offsetMax.x, -Pixel(84) - headerHeight);
        lastHeight = scroll.viewport.rect.height;
        Place(header, headings, 0, headerHeight);
        float oldScroll = scroll.verticalNormalizedPosition, y = 0;
        if (rows.Length > 0) {
            templateLabels ??= Labels(rowTemplate);
            tops = new float[rows.Length]; heights = new float[rows.Length];
            for (int row = 0; row < rows.Length; row++) {
                tops[row] = y; heights[row] = Measure(templateLabels, rows[row].texts); y += heights[row];
            }
            if (Application.isPlaying) EnsurePool();
            if (assignedRows != null) Array.Fill(assignedRows, -1);
        } else {
            labels = scroll.content.Cast<Transform>().Select(Labels).ToArray();
            for (int row = 0; row < labels.Length; row++) {
                float height = Measure(labels[row], null);
                Place((RectTransform)scroll.content.GetChild(row), labels[row], y, height); y += height;
            }
        }
        scroll.content.sizeDelta = new Vector2(edges[edges.Length - 1], y);
        scroll.horizontal = false;
        scroll.content.anchoredPosition = new Vector2(0, scroll.content.anchoredPosition.y);
        scroll.verticalNormalizedPosition = oldScroll;
        SnapScroll(); UpdateVisibleRows();
    }

    private float Measure(TMP_Text[] text, string[] values) {
        float height = Pixel(48);
        for (int col = 0; col < text.Length; col++) {
            Style(text[col]);
            height = Mathf.Max(height, CeilPixel(text[col].GetPreferredValues(values == null ? text[col].text : values[col], Mathf.Max(1, edges[col + 1] - edges[col] - 2 * padding), 0).y + 2 * padding + Pixel(4)));
        }
        return height;
    }

    private void Style(TMP_Text text) {
        text.fontSize = fontSize; text.enableAutoSizing = false; text.isOrthographic = true;
        text.rectTransform.offsetMin = new Vector2(padding, padding); text.rectTransform.offsetMax = new Vector2(-padding, -padding);
    }
    private void Place(RectTransform line, TMP_Text[] text, float y, float height) {
        line.anchorMin = line.anchorMax = line.pivot = new Vector2(0, 1);
        line.anchoredPosition = new Vector2(0, -y); line.sizeDelta = new Vector2(edges[edges.Length - 1], height);
        for (int col = 0; col < text.Length; col++) {
            Style(text[col]);
            var cell = (RectTransform)text[col].transform.parent;
            cell.anchoredPosition = new Vector2(edges[col], 0); cell.sizeDelta = new Vector2(edges[col + 1] - edges[col], height);
        }
    }

    private void EnsurePool() {
        int count = Mathf.Min(rows.Length, Mathf.CeilToInt(scroll.viewport.rect.height / Pixel(48)) + 2);
        while (scroll.content.childCount < count) {
            var item = Instantiate(rowTemplate, scroll.content, false);
            item.name = "VisibleRow_" + scroll.content.childCount;
        }
        labels = scroll.content.Cast<Transform>().Select(Labels).ToArray();
        assignedRows = Enumerable.Repeat(-1, labels.Length).ToArray();
    }
    private void UpdateVisibleRows() {
        if (rows.Length == 0 || labels == null || assignedRows == null || tops == null) return;
        float top = Mathf.Max(0, scroll.content.anchoredPosition.y), bottom = top + scroll.viewport.rect.height;
        int first = 0;
        while (first + 1 < rows.Length && tops[first] + heights[first] < top) first++;
        for (int slot = 0; slot < labels.Length; slot++) {
            int row = first + slot;
            var item = (RectTransform)scroll.content.GetChild(slot);
            bool visible = row < rows.Length && tops[row] <= bottom;
            if (item.gameObject.activeSelf != visible) item.gameObject.SetActive(visible);
            if (!visible || assignedRows[slot] == row) continue;
            assignedRows[slot] = row;
            for (int col = 0; col < labels[slot].Length; col++) {
                var text = labels[slot][col]; text.text = rows[row].texts[col]; text.color = rows[row].inks[col];
                text.transform.parent.GetComponent<Image>().color = rows[row].fills[col];
            }
            Place(item, labels[slot], tops[row], heights[row]);
        }
    }
    public void SnapScroll() {
        if (!scroll || lastScale <= 0) return;
        float maximum = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
        var aligned = new Vector2(0, Pixel(Mathf.Clamp(scroll.content.anchoredPosition.y, 0, maximum)));
        if (scroll.content.anchoredPosition != aligned) scroll.content.anchoredPosition = aligned;
        if (header.anchoredPosition != Vector2.zero) header.anchoredPosition = Vector2.zero;
    }
#if UNITY_EDITOR
    public void BakeVirtualRows(string hash) {
        rows = scroll.content.Cast<Transform>().Select(line => {
            var text = Labels(line);
            return new Row { texts = text.Select(t => t.text).ToArray(), inks = text.Select(t => t.color).ToArray(), fills = text.Select(t => t.transform.parent.GetComponent<Image>().color).ToArray() };
        }).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("Cannot bake an empty rule table.");
        rowTemplate = Instantiate((RectTransform)scroll.content.GetChild(0), transform, false);
        rowTemplate.name = "RowTemplate"; rowTemplate.gameObject.SetActive(false);
        for (int i = scroll.content.childCount - 1; i >= 0; i--) DestroyImmediate(scroll.content.GetChild(i).gameObject);
        labels = null; templateLabels = null; assignedRows = null; sourceHash = hash;
        UnityEditor.EditorUtility.SetDirty(this);
    }
    public bool Matches(string[][] expected, string hash) => rowTemplate && sourceHash == hash && rows.Length == expected.Length
        && rows.Select((row, i) => row.texts.SequenceEqual(expected[i])).All(equal => equal)
        && scroll.content.childCount == 0 && !scroll.horizontal;
#endif
}
