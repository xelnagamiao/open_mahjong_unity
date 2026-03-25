using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomListPanel : MonoBehaviour {
    public static RoomListPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private Transform roomListContent; // 房间列表容器
    [SerializeField] private GameObject roomItemPrefab; // 房间预制体
    [SerializeField] private TMP_InputField RoomIdInput;        // 房间ID输入框
    [SerializeField] private Button createButton;      // 创建房间按钮
    [SerializeField] private Button refreshButton;     // 刷新按钮
    [SerializeField] private Button JoinRoomButton;        // 加入房间按钮


    [Header("Password Input")]
    [SerializeField] private GameObject passwordInputPanel; // 密码输入面板
    [SerializeField] private float passwordPanelFadeDuration = 0.2f; // 与 WindowFadeTransition 一致的渐显/渐隐时长
    [SerializeField] private TMP_InputField passwordInput; // 密码输入框
    [SerializeField] private Button passwordInputAdmit; // 密码输入按钮
    [SerializeField] private Button passwordInputCancel; // 密码输入取消按钮

    private string roomId;
    private Coroutine _passwordPanelFadeRoutine;

    private void Start() {
        // 初始化按钮监听 1.显示房间面板 2.刷新房间列表 3.加入房间
        createButton.onClick.AddListener(OpenCreatePanel);
        refreshButton.onClick.AddListener(RefreshRoomList);
        JoinRoomButton.onClick.AddListener(JoinRoom);
        // 订阅密码输入按钮
        passwordInputAdmit.onClick.AddListener(PasswordInputAdmit);
        passwordInputCancel.onClick.AddListener(PasswordInputCancel);
        if (passwordInputPanel != null) passwordInputPanel.SetActive(false);
    }

    private void Awake() {
        // 单例模式 - 在Awake中初始化，确保在Start之前完成
        if (Instance == null) {
            Instance = this;

        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomListPanel实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    // 1.点击创建房间按钮 打开房间面板
    private void OpenCreatePanel() {
        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            NotificationManager.Instance.ShowTip("create_room", false, "必须先退出当前房间才能创建房间");
            return;
        }
        RoomWindowsManager.Instance.SwitchRoomWindow("createRoom");
        WindowsManager.Instance.SwitchWindow("room");
    }

    // 2.点击加入房间按钮 加入房间
    private void JoinRoom() {
        if (string.IsNullOrEmpty(RoomIdInput.text)) {
            NotificationManager.Instance.ShowTip("tips",false,"房间ID不能为空");
            return;
        } else {
            if (RoomNetworkManager.Instance != null) {
                RoomNetworkManager.Instance.JoinRoom(RoomIdInput.text, RoomIdInput.text);
            }
        }
    }

    // 3.刷新房间列表 调用 RoomNetworkManager 的 GetRoomList 方法（手动刷新时显示 tips）
    public void RefreshRoomList() {
        ClearRoomListContent();
        RoomNetworkManager.Instance.GetRoomList(showTipOnSuccess: true);
    }

    // 清理房间列表容器内的所有子物体
    private void ClearRoomListContent() {
        if (roomListContent == null) return;
        for (int i = roomListContent.childCount - 1; i >= 0; i--) {
            Destroy(roomListContent.GetChild(i).gameObject);
        }
    }

    // 4.获取房间列表响应
    public void GetRoomListResponse(bool success, string message, RoomInfo[] room_List){
        if (!success) {
            Debug.LogError($"获取房间列表失败: {message}");
            return;
        }

        // 清理容器内现有房间列表
        ClearRoomListContent();

        // 直接使用 roomList 数组
        if (room_List != null) {
            foreach (var roomData in room_List) {
                // room_type 为 custom/match 等；具体玩法由 room_rule 区分（与服务端 boardcast 一致）
                if (roomData.room_rule == "guobiao" || roomData.room_rule == "qingque") {
                    GameObject roomItem = Instantiate(roomItemPrefab, roomListContent);
                    roomItem.SetActive(true);

                    var roomItemComponent = roomItem.GetComponent<RoomItem>();
                    // 传入 RoomInfo，由 RoomItem 自己解包并显示
                    roomItemComponent.SetRoomInfo(roomData);
                }
            }
        }
    }

    // 接收到房间预制体的加入房间点击事件和预制体的roomid,needPassword
    // 如果需要密码 则打开密码输入面板 并保存roomId 否则直接调用joinRoom
    public void JoinClicked(string roomId, bool needPassword) {
        if (needPassword) {
            this.roomId = roomId;
            if (_passwordPanelFadeRoutine != null) StopCoroutine(_passwordPanelFadeRoutine);
            _passwordPanelFadeRoutine = StartCoroutine(ShowPasswordPanelFaded());
        } else {
            RoomNetworkManager.Instance.JoinRoom(roomId, "");
        }
    }

    private IEnumerator ShowPasswordPanelFaded() {
        yield return WindowFadeTransition.FadeOverlayIn(passwordInputPanel, passwordPanelFadeDuration);
        _passwordPanelFadeRoutine = null;
    }

    private IEnumerator HidePasswordPanelFaded() {
        yield return WindowFadeTransition.FadeOverlayOut(passwordInputPanel, passwordPanelFadeDuration);
        _passwordPanelFadeRoutine = null;
    }



    // 密码输入面板点击确认调用joinRoom
    private void PasswordInputAdmit() {
        if (string.IsNullOrEmpty(passwordInput.text)) {
            Debug.LogError("密码不能为空");
            return;
        }
        if (_passwordPanelFadeRoutine != null) StopCoroutine(_passwordPanelFadeRoutine);
        string pwd = passwordInput.text;
        _passwordPanelFadeRoutine = StartCoroutine(HidePasswordPanelThenJoin(pwd));
    }
    // 密码输入面板点击取消关闭密码输入面板
    private void PasswordInputCancel() {
        if (_passwordPanelFadeRoutine != null) StopCoroutine(_passwordPanelFadeRoutine);
        _passwordPanelFadeRoutine = StartCoroutine(HidePasswordPanelFaded());
    }

    private IEnumerator HidePasswordPanelThenJoin(string password) {
        yield return WindowFadeTransition.FadeOverlayOut(passwordInputPanel, passwordPanelFadeDuration);
        RoomNetworkManager.Instance.JoinRoom(roomId, password);
        _passwordPanelFadeRoutine = null;
    }
}

