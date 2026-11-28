using UnityEngine;

/// <summary>
/// 增量消息静默快进作用域：中短时后台积压（2~24 条同局消息）追帧期间开启，
/// 逻辑状态机照常逐条落桌，仅跳过历史音效/飘字/中间听牌重算与 3D 飞行补间。
/// 追平到队列最后一条时自动关闭，恢复正常反馈与最终听牌刷新。
/// </summary>
public static class TableCatchupMode {
    public const int FastForwardMaxMessagesPerFrame = 16;
    public const double FastForwardFrameBudgetSeconds = 0.008d;

    public static bool IsFastForwarding { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        IsFastForwarding = false;
    }

    public static void SetFastForwarding(bool active) {
        IsFastForwarding = active;
    }
}
