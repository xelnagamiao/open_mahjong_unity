using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>Public joker legend and end-of-hand accounting from server metadata.</summary>
public sealed class WenzhouPanel : MonoBehaviour {
    private static WenzhouPanel instance;
    private TMP_Text title, body;
    private WenzhouInfo info;
    private bool expanded;
    public static TMP_FontAsset SharedFont => Resources.Load<TMP_FontAsset>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF") ?? TMP_Settings.defaultFontAsset;
    public static WenzhouInfo DisplayedInfo { get; private set; }
    public static void Show(WenzhouInfo value) {
        DisplayedInfo = value;
        if (value == null || GameCanvas.Instance == null) { Hide(); return; }
        if (instance == null) {
            var go = new GameObject("WenzhouJokerLegend", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster), typeof(WenzhouPanel));
            go.transform.SetParent(GameCanvas.Instance.transform, false);
            go.layer = GameCanvas.Instance.gameObject.layer;
            var canvas = go.GetComponent<Canvas>(); canvas.overrideSorting = true;
            canvas.sortingLayerID = SortingLayer.NameToID("PageUI"); canvas.sortingOrder = 6;
            instance = go.GetComponent<WenzhouPanel>(); instance.Build();
        }
        if (instance.info?.hand_number != value.hand_number) instance.expanded = false;
        instance.info = value; instance.gameObject.SetActive(true); instance.Refresh();
        // Event-driven refresh; no per-frame solver or scene scans.
        foreach (var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true)) WenzhouJokerBadge.Refresh(card);
    }
    public static void Hide() {
        DisplayedInfo = null;
        if (instance != null) { instance.info = null; instance.gameObject.SetActive(false); }
        if (GameCanvas.Instance != null) foreach (var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true)) WenzhouJokerBadge.Refresh(card);
    }
    public static string Summary(WenzhouInfo value) {
        if (value == null) return "";
        var text = new StringBuilder();
        text.AppendLine(value.caishen == 46 ? "白板为财神，可代任意牌。" : $"白板固定代{WenzhouText.Tile(value.white_natural)}；白板仍按实体白板显示。");
        text.AppendLine($"庄家连庄{value.dealer_streak}次 · 收付{2 * (Math.Min(3, value.dealer_streak) + 1)}倍");
        text.AppendLine("财神不参与吃碰杠；以本牌成和可按无财计。");
        if (value.caishen_counts != null) {
            text.AppendLine("局终财神与总收支");
            for (int seat = 0; seat < 4; seat++) {
                int Get(int[] values) => values != null && seat < values.Length ? values[seat] : 0;
                int original = value.seat_to_original != null && seat < value.seat_to_original.Length ? value.seat_to_original[seat] : seat;
                string label = new[] { "东起", "南起", "西起", "北起" }[Mathf.Clamp(original, 0, 3)];
                text.AppendLine($"{label}　财{Get(value.caishen_counts)}张　财分{Get(value.caishen_changes):+#;-#;0}　合计{Get(value.round_changes):+#;-#;0}");
            }
        }
        if (value.next_dealer_dice != null && value.next_dealer_dice.Length > 0) {
            string next = new[] { "东", "南", "西", "北" }[Mathf.Clamp(value.next_dealer_shift, 0, 3)];
            text.AppendLine($"连庄上限掷骰：{string.Join("、", value.next_dealer_dice)}，下庄为本局{next}家。");
        }
        if (value.ledger != null && value.ledger.Count > 0) {
            text.AppendLine("逐笔收支（本局座位）");
            foreach (var entry in value.ledger) {
                string kind = (string)entry["kind"] ?? "";
                string label = kind == "concealed_kong" || kind == "angang" ? "暗杠" : kind == "exposed_kong" || kind == "minggang" || kind == "ming" ? "明杠" : kind == "jiagang" ? "加杠" : kind == "caishen" ? "财神" : kind == "win" ? "和牌" : kind;
                var changes = entry["changes"] as Newtonsoft.Json.Linq.JArray;
                if (changes == null || changes.Count < 4) continue;
                text.AppendLine($"{label}：东{(int)changes[0]:+#;-#;0} 南{(int)changes[1]:+#;-#;0} 西{(int)changes[2]:+#;-#;0} 北{(int)changes[3]:+#;-#;0}");
            }
        }
        return text.ToString().TrimEnd();
    }
    private void Refresh() {
        title.text = $"财神 {WenzhouText.Tile(info.caishen)} · 连庄{info.dealer_streak} · {(expanded ? "收起" : "规则与结算")}";
        body.text = Summary(info); body.transform.parent.gameObject.SetActive(expanded);
        ((RectTransform)transform).sizeDelta = new Vector2(390, expanded ? 400 : 32);
        GetComponent<UnityEngine.UI.Image>().raycastTarget = expanded;
    }
    private void Build() {
        var rect = (RectTransform)transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        // Keep the collapsed legend above both top-seat melds and settlement tiles.
        rect.anchoredPosition = new Vector2(370, -2);
        GetComponent<UnityEngine.UI.Image>().color = new Color(.055f, .075f, .095f, .98f);
        var font = SharedFont;
        var toggle = new GameObject("Toggle", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        toggle.transform.SetParent(transform, false);
        var tr = (RectTransform)toggle.transform; tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(.5f, 1);
        tr.sizeDelta = new Vector2(386, 28); tr.anchoredPosition = new Vector2(0, -2);
        toggle.GetComponent<UnityEngine.UI.Image>().color = new Color(.22f, .31f, .38f);
        toggle.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => { expanded = !expanded; Refresh(); });
        title = Label("Caption", font, toggle.transform); title.alignment = TextAlignmentOptions.Center;
        title.rectTransform.anchorMin = Vector2.zero; title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
        var viewport = new GameObject("DetailsViewport", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.RectMask2D), typeof(UnityEngine.UI.ScrollRect));
        viewport.transform.SetParent(transform, false);
        var vr = (RectTransform)viewport.transform; vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
        vr.offsetMin = new Vector2(12, 10); vr.offsetMax = new Vector2(-12, -43);
        viewport.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
        body = Label("Detail", font, viewport.transform); body.alignment = TextAlignmentOptions.TopLeft;
        body.rectTransform.anchorMin = new Vector2(0, 1); body.rectTransform.anchorMax = Vector2.one; body.rectTransform.pivot = new Vector2(.5f, 1);
        body.rectTransform.offsetMin = body.rectTransform.offsetMax = Vector2.zero;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.GetComponent<UnityEngine.UI.ScrollRect>(); scroll.viewport = vr; scroll.content = body.rectTransform;
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 25;
    }
    private static TMP_Text Label(string name, TMP_FontAsset font, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>(); label.font = font; label.fontSize = 18;
        label.color = new Color(.96f, .93f, .82f); label.raycastTarget = false; return label;
    }
}

/// <summary>Marks real joker hand tiles without changing face art or the physical id.</summary>
public static class WenzhouJokerBadge {
    public static void Refresh(TileCard card) {
        if (card == null) return;
        bool visible = WenzhouPanel.DisplayedInfo?.caishen == card.tileId;
        Transform existing = card.transform.Find("WenzhouJokerBadge");
        if (!visible) { if (existing != null) existing.gameObject.SetActive(false); return; }
        if (existing == null) {
            var go = new GameObject("WenzhouJokerBadge", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(card.transform, false); existing = go.transform;
            var rect = (RectTransform)existing; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-2, -2); rect.sizeDelta = new Vector2(21, 23);
            var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = new Color(.70f, .18f, .12f); image.raycastTarget = false;
            var textObject = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)); textObject.transform.SetParent(existing, false);
            var label = textObject.GetComponent<TextMeshProUGUI>(); label.text = "财"; label.fontSize = 16; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.font = WenzhouPanel.SharedFont;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        }
        existing.gameObject.SetActive(true); existing.SetAsLastSibling();
    }
}
