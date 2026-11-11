using System;

/// <summary>投影服务器的短等待窗口。到期只停止阻塞，不自行取消该弃牌的操作资格。</summary>
internal sealed class SichuanClaimWaitClock {
    private int tick = -1;
    private double deadline;
    private bool closed;
    private bool hasProjectedWindow;

    internal static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;
    internal bool IsOpen => tick >= 0 && !closed;
    internal int Tick => tick;
    internal bool IsStaleAsk(int actionTick, int currentTick) => hasProjectedWindow && actionTick < currentTick;

    internal static bool IsRetainedMode(string mode) => mode == "real_time" || mode == "waiting_draw";

    internal bool TryProject(int actionTick, string mode, double seconds, double receivedAt, double now,
                             out double remaining) {
        remaining = 0;
        if (!IsRetainedMode(mode) || actionTick < tick || (actionTick == tick && closed)) return false;
        hasProjectedWindow = true;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0;
        double proposedEnd = receivedAt + (mode == "waiting_draw" ? 0 : Math.Max(0, seconds));
        if (actionTick != tick) {
            tick = actionTick;
            closed = false;
            deadline = proposedEnd;
        } else {
            // 同一询问的补发/模式切换只能缩短时间，不能把软到期变成新的五秒。
            deadline = Math.Min(deadline, proposedEnd);
        }
        remaining = Math.Max(0, deadline - Math.Max(receivedAt, now));
        return true;
    }

    internal void Close(int actionTick = -1) {
        if (!hasProjectedWindow) return;
        tick = Math.Max(tick, actionTick);
        if (tick >= 0) closed = true;
    }
    internal void Reset() { tick = -1; deadline = 0; closed = false; hasProjectedWindow = false; }
}
