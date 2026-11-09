using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>房间卡片：名称、精简配置、按房间容量显示席位；完整信息由问号展示。</summary>
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
    private int pairColorIndex = -1;
    private static readonly Color ReadyColor = new Color(.92f, .92f, .92f);
    private static readonly Color MutedColor = new Color(.78f, .78f, .78f);

    private void Awake() {
        if (joinButton != null) joinButton.onClick.AddListener(JoinClick);
    }
    private void OnEnable() {
        StreamerModeHelper.OnChanged += RefreshDisplay;
        ConfigManager.OnLanguageChanged += RefreshDisplay;
    }
    private void OnDisable() {
        StreamerModeHelper.OnChanged -= RefreshDisplay;
        ConfigManager.OnLanguageChanged -= RefreshDisplay;
    }
    public void SetRoomInfo(RoomInfo roomData, int groupColorIndex = -1) {
        data = roomData;
        pairColorIndex = groupColorIndex;
        RefreshDisplay();
    }

    private void RefreshDisplay() {
        if (data == null) return;
        bool masked = ConfigManager.Instance != null && StreamerModeHelper.IsEnabled;
        string title = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.room_name, "未命名房间");
        string host = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.host_name, "暂无房主");
        int count = data.player_list?.Length ?? 0;
        bool eightPlayerLobby = data.duplicate_lobby?.version == 1;
        int capacity = eightPlayerLobby ? 8 : data.max_player > 0 ? Mathf.Min(data.max_player, 4) : MahjongPlayerCount.ForSubRule(data.sub_rule);
        bool isEvent = data.room_type == "events" || !string.IsNullOrEmpty(data.event_id);
        string rule = RuleNameDictionary.GetWholeName(Fallback(data.sub_rule, data.room_rule ?? ""));
        Set(roomName, title);
        Set(playRule, rule);
        string status = data.is_game_running ? "对局中" : count >= capacity ? "满员" : "等待";
        bool pairEnded = !string.IsNullOrEmpty(data.duplicate_match_id) && (data.duplicate_status == "completed" || data.duplicate_status == "cancelled");
        if (pairEnded) status = "已结束";
        else if (!string.IsNullOrEmpty(data.duplicate_match_id) && data.duplicate_status == "interrupted") status = "待赛务处理";
        else if (!string.IsNullOrEmpty(data.duplicate_match_id) && data.duplicate_status == "ready") status = "等待对室";
        Set(gameStatus, $"{status} · {count}/{capacity} · {(data.has_password ? "有密码" : "公开")}");
        if (gameStatus != null) gameStatus.color = data.is_game_running ? new Color(1f, .77f, .44f) : ReadyColor;
        Set(configSummary, BuildSummary(data), data.room_rule ?? string.Empty);
        if (upperPartImage != null) upperPartImage.color = isEvent ? eventUpperColor : normalUpperColor;
        if (lowerPartImage != null) lowerPartImage.color = isEvent ? eventLowerColor : normalLowerColor;
        if (!string.IsNullOrEmpty(data.duplicate_match_id)) {
            // Adjacent open/closed rows share the original underlays, without adding badges.
            int hue = 0;
            foreach (char ch in data.duplicate_match_id) hue = (hue * 31 + ch) % 3;
            if (pairColorIndex >= 0) hue = pairColorIndex % 3;
            Color tint = hue == 0 ? new Color(.23f, .32f, .44f) : hue == 1 ? new Color(.29f, .27f, .40f) : new Color(.20f, .34f, .34f);
            if (upperPartImage != null) upperPartImage.color = tint;
            if (lowerPartImage != null) lowerPartImage.color = tint * new Color(.78f, .78f, .78f, 1);
        }
        if (joinButton != null) {
            joinButton.interactable = !pairEnded && count < capacity && !data.is_game_running;
            Set(joinButton.GetComponentInChildren<TMP_Text>(), pairEnded ? "已结束" : data.is_game_running ? "对局中" : count >= capacity ? "已满员" : "加入房间");
        }

        var details = new StringBuilder();
        details.AppendLine(title).AppendLine("房主  " + host).AppendLine("房间  " + data.room_id);
        details.AppendLine().AppendLine("房间玩家");
        if (eightPlayerLobby) RefreshEightLobbyMembers(details, masked);
        else for (int seat = 0; seat < 4; seat++) {
            bool available = seat < capacity;
            if (seat < playerNames.Length && playerNames[seat] != null)
                playerNames[seat].transform.parent.gameObject.SetActive(available);
            if (!available) {
                if (seat < playerNames.Length) Set(playerNames[seat], "");
                if (seat < playerStates.Length) Set(playerStates[seat], "");
                continue;
            }
            int[] seats = !string.IsNullOrEmpty(data.duplicate_match_id) ? data.seat_list ?? data.player_list : data.player_list;
            bool occupied = seats != null && seat < seats.Length && seats[seat] >= 0;
            int id = occupied ? seats[seat] : 0;
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
            // Translate configuration fields separately; the preceding lines contain literal player names.
            details.AppendLine(PlatformText.GetContextual(field.Key, data.room_rule) + "  ·  "
                + PlatformText.GetContextual(field.Value, data.room_rule));
        }
        if (detailsButton != null) {
            var tooltip = detailsButton.GetComponent<RoomInfoTooltip>() ?? detailsButton.gameObject.AddComponent<RoomInfoTooltip>();
            tooltip.SetContent("房间详情", details.ToString().TrimEnd(), roomName != null ? roomName.font : null);
        }
    }

    private void RefreshEightLobbyMembers(StringBuilder details, bool masked) {
        int[] seats = data.seat_list ?? Array.Empty<int>();
        int[][] groups = { new[] { 0, 2 }, new[] { 1, 3 }, new[] { 4, 6 }, new[] { 5, 7 } };
        string[] labels = { "闭室 · 红队", "闭室 · 蓝队", "开室 · 蓝队", "开室 · 红队" };
        for (int row = 0; row < groups.Length; row++) {
            int count = 0;
            foreach (int index in groups[row]) if (index < seats.Length && seats[index] >= 0) count++;
            if (row < playerNames.Length && playerNames[row]) {
                playerNames[row].transform.parent.gameObject.SetActive(true);
                Set(playerNames[row], labels[row]);
            }
            if (row < playerStates.Length) {
                Set(playerStates[row], $"{count}/2");
                if (playerStates[row]) playerStates[row].color = MutedColor;
            }
        }
        for (int index = 0; index < 8; index++) {
            int uid = index < seats.Length ? seats[index] : -1;
            string name = uid < 0 ? "空位" : PlayerName(data, uid);
            if (uid >= 0 && masked) name = StreamerModeHelper.FormatRoomPlayerName(name, uid);
            details.AppendLine($"{(index < 4 ? "闭室" : "开室")} · {"东南西北"[index % 4]}  {name}");
        }
        if ((data.duplicate_lobby.waiting?.Length ?? 0) > 0) {
            details.AppendLine().AppendLine("等候区");
            foreach (int uid in data.duplicate_lobby.waiting) {
                string name = PlayerName(data, uid);
                details.AppendLine(masked ? StreamerModeHelper.FormatRoomPlayerName(name, uid) : name);
            }
        }
    }

    public static string BuildSummary(RoomInfo room) {
        if (room.duplicate_lobby != null)
            return $"8 人复式 · {(room.duplicate_sync_mode == false ? "非同步" : "同步")} · {room.duplicate_round_count} 副 · {(room.duplicate_scoring == "imp" ? "IMP" : "分差累计")}";
        if (!string.IsNullOrEmpty(room.duplicate_match_id))
            return $"{(room.duplicate_table == "closed" ? "闭室" : "开室")} · {(room.duplicate_sync_mode == false ? "非同步" : "同步")} · 对室 #{room.duplicate_peer_room_id}  ·  {room.duplicate_round_count} 副  ·  {(room.duplicate_scoring == "imp" ? "IMP" : "分差累计")}";
        string switches = $"  ·  错和{(room.open_cuohe ? "开" : "关")}  ·  提示{(room.tips ? "开" : "关")}";
        if (room.is_duplicate)
            switches += "  ·  " + DuplicateWallDisplay.TypeName(room.duplicate_wall_type);
        if (room.room_rule == "free") return "自由配牌  ·  " + (room.is_player_set_random_seed ? "场景复现开启" : "随机牌山") + switches;
        string summary = (room.room_rule == "qingque" && room.unlimited_rounds ? "无固定时限" : RoundTextDictionary.GetMaxRoundText(room.room_rule, room.game_round))
            + $"  ·  {room.round_timer}/{room.step_timer}秒";
        if (room.room_rule == "guobiao" || RuleRegistry.Resolve(room.room_rule, room.sub_rule)?.ShowsHepaiLimitInRoomList == true)
            summary += $"  ·  {room.hepai_limit}番起和";
        else if (room.room_rule == "shanghai" && (string.IsNullOrEmpty(room.sub_rule) || room.sub_rule == "shanghai/qiaoma"))
            summary += room.hepai_limit == 1 ? "  ·  一番和开" : "  ·  一番和关";
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
    private static void Set(TMP_Text label, string value, string ruleContext = null) {
        if (label == null) return;
        label.richText = false;
        if (ruleContext == null) label.text = value ?? "";
        else TableText.Set(label, value, ruleContext);
    }
    private void JoinClick() {
        if (data == null || string.IsNullOrEmpty(data.room_id)) return;
        PasswordJoinPanel.TryJoin(data.room_id, data.has_password);
    }
}
