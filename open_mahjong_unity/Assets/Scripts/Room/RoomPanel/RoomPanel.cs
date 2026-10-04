using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class RoomPanel : MonoBehaviour {
    public static RoomPanel Instance { get; private set; }

    private const string GuobiaoStandardSubRule = "guobiao/standard";

    [SerializeField] private TMP_Text roomIdText; // 房间号
    [SerializeField] private TMP_Text roomnameText; // 房间名
    [SerializeField] private TMP_Text hostNameText; // 沿用原房主标记，跟随房主所在座位
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
    [SerializeField] private Button addGuobiaoHeuristicBotButton; // 添加高性能机器人按钮（请在 Inspector 中拖拽绑定）
    [SerializeField] private RoomConfigContainer roomConfigContainer; // 房间设置容器
    [SerializeField] private TMP_Dropdown botSpeedDropdown;
    [SerializeField] private GameObject noRecordText;
    [SerializeField] private GameObject noSpectatorsText;

    bool selfReady = false; // 当前玩家（非房主）的准备状态
    private RoomInfo lastRoomInfo;

    // Start is called before the first frame update
    void Start() {
        backButton.onClick.AddListener(BackButtonClicked);
        startButton.onClick.AddListener(StartButtonClicked);
        if (readyButton != null) {
            readyButton.onClick.AddListener(ReadyButtonClicked);
        }
        if (botSpeedDropdown != null) botSpeedDropdown.onValueChanged.AddListener(BotSpeedChanged);
    }

    private void Awake() {
        // Awake 先于房间信息回调；避免 Start 覆盖首次回调已经设置好的显示状态。
        if (addGuobiaoHeuristicBotButton != null) {
            addGuobiaoHeuristicBotButton.gameObject.SetActive(false);
        }

        // 单例模式 - 在Awake中初始化，确保在Start之前完成
        if (Instance == null) {
            Instance = this;
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomPanel实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    public static bool SupportsClaimProtection(string rule) {
        return rule == "guobiao" || rule == "qingque" || rule == "sichuan" || rule == "changsha"
            || rule == "zhongyong" || rule == "nanque" || rule == "jiandan" || rule == "riichi";
    }

    private static bool SupportsHighPerformanceBot(RoomInfo roomInfo) {
        if (roomInfo == null) return false;
        if (roomInfo.room_rule == "hongque") return true;
        if (roomInfo.room_rule != "guobiao") return false;
        string sub = string.IsNullOrEmpty(roomInfo.sub_rule) ? GuobiaoStandardSubRule : roomInfo.sub_rule;
        return sub == GuobiaoStandardSubRule || sub == GuobiaoGameState.BloodBattleSubRule;
    }

    public void GetRoomInfoResponse(bool success, string message, RoomInfo roomInfo) {
        lastRoomInfo = roomInfo;
        // 设置房间号
        roomIdText.text = $"房间号: {roomInfo.room_id}";
        roomnameText.text = StreamerModeHelper.FormatRoomLabel("房间名: ", roomInfo.room_name);

        var panels = new[] { playerPanel1, playerPanel2, playerPanel3, playerPanel4 };
        if (hostNameText) hostNameText.gameObject.SetActive(false);
        for (int i = 0; i < panels.Length; i++) {
            panels[i].SetSeatIndex(i);
            panels[i].Clear();
        }

        // 判断当前玩家是否为房主（与服务器 refresh_room_info 同步的 host_user_id 一致）
        bool isHost = roomInfo.host_user_id == UserDataManager.Instance.UserId;

        // 当前玩家自身的准备状态（用于准备按钮切换）
        selfReady = IsSeatReady(UserDataManager.Instance.UserId, roomInfo);

        // 座位列表保留空位；兼容尚未下发 seat_list 的服务器。
        var seats = roomInfo.seat_list ?? roomInfo.player_list ?? System.Array.Empty<int>();
        for (int i = 0; i < seats.Length && i < panels.Length; i++) {
            // 按照玩家列表的user_id获取用户设置，同类型机器人共用同一份配置
            int userId = seats[i];
            if (userId < 0) continue;
            string key = userId.ToString();

            string username;
            if (roomInfo.player_settings != null && roomInfo.player_settings.TryGetValue(key, out UserSettings userSettings)) {
                username = userSettings.username;
            } else if (userId == 0) {
                username = "麻雀罗伯特";
            } else if (userId == 2) {
                username = "牌效罗伯特";
            } else if (userId == 3) {
                username = "高性能罗伯特";
            } else {
                username = $"用户{userId}";
            }

            // 房主可以移除除自己外的玩家（包括机器人）
            bool canRemove = isHost && userId != UserDataManager.Instance.UserId;

            bool seatHost = userId == roomInfo.host_user_id;
            bool seatReady = !seatHost && IsSeatReady(userId, roomInfo);

            string displayName = StreamerModeHelper.FormatRoomPlayerName(username, userId);

            panels[i].SetPlayer(displayName, userId, canRemove);
            panels[i].SetReady(seatReady);
            if (seatHost && hostNameText) {
                hostNameText.transform.SetParent(panels[i].transform, false);
                hostNameText.gameObject.SetActive(true);
            }
        }

        // 开始按钮：仅房主可见；人数达到规则最少人数且其余玩家全部准备时可点击。
        startButton.gameObject.SetActive(isHost);
        int seated = roomInfo.player_list != null ? roomInfo.player_list.Length : 0;
        int minPlayers = RuleRegistry.Resolve(roomInfo.room_rule)?.MinPlayersToStart ?? 4;
        if (minPlayers < 1) minPlayers = 1;
        startButton.interactable = isHost && seated >= minPlayers && AllOthersReady(roomInfo);

        // 准备按钮：仅非房主显示
        if (readyButton != null) {
            readyButton.gameObject.SetActive(!isHost);
            readyButton.interactable = !isHost;
        }
        if (readyButtonText != null) {
            readyButtonText.text = selfReady ? "取消准备" : "准备";
        }

        // 空座位显示添加入口，整桌已有机器人时才显示速度。
        bool allowBots = RuleRegistry.Resolve(roomInfo.room_rule)?.AllowsRoomBots ?? true;
        if (botSpeedDropdown != null) {
            botSpeedDropdown.transform.parent.gameObject.SetActive(allowBots && CountBots(roomInfo.player_list) > 0);
            botSpeedDropdown.SetValueWithoutNotify(BotSpeedIndex(roomInfo.bot_speed));
            botSpeedDropdown.interactable = isHost && !roomInfo.is_game_running;
        }
        bool canAddBot = allowBots && isHost && !roomInfo.is_game_running && seated < roomInfo.max_player;
        bool heuristic = SupportsHighPerformanceBot(roomInfo);
        playerPanel1.SetEmptySeatBotControls(canAddBot, heuristic);
        playerPanel2.SetEmptySeatBotControls(canAddBot, heuristic);
        playerPanel3.SetEmptySeatBotControls(canAddBot, heuristic);
        playerPanel4.SetEmptySeatBotControls(canAddBot, heuristic);
        if (allowBots) {
            UpdateBotHintTexts(roomInfo.player_list);
        } else {
            HideBotHintTexts();
        }

        this.roomConfigContainer.SetRoomConfig(roomInfo);
    }

    /// <summary>
    /// 判断指定席位是否已准备：机器人（user_id&lt;=10）默认已准备，真人需在 ready_list 中。
    /// </summary>
    private static bool IsSeatReady(int userId, RoomInfo roomInfo) {
        if (userId < 0) return false;
        if (userId <= 10) return true; // 机器人默认已准备
        if (roomInfo.ready_list == null) return false;
        for (int i = 0; i < roomInfo.ready_list.Length; i++) {
            if (roomInfo.ready_list[i] == userId) return true;
        }
        return false;
    }

    /// <summary>
    /// 除房主外的所有玩家是否都已准备。
    /// </summary>
    private static bool AllOthersReady(RoomInfo roomInfo) {
        if (roomInfo.player_list == null) return false;
        for (int i = 0; i < roomInfo.player_list.Length; i++) {
            if (roomInfo.player_list[i] == roomInfo.host_user_id) continue;
            if (!IsSeatReady(roomInfo.player_list[i], roomInfo)) return false;
        }
        return true;
    }

    private static int CountBots(int[] playerList) {
        if (playerList == null) return 0;
        int count = 0;
        for (int i = 0; i < playerList.Length; i++) {
            if (playerList[i] >= 0 && playerList[i] < 10) count++;
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
        if (hostNameText) hostNameText.gameObject.SetActive(false);
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
        if (botSpeedDropdown != null) {
            botSpeedDropdown.interactable = false;
            botSpeedDropdown.transform.parent.gameObject.SetActive(false);
        }
        roomConfigContainer.ClearRoomConfig();
        HideBotHintTexts();
    }

    private void BackButtonClicked() {
        RoomNetworkManager.Instance.LeaveRoom(UserDataManager.Instance.RoomId);
    }

    private static int BotSpeedIndex(string speed) => speed == "instant" ? 0 : speed == "medium" ? 2 : speed == "slow" ? 3 : 1;

    private void BotSpeedChanged(int index) {
        if (lastRoomInfo == null) return;
        // The shared room broadcast owns the value; a rejected request cannot leave a local draft.
        botSpeedDropdown.SetValueWithoutNotify(BotSpeedIndex(lastRoomInfo.bot_speed));
        if (lastRoomInfo.host_user_id != UserDataManager.Instance.UserId || lastRoomInfo.is_game_running) return;
        string speed = index == 0 ? "instant" : index == 2 ? "medium" : index == 3 ? "slow" : "fast";
        if (speed != lastRoomInfo.bot_speed) RoomNetworkManager.Instance.SetBotSpeed(lastRoomInfo.room_id, speed);
    }

    private void OnDestroy() {
        if (botSpeedDropdown != null) botSpeedDropdown.onValueChanged.RemoveListener(BotSpeedChanged);
        if (Instance == this) Instance = null;
    }

    private void StartButtonClicked() {
        RoomNetworkManager.Instance.StartGame(UserDataManager.Instance.RoomId);
    }

    private void ReadyButtonClicked() {
        // 切换准备状态：发送当前状态的取反
        RoomNetworkManager.Instance.SetReady(UserDataManager.Instance.RoomId, !selfReady);
    }

    public void AddBotFromSeat(int kind, int seatIndex) {
        if (lastRoomInfo == null || lastRoomInfo.is_game_running
            || lastRoomInfo.host_user_id != UserDataManager.Instance.UserId
            || lastRoomInfo.player_list.Length >= lastRoomInfo.max_player
            || !(RuleRegistry.Resolve(lastRoomInfo.room_rule)?.AllowsRoomBots ?? true)) return;
        var seats = lastRoomInfo.seat_list ?? lastRoomInfo.player_list;
        if (seatIndex < 0 || seatIndex >= lastRoomInfo.max_player
            || (seatIndex < seats.Length && seats[seatIndex] >= 0)) return;
        if (kind == 0) RoomNetworkManager.Instance.AddBotToRoom(lastRoomInfo.room_id, seatIndex);
        else if (kind == 2) RoomNetworkManager.Instance.AddSmartBotToRoom(lastRoomInfo.room_id, seatIndex);
        else if (kind == 3 && SupportsHighPerformanceBot(lastRoomInfo))
            RoomNetworkManager.Instance.AddGuobiaoHeuristicBotToRoom(lastRoomInfo.room_id, seatIndex);
    }
}
