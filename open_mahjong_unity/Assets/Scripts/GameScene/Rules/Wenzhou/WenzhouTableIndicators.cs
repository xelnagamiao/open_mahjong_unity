using System;
using TMPro;
using UnityEngine;

/// <summary>Projects public Wenzhou metadata into the shared round panel and physical tile badges.</summary>
public static class WenzhouTableIndicators {
    public static TMP_FontAsset SharedFont => Resources.Load<TMP_FontAsset>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF") ?? TMP_Settings.defaultFontAsset;
    public static WenzhouInfo DisplayedInfo { get; private set; }

    public static RoundIndicatorDisplay FieldState(WenzhouInfo value) {
        if (value == null) return new RoundIndicatorDisplay { Caption = "未翻财" };
        int streak = Math.Max(0, Math.Min(3, value.dealer_streak));
        return new RoundIndicatorDisplay {
            // The flipped face itself is the joker; never apply riichi's next-tile mapping.
            Tiles = new[] { value.indicator },
            Caption = $"财神 {WenzhouText.Tile(value.caishen)}\n"
                + (value.caishen == 46 ? "白板为财神" : $"白板当{WenzhouText.Tile(value.white_natural)}"),
            Counter = $"连庄{streak} · {2 * (streak + 1)}倍",
        };
    }

    public static void Refresh(WenzhouInfo value) {
        DisplayedInfo = value;
        RoundPanel.Instance?.RefreshRuleIndicators(FieldState(value));
        if (GameCanvas.Instance != null)
            foreach (var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true)) WenzhouJokerBadge.Refresh(card);
    }

    public static void Clear() => Refresh(null);
}

/// <summary>Marks real joker hand tiles without changing face art or the physical id.</summary>
public static class WenzhouJokerBadge {
    public static void Refresh(TileCard card) {
        if (card == null) return;
        bool visible = WenzhouTableIndicators.DisplayedInfo?.caishen == card.tileId;
        Transform parent = card.BadgeAnchor;
        Transform existing = parent.Find("WenzhouJokerBadge") ?? card.transform.Find("WenzhouJokerBadge");
        if (!visible) { if (existing != null) existing.gameObject.SetActive(false); return; }
        if (existing == null) {
            var go = new GameObject("WenzhouJokerBadge", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false); existing = go.transform;
            var rect = (RectTransform)existing; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-2, -2); rect.sizeDelta = new Vector2(21, 23);
            var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(.70f, .18f, .12f); image.raycastTarget = false;
            var textObject = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)); textObject.transform.SetParent(existing, false);
            var label = textObject.GetComponent<TextMeshProUGUI>(); label.text = "财"; label.fontSize = 16; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.font = WenzhouTableIndicators.SharedFont;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        }
        if (existing.parent != parent) existing.SetParent(parent, false);
        existing.gameObject.SetActive(true); existing.SetAsLastSibling();
    }
}
