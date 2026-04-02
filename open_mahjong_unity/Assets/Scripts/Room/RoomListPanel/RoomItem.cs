using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro; // Added TMPro namespace

// RoomItem 的作用是通过SetRoomInfo方法设置房间信息，并保存自己的房间号
// 监听JoinClick的事件，如果发生事件返回RoomPanel包含自己房间id的joinRoom调用

public class RoomItem : MonoBehaviour {

    [SerializeField] private TMP_Text roomName; // 房间名
    [SerializeField] private TMP_Text hostName; // 房主名
    [SerializeField] private TMP_Text playerCount; // 玩家人数
    [SerializeField] private TMP_Text roomID; // 房间号
    [SerializeField] private TMP_Text gameRound; // 游戏圈数
    [SerializeField] private Button joinButton; // 加入按钮
    [SerializeField] private TMP_Text hasPassword; // 是否有密码
    [SerializeField] private TMP_Text playRule; // 规则（用 sub_rule 经 RuleNameDictionary 显示）
    [SerializeField] private TMP_Text gameStatus; // 游戏状态（是否正在运行）
    [SerializeField] private TMP_Text fushiText; // 复式
    [SerializeField] private TMP_Text tipsText; // 提示
    [SerializeField] private TMP_Text cuoheText; // 错和
    [SerializeField] private TMP_Text allowSpectatorText; // 是否允许观战
    [SerializeField] private TMP_Text touristLimitText;   // 限制游客
    [SerializeField] private TMP_Text hepaiLimitText;     // 起和番

    private void Start() {
        // 监听房间元素的点击按钮事件
        joinButton.onClick.AddListener(JoinClick);
    }
    // 在创建RoomItem时，初始化房间号和是否有密码的布尔值 在点击按钮时，返回上一级至RoomListPanel
    private string roomId; // 房间号
    private bool needPassword; // 是否有密码

    /// <summary>
    /// 由 RoomListPanel 传入 RoomInfo，在 Item 内独立解包并显示。不存在的可选字段对应 UI 设为 SetActive(false)。
    /// </summary>
    public void SetRoomInfo(RoomInfo roomData) {
        if (roomData == null) return;

        // 独立解包：按字段逐个取值并设置，避免整包传递导致缺失字段时出错
        string rid = roomData.room_id ?? "";
        string rname = roomData.room_name ?? "";
        string host = roomData.host_name ?? "";
        int pCount = roomData.player_list != null ? roomData.player_list.Length : 0;
        int gameRound = roomData.game_round;
        bool hasPw = roomData.has_password;
        string roomType = roomData.room_type ?? "";
        string subRule = roomData.sub_rule ?? "";
        bool isRunning = roomData.is_game_running;
        int randomSeed = roomData.random_seed;
        bool tipsOn = roomData.tips;
        bool openCuohe = roomData.open_cuohe;

        this.roomId = rid;
        this.needPassword = hasPw;

        roomID.text = $"房间号:{rid}";
        roomName.text = $"房间名:{rname}";
        hostName.text = $"房主:{host}";
        playerCount.text = $"玩家数{pCount}/4";

        gameStatus.text = isRunning ? "游戏中" : "等待中";
        hasPassword.text = hasPw ? "密码:有" : "密码:无";
        joinButton.interactable = pCount < 4 && !isRunning;

        // 规则：用 sub_rule 经 RuleNameDictionary 显示
        playRule.text = RuleNameDictionary.GetWholeName(subRule);

        // 可选字段：允许观战
        if (allowSpectatorText != null) {
            try {
                allowSpectatorText.text = roomData.allow_spectator ? "观战:是" : "观战:否";
                allowSpectatorText.gameObject.SetActive(true);
            } catch {
                allowSpectatorText.gameObject.SetActive(false);
            }
        }

        // 可选字段：限制游客
        if (touristLimitText != null) {
            try {
                touristLimitText.text = roomData.tourist_limit ? "限制游客:是" : "限制游客:否";
                touristLimitText.gameObject.SetActive(true);
            } catch {
                touristLimitText.gameObject.SetActive(false);
            }
        }

        // 可选字段：起和番（国标有效，无此字段或非国标时隐藏）
        if (hepaiLimitText != null) {
            try {
                if (roomType == "guobiao") {
                    int hepai = roomData.hepai_limit;
                    hepaiLimitText.text = "起和番:" + (hepai > 0 ? hepai.ToString() : "8");
                    hepaiLimitText.gameObject.SetActive(true);
                } else {
                    hepaiLimitText.gameObject.SetActive(false);
                }
            } catch {
                hepaiLimitText.gameObject.SetActive(false);
            }
        }

        // 对局轮数：从 RoundTextDictionary 获取（东风战/东南战/东西战/全庄战）
        if (this.gameRound != null) this.gameRound.text = RoundTextDictionary.GetMaxRoundText(gameRound);
        if (fushiText != null) fushiText.text = randomSeed == 0 ? "复式:关" : "复式:开";
        if (tipsText != null) tipsText.text = tipsOn ? "提示:开" : "提示:关";
        if (cuoheText != null) cuoheText.text = openCuohe ? "错和:开" : "错和:关";
    }

    private void JoinClick() {
        RoomListPanel.Instance.JoinClicked(roomId, needPassword);
    }
}
