/// <summary>
/// 离开 gamePanel 后的页面跳转（真正退出，非对局挂后台）。
/// </summary>
public static class PostGameNavigator {
    public static void NavigateAfterGameEnd() {
        UserDataManager.Instance.SetGamestateId("");
        GameSceneTeardown.ResetToIdle();
        HeaderPanel.Instance?.SetBackToGameVisible(false);

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
