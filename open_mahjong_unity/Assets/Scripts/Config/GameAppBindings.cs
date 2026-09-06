using UnityEngine;

/// <summary>
/// 把大厅单例接到对局层接口上。对局代码只认 GameHost / GameSettings / PlayerSession。
/// </summary>
internal static class GameAppBindings {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Bind() {
        GameHost.Current = new HostAdapter();
        GameSettings.Current = new SettingsAdapter();
        PlayerSession.Current = new SessionAdapter();
    }

    private sealed class HostAdapter : IGameHost {
        public void ShowTip(string type, bool success, string message, float duration = -1f) {
            if (NotificationManager.Instance != null) {
                NotificationManager.Instance.ShowTip(type, success, message, duration);
            }
        }

        public void SwitchWindow(string window) {
            WindowsManager.Instance?.SwitchWindow(window);
        }

        public void HangGameToReturnWindow() {
            WindowsManager.Instance?.HangGameToReturnWindow();
        }

        public void SetBackToGameVisible(bool visible) {
            HeaderPanel.Instance?.SetBackToGameVisible(visible);
        }

        public string CurrentWindow => WindowsManager.Instance != null
            ? WindowsManager.Instance.GetCurrentWindow()
            : "game";

        public string WebUrl => ConfigManager.webUrl ?? "";
    }

    private sealed class SettingsAdapter : IGameSettings {
        private static ConfigManager Cfg => ConfigManager.Instance;

        public bool IsHandCutConfirmEnabled => Cfg != null && Cfg.IsHandCutConfirmEnabled;
        public int HandSortSuitOrderMode => Cfg != null ? Cfg.HandSortSuitOrderMode : 0;
        public bool ForcePassEnabled => Cfg != null && Cfg.ForcePassEnabled;
        public bool OpeningAutoBuhuaEnabled => Cfg == null || Cfg.OpeningAutoBuhuaEnabled;
        public bool MeldSpacingEnabled => Cfg != null && Cfg.MeldSpacingEnabled;
        public bool ActionButtonColorEnabled => Cfg != null && Cfg.ActionButtonColorEnabled;
        public bool IsEnglish => ConfigManager.IsEnglish;
        public bool UseBlankWhiteDragonFace(int tileId) => Cfg != null && Cfg.UseBlankWhiteDragonFace(tileId);
        public Color DefaultTableFaceFallbackColor => ConfigManager.DefaultTableFaceFallbackColor;
        public void ApplyTileOutlinePreset() => Cfg?.ApplyTileOutlinePreset();
        public string GetTitleText(int titleId) => ConfigManager.GetTitleText(titleId);
        public int MoqieShortcutMode => Cfg != null ? Cfg.MoqieShortcutMode : 0;
        public int AskOtherPassShortcutMode => Cfg != null ? Cfg.AskOtherPassShortcutMode : 0;
    }

    private sealed class SessionAdapter : ISessionInfo {
        private static UserDataManager U => UserDataManager.Instance;
        public int UserId => U != null ? U.UserId : 0;
        public string Username => U != null ? U.Username : "";
        public string RoomId => U != null ? U.RoomId : UserDataManager.ROOM_ID_NONE;
        public string GamestateId => U != null ? U.GamestateId : "";
        public string RoomIdNone => UserDataManager.ROOM_ID_NONE;
        public void SetRoomId(string roomId) => U?.SetRoomId(roomId);
        public void SetGamestateId(string id) => U?.SetGamestateId(id);
        public void UpdateGuobiaoRank(string rank, float score) => U?.UpdateGuobiaoRank(rank, score);
    }
}
