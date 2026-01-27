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
    [SerializeField] private Button addBotButton; // 离开房间按钮

    int player1_id = 0;
    int player2_id = 0;
    int player3_id = 0;
    int player4_id = 0;

    // Start is called before the first frame update
    void Start() {
        backButton.onClick.AddListener(BackButtonClicked);
        startButton.onClick.AddListener(StartButtonClicked);
        addBotButton.onClick.AddListener(AddBotButtonClicked);
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
            // 按照玩家列表的user_id获取用户设置
            int userId = roomInfo.player_list[i];
            string key = userId.ToString();

            UserSettings userSettings = roomInfo.player_settings[key];
            string username = userSettings.username;
            int titleId = userSettings.title_id;
            int profileImageId = userSettings.profile_image_id;
            int characterId = userSettings.character_id;
            int voiceId = userSettings.voice_id;

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

        if (roomInfo.room_type == "guobiao") { // 显示房间右侧的设置栏
            GB_RoomConfig gbRoomConfig = GB_RoomConfig.Instance;
            gbRoomConfig.SetGBRoomConfig(roomInfo);
        }
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

}
