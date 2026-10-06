using System;

/// <summary>敲麻显示窗口：从本机收件时刻起算，同一 tick 的补发不能延长截止点。</summary>
public sealed class ShanghaiAskClock {
    private int tick = -1;
    private double deadline;
    private double stepDeadline;

    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    private string match;
    private int round;
    private int seat;
    private bool bound;

    public void Bind(string matchId, int roundId, int viewSeat) {
        if (bound && match == matchId && round == roundId && seat == viewSeat) return;
        Reset();
        match = matchId;
        round = roundId;
        seat = viewSeat;
        bound = true;
    }

    public void Reset() { tick = -1; bound = false; }

    public void Project(int actionTick, int bank, int step, double receivedAt, double now,
                        out int bankRemaining, out int stepRemaining) {
        ProjectExact(actionTick, bank, step, receivedAt, now, out double bankExact, out double stepExact);
        bankRemaining = Seconds(bankExact);
        stepRemaining = Seconds(stepExact);
    }

    public void ProjectExact(int actionTick, double bank, double step, double receivedAt, double now,
                             out double bankRemaining, out double stepRemaining) {
        double proposedStep = receivedAt + Math.Max(0, step);
        double proposedEnd = proposedStep + Math.Max(0, bank);
        if (actionTick != tick) {
            tick = actionTick;
            stepDeadline = proposedStep;
            deadline = proposedEnd;
        } else {
            stepDeadline = Math.Min(stepDeadline, proposedStep);
            deadline = Math.Min(deadline, proposedEnd);
        }
        stepRemaining = Math.Max(0, stepDeadline - now);
        bankRemaining = Math.Max(0, deadline - Math.Max(now, stepDeadline));
    }

    private static int Seconds(double value) => (int)Math.Ceiling(Math.Max(0, value - 1e-9));
}
