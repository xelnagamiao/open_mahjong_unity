using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Positions the existing table controls inside the top-right screen margin.</summary>
[ExecuteAlways]
public sealed class TableHudUtilityLayout : MonoBehaviour {
    public RectTransform utility;
    public RectTransform exits;
    public RectTransform latency;
    public RectTransform replayTips;
    private int signature = -1;
    private Vector2 lastSize;
    private readonly List<Button> visible = new List<Button>(8);

    private void OnEnable() => RefreshLayout();
    private void LateUpdate() {
        if (!utility || !exits || transform is not RectTransform panel) return;
        int next = 17;
        foreach (Transform child in exits) next = next * 31 + (child.gameObject.activeSelf ? 1 : 0);
        foreach (Transform child in utility) if (child.GetComponent<Button>()) next = next * 31 + (child.gameObject.activeSelf ? 1 : 0);
        if (next != signature || panel.rect.size != lastSize) RefreshLayout();
    }
    private int CalculateSignature() {
        int next = 17;
        foreach (Transform child in exits) next = next * 31 + (child.gameObject.activeSelf ? 1 : 0);
        foreach (Transform child in utility) if (child.GetComponent<Button>()) next = next * 31 + (child.gameObject.activeSelf ? 1 : 0);
        return next;
    }
    public void RefreshLayout() {
        if (!utility || !exits || transform is not RectTransform panel) return;
        // OnEnable can run while exit controls are still hidden. Cache the state actually laid out.
        signature = CalculateSignature();
        lastSize = panel.rect.size;
        visible.Clear();
        foreach (string name in new[] { "SwitchToMeun", "OpenRuleBook", "OpenHistoryPanel" }) {
            var button = utility.Find(name)?.GetComponent<Button>();
            if (!button) continue;
            ArrangeContent(button);
            if (button.gameObject.activeSelf) visible.Add(button);
        }
        foreach (Transform child in exits) {
            var button = child.GetComponent<Button>();
            if (!button) continue;
            ArrangeContent(button);
            if (child.gameObject.activeSelf) visible.Add(button);
        }
        const float width = 64;
        const float gap = 2;
        float totalWidth = visible.Count * width + Mathf.Max(0, visible.Count - 1) * gap;
        const float latencyWidth = 92;
        float left = panel.rect.xMax - 14 - latencyWidth - 8 - totalWidth;
        for (int i = 0; i < visible.Count; i++) {
            var rect = (RectTransform)visible[i].transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
            rect.position = panel.TransformPoint(new Vector3(left + i * (width + gap) + width * .5f, panel.rect.yMax - 10, 0));
        }
        PlaceAuxiliary(latency, panel, 2, latencyWidth, 40);
        // Keep the hint below the utility captions without changing its size or opacity.
        PlaceAuxiliary(replayTips, panel, 94f, 172.1568f, 151.9682f, 6.0913f);
    }
    private static void PlaceAuxiliary(RectTransform rect, RectTransform panel, float top, float width, float height, float right = 14) {
        if (!rect) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(width, height);
        rect.position = panel.TransformPoint(new Vector3(panel.rect.xMax - right, panel.rect.yMax - top, 0));
    }
    private void ArrangeContent(Button button) {
        const float width = 64;
        const float height = 86;
        const float iconSize = 52;
        var rect = (RectTransform)button.transform;
        rect.sizeDelta = new Vector2(width, height);
        var icon = button.transform.Find("SourceArtwork") as RectTransform;
        if (icon) Place(icon, 0, 0, iconSize, iconSize);
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label) {
            Place(label.rectTransform, 0, 52, width - 4, 30);
            label.alignment = TextAlignmentOptions.Center;
            label.margin = Vector4.zero;
            label.fontStyle = FontStyles.Normal;
            label.fontWeight = FontWeight.Regular;
            label.fontSize = label.fontSizeMax = 18;
            label.fontSizeMin = 12;
            label.enableAutoSizing = true;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
        }
    }
    private static void Place(RectTransform rect, float x, float top, float width, float height) {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
