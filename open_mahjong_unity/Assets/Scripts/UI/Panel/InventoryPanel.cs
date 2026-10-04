using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Unified warehouse. Catalog ownership and mutations remain server-authoritative.</summary>
public sealed partial class InventoryPanel : MonoBehaviour {
    private static readonly string[] CategoryNames = { "背包", "头衔", "角色", "装扮" };
    [SerializeField] private UnityEngine.UI.Button[] categoryButtons;
    [SerializeField] private RectTransform content;
    [SerializeField] private InventoryRow rowTemplate;
    [SerializeField] private UnityEngine.UI.Image preview;
    [SerializeField] private WarehouseIconGraphic previewArtwork;
    [SerializeField] private TMP_Text previewGlyph, details, summaryText, statusText, actionLabel;
    [SerializeField] private TMP_Text detailName, detailType, detailCount, listCountText, emptyText;
    [SerializeField] private TMP_InputField quantityInput;
    [SerializeField] private UnityEngine.UI.Button actionButton, refreshButton, closeButton, renameButton;
    [SerializeField] private UnityEngine.UI.Button decreaseButton, increaseButton;

    private readonly List<InventoryRow> rows = new List<InventoryRow>();
    private List<WarehouseEntry> entries = new List<WarehouseEntry>();
    private UserDataManager user;
    private NetworkManager network;
    private WarehouseCategory category;
    private int pendingUserId, openedUserId;
    private string selectedKey;
    private InventoryCommand pending;
    private bool busy;
    private Coroutine timeout;
    private bool IsRead => pending != null && pending.type.EndsWith("/get", StringComparison.Ordinal);
    private WarehouseEntry Selected => entries.Find(e => e.Key == selectedKey);

    private void Awake() {
        rowTemplate.gameObject.SetActive(false);
        for (int i = 0; i < categoryButtons.Length; i++) { var value = (WarehouseCategory)i; categoryButtons[i].onClick.AddListener(() => ChangeCategory(value)); }
        actionButton.onClick.AddListener(Act);
        refreshButton.onClick.AddListener(Refresh);
        closeButton.onClick.AddListener(Close);
        renameButton.onClick.AddListener(ShowRenameConfirmation);
        quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        quantityInput.onValueChanged.AddListener(_ => UpdateControls());
        decreaseButton.onClick.AddListener(() => AdjustQuantity(-1));
        increaseButton.onClick.AddListener(() => AdjustQuantity(1));
    }

    private void OnEnable() {
        user = UserDataManager.Instance; network = NetworkManager.Instance;
        if (user != null) { user.OnInventoryChanged += StateChanged; user.OnTitlesChanged += StateChanged; }
        if (network != null) { network.InventoryResponseReceived += ResponseReceived; network.TitleResponseReceived += ResponseReceived; }
    }

    private void OnDisable() {
        if (user != null) { user.OnInventoryChanged -= StateChanged; user.OnTitlesChanged -= StateChanged; }
        if (network != null) { network.InventoryResponseReceived -= ResponseReceived; network.TitleResponseReceived -= ResponseReceived; }
        StopTimeout(); busy = false;
        if (IsRead) pending = null;
        // Keep uncertain mutations across close/reopen, including their original request ID.
    }

