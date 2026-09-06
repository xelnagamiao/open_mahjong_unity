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
    bool MeldSpacingEnabled { get; }
    bool ActionButtonColorEnabled { get; }
    bool IsEnglish { get; }
    bool UseBlankWhiteDragonFace(int tileId);
    Color DefaultTableFaceFallbackColor { get; }
    void ApplyTileOutlinePreset();
    string GetTitleText(int titleId);
    int MoqieShortcutMode { get; }
    int AskOtherPassShortcutMode { get; }
}

public static class GameSettings {
    public static IGameSettings Current { get; set; } = NullGameSettings.Instance;

    private sealed class NullGameSettings : IGameSettings {
        public static readonly NullGameSettings Instance = new NullGameSettings();
        public bool IsHandCutConfirmEnabled => false;
        public int HandSortSuitOrderMode => 0;
        public bool ForcePassEnabled => false;
        public bool OpeningAutoBuhuaEnabled => true;
        public bool MeldSpacingEnabled => false;
        public bool ActionButtonColorEnabled => false;
        public bool IsEnglish => false;
        public bool UseBlankWhiteDragonFace(int tileId) => false;
        public Color DefaultTableFaceFallbackColor => Color.white;
        public void ApplyTileOutlinePreset() { }
        public string GetTitleText(int titleId) => "";
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
    void UpdateGuobiaoRank(string rank, float score);
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
        public void UpdateGuobiaoRank(string rank, float score) { }
    }
}
