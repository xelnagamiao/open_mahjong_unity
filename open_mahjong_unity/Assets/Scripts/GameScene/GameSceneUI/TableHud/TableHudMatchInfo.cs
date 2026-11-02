using TMPro;
using UnityEngine;

/// <summary>Room metadata follows the original round content without resizing it.</summary>
public sealed class TableHudMatchInfo : MonoBehaviour {
    [SerializeField] private TMP_Text[] venueLabels;
    [SerializeField] private TMP_Text[] formatLabels;
    [SerializeField] private RectTransform[] contentRects;
    [SerializeField] private RectTransform[] stripRects;
    private readonly Vector3[] corners = new Vector3[4];
    private void OnEnable() {
        ConfigManager.OnLanguageChanged += RefreshLanguage;
        RefreshLanguage();
    }
    private void OnDisable() => ConfigManager.OnLanguageChanged -= RefreshLanguage;
    private void RefreshLanguage() {
        foreach (var labels in new[] { venueLabels, formatLabels }) {
            if (labels == null) continue;
            foreach (var label in labels) if (label) label.GetComponent<LocalizedPlatformLabel>()?.Refresh();
        }
        RefreshLayout();
    }
    public void RefreshInfo(GameInfo info) {
        string venue = Venue(info?.room_type, info?.match_queue_type, info?.match_tier);
        if (venueLabels != null) foreach (var label in venueLabels) if (label) TableText.Set(label, venue);
        if (info?.unlimited_rounds == true && formatLabels != null)
            foreach (var label in formatLabels) if (label) TableText.Set(label, "无固定时限");
        RefreshLayout();
    }

    public void RefreshLayout() {
        if (contentRects == null || stripRects == null) return;
        for (int i = 0; i < contentRects.Length && i < stripRects.Length; i++) {
            var content = contentRects[i]; var strip = stripRects[i];
            if (!content || !strip || strip.parent is not RectTransform parent) continue;
            content.GetWorldCorners(corners);
            float bottom = parent.InverseTransformPoint(corners[0]).y;
            float height = ArrangeLabels(i, parent.rect.width);
            strip.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, parent.rect.width);
            strip.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, parent.rect.yMax - bottom + 4, height);
        }
    }

    private float ArrangeLabels(int index, float width) {
        if (venueLabels == null || formatLabels == null || index >= venueLabels.Length || index >= formatLabels.Length) return 44;
        var venue = venueLabels[index]; var format = formatLabels[index];
        if (!venue || !format) return 44;
        var separator = stripRects[index].Find("Separator")?.GetComponent<TMP_Text>();
        foreach(var label in new[]{venue,format,separator}) if(label) {
            label.fontStyle=FontStyles.Normal;label.alignment=TextAlignmentOptions.Center;
            label.textWrappingMode=TextWrappingModes.NoWrap;
        }
        if(separator)separator.text="·";
        const float gap = 8, dotWidth = 8;
        venue.enableAutoSizing = format.enableAutoSizing = false;
        float available = width - 24;
        float size = 26;
        float vw = 0, fw = 0;
        for (; size >= 20; size--) {
            venue.fontSize = format.fontSize = size;
            vw = venue.GetPreferredValues(venue.text).x;
            fw = format.GetPreferredValues(format.text).x;
            if (vw + fw + gap * 2 + dotWidth <= available) break;
        }
        bool twoRows = size < 20;
        venue.fontSize = format.fontSize = Mathf.Max(20, size);
        venue.enableAutoSizing = format.enableAutoSizing = false;
        if (separator) separator.gameObject.SetActive(!twoRows);
        if (twoRows) {
            Place(venue.rectTransform, 12, 0, available, 32);
            Place(format.rectTransform, 12, -30, available, 32);
            return 64;
        }
        float x = (width - vw - fw - gap * 2 - dotWidth) * .5f;
        Place(venue.rectTransform, x, 0, vw, 44);
        if (separator) { separator.fontSize = size; Place(separator.rectTransform, x + vw + gap, 0, dotWidth, 44); }
        Place(format.rectTransform, x + vw + gap * 2 + dotWidth, 0, fw, 44);
        return 44;
    }

    private static void Place(RectTransform r, float x, float y, float w, float h) {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
    }

    public static string Venue(string roomType, string queue, string tier = null) {
        if (roomType == "custom") return "友人房";
        if (roomType == "events" || roomType == "event") return "赛事场";
        string tokens = "_" + (tier ?? "").ToLowerInvariant() + "_" + (queue ?? "").ToLowerInvariant() + "_";
        if (tokens.Contains("_beginner_")) return "初级场";
        if (tokens.Contains("_intermediate_")) return "中级场";
        if (tokens.Contains("_advanced_")) return "高级场";
        if (tokens.Contains("_mcrpl_")) return "MCRPL";
        if (tokens.Contains("_elo_")) return "Elo 匹配";
        return roomType == "match" ? "排位场" : string.IsNullOrEmpty(roomType) ? "场次未标注" : "自由对局";
    }
}
