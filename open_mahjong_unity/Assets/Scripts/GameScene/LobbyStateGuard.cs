/// <summary>
/// 大厅互斥状态：自定义房间与排位匹配队列不可同时进行。
/// </summary>
public static class LobbyStateGuard {
    public static bool IsInRoom =>
        UserDataManager.Instance != null
        && UserDataManager.Instance.RoomId != UserDataManager.ROOM_ID_NONE;

    public static bool IsInMatchQueue {
        get {
            var manager = MatchStateManager.Instance;
            return manager != null && (manager.IsQueueing || manager.IsMatchFound);
        }
    }

    /// <summary>若已在房间则提示并返回 true（应中止匹配）。</summary>
    public static bool BlockIfInRoomForMatch() {
        if (!IsInRoom) return false;
        NotificationManager.Instance?.ShowTip("匹配", false, "请先退出当前房间再进行排位匹配");
        return true;
    }

    /// <summary>若正在匹配队列中则提示并返回 true（应中止进入/创建房间）。</summary>
    public static bool BlockIfInMatchQueueForRoom() {
        if (!IsInMatchQueue) return false;
        NotificationManager.Instance?.ShowTip("房间", false, "正在匹配队列中，请先取消匹配再进入或创建房间");
        return true;
    }

    /// <summary>若正在匹配队列中则提示并返回 true（应中止进入观战）。</summary>
    public static bool BlockIfInMatchQueueForSpectator() {
        if (!IsInMatchQueue) return false;
        NotificationManager.Instance?.ShowTip("观战", false, "正在匹配队列中，请先取消匹配再进入观战");
        return true;
    }
}
