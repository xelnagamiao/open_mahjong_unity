/// <summary>
/// 对局/观战互斥会话判断。复用 NormalGameStateManager 与 GameRecordManager 已有标志，
/// 不引入额外状态字段。纯牌谱阅览（非 IsSpectating）不算互斥会话，可被其它行为覆盖；
/// 但随机桌面会清空 3D 牌桌，牌谱阅览与对局/观战一样禁止。
/// </summary>
public static class GameSessionGuard {
    /// <summary>
    /// 是否处于互斥的进行中会话：对局、实时观战、延时观战（牌谱流观战）。
    /// </summary>
    public static bool HasExclusiveSession {
        get {
            var gsm = NormalGameStateManager.Instance;
            if (gsm != null && (gsm.IsGameActive || gsm.IsRealtimeSpectator)) {
                return true;
            }
            if (GameRecordManager.HasPendingDelayedSpectatorSessionStatic) return true;
            var grm = GameRecordManager.Instance;
            return grm != null && grm.IsSpectating;
        }
    }

    /// <summary>
    /// 纯牌谱阅览进行中（含回主菜单挂后台）。退出牌谱后 GameRecordManager 会隐藏，不算。
    /// </summary>
    public static bool IsRecordViewing {
        get {
            var grm = GameRecordManager.Instance;
            return grm != null
                && grm.gameObject.activeInHierarchy
                && grm.gameRecord != null
                && !grm.IsSpectating;
        }
    }

    /// <summary>随机桌面会清空 3D 牌桌：对局、观战、牌谱阅览均禁止。</summary>
    public static bool BlocksRandomTable => HasExclusiveSession || IsRecordViewing;

    /// <summary>
    /// 若当前处于互斥会话则弹出提示并返回 true（表示应中止后续进入逻辑）。
    /// </summary>
    public static bool BlockIfExclusiveSession(string actionDescription) {
        if (!HasExclusiveSession) return false;
        ShowBlockedTip(actionDescription);
        return true;
    }

    /// <summary>
    /// 若当前对局/观战/牌谱阅览占用牌桌，则提示并返回 true。
    /// </summary>
    public static bool BlockIfRandomTableBlocked(string actionDescription) {
        if (!BlocksRandomTable) return false;
        ShowBlockedTip(actionDescription);
        return true;
    }

    private static void ShowBlockedTip(string actionDescription) {
        NotificationManager.Instance.ShowTip("提示", false,
            $"当前{DescribeCurrentSession()}，无法{actionDescription}");
    }

    private static string DescribeCurrentSession() {
        var gsm = NormalGameStateManager.Instance;
        if (gsm != null && gsm.IsRealtimeSpectator) return "正在实时观战中";
        if (GameRecordManager.HasPendingDelayedSpectatorSessionStatic
            && (GameRecordManager.Instance == null || !GameRecordManager.Instance.IsSpectating)) {
            return "正在加入延时观战";
        }
        var grm = GameRecordManager.Instance;
        if (grm != null && grm.IsSpectating) return "正在延时观战中";
        if (gsm != null && gsm.IsGameActive) return "正在对局中";
        if (IsRecordViewing) return "正在阅览牌谱";
        return "处于进行中的对局或观战";
    }
}
