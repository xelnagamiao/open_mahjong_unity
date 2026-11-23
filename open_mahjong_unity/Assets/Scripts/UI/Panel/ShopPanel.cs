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
    private ViewportCardList<string, ShopRow> goodsList;
    private ViewportCardList<long, ShopWalletRow> historyList;
    private float goodsHeight, historyHeight;
    private readonly List<WalletEntry> entries = new List<WalletEntry>();
    private readonly List<long?> pageCursors = new List<long?> { null };
    private int historyPage;
    private int? pendingHistoryPage;
    private ShopOffer[] offers = Array.Empty<ShopOffer>();
    private string selectedId;
    private ShopCommand pending;
    private int openedUserId;
    private bool busy, showingHistory, hasMore, hasCatalog, hasHistory;
    private Coroutine timeout;
    public bool IsOpen => gameObject.activeInHierarchy && !(fade && fade.IsClosing);
    private bool IsPurchase => pending != null && pending.type == "shop/purchase";
    private ShopOffer Selected => Array.Find(offers, offer => offer != null && offer.offer_id == selectedId);
    public static string Money(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public static string Kind(InventoryItem item) => WarehouseEntry.ItemKindLabel(item);

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
        goodsHeight = goodsContent.GetComponent<UnityEngine.UI.GridLayoutGroup>().cellSize.y;
        historyHeight = historyTemplate.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight;
        goodsList = new(goodsScroll, goodsContent, _ => Instantiate(rowTemplate, goodsContent), BindOffer);
        historyList = new(historyScroll, historyContent, _ => Instantiate(historyTemplate, historyContent),
            (row, index) => row.Bind(entries[index]));
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
            hasCatalog = hasHistory = false;
            historyPage = 0; pageCursors.Clear(); pageCursors.Add(null); pendingHistoryPage = null;
        }
        quantityInput.SetTextWithoutNotify(IsPurchase ? pending.quantity.ToString() : "1");
        // The language can change while this panel is closed and unsubscribed.
        SyncRows(true);
        RenderDetail(); UpdateControls();
        if (IsPurchase) Status("上次购买结果未确认，请重试原购买");
        else Refresh();
    }

    public void Close() { if (fade) fade.Hide(); else gameObject.SetActive(false); }

    private void LateUpdate() {
        if (user != null && user.UserId != openedUserId) { Close(); return; }
        if (showingHistory) historyList.Tick(); else goodsList.Tick();
    }

    private void LanguageChanged() {
        SyncRows(false);
        RenderDetail(); UpdateControls();
    }
    internal static void SetLabelText(TMP_Text label, string value) {
        if (label.text == value) return;
        label.text = value;
        // ScrollRect and currency icons can request geometry before the font binding's LateUpdate.
        if (!string.IsNullOrEmpty(value) && label.TryGetComponent(out PlatformFontBinding binding))
            binding.ApplyIfNeeded();
    }
    private void Status(string source) => SetLabelText(statusLabel, string.IsNullOrEmpty(source) ? string.Empty : PlatformText.Get(source));
    private void InventoryChanged() {
        if (user?.Inventory?.owned != null) foreach (var offer in offers)
            offer.owned_quantity = Array.Find(user.Inventory.owned, balance => balance.item_id == offer.item.item_id)?.quantity ?? 0;
        foreach (var offer in offers)
            if (goodsList.TryGetVisible(offer.offer_id, out var row)) row.RefreshOwned(offer.owned_quantity);
        UpdateControls();
    }

    public void Refresh() {
        if (busy) return;
        if (IsPurchase) { SendPending(); return; }
        pending = new ShopCommand { type = "shop/get", request_id = Guid.NewGuid().ToString("N") };
        Status(string.Empty); SendPending();
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
            if (response.shop_offers != null) {
                hasCatalog = true;
                var seen = new HashSet<string>();
                offers = response.shop_offers.Where(offer => offer?.item != null && offer.unit_price_minor > 0 &&
                    !string.IsNullOrEmpty(offer.offer_id) && seen.Add(offer.offer_id)).ToArray();
            }
            if (response.wallet_entries != null) {
                hasHistory = true;
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
            SyncRows(pageRequest); RenderDetail(); UpdateControls();
            Status(wasPurchase ? "购买成功，物品已放入背包" : "");
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
        Status(string.Empty); SendPending();
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

    private void SyncRows(bool resetHistory) {
        if (selectedId == null || Selected == null) selectedId = offers.FirstOrDefault()?.offer_id;
        string[] ids = Array.ConvertAll(offers, offer => offer.offer_id);
        goodsList.SetRows(ids, _ => 0, _ => goodsHeight);
        foreach (var offer in offers) {
            if (goodsList.TryGetVisible(offer.offer_id, out var row) && !row.Matches(offer))
                goodsList.Invalidate(offer.offer_id);
        }
        // History is paginated; an index also works for legacy entries without a unique ID.
        long[] historyKeys = new long[entries.Count];
        for (int i = 0; i < historyKeys.Length; i++) historyKeys[i] = i;
        historyList.SetRows(historyKeys, _ => 0, _ => historyHeight, resetScroll: resetHistory);
        foreach (var key in historyKeys) historyList.Invalidate(key);
    }

    private void BindOffer(ShopRow row, int index) {
        var offer = offers[index];
        row.Bind(offer, SelectOffer);
        row.SetSelected(offer.offer_id == selectedId, !busy && !IsPurchase);
    }

    private void SelectOffer(string id) {
        selectedId = id;
        quantityInput.SetTextWithoutNotify("1");
        RenderDetail(); UpdateControls();
    }

    private void RenderDetail() {
        var selected = Selected; detailRoot.SetActive(selected != null && !showingHistory);
        if (selected == null) return;
        var item = selected.item;
        SetLabelText(detailName, PlatformText.Get(item.name));
        SetLabelText(detailKind, PlatformText.Get(Kind(item)));
        SetLabelText(detailDescription, PlatformText.Get(item.description ?? ""));
        descriptionScroll.StopMovement();
        var content = descriptionScroll.content;
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0);
        ConfigManager.SetInventoryIcon(detailIcon, item); detailIcon.enabled = detailIcon.sprite != null;
        AvatarFrameGraphic.Apply(detailIcon, item.slot == "avatar_frame" ? item.item_id : -1);
        detailArtwork.gameObject.SetActive(!detailIcon.enabled); detailArtwork.SetKind(string.IsNullOrEmpty(item.slot) ? 2 : 3);
        SetLabelText(detailGlyph, PlatformText.Get(item.effect == "rename_credit" ? "名" : "礼")); detailGlyph.gameObject.SetActive(!detailIcon.enabled);
    }

    private void UpdateControls() {
        if (!balanceLabel || user == null) return;
        var wallet = user.Wallet;
        balanceLabel.text = wallet == null ? "—" : Money(wallet.balance_minor);
        SetLabelText(earnedLabel, wallet == null ? string.Empty : PlatformText.Format("累计获得 {0}", Money(wallet.earned_minor)));
        goodsRoot.SetActive(!showingHistory); historyRoot.SetActive(showingHistory);
        detailRoot.SetActive(Selected != null && !showingHistory);
        goodsTab.targetGraphic.color = showingHistory ? new Color(.25f,.25f,.25f) : new Color(.41f,.35f,.29f);
        historyTab.targetGraphic.color = showingHistory ? new Color(.41f,.35f,.29f) : new Color(.25f,.25f,.25f);
        emptyGoods.gameObject.SetActive(hasCatalog && offers.Length == 0 && !showingHistory);
        if (emptyGoods.gameObject.activeSelf) SetLabelText(emptyGoods, PlatformText.Get("暂无上架商品"));
        emptyHistory.gameObject.SetActive(hasHistory && entries.Count == 0 && showingHistory);
        moreButton.gameObject.SetActive(showingHistory); moreButton.interactable = hasMore && !busy && !IsPurchase;
        previousPageButton.gameObject.SetActive(showingHistory); previousPageButton.interactable = historyPage > 0 && !busy && !IsPurchase;
        historyPageLabel.gameObject.SetActive(showingHistory); SetLabelText(historyPageLabel, PlatformText.Format("第 {0} 页", historyPage + 1));
        var selected = Selected;
        int quantity = Quantity, max = MaxQuantity(selected);
        bool allowed = !busy && !IsPurchase;
        quantityInput.interactable = allowed && max > 0;
        decreaseButton.interactable = allowed && quantity > 1;
        increaseButton.interactable = allowed && quantity < max;
        if (selected != null) {
            SetLabelText(detailOwned, PlatformText.Format("已拥有 {0} 件", selected.owned_quantity));
            SetLabelText(detailPrice, PlatformText.Format("合计 {0}", Money(selected.unit_price_minor * Math.Max(0, quantity))));
        }
        foreach (var offer in offers)
            if (goodsList.TryGetVisible(offer.offer_id, out var row)) row.SetSelected(offer == selected, !busy && !IsPurchase);
        bool registered = wallet != null && wallet.registered && !user.IsTourist;
        bool enough = selected != null && quantity >= 1 && quantity <= max && wallet != null && selected.unit_price_minor * quantity <= wallet.balance_minor;
        buyButton.interactable = !busy && (IsPurchase || registered && enough);
        SetLabelText(buyLabel, PlatformText.Get(busy ? "正在处理…" : IsPurchase ? "重试原购买" : !registered ? "游客无法购买物品" : max == 0 ? "已拥有" : !enough ? "萨拉币不足或数量无效" : "购买"));
    }
}
