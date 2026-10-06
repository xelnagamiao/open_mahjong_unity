using System;

/// <summary>推倒和显示窗口：从收件时刻投影；同一询问补发不延长截止点。</summary>
public sealed class TuidaoAskClock {
    private int tick = -1;
    private double deadline;
    private double stepDeadline;
    private bool closed;

    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    public void Reset() { tick = -1; closed = false; }
    public void Close() { closed = true; }
    public bool ShouldIgnore(int actionTick) => actionTick < tick || actionTick == tick && closed;

    public void Project(int actionTick, int bank, int step, double receivedAt, double now,
                        out int bankRemaining, out int stepRemaining) {
        ProjectExact(actionTick, (Math.Max(0, bank) + (double)Math.Max(0, step)) * 1000,
            Math.Max(0, step) * 1000d, receivedAt, now, out double exactBank, out double exactStep);
        bankRemaining = Seconds(exactBank);
        stepRemaining = Seconds(exactStep);
    }

    public void ProjectExact(int actionTick, double budgetMs, double stepMs, double receivedAt, double now,
                             out double bankRemaining, out double stepRemaining) {
        if (actionTick < tick || actionTick == tick && closed) {
            bankRemaining = stepRemaining = 0;
            return;
        }
        double budget = Math.Max(0, budgetMs) / 1000;
        double step = Math.Min(budget, Math.Max(0, stepMs) / 1000);
        double proposedStep = receivedAt + step;
        double proposedEnd = receivedAt + budget;
        if (actionTick != tick) {
            tick = actionTick;
            closed = false;
            stepDeadline = proposedStep;
            deadline = proposedEnd;
        } else {
            stepDeadline = Math.Min(stepDeadline, proposedStep);
            deadline = Math.Min(deadline, proposedEnd);
        }
        RemainingSeconds(now, out bankRemaining, out stepRemaining);
    }

    public void RemainingSeconds(double now, out double bankRemaining, out double stepRemaining) {
        if (closed || tick < 0) { bankRemaining = stepRemaining = 0; return; }
        stepRemaining = Math.Max(0, stepDeadline - now);
        bankRemaining = Math.Max(0, deadline - Math.Max(now, stepDeadline));
    }

    private static int Seconds(double value) => (int)Math.Ceiling(Math.Max(0, value - 1e-9));
}
