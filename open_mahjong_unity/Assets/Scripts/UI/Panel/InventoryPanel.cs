using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Unified warehouse. Catalog ownership and mutations remain server-authoritative.</summary>
public sealed partial class InventoryPanel : MonoBehaviour {
    private static readonly string[] CategoryNames = { "背包", "头衔", "角色", "装扮", "头像" };
    [SerializeField] private UnityEngine.UI.Button[] categoryButtons;
    [SerializeField] private RectTransform content;
    [SerializeField] private InventoryRow rowTemplate;
    [SerializeField] private UnityEngine.UI.Image preview;
    [SerializeField] private WarehouseIconGraphic previewArtwork;
    [SerializeField] private UnityEngine.UI.Image detailRule;
    [SerializeField] private TMP_Text previewGlyph, details, statusText, actionLabel;
    [SerializeField] private TMP_Text detailName, detailType, detailCount, emptyText;
    [SerializeField] private TMP_InputField quantityInput;
    [SerializeField] private UnityEngine.UI.Button actionButton, closeButton;
    [SerializeField] private UnityEngine.UI.Button decreaseButton, increaseButton;

    [SerializeField] private Color categorySurface = new Color(.12f,.14f,.22f);
    [SerializeField] private Color categorySelectedSurface = new Color(.24f,.30f,.47f);
    [SerializeField] private Color categoryInk = new Color(.69f,.73f,.83f);
    [SerializeField] private Color categorySelectedInk = Color.white;

    private readonly List<InventoryRow> rows = new List<InventoryRow>();
    private List<WarehouseEntry> entries = new List<WarehouseEntry>();
    private UserDataManager user;
    private NetworkManager network;
    private WarehouseCategory category;
    private int pendingUserId, openedUserId;
    private string selectedKey;
    private InventoryCommand pending;
    private bool busy;
    private bool developmentVisible;
    private Salasasa.Home.BakedModalFade modalFade;
    public bool IsOpen => gameObject.activeInHierarchy && !(ModalFade && ModalFade.IsClosing);
    private Salasasa.Home.BakedModalFade ModalFade => modalFade ? modalFade : modalFade = GetComponent<Salasasa.Home.BakedModalFade>();
    private Coroutine timeout;
    private bool IsRead => pending != null && pending.type.EndsWith("/get", StringComparison.Ordinal);
    private WarehouseEntry Selected => entries.Find(e => e.Key == selectedKey);

    public UnityEngine.UI.Button redeemButton;
    public RedeemPanel redeemPanel;

    private void Awake() {
        if (redeemButton && redeemPanel) redeemButton.onClick.AddListener(redeemPanel.Open);
        rowTemplate.gameObject.SetActive(false);
        for (int i = 0; i < categoryButtons.Length; i++) { var value = (WarehouseCategory)i; categoryButtons[i].onClick.AddListener(() => ChangeCategory(value)); }
        actionButton.onClick.AddListener(Act);
        closeButton.onClick.AddListener(Close);
        quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        quantityInput.onValueChanged.AddListener(_ => UpdateControls());
        decreaseButton.onClick.AddListener(() => AdjustQuantity(-1));
        increaseButton.onClick.AddListener(() => AdjustQuantity(1));
    }

    private void OnEnable() {
        user = UserDataManager.Instance; network = NetworkManager.Instance;
        developmentVisible = Salasasa.Home.CompleteLobbyEnvironment.IsDevelopment;
        if (user != null) { user.OnInventoryChanged += StateChanged; user.OnTitlesChanged += StateChanged; }
        if (network != null) { network.InventoryResponseReceived += ResponseReceived; network.TitleResponseReceived += ResponseReceived; }
    }

    private void LateUpdate() {
        bool development = Salasasa.Home.CompleteLobbyEnvironment.IsDevelopment;
        if (developmentVisible == development) return;
        developmentVisible = development;
        if (user != null) Render();
    }

    private void OnDisable() {
        if (user != null) { user.OnInventoryChanged -= StateChanged; user.OnTitlesChanged -= StateChanged; }
        if (network != null) { network.InventoryResponseReceived -= ResponseReceived; network.TitleResponseReceived -= ResponseReceived; }
        StopTimeout(); busy = false;
        if (IsRead) pending = null;
        // Keep uncertain mutations across close/reopen, including their original request ID.
    }

