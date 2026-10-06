using System;

/// <summary>山西询问的本机显示截止点；消息排队和同 tick 补发不增加操作时间。</summary>
public sealed class ShanxiAskClock {
    private int tick = -1;
    private double deadline, stepDeadline;
    private bool closed;
    private string gameId;
    private int round = -1;

    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    public void Reset() { tick = -1; closed = false; gameId = null; round = -1; }
    public void StartHand(string id, int currentRound, int actionTick) {
        // Fresh hand broadcasts reset the server tick to zero, including a
        // repeated dealer hand. Reconnection keeps the current nonzero tick.
        if (gameId != id || round != currentRound || actionTick == 0) {
            tick = -1;
            closed = false;
        }
        gameId = id;
        round = currentRound;
    }
    public void Close() { closed = true; }
    public bool CanPresent(int actionTick) => actionTick > tick || (actionTick == tick && !closed);
    public void ObserveOtherHand(int actionTick) {
        if (actionTick > tick) { tick = actionTick; closed = true; }
    }

    public void Project(int actionTick, int bank, int step, double receivedAt, double now,
                        out double bankSeconds, out double stepSeconds) {
        double nextStep = receivedAt + Math.Max(0, step);
        double nextEnd = nextStep + Math.Max(0, bank);
        if (actionTick != tick) {
            tick = actionTick;
            stepDeadline = nextStep;
            deadline = nextEnd;
            closed = false;
        } else {
            stepDeadline = Math.Min(stepDeadline, nextStep);
            deadline = Math.Min(deadline, nextEnd);
        }
        stepSeconds = closed ? 0 : Math.Max(0, stepDeadline - now);
        bankSeconds = closed ? 0 : Math.Max(0, deadline - Math.Max(now, stepDeadline));
    }
}
