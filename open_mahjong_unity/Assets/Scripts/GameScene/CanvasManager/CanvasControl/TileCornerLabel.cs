using TMPro;
using UnityEngine;

/// <summary>Optional physical tile names, independent of a rule's joker/gold badges.</summary>
public sealed class TileCornerLabel : MonoBehaviour {
    public const string Restriction = "*该卡牌角标只有在匹配场中级场及以上段位不可使用。";
    public const string EnglishRestriction = "Labels are disabled in Intermediate and higher ranked rooms.";
    public const float FontSize = 26f;
    public static readonly Color BlackInk = new Color32(24, 24, 24, 255);
    public static readonly Color RedInk = new Color32(177, 25, 22, 255);
    private TMP_Text label;
    private bool? preview;

    public static string Caption(int physicalTile) {
        int tile = TileIdOrder.Normalize(physicalTile);
        int suit = tile / 10, number = tile % 10;
        // Physical Western indices: Arabic ranks, compass initials, C/F/P dragons.
        // Reference: https://www.ymimports.com/products/us-cm003-a and
        // https://c.tabletopia.com/games/old-hong-kong-mahjong/rules/mahjong-rules/en
        if (suit >= 1 && suit <= 3 && number >= 1 && number <= 9) return number.ToString();
        switch (tile) {
            case 41: return "E"; case 42: return "S"; case 43: return "W"; case 44: return "N";
            case 45: return "C"; case 46: return "P"; case 47: return "F";
            case 51: case 52: case 53: case 54: return (tile - 50).ToString();
            case 55: case 56: case 57: case 58: return (tile - 54).ToString();
            default: return null; // Never identify backs, unknown tiles or custom game pieces as ordinary tiles.
        }
    }

    public static bool IsRestricted(string roomType, string tier, string queue) {
        if (roomType != "match") return false;
        string tokens = "_" + (tier ?? "").ToLowerInvariant() + "_" + (queue ?? "").ToLowerInvariant() + "_";
        return tokens.Contains("_intermediate_") || tokens.Contains("_advanced_") || tokens.Contains("_mcrpl_");
    }

    public static Color Ink(int physicalTile) {
        int tile = TileIdOrder.Normalize(physicalTile);
        bool red = tile >= 11 && tile <= 19 || tile == 45 || tile >= 51 && tile <= 54
            || physicalTile == 105 || physicalTile == 205 || physicalTile == 305;
        return red ? RedInk : BlackInk;
    }

    public static bool Restricted {
        get {
            var record = GameRecordManager.Instance;
            if (record != null && record.isActiveAndEnabled && record.gameRecord?.gameTitle != null) {
                var title = record.gameRecord.gameTitle;
                string Read(string key) => title.TryGetValue(key, out object value) ? value?.ToString() : null;
                return IsRestricted(Read("room_type"), Read("match_tier"), Read("match_queue_type"));
            }
            var session = GameSession.Current;
            return session != null && !string.IsNullOrEmpty(session.RoomRule)
                && IsRestricted(session.RoomType, session.MatchTier, session.MatchQueueType);
        }
    }
    public static bool Enabled => ConfigManager.Instance != null && ConfigManager.Instance.TileCornerLabelsEnabled && !Restricted;
    public static bool ShowsOn(Transform card, int tile) =>
        (card.GetComponent<TileCornerLabel>()?.preview ?? Enabled) && !string.IsNullOrEmpty(Caption(tile));

    public static void Apply(Transform card, int tile, bool? previewEnabled = null) {
        var marker = card.GetComponent<TileCornerLabel>();
        bool visible = previewEnabled ?? marker?.preview ?? Enabled;
        string text = visible ? Caption(tile) : null;
        TileFaceOverlay.RefreshRuleCorners(card, !string.IsNullOrEmpty(text));
        if (marker == null && !previewEnabled.HasValue && string.IsNullOrEmpty(text)) return;
        if (marker == null) marker = card.gameObject.AddComponent<TileCornerLabel>();
        if (previewEnabled.HasValue) marker.preview = previewEnabled;
        if (string.IsNullOrEmpty(text)) { if (marker.label) marker.label.gameObject.SetActive(false); return; }
        var parent = TileFaceOverlay.Get(card);
        if (!marker.label) {
            var go = new GameObject("TileCornerLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = card.gameObject.layer; go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>(); marker.label = label;
            label.font = PlatformTypography.LatinBold ?? TMP_Settings.defaultFontAsset;
            label.fontStyle = FontStyles.Normal; label.fontSize = FontSize;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.alignment = TextAlignmentOptions.TopRight;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = -TileFaceOverlay.CornerInset; rect.sizeDelta = new Vector2(32, 32);
        }
        marker.label.text = text; marker.label.color = Ink(tile); marker.label.gameObject.SetActive(true);
    }

    public static void RefreshAll() {
        foreach (var card in Object.FindObjectsByType<TileCard>(UnityEngine.FindObjectsSortMode.None)) Apply(card.transform, card.tileId);
        foreach (var card in Object.FindObjectsByType<StaticCard>(UnityEngine.FindObjectsSortMode.None)) Apply(card.transform, card.TileId);
        foreach (var card in Object.FindObjectsByType<Tile3D>(UnityEngine.FindObjectsSortMode.None)) card.RefreshRuleBadge();
    }
}
