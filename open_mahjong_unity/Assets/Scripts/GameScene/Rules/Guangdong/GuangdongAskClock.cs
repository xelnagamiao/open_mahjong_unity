using System;

/// <summary>广东动作显示从本机收件时刻起算；同一 tick 的补发只能收紧窗口。</summary>
internal sealed class GuangdongAskClock {
    private int tick = -1;
    private double deadline;
    private double stepDeadline;
    private string gameId;
    private int round;
    private int? viewSeat;
    private bool closed;

    internal static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    internal void Reset() { tick = -1; closed = false; gameId = null; round = 0; viewSeat = null; }

    internal bool IsClosed(int actionTick) => tick >= 0 && (actionTick < tick || (actionTick == tick && closed));
    internal void Close() { if (tick >= 0) closed = true; }

    internal void EnterRound(string id, int number, int? viewer = null) {
        if (gameId == id && round == number && viewSeat == viewer) return;
        tick = -1;
        closed = false;
        gameId = id;
        round = number;
        viewSeat = viewer;
    }

    internal void ApplyHand(Response response, int roomStep, double? nowOverride = null) {
        var info = response?.ask_hand_action_info;
        if (info?.action_list == null || info.action_list.Length == 0) return;
        double now = nowOverride ?? Now;
        Project(info.action_tick, info.remaining_time_ms.HasValue ? info.remaining_time_ms.Value / 1000d : info.remaining_time,
            info.step_remaining_ms.HasValue ? info.step_remaining_ms.Value / 1000d : info.step_remaining ?? roomStep,
            response.received_monotonic ?? now, now, out int bankLeft, out int stepLeft);
        info.remaining_time = bankLeft;
        info.step_remaining = stepLeft;
    }

    internal void ApplyClaim(Response response, int roomStep, double? nowOverride = null) {
        var info = response?.ask_other_action_info;
        if (info?.action_list == null || info.action_list.Length == 0) return;
        double now = nowOverride ?? Now;
        Project(info.action_tick, info.remaining_time_ms.HasValue ? info.remaining_time_ms.Value / 1000d : info.remaining_time,
            info.is_tactical_recheck == true ? 0 : info.step_remaining_ms.HasValue
                ? info.step_remaining_ms.Value / 1000d : info.step_remaining ?? roomStep,
            response.received_monotonic ?? now, now, out int bankLeft, out int stepLeft);
        info.remaining_time = bankLeft;
        info.step_remaining = stepLeft;
    }

    internal void Project(int actionTick, double bank, double step, double receivedAt, double now,
                          out int bankRemaining, out int stepRemaining) {
        double proposedStep = receivedAt + Math.Max(0, step);
        double proposedEnd = proposedStep + Math.Max(0, bank);
        if (tick != actionTick) {
            tick = actionTick;
            closed = false;
            stepDeadline = proposedStep;
            deadline = proposedEnd;
        } else {
            stepDeadline = Math.Min(stepDeadline, proposedStep);
            deadline = Math.Min(deadline, proposedEnd);
        }
        now = Math.Max(now, receivedAt);
        stepRemaining = Seconds(stepDeadline - now);
        bankRemaining = Seconds(deadline - Math.Max(now, stepDeadline));
    }

    internal void Remaining(double now, out double bankRemaining, out double stepRemaining) {
        stepRemaining = Math.Max(0, stepDeadline - now);
        bankRemaining = Math.Max(0, deadline - Math.Max(now, stepDeadline));
    }

    private static int Seconds(double value) => (int)Math.Ceiling(Math.Max(0, value - 1e-9));
}
