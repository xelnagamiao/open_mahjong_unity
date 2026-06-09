using System;

/// <summary>
/// 主播模式：隐藏聊天、遮蔽房间与他人昵称。
/// 对局内 UI 由调用方通过 state / maskPlayerNames 等参数区分实时对局与牌谱，不在此处猜测全局场景状态。
/// </summary>
public static class StreamerModeHelper {
    public const string MaskedPlayerName = "玩家";
    public const string MaskedRoomText = "****";

    public static event Action OnChanged;

    public static bool IsEnabled =>
        ConfigManager.Instance != null && ConfigManager.Instance.StreamerModeEnabled;

    public static void NotifyChanged() {
        OnChanged?.Invoke();
        RefreshAllDisplays();
    }

    public static string FormatRoomLabel(string label, string value) {
        if (!IsEnabled) {
            return $"{label}{value}";
        }
        return $"{label}{MaskedRoomText}";
    }

    public static string FormatRoomPlayerName(string username, int userId) {
        if (!IsEnabled || IsSelfUser(userId, username)) {
            return username ?? string.Empty;
        }
        return MaskedPlayerName;
    }

    /// <summary>实时对局（state=gamestate）四角玩家名；牌谱请直接显示原名，不要调用本方法。</summary>
    public static string FormatGamestatePlayerName(string username, string position, int userId = 0) {
        if (!IsEnabled || IsSelfUser(userId, username) || position == "self") {
            return username ?? string.Empty;
        }
        if (NormalGameStateManager.Instance != null && NormalGameStateManager.Instance.IsRealtimeSpectator) {
            return username ?? string.Empty;
        }
        return MaskedPlayerName;
    }

    private static bool IsSelfUser(int userId, string username) {
        var userData = UserDataManager.Instance;
        if (userData == null) {
            return false;
        }
        if (userId > 0 && userId == userData.UserId) {
            return true;
        }
        return !string.IsNullOrEmpty(username) && username == userData.Username;
    }

    public static void RefreshAllDisplays() {
        WindowsManager.Instance?.ApplyStreamerModePanels();

        if (RoomListPanel.Instance != null && RoomListPanel.Instance.gameObject.activeInHierarchy) {
            RoomListPanel.Instance.RefreshRoomList();
        }

        RoomPanel.Instance?.RefreshStreamerModeDisplay();

        if (ScoreHistoryPanel.Instance != null && ScoreHistoryPanel.Instance.gameObject.activeInHierarchy) {
            GameSceneUIManager.Instance?.UpdateScoreRecord();
        }
    }
}
