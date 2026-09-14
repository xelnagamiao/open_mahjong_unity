using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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
    private readonly Dictionary<string, RoomItem> roomItems = new Dictionary<string, RoomItem>();

    private void Start() {
        createButton.onClick.AddListener(OpenCreatePanel);
        refreshButton.onClick.AddListener(RefreshRoomList);
        JoinRoomButton.onClick.AddListener(JoinRoom);
    }

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            ConfigureRoomListLayout();
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomListPanel实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    private void OnEnable() {
        NetworkPollingManager.Instance.StartRoomListPolling();
        RefreshJoinInputForCurrentUser();
    }

    /// <summary>登出/重登后清掉加入房间号输入，避免带到下一个账号。</summary>
    public void ResetSessionCaches() {
        _joinInputBoundUserId = int.MinValue;
        if (RoomIdInput != null) RoomIdInput.text = "";
        ClearRoomListContent();
    }

    private void RefreshJoinInputForCurrentUser() {
        int userId = UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0;
        if (userId == _joinInputBoundUserId) return;
        _joinInputBoundUserId = userId;
        if (RoomIdInput != null) RoomIdInput.text = "";
    }

    private void OnDisable() {
        NetworkPollingManager.Instance.StopRoomListPolling();
    }

    private void OpenCreatePanel() {
        if (LobbyStateGuard.BlockIfInMatchQueueForRoom()) {
            return;
        }
        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            NotificationManager.Instance.ShowTip("create_room", false, "必须先退出当前房间才能创建房间");
            return;
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

    private void ClearRoomListContent() {
        if (roomListContent == null) return;
        for (int i = roomListContent.childCount - 1; i >= 0; i--) {
            roomListContent.GetChild(i).gameObject.SetActive(false);
            Destroy(roomListContent.GetChild(i).gameObject);
        }
        roomItems.Clear();
    }

    public void GetRoomListResponse(bool success, string message, RoomInfo[] room_List){
        if (!success) {
            Debug.LogError($"获取房间列表失败: {message}");
            return;
        }

        if (roomListContent == null || roomItemPrefab == null) return;
        // 首次响应移除编辑器示例。后续按 ID 更新，保留悬浮详情和滚动位置。
        if (roomItems.Count == 0) ClearRoomListContent();
        var retained = new HashSet<string>();
        if (room_List != null) {
            foreach (var roomData in room_List) {
                if (roomData == null || string.IsNullOrEmpty(roomData.room_id) || !retained.Add(roomData.room_id)) continue;
                if (!roomItems.TryGetValue(roomData.room_id, out RoomItem item) || item == null) {
                    var instance = Instantiate(roomItemPrefab, roomListContent);
                    instance.SetActive(true);
                    item = instance.GetComponent<RoomItem>();
                    roomItems[roomData.room_id] = item;
                }
                item.SetRoomInfo(roomData);
                item.transform.SetSiblingIndex(retained.Count - 1);
            }
        }
        var removed = new List<string>();
        foreach (var pair in roomItems) {
            if (retained.Contains(pair.Key)) continue;
            if (pair.Value != null) {
                pair.Value.gameObject.SetActive(false);
                Destroy(pair.Value.gameObject);
            }
            removed.Add(pair.Key);
        }
        foreach (string id in removed) roomItems.Remove(id);
    }
}
