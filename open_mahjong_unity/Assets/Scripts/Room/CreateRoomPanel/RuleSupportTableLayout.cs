using TMPro;
using UnityEngine;

/// <summary>Fits every table column to the viewport, with native-pixel text and vertical scrolling.</summary>
[DefaultExecutionOrder(100)]
public sealed class RuleSupportTableLayout : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private RectTransform header;
    [SerializeField] private RectTransform dialog;
    private TMP_Text[][] labels;
    private TMP_Text[] headings;
    private float lastWidth = -1, lastScale = -1;
    private static readonly float[] ColumnWeights = { 48, 96, 88, 172, 188, 240, 66, 66, 66, 304 };

    public void Configure(UnityEngine.UI.ScrollRect tableScroll, RectTransform tableHeader, RectTransform tableDialog) {
        scroll = tableScroll; header = tableHeader; dialog = tableDialog;
        labels = null; headings = null; lastWidth = lastScale = -1;
    }

    private void OnEnable() { RefreshLayout(true); }
    private void LateUpdate() { RefreshLayout(); SnapScroll(); }

    public void RefreshLayout(bool force = false) {
        if (!scroll || !header || !dialog) return;
        var canvas = GetComponentInParent<Canvas>(true);
        float scale = canvas ? Mathf.Max(.1f, canvas.rootCanvas.scaleFactor) : 1;
        float width = scroll.viewport.rect.width;
        if (width < 1 || (!force && Mathf.Approximately(width, lastWidth) && Mathf.Approximately(scale, lastScale))) return;
        float Pixel(float value) => Mathf.Round(value * scale) / scale;
        float CeilPixel(float value) => Mathf.Ceil(value * scale) / scale;
        dialog.offsetMin = new Vector2(Pixel(28), Pixel(24));
        dialog.offsetMax = -dialog.offsetMin;
        var title = dialog.Find("Title").GetComponent<TMP_Text>();
        title.fontSize = Pixel(34);
        title.rectTransform.anchoredPosition = new Vector2(Pixel(24), -Pixel(12));
        title.rectTransform.sizeDelta = new Vector2(Pixel(620), Pixel(62));
        var close = (RectTransform)dialog.Find("Close");
        close.anchoredPosition = new Vector2(-Pixel(20), -Pixel(16));
        close.sizeDelta = new Vector2(Pixel(112), Pixel(52));
        close.GetComponentInChildren<TMP_Text>(true).fontSize = Pixel(24);
        var headerViewport = (RectTransform)header.parent;
        headerViewport.offsetMin = new Vector2(Pixel(20), headerViewport.offsetMin.y);
        headerViewport.offsetMax = new Vector2(-2 * Pixel(20), headerViewport.offsetMax.y);
        var scrollRoot = (RectTransform)scroll.transform;
        scrollRoot.offsetMin = new Vector2(Pixel(20), Pixel(20));
        scrollRoot.offsetMax = new Vector2(-Pixel(20), scrollRoot.offsetMax.y);
        scroll.viewport.offsetMin = Vector2.zero;
        scroll.viewport.offsetMax = new Vector2(-Pixel(20), 0);
        width = scroll.viewport.rect.width;
        lastWidth = width; lastScale = scale;
        float padding = Pixel(6);
        // Keep small windows readable; avoid fractional output glyph sizes at any desktop scaling.
        float fontSize = Mathf.Clamp(Mathf.Round(21 * scale), 16, 42) / scale;
        if (labels == null || labels.Length != scroll.content.childCount) {
            labels = new TMP_Text[scroll.content.childCount][];
            for (int row = 0; row < labels.Length; row++) {
                labels[row] = new TMP_Text[ColumnWeights.Length];
                for (int col = 0; col < ColumnWeights.Length; col++)
                    labels[row][col] = scroll.content.GetChild(row).GetChild(col).GetComponentInChildren<TMP_Text>(true);
            }
            headings = new TMP_Text[ColumnWeights.Length];
            for (int col = 0; col < headings.Length; col++) headings[col] = header.GetChild(col).GetComponentInChildren<TMP_Text>(true);
        }
        float total = 0;
        foreach (float weight in ColumnWeights) total += weight;
        var edges = new float[ColumnWeights.Length + 1];
        float sequenceWidth = Mathf.Max(width * ColumnWeights[0] / total, Mathf.Min(2 * fontSize + 2 * padding + Pixel(2), width * .12f));
        float mainWidth = Mathf.Max(width * ColumnWeights[1] / total, Mathf.Min(4 * fontSize + 2 * padding + Pixel(2), width * .12f));
        float remainingWidth = width - sequenceWidth - mainWidth;
        float remainingWeight = total - ColumnWeights[0] - ColumnWeights[1];
        float cumulative = 0;
        for (int col = 0; col < ColumnWeights.Length; col++) {
            cumulative += col == 0 ? sequenceWidth : col == 1 ? mainWidth : remainingWidth * ColumnWeights[col] / remainingWeight;
            edges[col + 1] = Pixel(cumulative);
        }
        width = edges[edges.Length - 1];
        float Measure(TMP_Text[] row) {
            float height = Pixel(48);
            for (int col = 0; col < row.Length; col++) {
                var text = row[col];
                text.fontSize = fontSize;
                text.enableAutoSizing = false;
                text.isOrthographic = true;
                text.rectTransform.offsetMin = new Vector2(padding, padding);
                text.rectTransform.offsetMax = new Vector2(-padding, -padding);
                float textWidth = Mathf.Max(1, edges[col + 1] - edges[col] - 2 * padding);
                height = Mathf.Max(height, CeilPixel(text.GetPreferredValues(text.text, textWidth, 0).y + 2 * padding + Pixel(4)));
            }
            return height;
        }
        void Place(RectTransform line, TMP_Text[] row, float y, float height) {
            line.anchorMin = line.anchorMax = line.pivot = new Vector2(0, 1);
            line.anchoredPosition = new Vector2(0, -y); line.sizeDelta = new Vector2(width, height);
            for (int col = 0; col < row.Length; col++) {
                var cell = (RectTransform)row[col].transform.parent;
                cell.anchoredPosition = new Vector2(edges[col], 0);
                cell.sizeDelta = new Vector2(edges[col + 1] - edges[col], height);
            }
        }
        float headerHeight = Measure(headings);
        headerViewport.offsetMin = new Vector2(headerViewport.offsetMin.x, -Pixel(84) - headerHeight);
        headerViewport.offsetMax = new Vector2(headerViewport.offsetMax.x, -Pixel(84));
        scrollRoot.offsetMax = new Vector2(scrollRoot.offsetMax.x, -Pixel(84) - headerHeight);
        Place(header, headings, 0, headerHeight);
        float oldScroll = scroll.verticalNormalizedPosition;
        float y = 0;
        for (int row = 0; row < labels.Length; row++) {
            float height = Measure(labels[row]);
            Place((RectTransform)scroll.content.GetChild(row), labels[row], y, height);
            y += height;
        }
        scroll.content.sizeDelta = new Vector2(width, y);
        scroll.horizontal = false;
        scroll.content.anchoredPosition = new Vector2(0, scroll.content.anchoredPosition.y);
        scroll.verticalNormalizedPosition = oldScroll;
        SnapScroll();
    }

    public void SnapScroll() {
        if (!scroll || lastScale <= 0) return;
        float maximum = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
        float position = Mathf.Clamp(scroll.content.anchoredPosition.y, 0, maximum);
        var aligned = new Vector2(0, Mathf.Round(position * lastScale) / lastScale);
        if (scroll.content.anchoredPosition != aligned) scroll.content.anchoredPosition = aligned;
        if (header.anchoredPosition != Vector2.zero) header.anchoredPosition = Vector2.zero;
    }
}
