using System;

/// <summary>Exact local display of one server Guizhou action window.</summary>
public sealed class GuizhouAskClock {
    private int tick = -1;
    private double deadline, stepDeadline;
    private bool closed;
    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;
    public void Reset() { tick = -1; closed = false; }
    public void Close() { closed = true; }
    public bool IsClosed(int actionTick) => actionTick < tick || (actionTick == tick && closed);

    public void Project(int actionTick, double bank, double step, double receivedAt, double now,
                        out double bankRemaining, out double stepRemaining) {
        double proposedStep = receivedAt + Math.Max(0, step);
        double proposedEnd = proposedStep + Math.Max(0, bank);
        if (actionTick > tick) {
            tick = actionTick;
            stepDeadline = proposedStep;
            deadline = proposedEnd;
            closed = false;
        } else if (actionTick == tick) {
            // A reconnect or repeated packet can shorten but never extend it.
            stepDeadline = Math.Min(stepDeadline, proposedStep);
            deadline = Math.Min(deadline, proposedEnd);
        }
        stepRemaining = closed ? 0 : Math.Max(0, stepDeadline-now);
        bankRemaining = closed ? 0 : Math.Max(0, deadline-Math.Max(now, stepDeadline));
    }
}
