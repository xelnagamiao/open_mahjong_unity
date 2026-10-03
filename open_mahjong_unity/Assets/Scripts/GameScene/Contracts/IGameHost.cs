using UnityEngine;

/// <summary>
/// 对局层对外层 App 的出口：提示、切窗、网页入口。由大厅在启动时注入 <see cref="GameHost.Current"/>。
/// OM.Game 不得直接引用 WindowsManager / NotificationManager。
/// </summary>
public interface IGameHost {
    void ShowTip(string type, bool success, string message, float duration = -1f);
    void SwitchWindow(string window);
    void HangGameToReturnWindow();
    void SetBackToGameVisible(bool visible);
    string CurrentWindow { get; }
    string WebUrl { get; }
}

/// <summary>当前注入的 App 宿主；未注入时各属性返回空实现。</summary>
public static class GameHost {
    public static IGameHost Current { get; set; } = NullGameHost.Instance;

    private sealed class NullGameHost : IGameHost {
        public static readonly NullGameHost Instance = new NullGameHost();
        public void ShowTip(string type, bool success, string message, float duration = -1f) { }
        public void SwitchWindow(string window) { }
        public void HangGameToReturnWindow() { }
        public void SetBackToGameVisible(bool visible) { }
        public string CurrentWindow => "game";
        public string WebUrl => "";
    }
}

/// <summary>
/// 对局层可读的本地设置。由 ConfigManager 注入 <see cref="GameSettings.Current"/>。
/// </summary>
public interface IGameSettings {
    bool IsHandCutConfirmEnabled { get; }
    int HandSortSuitOrderMode { get; }
    bool ForcePassEnabled { get; }
    bool OpeningAutoBuhuaEnabled { get; }
    bool ActionButtonColorEnabled { get; }
    bool IsEnglish { get; }
    bool UseBlankWhiteDragonFace(int tileId);
    Color DefaultTableFaceFallbackColor { get; }
    void ApplyTileOutlineStyle();
    string GetTitleText(int titleId);
    Sprite GetProfileSprite(int profileId);
    Color GetAvatarFrameColor(int itemId);
    int MoqieShortcutMode { get; }
    int AskOtherPassShortcutMode { get; }
}

public static class GameSettings {
    public static IGameSettings Current { get; set; } = NullGameSettings.Instance;
    // userId == 0 signals a catalog refresh. Live equipment updates never alter replay snapshots.
    public static event System.Action<int, int> TitleChanged;
    public static void NotifyTitleChanged(int userId, int titleId) => TitleChanged?.Invoke(userId, titleId);
    public static event System.Action<InventoryAppearance> AppearanceChanged;
    public static void NotifyAppearanceChanged(InventoryAppearance appearance) => AppearanceChanged?.Invoke(appearance);

    private sealed class NullGameSettings : IGameSettings {
        public static readonly NullGameSettings Instance = new NullGameSettings();
        public bool IsHandCutConfirmEnabled => false;
        public int HandSortSuitOrderMode => 0;
        public bool ForcePassEnabled => false;
        public bool OpeningAutoBuhuaEnabled => true;
        public bool ActionButtonColorEnabled => false;
        public bool IsEnglish => false;
        public bool UseBlankWhiteDragonFace(int tileId) => false;
        public Color DefaultTableFaceFallbackColor => Color.white;
        public void ApplyTileOutlineStyle() { }
        public string GetTitleText(int titleId) => "";
        public Sprite GetProfileSprite(int profileId) => Resources.Load<Sprite>("image/Profiles/1");
        public Color GetAvatarFrameColor(int itemId) => Color.clear;
        public int MoqieShortcutMode => 0;
        public int AskOtherPassShortcutMode => 0;
    }
}

/// <summary>当前登录用户与房间会话。由 UserDataManager 注入 <see cref="PlayerSession.Current"/>。</summary>
public interface ISessionInfo {
    int UserId { get; }
    string Username { get; }
    string RoomId { get; }
    string GamestateId { get; }
    string RoomIdNone { get; }
    void SetRoomId(string roomId);
    void SetGamestateId(string id);
    void SetGameSession(string id, string gameRoomId);
    void UpdateGuobiaoRank(string rank, float score);
    void UpdateRating(string rule, string rank, float score, float elo, int games);
}

public static class PlayerSession {
    public static ISessionInfo Current { get; set; } = NullSession.Instance;

    private sealed class NullSession : ISessionInfo {
        public static readonly NullSession Instance = new NullSession();
        public int UserId => 0;
        public string Username => "";
        public string RoomId => "NOROOM";
        public string GamestateId => "";
        public string RoomIdNone => "NOROOM";
        public void SetRoomId(string roomId) { }
        public void SetGamestateId(string id) { }
        public void SetGameSession(string id, string gameRoomId) { }
        public void UpdateGuobiaoRank(string rank, float score) { }
        public void UpdateRating(string rule, string rank, float score, float elo, int games) { }
    }
}
