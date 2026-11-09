using System.Collections.Generic;
using TMPro;
using UnityEngine;

public partial class RoundPanel {
    private readonly Dictionary<TMP_Text, TableLabelLayout> languageLayouts = new();
    private TMP_Text[] statusLabels;
    private TMP_Text simpleSeedHint, riichiSeedHint, riichiDuplicateSeedHint;
    private float riichiSeedHeight;
    private bool languageLayoutCaptured, englishLayoutApplied;

    private void OnEnable() {
        ConfigManager.OnLanguageChanged += RefreshLocalizedLayout;
        RefreshLocalizedLayout();
    }

    public void RefreshLocalizedLayout() {
        if (!languageLayoutCaptured) {
            statusLabels = new[] { FindLabel("SimpleRoundPanel/Content/Tips"),
                FindLabel("SimpleRoundPanel/Content/Cuohe"), FindLabel("SimpleRoundPanel/Content/Fushi") };
            simpleSeedHint = FindLabel("SimpleRoundPanel/Content/RandomorFushi");
            riichiSeedHint = FindLabel("RiichiRoundPanel/Content/RandomSeed");
            if (riichiSeedHint) riichiSeedHeight = riichiSeedHint.rectTransform.rect.height;
            riichiDuplicateSeedHint = FindLabel("RiichiRoundPanel/RandomorFushi");
            foreach (var label in statusLabels) Capture(label);
            Capture(simpleSeedHint); Capture(riichiSeedHint); Capture(riichiDuplicateSeedHint);
            languageLayoutCaptured = true;
        }
        bool english = ConfigManager.CurrentLanguage == AppLanguage.English || PlatformText.HasLocaleCatalog(ConfigManager.CurrentLanguage);
        if (!english) {
            if (englishLayoutApplied) foreach (var item in languageLayouts)
                item.Value.Restore(System.Array.IndexOf(statusLabels, item.Key) >= 0);
            englishLayoutApplied = false;
            return;
        }
        englishLayoutApplied = true;
        if (statusLabels[0] && statusLabels[0].transform.parent is RectTransform content) {
            float width = Mathf.Max(0, (content.rect.width - 44f) / 3f);
            for (int i = 0; i < statusLabels.Length; i++) {
                var label = statusLabels[i];
                if (!label) continue;
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(14 + i * (width + 8), -6);
                rect.sizeDelta = new Vector2(width, 28);
                var binding = label.GetComponent<LocalizedPlatformLabel>();
                string source = binding ? binding.Source : label.text;
                string key = source switch {
                    "提示:开" => "table.round.hints.on", "提示:关" => "table.round.hints.off",
                    "错和:开" => "table.round.chombo.on", "错和:关" => "table.round.chombo.off",
                    "复现:开" => "table.round.seed.on", "复现:关" => "table.round.seed.off", _ => source
                };
                TableText.Set(label, key);
                Constrain(label, 14, 11, TextAlignmentOptions.MidlineLeft);
            }
        }
        if (simpleSeedHint) {
            TableText.Set(simpleSeedHint, "table.round.seed_hint");
            Constrain(simpleSeedHint, 16, 14, TextAlignmentOptions.Midline);
        }
        if (riichiSeedHint) {
            TableText.Set(riichiSeedHint, "table.round.seed_short");
            riichiSeedHint.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                PlatformText.HasLocaleCatalog(ConfigManager.CurrentLanguage) ? 22 : riichiSeedHeight);
            Constrain(riichiSeedHint, 12, 11, TextAlignmentOptions.MidlineLeft);
        }
        if (riichiDuplicateSeedHint) riichiDuplicateSeedHint.enabled = false;
    }

    private TMP_Text FindLabel(string path) => transform.Find(path)?.GetComponent<TMP_Text>();
    private void Capture(TMP_Text label) { if (label && !languageLayouts.ContainsKey(label)) languageLayouts.Add(label, new TableLabelLayout(label)); }
    private static void Constrain(TMP_Text label, float size, float minimum, TextAlignmentOptions alignment) {
        var binding = label.GetComponent<LocalizedPlatformLabel>();
        if (binding) binding.fitTranslation = false;
        label.fontSize = label.fontSizeMax = size; label.fontSizeMin = minimum;
        label.enableAutoSizing = true;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = alignment;
        label.margin = Vector4.zero;
    }
}

internal sealed class TableLabelLayout {
    readonly TMP_Text label;
    readonly Vector2 min, max, pivot, position, size;
    readonly float fontSize, fontMin, fontMax;
    readonly bool autoSize, enabled;
    readonly TextWrappingModes wrapping;
    readonly TextOverflowModes overflow;
    readonly TextAlignmentOptions alignment;
    readonly Vector4 margin;
    public TableLabelLayout(TMP_Text value) {
        label = value; var rect = value.rectTransform;
        min = rect.anchorMin; max = rect.anchorMax; pivot = rect.pivot;
        position = rect.anchoredPosition; size = rect.sizeDelta;
        fontSize = value.fontSize; fontMin = value.fontSizeMin; fontMax = value.fontSizeMax;
        autoSize = value.enableAutoSizing; enabled = value.enabled;
        wrapping = value.textWrappingMode; overflow = value.overflowMode;
        alignment = value.alignment; margin = value.margin;
    }
    public void Restore(bool restoreGeometry = true) {
        if (!label) return;
        var rect = label.rectTransform;
        if (restoreGeometry) {
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }
        label.fontSize = fontSize; label.fontSizeMin = fontMin; label.fontSizeMax = fontMax;
        label.enableAutoSizing = autoSize; label.enabled = enabled;
        label.textWrappingMode = wrapping; label.overflowMode = overflow;
        label.alignment = alignment; label.margin = margin;
    }
}
