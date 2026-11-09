using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>Authored shop; balances and ownership always come from the server.</summary>
public sealed class ShopPanel : MonoBehaviour {
    public const string RewardRuleText = "在匹配对局中每完成一小局，获得0.05个萨拉币，例如国标麻将全庄战一共16小局，对局结束后获得0.8萨拉币。";
    public UnityEngine.UI.Button backdrop, closeButton, goodsTab, historyTab;
    public UnityEngine.UI.Button buyButton, decreaseButton, increaseButton, moreButton;
    public UnityEngine.UI.Button previousPageButton;
    public TMP_Text historyPageLabel;
    public TMP_Text balanceLabel, earnedLabel, statusLabel, buyLabel, detailName, detailKind;
    public TMP_Text detailDescription, detailPrice, detailOwned, emptyGoods, emptyHistory;
    public TMP_InputField quantityInput;
    public UnityEngine.UI.Image detailIcon;
    public WarehouseIconGraphic detailArtwork;
    public TMP_Text detailGlyph;
    public GameObject goodsRoot, historyRoot, detailRoot;
    public RectTransform goodsContent, historyContent;
    public ShopRow rowTemplate;
    public ShopWalletRow historyTemplate;
    public UnityEngine.UI.ScrollRect goodsScroll, historyScroll, descriptionScroll;

