public partial class NormalGameStateManager {
    /// <summary>
    /// 进入实时观战模式：客户端只渲染服务器推送的 gamestate，所有发送动作的接口（cut/action/riichi 等）均提前 return。
    /// 调用方应当先在 RealtimeRequestWaitPanel 收到 friend/realtime_started 后再调用此方法，
    /// 服务器随后会按 B 的座位转发完整 game_start + 后续广播。
    /// </summary>
    public void StartAsRealtimeSpectator(string gamestateId, int hostUserId = 0) {
        if (LobbyStateGuard.BlockIfInMatchQueueForSpectator()) return;
        if (GameSessionGuard.BlockIfExclusiveSession("进入实时观战")) return;
        IsRealtimeSpectator = true;
        RealtimeSpectatorHostUserId = hostUserId;
        UserDataManager.Instance.SetGamestateId(gamestateId);
        if (ExitButtonManager.Instance != null) {
            ExitButtonManager.Instance.ShowForRealtimeSpectator();
        }
        SubscribeRealtimeEndEvents();
    }

    /// <summary>
    /// 退出实时观战模式：清空标志位与按钮显示，不销毁场景（由 PostGameNavigator 统一 teardown）。
    /// </summary>
    public void StopAsRealtimeSpectator() {
        IsRealtimeSpectator = false;
        RealtimeSpectatorHostUserId = 0;
        UserDataManager.Instance.SetGamestateId("");
        if (ExitButtonManager.Instance != null) {
            ExitButtonManager.Instance.HideAll();
        }
        UnsubscribeRealtimeEndEvents();
    }

    private bool _realtimeEndSubscribed;
    private void SubscribeRealtimeEndEvents() {
        if (_realtimeEndSubscribed) return;
        if (FriendNetworkManager.Instance == null) return;
        FriendNetworkManager.Instance.OnRealtimeKicked += HandleRealtimeKicked;
        FriendNetworkManager.Instance.OnRealtimeEnded += HandleRealtimeEnded;
        _realtimeEndSubscribed = true;
    }
    private void UnsubscribeRealtimeEndEvents() {
        if (!_realtimeEndSubscribed) return;
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeKicked -= HandleRealtimeKicked;
            FriendNetworkManager.Instance.OnRealtimeEnded -= HandleRealtimeEnded;
        }
        _realtimeEndSubscribed = false;
    }

    private void HandleRealtimeKicked(Response response) {
        if (!IsRealtimeSpectator) return;
        NotificationManager.Instance?.ShowTip("实时观战", false, response?.message ?? "您已被踢出实时观战");
        PostGameNavigator.ExitToFriend();
    }

    private void HandleRealtimeEnded(Response response) {
        if (!IsRealtimeSpectator) return;
        NotificationManager.Instance?.ShowTip("实时观战", true, response?.message ?? "被观战的对局已结束");
        PostGameNavigator.ExitToFriend();
    }
}
