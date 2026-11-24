using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class RoomListPanel : MonoBehaviour {
    public static RoomListPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private Transform roomListContent; // 房间列表容器
    [SerializeField] private GameObject roomItemPrefab; // 房间预制体
    public GameObject RoomItemPrefab => roomItemPrefab;
    [SerializeField] private TMP_InputField RoomIdInput;        // 房间ID输入框
    [SerializeField] private Button createButton;      // 创建房间按钮
    [SerializeField] private Button refreshButton;     // 刷新按钮
    [SerializeField] private Button JoinRoomButton;        // 加入房间按钮
    private int _joinInputBoundUserId = int.MinValue;
    private ViewportCardList<string, RoomItem> cards;
    private float rowHeight, duplicateHeight;
    private RoomInfo[] latest = System.Array.Empty<RoomInfo>();
    private List<(RoomInfo room, int color)> rows = new();
    private readonly List<string> keys = new();
    private readonly HashSet<string> seen = new();
    private bool pageVisible, presenting;
    private void Start() {
        createButton.onClick.AddListener(OpenCreatePanel);
        refreshButton.onClick.AddListener(RefreshRoomList);
        JoinRoomButton.onClick.AddListener(JoinRoom);
    }

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            ConfigureRoomListLayout();
            var content = (RectTransform)roomListContent;
            var prefab = roomItemPrefab.GetComponent<RoomItem>();
            var layout = prefab.GetComponent<LayoutElement>();
            duplicateHeight = prefab.DuplicateHeight;
            rowHeight = layout != null && layout.preferredHeight > 0 ? layout.preferredHeight : ((RectTransform)prefab.transform).rect.height;
            foreach (Transform child in content) {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            cards = new ViewportCardList<string, RoomItem>(content.GetComponentInParent<ScrollRect>(true), content,
                _ => Instantiate(prefab, content), (item, index) => item.SetRoomInfo(rows[index].room, rows[index].color));
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomListPanel实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    private void OnEnable() {
        RefreshJoinInputForCurrentUser();
        UpdateVisibility();
    }

    /// <summary>登出/重登后清掉加入房间号输入，避免带到下一个账号。</summary>
    public void ResetSessionCaches() {
        _joinInputBoundUserId = int.MinValue;
        if (RoomIdInput != null) RoomIdInput.text = "";
        SetPageVisible(false);
        latest = System.Array.Empty<RoomInfo>();
        rows.Clear();
        keys.Clear();
        cards?.SetRows(keys, _ => 0, _ => rowHeight, resetScroll: true);
    }

    private void RefreshJoinInputForCurrentUser() {
        int userId = UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0;
        if (userId == _joinInputBoundUserId) return;
        _joinInputBoundUserId = userId;
        if (RoomIdInput != null) RoomIdInput.text = "";
    }

    public void SetPageVisible(bool visible) {
        pageVisible = visible;
        UpdateVisibility();
    }

    private void UpdateVisibility() {
        bool show = pageVisible && isActiveAndEnabled && cards != null;
        if (show == presenting) return;
        presenting = show;
        if (show) {
            ApplySnapshot(true);
            if (NetworkManager.Instance != null && NetworkManager.Instance.isActiveAndEnabled)
                NetworkPollingManager.Instance?.StartRoomListPolling();
        } else {
            NetworkPollingManager.Existing?.StopRoomListPolling();
            cards?.Hide();
        }
    }

    private void LateUpdate() { if (presenting) cards.Tick(); }
    private void OnDisable() {
        SetPageVisible(false);
        cards?.TrimPooledRows(8);
    }
    private void OnDestroy() {
        cards?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void OpenCreatePanel() {
        if (LobbyStateGuard.BlockIfInMatchQueueForRoom()) {
            return;
        }
        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            NotificationManager.Instance.ShowTip("create_room", false, "必须先退出当前房间才能创建房间");
            return;
        }
        var events = EventSystem.current;
        if (events != null && createButton != null) {
            var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left };
            createButton.OnPointerExit(pointer);
            createButton.OnPointerUp(pointer);
            if (events.currentSelectedGameObject == createButton.gameObject) events.SetSelectedGameObject(null);
        }
        RoomWindowsManager.Instance.SwitchRoomWindow("createRoom");
        WindowsManager.Instance.SwitchWindow("room");
    }

    private void JoinRoom() {
        if (LobbyStateGuard.BlockIfInMatchQueueForRoom()) {
            return;
        }
        if (LobbyStateGuard.IsInRoom) {
            NotificationManager.Instance.ShowTip("join_room", false, "请先退出当前房间");
            return;
        }
        if (string.IsNullOrEmpty(RoomIdInput.text)) {
            NotificationManager.Instance.ShowTip("tips",false,"房间ID不能为空");
            return;
        }
        RoomNetworkManager.Instance.JoinRoom(RoomIdInput.text, RoomIdInput.text);
    }

    public void RefreshRoomList() {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.isActiveAndEnabled) return;
        RoomNetworkManager.Instance.GetRoomList(showTipOnSuccess: true);
    }

    // 通过运行时布局适配新的卡片高度，不写入场景文件。
    private void ConfigureRoomListLayout() {
        if (roomListContent == null) return;
        var layout = roomListContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null) return;
        layout.spacing = 4;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(4, 4, 4, 4);
    }

    public void GetRoomListResponse(bool success, string message, RoomInfo[] room_List) {
        if (!success) {
            Debug.LogError($"获取房间列表失败: {message}");
            return;
        }
        latest = room_List ?? System.Array.Empty<RoomInfo>();
        if (presenting) ApplySnapshot(false);
    }

    private void ApplySnapshot(bool reopening) {
        var next = new List<(RoomInfo room, int color)>(latest.Length);
        seen.Clear();
        keys.Clear();
        bool reorder = reopening;
        // A paired match keeps adjacent open/closed rooms in the same group color.
        var groups = latest.Where(r => r != null && !string.IsNullOrEmpty(r.room_id))
            .GroupBy(r => string.IsNullOrEmpty(r.duplicate_match_id) ? "room:" + r.room_id : "pair:" + r.duplicate_match_id);
        int color = 0;
        foreach (var group in groups) {
            foreach (var room in group.OrderBy(r => r.duplicate_table == "closed" ? 1 : 0)) {
                if (!seen.Add(room.room_id)) continue;
                int index = next.Count;
                if (index >= rows.Count || rows[index].room.room_id != room.room_id ||
                    RoomItem.IsEightPlayerLobby(rows[index].room) != RoomItem.IsEightPlayerLobby(room)) reorder = true;
                next.Add((room, color));
                keys.Add(room.room_id);
                if (cards.TryGetVisible(room.room_id, out var item) && item.UpdateSnapshot(room, color))
                    cards.Invalidate(room.room_id);
            }
            color++;
        }
        reorder |= rows.Count != next.Count;
        rows = next;
        if (reorder) cards.SetRows(keys, _ => 0, i => RoomItem.IsEightPlayerLobby(rows[i].room) ? duplicateHeight : rowHeight);
    }
}
