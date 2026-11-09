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
    private readonly List<Button> visible = new List<Button>(8);

    private void OnEnable() => RefreshLayout();
    private void LateUpdate() {
        if (!utility || !exits || transform is not RectTransform) return;
        int next = CalculateSignature();
        if (next != signature) RefreshLayout();
    }
    private int CalculateSignature() {
        int next = 17 + (int)ConfigManager.CurrentLanguage;
        foreach (Transform child in exits) next = next * 31 + (child.gameObject.activeSelf ? 1 : 0);
        foreach (Transform child in utility) if (child.GetComponent<Button>()) next = next * 31 + (child.gameObject.activeSelf ? 1 : 0);
        return next;
    }
    public void RefreshLayout() {
        if (!utility || !exits || transform is not RectTransform) return;
        // OnEnable can run while exit controls are still hidden. Cache the state actually laid out.
        signature = CalculateSignature();
        // Both containers span the same table viewport. Child positions are now
        // native top-right offsets, so a resize never needs world-space correction.
        StretchToParent(utility);
        StretchToParent(exits);
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
        float width = PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage) ? 88 : 64;
        const float gap = 2;
        const float latencyWidth = 92;
        const float right = 14 + latencyWidth + 8;
        for (int i = 0; i < visible.Count; i++) {
            var rect = (RectTransform)visible[i].transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-right - (visible.Count - 1 - i) * (width + gap), -10);
        }
        PlaceAuxiliary(latency, 2, latencyWidth, 40);
        // Translated mouse instructions need room for all six operations.
        bool translated = PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage);
        PlaceAuxiliary(replayTips, 94f, translated ? 250f : 172.1568f, translated ? 174f : 151.9682f, 6.0913f);
    }
    private static void StretchToParent(RectTransform rect) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * .5f;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private static void PlaceAuxiliary(RectTransform rect, float top, float width, float height, float right = 14) {
        if (!rect) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(-right, -top);
    }
    private void ArrangeContent(Button button) {
        bool translated = PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage);
        float width = translated ? 88 : 64;
        const float height = 86;
        const float iconSize = 52;
        var rect = (RectTransform)button.transform;
        rect.sizeDelta = new Vector2(width, height);
        var icon = button.transform.Find("SourceArtwork") as RectTransform;
        if (icon) Place(icon, 0, 0, iconSize, iconSize);
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label) {
            Place(label.rectTransform, 0, 52, width - 4, translated ? 38 : 30);
            label.alignment = TextAlignmentOptions.Center;
            label.margin = Vector4.zero;
            label.fontStyle = FontStyles.Normal;
            label.fontWeight = FontWeight.Regular;
            label.fontSize = label.fontSizeMax = 18;
            label.fontSizeMin = 12;
            label.enableAutoSizing = true;
            label.textWrappingMode = translated ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
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
