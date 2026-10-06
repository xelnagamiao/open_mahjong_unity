using System;

/// <summary>长春询问显示：扣除本机排队时间，同一询问的补发不能延长截止点。</summary>
public sealed class ChangchunAskClock {
    private int tick = -1;
    private double deadline;
    private double stepDeadline;
    private bool closed;

    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    public void Reset() { tick = -1; closed = false; }
    public void Close() { closed = true; }
    public bool ShouldIgnore(int actionTick) => actionTick < tick || (actionTick == tick && closed);
    public bool AcceptsClosure(int actionTick, int? targetSeat, int selfSeat) =>
        actionTick == tick && targetSeat == selfSeat;

    public void Project(int actionTick, int bank, int step, double receivedAt, double now,
                        out int bankRemaining, out int stepRemaining) {
        Project(actionTick, (double)bank, step, receivedAt, now, out double b, out double s);
        bankRemaining = Seconds(b);
        stepRemaining = Seconds(s);
    }

    public void Project(int actionTick, double bank, double step, double receivedAt, double now,
                        out double bankRemaining, out double stepRemaining) {
        double proposedStep = receivedAt + Math.Max(0, step);
        double proposedEnd = proposedStep + Math.Max(0, bank);
        if (actionTick != tick) {
            tick = actionTick;
            closed = false;
            stepDeadline = proposedStep;
            deadline = proposedEnd;
        } else {
            stepDeadline = Math.Min(stepDeadline, proposedStep);
            deadline = Math.Min(deadline, proposedEnd);
        }
        stepRemaining = closed ? 0 : Math.Max(0, stepDeadline - now);
        bankRemaining = closed ? 0 : Math.Max(0, deadline - Math.Max(now, stepDeadline));
    }

    public static int Seconds(double value) => (int)Math.Ceiling(Math.Max(0, value - 1e-9));
}
