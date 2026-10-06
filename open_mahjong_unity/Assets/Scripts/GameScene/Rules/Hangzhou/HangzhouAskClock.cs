using System;

/// <summary>杭州询问显示时钟：扣除本机收件后的排队时间，同一询问补发不能延长截止点。</summary>
public sealed class HangzhouAskClock {
    private bool hasTick;
    private int tick;
    private double deadline;
    private double stepDeadline;
    private string gameId;
    private int handNumber = -1;
    private int seat = -1;
    private int closedTick = -1;

    public static double Now => System.Diagnostics.Stopwatch.GetTimestamp()
        / (double)System.Diagnostics.Stopwatch.Frequency;

    public void Reset() { hasTick = false; gameId = null; handNumber = seat = closedTick = -1; }
    public void Close() { if (hasTick) closedTick = tick; }
    public bool IsClosed(int actionTick) => actionTick == closedTick || (hasTick && actionTick < tick);

    public void BindRound(string matchId, int hand, int viewSeat) {
        if (gameId != matchId || handNumber != hand || seat != viewSeat) {
            hasTick = false;
            closedTick = -1;
            gameId = matchId;
            handNumber = hand;
            seat = viewSeat;
        }
    }

    public void Project(int actionTick, int bank, int step, double receivedAt, double now,
                        out int bankRemaining, out int stepRemaining) {
        double proposedStep = receivedAt + Math.Max(0, step);
        double proposedEnd = proposedStep + Math.Max(0, bank);
        if (!hasTick || actionTick != tick) {
            hasTick = true;
            tick = actionTick;
            stepDeadline = proposedStep;
            deadline = proposedEnd;
        } else {
            stepDeadline = Math.Min(stepDeadline, proposedStep);
            deadline = Math.Min(deadline, proposedEnd);
        }
        int total = Seconds(deadline - now);
        stepRemaining = Math.Min(total, Seconds(stepDeadline - now));
        bankRemaining = total - stepRemaining;
    }

    private static int Seconds(double value) => (int)Math.Ceiling(Math.Max(0, value - 1e-9));
}
