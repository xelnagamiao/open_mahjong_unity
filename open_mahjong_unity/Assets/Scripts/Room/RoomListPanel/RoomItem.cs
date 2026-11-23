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
    [SerializeField] private RectTransform duplicateSeats;
    // Server seat order is closed E/S/W/N followed by open E/S/W/N.
    [SerializeField] private TMP_Text[] duplicateNames;
    [SerializeField] private Image[] duplicateTeamBackgrounds;
    [SerializeField] private Image upperPartImage;
    [SerializeField] private Image lowerPartImage;
    [SerializeField] private Color normalUpperColor = new Color(.32f, .32f, .32f);
    [SerializeField] private Color normalLowerColor = new Color(.18f, .18f, .18f);
    [SerializeField] private Color eventUpperColor = new Color(.36f, .36f, .36f);
    [SerializeField] private Color eventLowerColor = new Color(.22f, .22f, .22f);

    private RoomInfo data;
    private int pairColorIndex = -1;
    private RoomInfoTooltip tooltip;
    private PlatformFontBinding[] fontBindings;
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
        if (detailsButton != null && tooltip == null) {
            tooltip = detailsButton.GetComponent<RoomInfoTooltip>() ?? detailsButton.gameObject.AddComponent<RoomInfoTooltip>();
            tooltip.SetRoom(this, roomName != null ? roomName.font : null);
        }
        data = roomData;
        pairColorIndex = groupColorIndex;
        RefreshDisplay();
    }

    // Even an unchanged card must use the latest room for its join action and on-demand details.
    public bool UpdateSnapshot(RoomInfo next, int color) {
        bool changed = !SameCard(data, next) || (!string.IsNullOrEmpty(next.duplicate_match_id) && pairColorIndex != color);
        data = next;
        pairColorIndex = color;
        if (!changed) tooltip?.RefreshIfOpen();
        return changed;
    }

    private static bool SameCard(RoomInfo a, RoomInfo b) {
        if (a == b) return true;
        if (a == null || a.room_id != b.room_id || a.room_name != b.room_name ||
            a.room_rule != b.room_rule || a.sub_rule != b.sub_rule || a.max_player != b.max_player ||
            a.room_type != b.room_type || a.event_id != b.event_id || a.host_user_id != b.host_user_id ||
            a.has_password != b.has_password || a.is_game_running != b.is_game_running ||
            a.duplicate_match_id != b.duplicate_match_id || a.duplicate_status != b.duplicate_status ||
            a.duplicate_lobby?.version != b.duplicate_lobby?.version || BuildSummary(a) != BuildSummary(b))
            return false;
        if (!SameIds(a.player_list, b.player_list) || !SameIds(a.seat_list, b.seat_list) || !SameIds(a.ready_list, b.ready_list))
            return false;
        bool eight = IsEightPlayerLobby(b);
        if (eight && !SameIds(a.team_by_seat, b.team_by_seat)) return false;
        var seats = eight ? b.seat_list : string.IsNullOrEmpty(b.duplicate_match_id) ? b.player_list : b.seat_list ?? b.player_list;
        if (seats != null)
            foreach (int uid in seats)
                if (uid >= 0 && PlayerName(a, uid) != PlayerName(b, uid)) return false;
        return true;
    }

    private static bool SameIds(int[] a, int[] b) {
        if (a == b) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public static bool IsEightPlayerLobby(RoomInfo info) => info?.duplicate_lobby?.version == 1;
    public float DuplicateHeight => duplicateSeats.sizeDelta.y - duplicateSeats.anchoredPosition.y;
    private static readonly string[] Winds = { "东", "南", "西", "北" };

    private int Capacity => IsEightPlayerLobby(data) ? 8 : data.max_player > 0
        ? Mathf.Min(data.max_player, 4) : MahjongPlayerCount.ForSubRule(data.sub_rule);

    private void RefreshDisplay() {
        if (data == null) return;
        bool masked = ConfigManager.Instance != null && StreamerModeHelper.IsEnabled;
        string title = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.room_name, "未命名房间");
        int count = data.player_list?.Length ?? 0;
        bool eightPlayerLobby = IsEightPlayerLobby(data);
        int capacity = Capacity;
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

        duplicateSeats.gameObject.SetActive(eightPlayerLobby);
        if (eightPlayerLobby) RefreshEightLobbyMembers(masked);
        for (int seat = 0; seat < playerNames.Length; seat++) {
            bool available = !eightPlayerLobby && seat < capacity;
            if (seat < playerNames.Length && playerNames[seat] != null)
                playerNames[seat].transform.parent.gameObject.SetActive(available);
            if (!available) {
                if (seat < playerNames.Length) Set(playerNames[seat], "");
                if (seat < playerStates.Length) Set(playerStates[seat], "");
                continue;
            }
            SeatDisplay(seat, masked, out string name, out string state);
            if (seat < playerNames.Length) Set(playerNames[seat], name);
            if (seat < playerStates.Length) {
                Set(playerStates[seat], state);
                if (playerStates[seat] != null) playerStates[seat].color = state == "房主"
                    ? new Color(1f, .77f, .44f) : state == "已准备" ? ReadyColor : MutedColor;
            }
        }
        // A row first created in LateUpdate can render before its own LateUpdate. Resolve the
        // actual seat names now so TMP never renders them with the prefab's sample-name font.
        fontBindings ??= GetComponentsInChildren<PlatformFontBinding>(true);
        foreach (var binding in fontBindings)
            if (binding.isActiveAndEnabled) binding.ApplyIfNeeded();
        tooltip?.RefreshIfOpen();
    }

    private void SeatDisplay(int seat, bool masked, out string name, out string state) {
        int[] seats = !string.IsNullOrEmpty(data.duplicate_match_id) ? data.seat_list ?? data.player_list : data.player_list;
        bool occupied = seats != null && seat < seats.Length && seats[seat] >= 0;
        int id = occupied ? seats[seat] : 0;
        name = occupied ? PlayerName(data, id) : "等待加入";
        if (occupied && masked) name = StreamerModeHelper.FormatRoomPlayerName(name, id);
        state = !occupied ? "空位" : id == data.host_user_id ? "房主" :
            data.is_game_running ? "对局中" : id < 10 || Array.IndexOf(data.ready_list ?? Array.Empty<int>(), id) >= 0 ? "已准备" : "未准备";
    }

    public string BuildDetails() {
        if (data == null) return "";
        bool masked = ConfigManager.Instance != null && StreamerModeHelper.IsEnabled;
        string title = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.room_name, "未命名房间");
        string host = masked ? StreamerModeHelper.MaskedRoomText : Fallback(data.host_name, "暂无房主");
        var details = new StringBuilder();
        details.AppendLine(title).AppendLine("房主  " + host).AppendLine("房间  " + data.room_id);
        details.AppendLine().AppendLine("房间玩家");
        if (data.duplicate_lobby?.version == 1) AppendEightLobbyDetails(details, masked);
        else for (int seat = 0; seat < Capacity; seat++) {
            SeatDisplay(seat, masked, out string name, out string state);
            details.AppendLine($"{seat + 1}  {name}  ·  {state}");
        }
        details.AppendLine().AppendLine("完整房间配置");
        foreach (var field in RoomConfigContainer.BuildDisplayFields(data)) {
            // Translate configuration fields separately; the preceding lines contain literal player names.
            details.AppendLine(PlatformText.GetContextual(field.Key, data.room_rule) + "  ·  "
                + PlatformText.GetContextual(field.Value, data.room_rule));
        }
        return details.ToString().TrimEnd();
    }

    private void RefreshEightLobbyMembers(bool masked) {
        var seats = data.seat_list ?? Array.Empty<int>();
        string waiting = PlatformText.Get("等待加入");
        for (int i = 0; i < duplicateNames.Length; i++) {
            string wind = PlatformText.Get(Winds[i % 4]);
            string name = waiting;
            if (i < seats.Length && seats[i] >= 0) {
                int uid = seats[i];
                name = PlayerName(data, uid);
                if (masked) name = StreamerModeHelper.FormatRoomPlayerName(name, uid);
            }
            Set(duplicateNames[i], wind + "  " + name);
            // Older room snapshots omit the team array, but the two tables have fixed opposite teams.
            bool open = i >= 4;
            int defaultTeam = open ? i % 2 : 1 - i % 2;
            int team = data.team_by_seat != null && i < data.team_by_seat.Length ? data.team_by_seat[i] : defaultTeam;
            duplicateTeamBackgrounds[i].color = SpectatorRoomPresentation.TeamBackground(team);
        }
    }

    private void AppendEightLobbyDetails(StringBuilder details, bool masked) {
        int[] seats = data.seat_list ?? Array.Empty<int>();
        for (int index = 0; index < 8; index++) {
            int uid = index < seats.Length ? seats[index] : -1;
            string name = uid < 0 ? PlatformText.Get("等待加入") : PlayerName(data, uid);
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

    private static string DuplicateScoringLabel(RoomInfo room) {
        bool imp = room.duplicate_scoring == "imp" ||
            (string.IsNullOrEmpty(room.duplicate_scoring) && room.sub_rule == SichuanLobby.DoublesDuplicateSubRule);
        return imp ? "IMP计分" : "分差累计";
    }

    public static string BuildSummary(RoomInfo room) {
        if (room.duplicate_lobby != null)
            return $"8 人复式 · {(room.duplicate_sync_mode == false ? "非同步模式" : "同步模式")} · {room.duplicate_round_count} 副 · {DuplicateScoringLabel(room)}";
        if (!string.IsNullOrEmpty(room.duplicate_match_id))
            return $"{(room.duplicate_table == "closed" ? "闭室" : "开室")} · {(room.duplicate_sync_mode == false ? "非同步模式" : "同步模式")} · 对室 #{room.duplicate_peer_room_id}  ·  {room.duplicate_round_count} 副  ·  {DuplicateScoringLabel(room)}";
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
