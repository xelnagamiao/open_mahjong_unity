/// <summary>
/// 离开 gamePanel 后的页面跳转（真正退出，非对局挂后台）。
/// </summary>
public static class PostGameNavigator {
    public static void NavigateAfterGameEnd() {
        // 匹配对局不依赖房间系统，结束后无房间可返回：直接回主菜单，并清除其仅用于聊天/协议的房间号。
        bool wasMatch = NormalGameStateManager.Instance != null
            && NormalGameStateManager.Instance.roomType == "match";

        UserDataManager.Instance.SetGamestateId("");
        GameSceneTeardown.ResetToIdle();
        // 兜底：确保对局结束后匹配承诺锁已释放，否则将无法再次匹配。
        MatchNetworkManager.Instance?.ResetMatchLock();
        HeaderPanel.Instance?.SetBackToGameVisible(false);

        if (wasMatch) {
            UserDataManager.Instance.SetRoomId("");
            WindowsManager.Instance.ExitGameTo("menu");
            return;
        }

        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            WindowsManager.Instance.ExitGameTo("room");
            RoomWindowsManager.Instance.SwitchRoomWindow("roomInfo");
            return;
        }

        WindowsManager.Instance.ExitGameTo("menu");
    }

    public static void ExitToRecord() {
        UserDataManager.Instance.SetGamestateId("");
        GameSceneTeardown.ResetToIdle();
        HeaderPanel.Instance?.SetBackToGameVisible(false);
        WindowsManager.Instance.ExitGameTo("record");
        DataNetworkManager.Instance?.GetRecordList();
    }

    public static void ExitToSpectator() {
        if (GameRecordManager.Instance != null && GameRecordManager.Instance.IsSpectating) {
            GameRecordManager.Instance.StopSpectating();
        } else {
            UserDataManager.Instance.SetGamestateId("");
            GameSceneTeardown.ResetToIdle();
        }
        HeaderPanel.Instance?.SetBackToGameVisible(false);
        WindowsManager.Instance.ExitGameTo("spectator");
    }

    public static void ExitToFriend() {
        NormalGameStateManager.Instance?.StopAsRealtimeSpectator();
        GameSceneTeardown.ResetToIdle();
        HeaderPanel.Instance?.SetBackToGameVisible(false);
        WindowsManager.Instance.ExitGameTo("friend");
        FriendNetworkManager.Instance?.ListAllFriendPanels();
    }
}
