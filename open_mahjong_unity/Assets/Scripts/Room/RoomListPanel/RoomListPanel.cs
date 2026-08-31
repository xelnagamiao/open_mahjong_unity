using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    private void Start() {
        createButton.onClick.AddListener(OpenCreatePanel);
        refreshButton.onClick.AddListener(RefreshRoomList);
        JoinRoomButton.onClick.AddListener(JoinRoom);
    }

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomListPanel实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    private void OnEnable() {
        NetworkPollingManager.Instance.StartRoomListPolling();
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
        ClearRoomListContent();
        RoomNetworkManager.Instance.GetRoomList(showTipOnSuccess: true);
    }

    private void ClearRoomListContent() {
        if (roomListContent == null) return;
        for (int i = roomListContent.childCount - 1; i >= 0; i--) {
            Destroy(roomListContent.GetChild(i).gameObject);
        }
    }

    public void GetRoomListResponse(bool success, string message, RoomInfo[] room_List){
        if (!success) {
            Debug.LogError($"获取房间列表失败: {message}");
            return;
        }

        ClearRoomListContent();

        if (room_List != null) {
            foreach (var roomData in room_List) {
                GameObject roomItem = Instantiate(roomItemPrefab, roomListContent);
                roomItem.SetActive(true);
                roomItem.GetComponent<RoomItem>().SetRoomInfo(roomData);
            }
        }
    }
}