    public void Open() {
        gameObject.SetActive(true); transform.SetAsLastSibling();
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

    public void Close() => gameObject.SetActive(false);

    private void ShowRenameConfirmation() {
        NotificationManager.Instance?.ShowConfirmation("前往改名", "需要在浏览器中打开个人账户面板，是否确定？",
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
        int count = 1;
        if (!item.IsEquipment && (!int.TryParse(quantityInput.text, out count) || count < 1 || count > Math.Min(100, item.Quantity))) {
            statusText.text = "使用数量须为 1–" + Math.Min(100, item.Quantity); return;
        }
        var command = item.CreateCommand(count);
        if (command != null) Begin(command);
    }

    private void Send() {
        if (user == null || user.UserId == 0 || network == null || pending == null || pendingUserId != user.UserId) return;
        busy = true; statusText.text = IsRead ? "正在同步…" : "正在处理…";
        StopTimeout(); timeout = StartCoroutine(WaitForResponse(pending.request_id)); UpdateControls();
        if (pending.type.StartsWith("title/", StringComparison.Ordinal)) network.RequestTitles(pending.request_id, IsRead ? (int?)null : pending.item_id);
        else network.RequestInventory(pending);
    }

    private IEnumerator WaitForResponse(string requestId) {
        yield return new WaitForSecondsRealtime(15);
        if (pending?.request_id != requestId) yield break;
        busy = false; timeout = null;
        if (IsRead) pending = null;
        statusText.text = pending == null ? "仓库同步超时，请刷新" : "操作结果未确认，请重试原操作";
        UpdateControls();
    }

    private void StopTimeout() { if (timeout != null) StopCoroutine(timeout); timeout = null; }

    private void ResponseReceived(Response response) {
        if (pending == null || pending.request_id != response.request_id || user == null || pendingUserId != user.UserId) return;
        StopTimeout(); busy = false;
        bool read = IsRead;
        bool loadTitles = pending.type == "inventory/get" && response.success;
        bool usedRenameCard = pending.type == "inventory/use" && Array.Exists(user.Inventory?.catalog ?? Array.Empty<InventoryItem>(),
            item => item != null && item.item_id == pending.item_id && item.effect == "rename_credit");
        string successMessage = usedRenameCard
            ? "改名次数 +" + pending.quantity : response.type.EndsWith("/equip", StringComparison.Ordinal) ? "已更新" : response.message ?? "使用成功";
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
        summaryText.text = "可用改名次数  " + (user?.Inventory?.rename_count ?? 0);
        bool showRename = category == WarehouseCategory.Backpack && user?.Inventory != null && !user.Inventory.is_tourist;
        summaryText.gameObject.SetActive(showRename);
        renameButton.gameObject.SetActive(showRename);
        listCountText.text = CategoryNames[(int)category] + "  ·  " + visible.Count + " 件";
        emptyText.gameObject.SetActive(visible.Count == 0);
        emptyText.text = category == WarehouseCategory.Character || category == WarehouseCategory.Cosmetic
                ? "暂无" + CategoryNames[(int)category] : "背包里还没有道具";
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
        detailName.text = item?.Name ?? "暂无物品";
        detailType.text = item?.KindLabel ?? "";
        detailCount.text = item == null ? "" : item.IsEquipment ? item.IsEquipped ? "使用中" : item.IsDefault ? "默认" + item.KindLabel : "已拥有"
            : "持有 " + item.Quantity + (item.Definition?.effect == "rename_credit" ? " 张" : " 件");
        details.text = item?.Description ?? "";
        var detailPosition = details.rectTransform.anchoredPosition;
        detailPosition.y = 0; details.rectTransform.anchoredPosition = detailPosition;
        quantityInput.gameObject.SetActive(item != null && !item.IsEquipment && !item.GuestRestricted);
        UpdateControls();
    }

    private void UpdateControls() {
        var item = Selected; bool idle = !busy && pending == null;
        foreach (var row in rows) if (row.gameObject.activeSelf) row.Select(row.Key == selectedKey, idle);
        for (int i = 0; i < categoryButtons.Length; i++) {
            categoryButtons[i].interactable = idle;
            bool selected = (int)category == i;
            categoryButtons[i].targetGraphic.color = selected ? new Color(.24f,.30f,.47f) : new Color(.12f,.14f,.22f);
            categoryButtons[i].GetComponentInChildren<TMP_Text>().color = selected ? Color.white : new Color(.69f,.73f,.83f);
            categoryButtons[i].transform.Find("SelectedMark").gameObject.SetActive(selected);
        }
        actionButton.gameObject.SetActive(item != null || pending != null && !IsRead);
        actionLabel.text = pending != null && !IsRead ? "重试本次操作" : item?.ActionLabel ?? "选择物品";
        actionButton.interactable = !busy && (pending != null || item != null && item.CanAct);
        refreshButton.interactable = idle; quantityInput.interactable = idle && item != null && item.CanAct;
        int.TryParse(quantityInput.text, out int count);
        decreaseButton.interactable = quantityInput.interactable && count > 1;
        increaseButton.interactable = quantityInput.interactable && count < Math.Min(100, item?.Quantity ?? 0);
    }
}