    private UserDataManager user;
    private NetworkManager network;
    private Salasasa.Home.BakedModalFade fade;
    private readonly List<ShopRow> rows = new List<ShopRow>();
    private readonly List<ShopWalletRow> historyRows = new List<ShopWalletRow>();
    private readonly List<WalletEntry> entries = new List<WalletEntry>();
    private readonly List<long?> pageCursors = new List<long?> { null };
    private int historyPage;
    private int? pendingHistoryPage;
    private ShopOffer[] offers = Array.Empty<ShopOffer>();
    private string selectedId;
    private ShopCommand pending;
    private int openedUserId;
    private bool busy, showingHistory, hasMore;
    private Coroutine timeout;
    public bool IsOpen => gameObject.activeInHierarchy && !(fade && fade.IsClosing);
    private bool IsPurchase => pending != null && pending.type == "shop/purchase";
    private ShopOffer Selected => Array.Find(offers, offer => offer != null && offer.offer_id == selectedId);
    public static string Money(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public static string Kind(InventoryItem item) => !string.IsNullOrEmpty(item?.slot) ? "永久装扮" : item?.effect == "collectible" ? "收藏品" : "消耗品";

    private void Awake() {
        fade = GetComponent<Salasasa.Home.BakedModalFade>();
        backdrop.onClick.AddListener(Close); closeButton.onClick.AddListener(Close);
        goodsTab.onClick.AddListener(() => SetTab(false)); historyTab.onClick.AddListener(() => SetTab(true));
        buyButton.onClick.AddListener(Buy); moreButton.onClick.AddListener(More);
        previousPageButton.onClick.AddListener(PreviousPage);
        decreaseButton.onClick.AddListener(() => AdjustQuantity(-1));
        increaseButton.onClick.AddListener(() => AdjustQuantity(1));
        quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        quantityInput.onValueChanged.AddListener(_ => UpdateControls());
        rowTemplate.gameObject.SetActive(false); historyTemplate.gameObject.SetActive(false);
    }

    private void OnEnable() {
        user = UserDataManager.Instance; network = NetworkManager.Instance;
        if (user != null) { user.OnWalletChanged += UpdateControls; user.OnInventoryChanged += InventoryChanged; }
        if (network != null) network.CommerceResponseReceived += ResponseReceived;
        ConfigManager.OnLanguageChanged += LanguageChanged;
    }

    private void OnDisable() {
        if (user != null) { user.OnWalletChanged -= UpdateControls; user.OnInventoryChanged -= InventoryChanged; }
        if (network != null) network.CommerceResponseReceived -= ResponseReceived;
        ConfigManager.OnLanguageChanged -= LanguageChanged;
        StopTimeout(); busy = false;
        if (!IsPurchase) pending = null;
    }

    public void Open() {
        if (fade) fade.Show(); else gameObject.SetActive(true);
        transform.SetAsLastSibling();
        if (user == null || user.UserId == 0) { Close(); return; }
        if (openedUserId != user.UserId) {
            openedUserId = user.UserId; pending = null; offers = Array.Empty<ShopOffer>();
            entries.Clear(); selectedId = null; showingHistory = false; hasMore = false;
            historyPage = 0; pageCursors.Clear(); pageCursors.Add(null); pendingHistoryPage = null;
        }
        quantityInput.SetTextWithoutNotify(IsPurchase ? pending.quantity.ToString() : "1");
        Render();
        if (IsPurchase) Status("上次购买结果未确认，请重试原购买");
        else Refresh();
    }

    public void Close() { if (fade) fade.Hide(); else gameObject.SetActive(false); }

    private void LateUpdate() {
        if (user != null && user.UserId != openedUserId) Close();
    }

    private void LanguageChanged() => Render();
    private void Status(string source) => statusLabel.text = PlatformText.Get(source);
    private void InventoryChanged() {
        if (user?.Inventory?.owned != null) foreach (var offer in offers)
            offer.owned_quantity = Array.Find(user.Inventory.owned, balance => balance.item_id == offer.item.item_id)?.quantity ?? 0;
        for (int i = 0; i < Math.Min(rows.Count, offers.Length); i++) rows[i].RefreshOwned(offers[i].owned_quantity);
        UpdateControls();
    }

    public void Refresh() {
        if (busy) return;
        if (IsPurchase) { SendPending(); return; }
        pending = new ShopCommand { type = "shop/get", request_id = Guid.NewGuid().ToString("N") };
        Status("正在同步商店与萨拉币…"); SendPending();
    }

    private void SendPending() {
        if (pending == null || network == null || busy) return;
        busy = true; UpdateControls(); StopTimeout();
        timeout = StartCoroutine(WaitForResult(pending.request_id));
        network.RequestCommerce(pending);
    }

    private IEnumerator WaitForResult(string requestId) {
        yield return new WaitForSecondsRealtime(12);
        timeout = null;
        if (pending?.request_id != requestId) yield break;
        busy = false;
        Status(IsPurchase ? "购买结果未确认，请重试原购买" : "商店同步超时，请重新打开商店");
        if (!IsPurchase) pending = null;
        UpdateControls();
    }

    private void StopTimeout() { if (timeout != null) StopCoroutine(timeout); timeout = null; }

    private void ResponseReceived(Response response) {
        if (!IsOpen || user == null || user.UserId != openedUserId || pending == null || response.request_id != pending.request_id) return;
        if (response.success && response.wallet_state?.user_id != openedUserId) return;
        bool pageRequest = pending.type == "wallet/get";
        bool wasPurchase = IsPurchase;
        StopTimeout(); busy = false;
        if (response.success) {
            if (response.shop_offers != null) offers = response.shop_offers.Where(offer => offer?.item != null && offer.unit_price_minor > 0).ToArray();
            if (response.wallet_entries != null) {
                bool emptyNext = pageRequest && pendingHistoryPage > historyPage && response.wallet_entries.Length == 0;
                if (!emptyNext) {
                    entries.Clear();
                    entries.AddRange(response.wallet_entries.Where(entry => entry != null).Take(30));
                    if (pageRequest) historyPage = pendingHistoryPage ?? 0;
                    else { historyPage = 0; pageCursors.Clear(); pageCursors.Add(null); }
                }
                hasMore = !emptyNext && response.wallet_entries.Length >= 30;
            }
            pendingHistoryPage = null;
            pending = null;
            if (wasPurchase) quantityInput.SetTextWithoutNotify("1");
            Render(); Status(wasPurchase ? "购买成功，物品已放入背包" : "");
        } else {
            if (!response.commerce_retryable || !wasPurchase) pending = null;
            Status(response.message ?? "商店请求失败，请重新打开商店"); UpdateControls();
        }
    }

    private void SetTab(bool history) { showingHistory = history; UpdateControls(); }
    private int Quantity => int.TryParse(quantityInput.text, out int value) ? value : 0;
    private int MaxQuantity(ShopOffer offer) => offer == null ? 0 : offer.prevent_duplicate_purchase || offer.item.category != "item" ? offer.owned_quantity > 0 ? 0 : 1
        : Math.Max(0, Math.Min(offer.max_purchase_quantity, offer.item.max_quantity - offer.owned_quantity));

    private void AdjustQuantity(int delta) {
        if (busy || IsPurchase) return;
        quantityInput.text = Mathf.Clamp(Quantity + delta, 1, Math.Max(1, MaxQuantity(Selected))).ToString();
    }

    private void Buy() {
        if (busy) return;
        if (IsPurchase) { SendPending(); return; }
        var selected = Selected;
        if (selected == null || user?.Wallet == null || user.IsTourist || !user.Wallet.registered) return;
        int quantity = Quantity;
        if (quantity < 1 || quantity > MaxQuantity(selected) || selected.unit_price_minor * quantity > user.Wallet.balance_minor) return;
        pending = new ShopCommand { type = "shop/purchase", request_id = Guid.NewGuid().ToString("N"),
            offer_id = selected.offer_id, quantity = quantity, price_version = selected.price_version };
        Status("正在购买…"); SendPending();
    }

    private void More() {
        if (busy || IsPurchase || !hasMore || entries.Count == 0) return;
        long cursor = entries.Min(entry => entry.entry_id);
        if (pageCursors.Count <= historyPage + 1) pageCursors.Add(cursor); else pageCursors[historyPage + 1] = cursor;
        RequestHistoryPage(historyPage + 1);
    }

    private void PreviousPage() {
        if (busy || IsPurchase || historyPage == 0) return;
        RequestHistoryPage(historyPage - 1);
    }

    private void RequestHistoryPage(int page) {
        pendingHistoryPage = page;
        pending = new ShopCommand { type = "wallet/get", request_id = Guid.NewGuid().ToString("N"), before_entry_id = pageCursors[page] };
        SendPending();
    }

    private void Render() {
        if (selectedId == null || Selected == null) selectedId = offers.FirstOrDefault()?.offer_id;
        foreach (var row in rows) { row.gameObject.SetActive(false); Destroy(row.gameObject); } rows.Clear();
        foreach (var offer in offers) {
            var row = Instantiate(rowTemplate, goodsContent); row.gameObject.SetActive(true);
            row.Bind(offer, id => { selectedId = id; quantityInput.SetTextWithoutNotify("1"); RenderDetail(); UpdateControls(); }); rows.Add(row);
        }
        for (int i = 0; i < entries.Count; i++) {
            if (i >= historyRows.Count) historyRows.Add(Instantiate(historyTemplate, historyContent));
            historyRows[i].gameObject.SetActive(true); historyRows[i].Bind(entries[i]);
        }
        for (int i = entries.Count; i < historyRows.Count; i++) historyRows[i].gameObject.SetActive(false);
        historyScroll.verticalNormalizedPosition = 1;
        RenderDetail(); UpdateControls();
    }

    private void RenderDetail() {
        var selected = Selected; detailRoot.SetActive(selected != null && !showingHistory);
        if (selected == null) return;
        var item = selected.item;
        detailName.text = PlatformText.Get(item.name); detailKind.text = PlatformText.Get(Kind(item));
        detailDescription.text = PlatformText.Get(item.description ?? ""); descriptionScroll.verticalNormalizedPosition = 1;
        detailIcon.sprite = ConfigManager.GetInventoryIcon(item); detailIcon.enabled = detailIcon.sprite != null;
        if (item.slot == "avatar_frame") AvatarFrameGraphic.Apply(detailIcon, item.item_id);
        detailArtwork.gameObject.SetActive(!detailIcon.enabled); detailArtwork.SetKind(string.IsNullOrEmpty(item.slot) ? 2 : 3);
        detailGlyph.text = PlatformText.Get(item.effect == "rename_credit" ? "名" : "礼"); detailGlyph.gameObject.SetActive(!detailIcon.enabled);
    }

    private void UpdateControls() {
        if (!balanceLabel || user == null) return;
        var wallet = user.Wallet;
        balanceLabel.text = wallet == null ? "—" : Money(wallet.balance_minor);
        earnedLabel.text = wallet == null ? PlatformText.Get("正在同步萨拉币") : PlatformText.Format("累计获得 {0}", Money(wallet.earned_minor));
        goodsRoot.SetActive(!showingHistory); historyRoot.SetActive(showingHistory);
        detailRoot.SetActive(Selected != null && !showingHistory);
        goodsTab.targetGraphic.color = showingHistory ? new Color(.25f,.25f,.25f) : new Color(.41f,.35f,.29f);
        historyTab.targetGraphic.color = showingHistory ? new Color(.41f,.35f,.29f) : new Color(.25f,.25f,.25f);
        emptyGoods.gameObject.SetActive(offers.Length == 0 && !showingHistory);
        emptyGoods.text = PlatformText.Get(busy ? "正在加载商品…" : "暂无上架商品");
        emptyHistory.gameObject.SetActive(entries.Count == 0 && showingHistory);
        moreButton.gameObject.SetActive(showingHistory); moreButton.interactable = hasMore && !busy && !IsPurchase;
        previousPageButton.gameObject.SetActive(showingHistory); previousPageButton.interactable = historyPage > 0 && !busy && !IsPurchase;
        historyPageLabel.gameObject.SetActive(showingHistory); historyPageLabel.text = PlatformText.Format("第 {0} 页", historyPage + 1);
        var selected = Selected;
        int quantity = Quantity, max = MaxQuantity(selected);
        bool allowed = !busy && !IsPurchase;
        quantityInput.interactable = allowed && max > 0;
        decreaseButton.interactable = allowed && quantity > 1;
        increaseButton.interactable = allowed && quantity < max;
        if (selected != null) {
            detailOwned.text = PlatformText.Format("已拥有 {0} 件", selected.owned_quantity);
            detailPrice.text = PlatformText.Format("合计 {0}", Money(selected.unit_price_minor * Math.Max(0, quantity)));
        }
        for (int i = 0; i < rows.Count; i++) rows[i].SetSelected(i < offers.Length && offers[i] == selected, !busy && !IsPurchase);
        bool registered = wallet != null && wallet.registered && !user.IsTourist;
        bool enough = selected != null && quantity >= 1 && quantity <= max && wallet != null && selected.unit_price_minor * quantity <= wallet.balance_minor;
        buyButton.interactable = !busy && (IsPurchase || registered && enough);
        buyLabel.text = PlatformText.Get(busy ? "正在处理…" : IsPurchase ? "重试原购买" : !registered ? "游客无法购买物品" : max == 0 ? "已拥有" : !enough ? "萨拉币不足或数量无效" : "购买");
    }
}
