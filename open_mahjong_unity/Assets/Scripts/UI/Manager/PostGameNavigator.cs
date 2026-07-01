/// <summary>
/// 离开 gamePanel 后的页面跳转（真正退出，非对局挂后台）。
/// 统一回到进入对局/牌谱前的大厅顶栏标签，由 WindowsManager 在 SwitchWindow 时自动维护。
/// </summary>
public static class PostGameNavigator {
    /// <summary>
    /// 真正离开 gamePanel，切回上次大厅标签并做场景清理。
    /// </summary>
    /// <param name="forceTeardown">对局结束、退出牌谱等须强制清理；延时观战被踢等可保留进行中的对局场景。</param>
    public static void ExitToLobby(bool forceTeardown = false) {
        bool wasSpectating = GameRecordManager.Instance != null && GameRecordManager.Instance.IsSpectating;
        if (wasSpectating) {
            GameRecordManager.Instance.StopSpectating();
        }

        NormalGameStateManager.Instance?.StopAsRealtimeSpectator();

        bool wasMatch = NormalGameStateManager.Instance != null
            && NormalGameStateManager.Instance.roomType == "match";

        if (forceTeardown || (!wasSpectating && !ShouldPreserveActiveGameScene())) {
            UserDataManager.Instance.SetGamestateId("");
            GameSceneTeardown.ResetToIdle();
        }

        MatchNetworkManager.Instance?.ResetMatchLock();
        HeaderPanel.Instance?.SetBackToGameVisible(false);

        if (wasMatch) {
            UserDataManager.Instance.SetRoomId("");
        }

        string tab = WindowsManager.Instance.GetLastLobbyTab();
        WindowsManager.Instance.ExitGameToLastLobbyTab();
        RefreshLobbyTabIfNeeded(tab);
    }

    private static void RefreshLobbyTabIfNeeded(string tab) {
        switch (tab) {
            case "record":
                DataNetworkManager.Instance?.GetRecordList();
                break;
            case "friend":
                FriendNetworkManager.Instance?.ListAllFriendPanels();
                break;
            case "room":
                if (UserDataManager.Instance != null
                    && UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
                    RoomWindowsManager.Instance?.SwitchRoomWindow("roomInfo");
                }
                break;
        }
    }

    /// <summary>
    /// 匹配排队/已匹配、正常对局、实时观战进行中时不应因迟到的延时观战消息清空游戏场景。
    /// </summary>
    private static bool ShouldPreserveActiveGameScene() {
        var gsm = NormalGameStateManager.Instance;
        if (gsm != null && (gsm.IsGameActive || gsm.IsRealtimeSpectator)) {
            return true;
        }
        return LobbyStateGuard.IsInMatchQueue;
    }

    public static void NavigateAfterGameEnd() => ExitToLobby(forceTeardown: true);

    public static void ExitToRecord() => ExitToLobby(forceTeardown: true);

    public static void ExitToSpectator() => ExitToLobby();

    public static void ExitToFriend() => ExitToLobby(forceTeardown: true);
}
