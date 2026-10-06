using System;

public static class ShanghaiTimingValidation {
    public static object RunAll() {
        int checks = 0;
        var clock = new ShanghaiAskClock();
        Check(clock, 1, 20, 5, 100, 100, 20, 5, ref checks);
        Check(clock, 1, 20, 5, 100, 103, 20, 2, ref checks);
        Check(clock, 1, 20, 5, 105, 108, 17, 0, ref checks); // duplicate cannot restart
        Check(clock, 1, 17, 0, 108, 108, 17, 0, ref checks); // reconnect projection
        Check(clock, 1, 17, 0, 108, 125, 0, 0, ref checks);
        Check(clock, 2, 17, 5, 125, 125, 17, 5, ref checks); // new discard step
        Check(clock, 3, 0, 7, 200, 206, 0, 1, ref checks);
        Check(clock, 3, 0, 7, 205, 207, 0, 0, ref checks);
        Check(clock, 4, 13, 8, 300, 310, 11, 0, ref checks);
        Check(clock, 4, 11, 0, 310, 311.1, 10, 0, ref checks);
        clock.Reset();
        Check(clock, 4, 20, 5, 400, 400, 20, 5, ref checks);
        Check(clock, 5, 0, 0, 500, 500, 0, 0, ref checks);
        clock.Reset();
        clock.ProjectExact(10, 19.1, 5, 100, 104.5, out double exactBank, out double exactStep);
        if (Math.Abs(exactBank - 19.1) > 1e-6 || Math.Abs(exactStep - 0.5) > 1e-6)
            throw new InvalidOperationException("Fractional budget lost precision");
        checks += 2;
        clock.ProjectExact(10, 20, 5, 104.5, 105.9, out exactBank, out exactStep);
        if (Math.Abs(exactBank - 18.2) > 1e-6 || exactStep != 0)
            throw new InvalidOperationException("Duplicate fractional window extended");
        checks += 2;
        clock.ProjectExact(10, 19, 0, 105.9, 124.1, out exactBank, out exactStep);
        if (Math.Abs(exactBank) > 1e-6 || exactStep != 0)
            throw new InvalidOperationException("Fractional window did not expire");
        checks += 2;
        clock.Bind("match-a", 1, 0);
        Check(clock, 11, 20, 5, 200, 200, 20, 5, ref checks);
        clock.Bind("match-a", 1, 0); // reconnect game_start preserves the original deadline
        Check(clock, 11, 20, 5, 208, 208, 17, 0, ref checks);
        clock.Bind("match-a", 2, 0); // next actual hand resets the display window
        Check(clock, 11, 20, 5, 208, 208, 20, 5, ref checks);
        clock.Bind("match-a", 2, 1); // spectator changing host seat has its own clock
        Check(clock, 11, 13, 8, 210, 210, 13, 8, ref checks);
        clock.Bind("match-b", 2, 1); // a new match may reuse the same action tick
        Check(clock, 11, 20, 5, 212, 212, 20, 5, ref checks);
        return new { passed = true, checks };
    }

    private static void Check(ShanghaiAskClock clock, int tick, int bank, int step,
        double receipt, double now, int expectedBank, int expectedStep, ref int checks) {
        clock.Project(tick, bank, step, receipt, now, out int actualBank, out int actualStep);
        if (actualBank != expectedBank || actualStep != expectedStep)
            throw new InvalidOperationException($"tick={tick}: {actualBank}+{actualStep}, expected {expectedBank}+{expectedStep}");
        checks += 2;
    }
}
