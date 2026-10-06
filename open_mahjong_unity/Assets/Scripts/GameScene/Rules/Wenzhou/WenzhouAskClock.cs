using System;

/// <summary>Local presentation of the server's existing Wenzhou action window.</summary>
public sealed class WenzhouAskClock {
    private int tick = -1;
    private double deadline, stepDeadline;
    private bool closed;

    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    public void Reset() { tick = -1; closed = false; }
    public void Close() { closed = true; }
    public bool IsClosed(int actionTick) => tick == actionTick && closed;

    public void Project(int actionTick, double remainingMs, double stepRemainingMs,
        double receivedAt, double now, out double bankSeconds, out double stepSeconds) {
        double nextDeadline = receivedAt + Math.Max(0,remainingMs)/1000;
        double nextStepDeadline = receivedAt + Math.Max(0,Math.Min(stepRemainingMs,remainingMs))/1000;
        if (tick != actionTick) {
            tick = actionTick;
            deadline = nextDeadline;
            stepDeadline = nextStepDeadline;
            closed = false;
        } else {
            // Reconnect / duplicate asks cannot give this window more time.
            deadline = Math.Min(deadline,nextDeadline);
            stepDeadline = Math.Min(stepDeadline,nextStepDeadline);
        }
        stepSeconds = closed ? 0 : Math.Max(0,stepDeadline-now);
        bankSeconds = closed ? 0 : Math.Max(0,deadline-Math.Max(now,stepDeadline));
    }
}
