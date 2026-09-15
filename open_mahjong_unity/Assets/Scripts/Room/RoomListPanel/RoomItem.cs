using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>房间卡片：名称、精简配置、四个席位；完整信息由问号展示。</summary>
public class RoomItem : MonoBehaviour {
    [SerializeField] private TMP_Text roomName;
    [SerializeField] private TMP_Text playRule;
    [SerializeField] private TMP_Text gameStatus;
    [SerializeField] private TMP_Text configSummary;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button detailsButton;
    [SerializeField] private TMP_Text[] playerNames = new TMP_Text[4];
    [SerializeField] private TMP_Text[] playerStates = new TMP_Text[4];
    [SerializeField] private Image upperPartImage;
    [SerializeField] private Image lowerPartImage;
    [SerializeField] private Color normalUpperColor = new Color(.32f, .32f, .32f);
    [SerializeField] private Color normalLowerColor = new Color(.18f, .18f, .18f);
    [SerializeField] private Color eventUpperColor = new Color(.36f, .36f, .36f);
    [SerializeField] private Color eventLowerColor = new Color(.22f, .22f, .22f);

    private RoomInfo data;
    private static readonly Color ReadyColor = new Color(.92f, .92f, .92f);
    private static readonly Color MutedColor = new Color(.78f, .78f, .78f);

    private void Awake() {
        if (joinButton != null) joinButton.onClick.AddListener(JoinClick);
    }
    private void OnEnable() { StreamerModeHelper.OnChanged += RefreshDisplay; }
    private void OnDisable() { StreamerModeHelper.OnChanged -= RefreshDisplay; }
    public void SetRoomInfo(RoomInfo roomData) {
        data = roomData;
        RefreshDisplay();
    }

    private void RefreshDisplay() {
        if (data == null) return;
        bool masked = ConfigManager.Instance != null && StreamerModeHelper.IsEnabled;
        string title = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.room_name, "未命名房间");
        string host = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.host_name, "暂无房主");
        int count = data.player_list?.Length ?? 0;
        int capacity = data.max_player > 0 ? Mathf.Min(data.max_player, 4) : 4;
        bool isEvent = data.room_type == "events" || !string.IsNullOrEmpty(data.event_id);
        string rule = RuleNameDictionary.GetWholeName(Fallback(data.sub_rule, data.room_rule ?? ""));
        Set(roomName, title);
        Set(playRule, rule);
        string status = data.is_game_running ? "对局中" : count >= capacity ? "满员" : "等待";
        Set(gameStatus, $"{status} · {count}/{capacity} · {(data.has_password ? "有密码" : "公开")}");
        if (gameStatus != null) gameStatus.color = data.is_game_running ? new Color(1f, .77f, .44f) : ReadyColor;
        Set(configSummary, BuildSummary(data));
        if (upperPartImage != null) upperPartImage.color = isEvent ? eventUpperColor : normalUpperColor;
        if (lowerPartImage != null) lowerPartImage.color = isEvent ? eventLowerColor : normalLowerColor;
        if (joinButton != null) {
            joinButton.interactable = count < capacity && !data.is_game_running;
            Set(joinButton.GetComponentInChildren<TMP_Text>(), data.is_game_running ? "对局中" : count >= capacity ? "已满员" : "加入房间");
        }

        var details = new StringBuilder();
        details.AppendLine(title).AppendLine("房主  " + host).AppendLine("房间  " + data.room_id);
        details.AppendLine().AppendLine("房间玩家");
        for (int seat = 0; seat < 4; seat++) {
            bool occupied = seat < count;
            int id = occupied ? data.player_list[seat] : 0;
            string name = occupied ? PlayerName(data, id) : "等待加入";
            if (occupied && masked) name = StreamerModeHelper.FormatRoomPlayerName(name, id);
            string state = !occupied ? "空位" : id == data.host_user_id ? "房主" :
                data.is_game_running ? "对局中" : id < 10 || Array.IndexOf(data.ready_list ?? Array.Empty<int>(), id) >= 0 ? "已准备" : "未准备";
            if (seat < playerNames.Length) Set(playerNames[seat], name);
            if (seat < playerStates.Length) {
                Set(playerStates[seat], state);
                if (playerStates[seat] != null) playerStates[seat].color = state == "房主"
                    ? new Color(1f, .77f, .44f) : state == "已准备" ? ReadyColor : MutedColor;
            }
            details.AppendLine($"{seat + 1}  {name}  ·  {state}");
        }
        details.AppendLine().AppendLine("完整房间配置");
        foreach (var field in RoomConfigContainer.BuildDisplayFields(data)) {
            details.AppendLine(field.Key + "  ·  " + field.Value);
        }
        if (detailsButton != null) {
            var tooltip = detailsButton.GetComponent<RoomInfoTooltip>() ?? detailsButton.gameObject.AddComponent<RoomInfoTooltip>();
            tooltip.SetContent("房间详情", details.ToString().TrimEnd(), roomName != null ? roomName.font : null);
        }
    }

    public static string BuildSummary(RoomInfo room) {
        string switches = $"  ·  错和{(room.open_cuohe ? "开" : "关")}  ·  提示{(room.tips ? "开" : "关")}";
        if (room.room_rule == "free") return "自由配牌  ·  " + (room.is_player_set_random_seed ? "复式开启" : "随机牌山") + switches;
        string summary = RoundTextDictionary.GetMaxRoundText(room.room_rule, room.game_round)
            + $"  ·  {room.round_timer}/{room.step_timer}秒";
        if (room.room_rule == "guobiao" || RuleRegistry.Resolve(room.room_rule, room.sub_rule)?.ShowsHepaiLimitInRoomList == true)
            summary += $"  ·  {room.hepai_limit}番起和";
        else if (room.room_rule == "riichi" && room.red_dora.HasValue)
            summary += room.red_dora.Value ? "  ·  赤宝牌开" : "  ·  赤宝牌关";
        else if (room.room_rule == "sichuan")
            summary += (room.blood_battle ?? true) ? "  ·  血战到底" : "  ·  一家和止";
        return summary + switches;
    }
    private static string PlayerName(RoomInfo room, int id) {
        if (room.player_settings != null && room.player_settings.TryGetValue(id.ToString(), out UserSettings settings)
            && !string.IsNullOrWhiteSpace(settings?.username)) return settings.username;
        if (id == room.host_user_id && !string.IsNullOrWhiteSpace(room.host_name)) return room.host_name;
        return id < 10 ? "机器人 " + id : "玩家 " + id;
    }
    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static void Set(TMP_Text label, string value) {
        if (label == null) return;
        label.richText = false;
        label.text = value ?? "";
    }
    private void JoinClick() {
        if (data == null || string.IsNullOrEmpty(data.room_id)) return;
        PasswordJoinPanel.TryJoin(data.room_id, data.has_password);
    }
}
