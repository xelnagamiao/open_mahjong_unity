using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomPanel : MonoBehaviour {
    public static RoomPanel Instance { get; private set; }
    
    [SerializeField] private TMP_Text roomIdText; // 房间号
    [SerializeField] private TMP_Text roomnameText; // 房间名
    [SerializeField] private TMP_Text player_1; // 玩家1
    [SerializeField] private TMP_Text player_2; // 玩家2
    [SerializeField] private TMP_Text player_3; // 玩家3
    [SerializeField] private TMP_Text player_4; // 玩家4
    [SerializeField] private Button backButton; // 返回按钮
    [SerializeField] private Button startButton; // 开始按钮

    int player1_id = 0;
    int player2_id = 0;
    int player3_id = 0;
    int player4_id = 0;

    // Start is called before the first frame update
    void Start() {
        backButton.onClick.AddListener(BackButtonClicked);
        startButton.onClick.AddListener(StartButtonClicked);
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

        // 清空文本
        player_1.text = "";
        player_2.text = "";
        player_3.text = "";
        player_4.text = "";

        // 根据实际玩家数量设置文本
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

            switch (i) {
                case 0: player1_id = userId; player_1.text = username; break;
                case 1: player2_id = userId; player_2.text = username; break;
                case 2: player3_id = userId; player_3.text = username; break;
                case 3: player4_id = userId; player_4.text = username; break;
            }
        }

        // 如果不满4人 则禁用开始按钮
        startButton.interactable = roomInfo.player_list.Length == 4;

        // 如果玩家1不是自己 则禁用开始按钮
        if (player_1.text != UserDataManager.Instance.Username) {
            startButton.interactable = false;
        }

        if (roomInfo.room_type == "guobiao") { // 显示房间右侧的设置栏
            GB_RoomConfig gbRoomConfig = GB_RoomConfig.Instance;
            gbRoomConfig.SetGBRoomConfig(roomInfo);
        }
    }

    private void BackButtonClicked() {
        WindowsManager.Instance.SwitchWindow("menu");
        NetworkManager.Instance.LeaveRoom(UserDataManager.Instance.RoomId);
    }

    private void StartButtonClicked() {
        NetworkManager.Instance.StartGame(UserDataManager.Instance.RoomId);
    }

}
