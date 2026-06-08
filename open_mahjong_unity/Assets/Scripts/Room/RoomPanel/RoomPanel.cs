using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomPanel : MonoBehaviour {
    public static RoomPanel Instance { get; private set; }
    
    [SerializeField] private TMP_Text roomIdText; // 房间号
    [SerializeField] private TMP_Text roomnameText; // 房间名
    [SerializeField] private PlayerRoomPanel playerPanel1; // 玩家1 面板
    [SerializeField] private PlayerRoomPanel playerPanel2; // 玩家2 面板
    [SerializeField] private PlayerRoomPanel playerPanel3; // 玩家3 面板
    [SerializeField] private PlayerRoomPanel playerPanel4; // 玩家4 面板
    [SerializeField] private Button backButton; // 返回按钮
    [SerializeField] private Button startButton; // 开始按钮
    [SerializeField] private Button readyButton; // 准备按钮（仅非房主显示）
    [SerializeField] private TMP_Text readyButtonText; // 准备按钮文本（可选，用于切换“准备/取消准备”）
    [SerializeField] private Button addBotButton; // 添加摸切机器人按钮
    [SerializeField] private Button addSmartBotButton; // 添加牌效机器人按钮
    [SerializeField] private RoomConfigContainer roomConfigContainer; // 房间设置容器
    [SerializeField] private GameObject noRecordText;
    [SerializeField] private GameObject noSpectatorsText;

    int player1_id = 0;
    int player2_id = 0;
    int player3_id = 0;
    int player4_id = 0;

    bool selfReady = false; // 当前玩家（非房主）的准备状态
    private RoomInfo lastRoomInfo;

    // Start is called before the first frame update
    void Start() {
        backButton.onClick.AddListener(BackButtonClicked);
        startButton.onClick.AddListener(StartButtonClicked);
        if (readyButton != null) {
            readyButton.onClick.AddListener(ReadyButtonClicked);
        }
        addBotButton.onClick.AddListener(AddBotButtonClicked);
        addSmartBotButton.onClick.AddListener(AddSmartBotButtonClicked);
    }

    private void Awake() {
        // 单例模式 - 在Awake中初始化，确保在Start之前完成
        if (Instance == null) {
            Instance = this;
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomPanel实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }



    public void GetRoomInfoResponse(bool success, string message, RoomInfo roomInfo) {
        lastRoomInfo = roomInfo;
        // 设置房间号
        roomIdText.text = $"房间号: {roomInfo.room_id}";
        roomnameText.text = StreamerModeHelper.FormatRoomLabel("房间名: ", roomInfo.room_name);

        // 清空玩家面板
        playerPanel1.Clear();
        playerPanel2.Clear();
        playerPanel3.Clear();
        playerPanel4.Clear();

        // 判断当前玩家是否为房主（与服务器 refresh_room_info 同步的 host_user_id 一致）
        bool isHost = roomInfo.host_user_id == UserDataManager.Instance.UserId;

        // 当前玩家自身的准备状态（用于准备按钮切换）
        selfReady = IsSeatReady(UserDataManager.Instance.UserId, roomInfo);

        // 根据实际玩家数量设置面板
        for (int i = 0; i < roomInfo.player_list.Length && i < 4; i++) {
            // 按照玩家列表的user_id获取用户设置，同类型机器人共用同一份配置
            int userId = roomInfo.player_list[i];
            string key = userId.ToString();

            string username;
            if (roomInfo.player_settings.TryGetValue(key, out UserSettings userSettings)) {
                username = userSettings.username;
            } else if (userId == 0) {
                username = "麻雀罗伯特";
            } else if (userId == 2) {
                username = "牌效罗伯特";
            } else {
                username = $"用户{userId}";
            }

            // 房主可以移除除自己外的玩家（包括机器人）
            bool canRemove = isHost && userId != UserDataManager.Instance.UserId;

            // 索引0为房主席位，无需准备图标；后3个席位显示准备状态
            bool seatReady = i != 0 && IsSeatReady(userId, roomInfo);

            string displayName = StreamerModeHelper.FormatRoomPlayerName(username, userId);

            switch (i) {
                case 0:
                    player1_id = userId;
                    playerPanel1.SetPlayer(displayName, userId, canRemove: false); // 房主面板不显示移除按钮
                    playerPanel1.SetReady(false);
                    break;
                case 1:
                    player2_id = userId;
                    playerPanel2.SetPlayer(displayName, userId, canRemove);
                    playerPanel2.SetReady(seatReady);
                    break;
                case 2:
                    player3_id = userId;
                    playerPanel3.SetPlayer(displayName, userId, canRemove);
                    playerPanel3.SetReady(seatReady);
                    break;
                case 3:
                    player4_id = userId;
                    playerPanel4.SetPlayer(displayName, userId, canRemove);
                    playerPanel4.SetReady(seatReady);
                    break;
            }
        }

        // 开始按钮：仅房主可见；满 4 人且其余玩家全部准备时可点击（与服务器 start_game 校验一致）
        startButton.gameObject.SetActive(isHost);
        startButton.interactable = isHost && roomInfo.player_list.Length == 4 && AllOthersReady(roomInfo);

        // 准备按钮：仅非房主显示
        if (readyButton != null) {
            readyButton.gameObject.SetActive(!isHost);
            readyButton.interactable = !isHost;
        }
        if (readyButtonText != null) {
            readyButtonText.text = selfReady ? "取消准备" : "准备";
        }

        // 只有房主可以添加机器人
        addBotButton.interactable = isHost;
        addSmartBotButton.interactable = isHost;


        this.roomConfigContainer.SetRoomConfig(roomInfo);
        UpdateBotHintTexts(roomInfo.player_list);
    }

    /// <summary>
    /// 判断指定席位是否已准备：机器人（user_id&lt;=10）默认已准备，真人需在 ready_list 中。
    /// </summary>
    private static bool IsSeatReady(int userId, RoomInfo roomInfo) {
        if (userId <= 10) return true; // 机器人默认已准备
        if (roomInfo.ready_list == null) return false;
        for (int i = 0; i < roomInfo.ready_list.Length; i++) {
            if (roomInfo.ready_list[i] == userId) return true;
        }
        return false;
    }

    /// <summary>
    /// 除房主（player_list[0]）外的所有玩家是否都已准备。
    /// </summary>
    private static bool AllOthersReady(RoomInfo roomInfo) {
        if (roomInfo.player_list == null) return false;
        for (int i = 1; i < roomInfo.player_list.Length; i++) {
            if (!IsSeatReady(roomInfo.player_list[i], roomInfo)) return false;
        }
        return true;
    }

    private static int CountBots(int[] playerList) {
        if (playerList == null) return 0;
        int count = 0;
        for (int i = 0; i < playerList.Length; i++) {
            if (playerList[i] <= 10) count++;
        }
        return count;
    }

    private void UpdateBotHintTexts(int[] playerList) {
        int botCount = CountBots(playerList);
        if (noRecordText != null) noRecordText.SetActive(botCount >= 1);
        if (noSpectatorsText != null) noSpectatorsText.SetActive(botCount == 3);
    }

    private void HideBotHintTexts() {
        if (noRecordText != null) noRecordText.SetActive(false);
        if (noSpectatorsText != null) noSpectatorsText.SetActive(false);
    }

    public void RefreshStreamerModeDisplay() {
        if (lastRoomInfo != null) {
            GetRoomInfoResponse(true, string.Empty, lastRoomInfo);
        }
    }

    public void ClearRoomState() {
        lastRoomInfo = null;
        roomIdText.text = "";
        roomnameText.text = "";
        playerPanel1.Clear();
        playerPanel2.Clear();
        playerPanel3.Clear();
        playerPanel4.Clear();
        startButton.gameObject.SetActive(false);
        startButton.interactable = false;
        if (readyButton != null) {
            readyButton.gameObject.SetActive(false);
            readyButton.interactable = false;
        }
        selfReady = false;
        addBotButton.interactable = false;
        addSmartBotButton.interactable = false;
        roomConfigContainer.ClearRoomConfig();
        HideBotHintTexts();
    }

    private void BackButtonClicked() {
        WindowsManager.Instance.SwitchWindow("menu");
        if (RoomNetworkManager.Instance != null) {
            RoomNetworkManager.Instance.LeaveRoom(UserDataManager.Instance.RoomId);
        }
    }

    private void StartButtonClicked() {
        if (RoomNetworkManager.Instance != null) {
            RoomNetworkManager.Instance.StartGame(UserDataManager.Instance.RoomId);
        }
    }

    private void ReadyButtonClicked() {
        if (RoomNetworkManager.Instance != null) {
            // 切换准备状态：发送当前状态的取反
            RoomNetworkManager.Instance.SetReady(UserDataManager.Instance.RoomId, !selfReady);
        }
    }

    private void AddBotButtonClicked() {
        if (RoomNetworkManager.Instance != null) {
            RoomNetworkManager.Instance.AddBotToRoom(UserDataManager.Instance.RoomId);
        }
    }

    private void AddSmartBotButtonClicked() {
        if (RoomNetworkManager.Instance != null) {
            RoomNetworkManager.Instance.AddSmartBotToRoom(UserDataManager.Instance.RoomId);
        }
    }
}
