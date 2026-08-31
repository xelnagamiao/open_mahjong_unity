/// <summary>
/// 离开 gamePanel 后的页面跳转（真正退出，非对局挂后台）。
/// 统一回到进入对局/牌谱前冻结的 MainCanvas 页面，与场景内「主菜单」同一锚点。
/// </summary>
public static class PostGameNavigator {
    /// <summary>
    /// 真正离开 gamePanel，切回进入前所在大厅页并做场景清理。
    /// </summary>
    /// <param name="forceTeardown">对局结束、退出牌谱等须强制清理；延时观战被踢等可保留进行中的对局场景。</param>
    public static void ExitToLobby(bool forceTeardown = false) {
        // 未登录访问没有“大厅会话”可返回，统一结束当前会话并建立全新的登录连接。
        if (SharedRecordLink.IsPublicSharePlayback || UserDataManager.Instance.UserId == 0) {
            AppSession.ReturnToLogin();
            return;
        }

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

        MatchNetworkManager.Instance.ResetMatchLock();
        HeaderPanel.Instance?.SetBackToGameVisible(false);

        if (wasMatch) {
            UserDataManager.Instance.SetRoomId("");
        }

        string tab = WindowsManager.Instance.GetGameReturnWindow();
        bool alreadyOnReturn = WindowsManager.Instance.GetCurrentWindow() == tab;
        WindowsManager.Instance.ExitGameToReturnWindow();
        RefreshLobbyTabIfNeeded(tab, alreadyOnReturn);
    }

    private static void RefreshLobbyTabIfNeeded(string tab, bool alreadyOnTab) {
        switch (tab) {
            case "record":
                RecordPanel.Instance.OpenAndReload();
                break;
            case "friend":
                FriendNetworkManager.Instance.ListAllFriendPanels();
                break;
            case "event":
                // 赛事面板刚被重新打开时 OnEnable 已经恢复详情并刷新。
                if (alreadyOnTab) EventLobbyPanel.Instance?.OnReturnedFromGame();
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
}