    public void Open() {
        if (ModalFade) ModalFade.Show(); else gameObject.SetActive(true);
        transform.SetAsLastSibling();
        if (user == null || user.UserId == 0) { Close(); return; }
        if (openedUserId != user.UserId) {
            pending = null; category = WarehouseCategory.Backpack; selectedKey = null;
            openedUserId = user.UserId;
        }
        if (pending == null) {
            category = WarehouseCategory.Backpack; selectedKey = null;
            content.anchoredPosition = Vector2.zero;
        }
        quantityInput.SetTextWithoutNotify(pending != null && pending.type == "inventory/use" ? pending.quantity.ToString() : "1");
        Render();
        if (pending == null) Refresh();
        else {
            statusText.text = "上次操作尚未确认，请重试";
            UpdateControls();
        }
    }

    public void Close() {
        if (ModalFade) ModalFade.Hide(); else gameObject.SetActive(false);
    }

    private void ShowRenameConfirmation() {
        NotificationManager.Instance?.ShowConfirmation("前往改名", "将在浏览器中打开个人账户面板，改名成功时自动消耗 1 张改名卡。是否前往？",
            () => Application.OpenURL(ConfigManager.webUrl.TrimEnd('/') + "/account"));
    }

    private void StateChanged() {
        if (user == null || user.UserId == 0 || user.UserId != openedUserId) { pending = null; Close(); return; }
        Render();
    }

    private void ChangeCategory(WarehouseCategory value) {
        if (busy || pending != null) return;
        category = value; quantityInput.SetTextWithoutNotify("1");
        statusText.text = ""; Render();
        content.anchoredPosition = Vector2.zero;
    }

    private void Select(string key) {
        if (busy || pending != null) return;
        selectedKey = key; quantityInput.SetTextWithoutNotify("1"); statusText.text = ""; RenderDetails();
    }

    private void AdjustQuantity(int delta) {
        var item = Selected;
        if (busy || pending != null || item == null || item.IsEquipment || !item.CanAct) return;
        int.TryParse(quantityInput.text, out int count);
        quantityInput.text = Mathf.Clamp(count + delta, 1, Math.Min(100, item.Quantity)).ToString();
    }

    private void Begin(InventoryCommand command) {
        pending = command; pending.request_id = Guid.NewGuid().ToString("N");
        pendingUserId = user.UserId; Send();
    }

    private void Refresh() {
        if (!busy && pending == null) Begin(new InventoryCommand { type = "inventory/get" });
    }

    private void Act() {
        if (busy) return;
        if (pending != null) { Send(); return; }
        var item = Selected;
        if (item == null || !item.CanAct) return;
        if (item.IsRenameCard) { ShowRenameConfirmation(); return; }
        int count = 1;
        if (!item.IsEquipment && (!int.TryParse(quantityInput.text, out count) || count < 1 || count > Math.Min(100, item.Quantity))) {
            statusText.text = "使用数量须为 1–" + Math.Min(100, item.Quantity); return;
        }
        var command = item.CreateCommand(count);
        if (command != null) Begin(command);
    }

    private void Send() {
        if (user == null || user.UserId == 0 || network == null || pending == null || pendingUserId != user.UserId) return;
        busy = true; statusText.text = IsRead ? "" : "正在处理…";
        StopTimeout(); timeout = StartCoroutine(WaitForResponse(pending.request_id)); UpdateControls();
        if (pending.type.StartsWith("title/", StringComparison.Ordinal)) network.RequestTitles(pending.request_id, IsRead ? (int?)null : pending.item_id);
        else network.RequestInventory(pending);
    }

    private IEnumerator WaitForResponse(string requestId) {
        yield return new WaitForSecondsRealtime(15);
        if (pending?.request_id != requestId) yield break;
        busy = false; timeout = null;
        if (IsRead) pending = null;
        statusText.text = pending == null ? "仓库同步超时，请重新打开背包" : "操作结果未确认，请重试原操作";
        UpdateControls();
    }

    private void StopTimeout() { if (timeout != null) StopCoroutine(timeout); timeout = null; }

