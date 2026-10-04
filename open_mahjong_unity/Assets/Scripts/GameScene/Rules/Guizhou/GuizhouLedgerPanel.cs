using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>局终可展开、分页的账本；明细来自服务端，始终使用游戏现有的 TMP 字体。</summary>
public sealed class GuizhouLedgerPanel : MonoBehaviour {
    private const int PageSize = 6;
    private static GuizhouLedgerPanel instance;
    private GuizhouInfo info;
    private TMP_Text caption, body, pageText;
    private UnityEngine.UI.Button previous, next;
    private bool expanded;
    private int page;

    public static void Show(GuizhouInfo value) {
        if (value?.ledger == null || GameCanvas.Instance == null) { Hide(); return; }
        if (instance == null) {
            var root = new GameObject("GuizhouLedger", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster), typeof(GuizhouLedgerPanel));
            root.transform.SetParent(GameCanvas.Instance.transform, false);
            root.layer = GameCanvas.Instance.gameObject.layer;
            root.transform.SetAsLastSibling();
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            // PageUI is above the BoardUI avatars and the settlement page is
            // order 5. Keep actual OverlayUI dialogs above this optional ledger.
            canvas.sortingLayerID = SortingLayer.NameToID("PageUI");
            canvas.sortingOrder = 6;
            instance = root.GetComponent<GuizhouLedgerPanel>();
            instance.Build();
        }
        if (instance.info?.hand_number != value.hand_number || !instance.gameObject.activeSelf) {
            instance.page = 0; instance.expanded = false;
        }
        instance.info = value;
        instance.gameObject.SetActive(true);
        instance.Refresh();
    }
    public static void Hide() {
        if (instance != null) { instance.gameObject.SetActive(false); instance.info = null; }
    }
    private static string Signed(int value) => value.ToString("+#;-#;0");
    private string Seat(int seat) {
        int original = info.seat_to_original != null && seat >= 0 && seat < info.seat_to_original.Length ? info.seat_to_original[seat] : seat;
        return new[] { "东起", "南起", "西起", "北起" }[Mathf.Clamp(original, 0, 3)];
    }
    private static string Tile(int? tile) => !tile.HasValue || tile.Value == 0 ? "无" :
        $"{tile.Value % 10}{(tile.Value / 10 == 1 ? "万" : tile.Value / 10 == 2 ? "饼" : "条")}";
    private void Refresh() {
        var ledger = info.ledger;
        var transfers = ledger.transfers ?? Array.Empty<GuizhouTransfer>();
        int pages = Math.Max(1, (transfers.Length + PageSize - 1) / PageSize);
        page = Mathf.Clamp(page, 0, pages - 1);
        caption.text = $"鸡杠结算 · {(expanded ? "收起" : "展开")}";
        var text = new StringBuilder();
        text.AppendLine(ledger.indicator.HasValue ? $"翻牌 {Tile(ledger.indicator)} · 和牌鸡 {Tile(ledger.chicken_tile)}" : "无和牌鸡");
        text.AppendLine("<pos=78>和/查叫<pos=194>鸡<pos=247>杠<pos=304>合计");
        for (int seat = 0; seat < 4; seat++) {
            int Net(string category) => transfers.Where(t => t.category == category)
                .Sum(t => t.payee == seat ? t.points : t.payer == seat ? -t.points : 0);
            int total = ledger.score_changes != null && seat < ledger.score_changes.Length ? ledger.score_changes[seat] : 0;
            text.AppendLine($"{Seat(seat)}<pos=78>{Signed(Net("hand") + Net("ready"))}<pos=194>{Signed(Net("chicken"))}<pos=247>{Signed(Net("kong"))}<pos=304>{Signed(total)}");
        }
        text.AppendLine("支付明细");
        if (transfers.Length == 0) text.AppendLine("本局无分数转移");
        foreach (var transfer in transfers.Skip(page * PageSize).Take(PageSize)) {
            string tile = transfer.tile.HasValue ? $" {Tile(transfer.tile)}" : "";
            text.AppendLine($"{Seat(transfer.payer)} 付 {Seat(transfer.payee)}  {transfer.points}分\n  {transfer.reason}{tile}");
        }
        body.text = text.ToString().TrimEnd();
        body.gameObject.SetActive(expanded);
        previous.gameObject.SetActive(expanded); next.gameObject.SetActive(expanded); pageText.gameObject.SetActive(expanded);
        previous.interactable = page > 0; next.interactable = page + 1 < pages;
        pageText.text = $"{page + 1}/{pages}";
        ((RectTransform)transform).sizeDelta = new Vector2(390, expanded ? 590 : 36);
        GetComponent<UnityEngine.UI.Image>().raycastTarget = expanded;
    }
    private void Build() {
        var rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(16, -185);
        var background = GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(.055f, .075f, .095f, .99f); background.raycastTarget = false;
        TMP_FontAsset font = GameCanvas.Instance.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.font != null)?.font ?? TMP_Settings.defaultFontAsset;
        var toggle = Button("Toggle", font, "", new Vector2(0, -2), new Vector2(386, 32), true);
        caption = toggle.GetComponentInChildren<TMP_Text>();
        toggle.onClick.AddListener(() => { expanded = !expanded; Refresh(); });
        body = Label("Entries", font, transform);
        body.fontSize = 18; body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.NoWrap; body.overflowMode = TextOverflowModes.Truncate;
        body.rectTransform.anchorMin = Vector2.zero; body.rectTransform.anchorMax = Vector2.one;
        body.rectTransform.offsetMin = new Vector2(12, 44); body.rectTransform.offsetMax = new Vector2(-12, -43);
        previous = Button("Previous", font, "上一页", new Vector2(-99, 8), new Vector2(104, 30), false);
        next = Button("Next", font, "下一页", new Vector2(99, 8), new Vector2(104, 30), false);
        previous.onClick.AddListener(() => { page--; Refresh(); }); next.onClick.AddListener(() => { page++; Refresh(); });
        pageText = Label("Page", font, transform); pageText.alignment = TextAlignmentOptions.Center; pageText.fontSize = 18;
        pageText.rectTransform.anchorMin = pageText.rectTransform.anchorMax = pageText.rectTransform.pivot = new Vector2(.5f, 0);
        pageText.rectTransform.anchoredPosition = new Vector2(0, 8); pageText.rectTransform.sizeDelta = new Vector2(76, 30);
    }
    private TMP_Text Label(string name, TMP_FontAsset font, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>(); label.font = font; label.color = new Color(.96f, .93f, .82f);
        label.raycastTarget = false; return label;
    }
    private UnityEngine.UI.Button Button(string name, TMP_FontAsset font, string text, Vector2 position, Vector2 size, bool top) {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        go.transform.SetParent(transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, top ? 1 : 0);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        go.GetComponent<UnityEngine.UI.Image>().color = new Color(.22f, .31f, .38f);
        var label = Label("Caption", font, go.transform); label.text = text; label.fontSize = 18; label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return go.GetComponent<UnityEngine.UI.Button>();
    }
}
