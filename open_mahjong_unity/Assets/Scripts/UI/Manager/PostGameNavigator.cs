/// <summary>
/// 对局结束后的页面跳转：仍在房间则回房间等待界面，否则回主菜单。
/// </summary>
public static class PostGameNavigator {
    public static void NavigateAfterGameEnd() {
        UserDataManager.Instance.SetGamestateId("");
        Game3DManager.Instance.Clear3DTile();
        HeaderPanel.Instance?.SetBackToGameVisible(false);

        if (UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE) {
            WindowsManager.Instance.SwitchWindow("room");
            RoomWindowsManager.Instance.SwitchRoomWindow("roomInfo");
            return;
        }

        WindowsManager.Instance.SwitchWindow("menu");
    }
}