    private void ResponseReceived(Response response) {
        if (pending == null || pending.request_id != response.request_id || user == null || pendingUserId != user.UserId) return;
        StopTimeout(); busy = false;
        bool read = IsRead;
        bool loadTitles = pending.type == "inventory/get" && response.success;
        string successMessage = response.type.EndsWith("/equip", StringComparison.Ordinal) ? "已更新" : response.message ?? "使用成功";
        if (response.success || read || !response.inventory_retryable) pending = null;
        statusText.text = response.success ? read ? "" : successMessage
            : response.message ?? "操作失败，请重试";
        Render();
        if (loadTitles) Begin(new InventoryCommand { type = "title/get" });
    }

    private void Render() {
        entries = WarehouseEntry.Build(user?.Titles, user?.Inventory, user != null ? user.TitleId : 1);
        var visible = entries.FindAll(e => e.Category == category);
        if (!visible.Exists(e => e.Key == selectedKey)) selectedKey = visible.Count > 0 ? visible[0].Key : null;
        emptyText.gameObject.SetActive(visible.Count == 0);
        emptyText.text = "没有物品";
        for (int i = 0; i < visible.Count; i++) {
            if (i == rows.Count) rows.Add(Instantiate(rowTemplate, content));
            var row = rows[i]; row.name = "Warehouse_" + visible[i].Key.Replace(':', '_');
            row.Bind(visible[i], Select); row.gameObject.SetActive(true);
        }
        for (int i = visible.Count; i < rows.Count; i++) rows[i].gameObject.SetActive(false);
        RenderDetails();
    }

    private void RenderDetails() {
        var item = Selected;
        preview.sprite = ConfigManager.GetInventoryIcon(item?.Definition); preview.enabled = preview.sprite != null;
        AvatarFrameGraphic.Apply(preview, item?.Slot == "avatar_frame" ? item.Id : -1);
        previewArtwork.gameObject.SetActive(item != null && preview.sprite == null);
        if (item != null) previewArtwork.SetKind(item.IconKind);
        previewGlyph.text = item?.Glyph ?? ""; previewGlyph.gameObject.SetActive(item != null && preview.sprite == null);
        detailName.text = item?.Name ?? "";
        detailType.text = item?.KindLabel ?? "";
        detailCount.text = item == null || item.IsEquipment ? ""
            : "持有 " + item.Quantity + (item.Definition?.effect == "rename_credit" ? " 张" : " 件");
        detailCount.gameObject.SetActive(!string.IsNullOrEmpty(detailCount.text));
        detailRule.gameObject.SetActive(item != null);
        details.text = item != null && !item.IsDefault ? item.Description : "";
        var detailPosition = details.rectTransform.anchoredPosition;
        detailPosition.y = 0; details.rectTransform.anchoredPosition = detailPosition;
        bool showQuantity = item != null && !item.IsEquipment && !item.IsRenameCard && !item.IsCollectible && !item.GuestRestricted;
        quantityInput.gameObject.SetActive(showQuantity);
        var viewport = (RectTransform)details.transform.parent;
        var lowerControl = (RectTransform)(showQuantity ? quantityInput.transform : actionButton.transform);
        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0, viewport.anchoredPosition.y - lowerControl.anchoredPosition.y - 8));
        UpdateControls();
    }

    private void UpdateControls() {
        var item = Selected; bool idle = !busy && pending == null;
        foreach (var row in rows) if (row.gameObject.activeSelf) row.Select(row.Key == selectedKey, idle);
        for (int i = 0; i < categoryButtons.Length; i++) {
            categoryButtons[i].interactable = idle;
            bool selected = (int)category == i;
            categoryButtons[i].targetGraphic.color = selected ? categorySelectedSurface : categorySurface;
            categoryButtons[i].GetComponentInChildren<TMP_Text>().color = selected ? categorySelectedInk : categoryInk;
        }
        actionButton.gameObject.SetActive(item != null || pending != null && !IsRead);
        actionLabel.text = pending != null && !IsRead ? "重试本次操作" : item?.ActionLabel ?? "选择物品";
        actionButton.interactable = !busy && (pending != null || item != null && item.CanAct);
        quantityInput.interactable = idle && item != null && item.CanAct && !item.IsRenameCard;
        int.TryParse(quantityInput.text, out int count);
        decreaseButton.interactable = quantityInput.interactable && count > 1;
        increaseButton.interactable = quantityInput.interactable && count < Math.Min(100, item?.Quantity ?? 0);
    }
}
