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
    [SerializeField] private Button addBotButton; // 添加摸切机器人按钮
    [SerializeField] private Button addSmartBotButton; // 添加牌效机器人按钮
    [SerializeField] private RoomConfigContainer roomConfigContainer; // 房间设置容器
    [SerializeField] private GameObject noRecordText;
    [SerializeField] private GameObject noSpectatorsText;

    int player1_id = 0;
    int player2_id = 0;
    int player3_id = 0;
    int player4_id = 0;

    // Start is called before the first frame update
    void Start() {
        backButton.onClick.AddListener(BackButtonClicked);
        startButton.onClick.AddListener(StartButtonClicked);
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
        // 设置房间号
        roomIdText.text = $"房间号: {roomInfo.room_id}";
        roomnameText.text = $"房间名: {roomInfo.room_name}";

        // 清空玩家面板
        playerPanel1.Clear();
        playerPanel2.Clear();
        playerPanel3.Clear();
        playerPanel4.Clear();

        // 判断当前玩家是否为房主（房主为 player_list[0]）
        bool isHost = roomInfo.player_list.Length > 0 && roomInfo.player_list[0] == UserDataManager.Instance.UserId;

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

            switch (i) {
                case 0:
                    player1_id = userId;
                    playerPanel1.SetPlayer(username, userId, canRemove: false); // 房主面板不显示移除按钮
                    break;
                case 1:
                    player2_id = userId;
                    playerPanel2.SetPlayer(username, userId, canRemove);
                    break;
                case 2:
                    player3_id = userId;
                    playerPanel3.SetPlayer(username, userId, canRemove);
                    break;
                case 3:
                    player4_id = userId;
                    playerPanel4.SetPlayer(username, userId, canRemove);
                    break;
            }
        }

        // 只有房主且房间人数为4时才能开始游戏
        startButton.interactable = isHost && roomInfo.player_list.Length == 4;

        // 只有房主可以添加机器人
        addBotButton.interactable = isHost;
        addSmartBotButton.interactable = isHost;


        this.roomConfigContainer.SetRoomConfig(roomInfo);
        UpdateBotHintTexts(roomInfo.player_list);
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

    public void ClearRoomState() {
        roomIdText.text = "";
        roomnameText.text = "";
        playerPanel1.Clear();
        playerPanel2.Clear();
        playerPanel3.Clear();
        playerPanel4.Clear();
        startButton.interactable = false;
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
