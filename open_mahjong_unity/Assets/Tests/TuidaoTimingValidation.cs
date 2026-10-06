using System;

/// <summary>推倒和计时回归；可在 Unity Editor 中调用 RunAll，不改变活动场景。</summary>
public static class TuidaoTimingValidation {
    public static object RunAll() {
        int checks = 0;
        void Expect(TuidaoAskClock clock, int tick, int bank, int step,
                    double received, double now, int expectedBank, int expectedStep) {
            clock.Project(tick, bank, step, received, now, out int actualBank, out int actualStep);
            if (actualBank != expectedBank || actualStep != expectedStep)
                throw new Exception($"推倒和时钟 {actualBank}+{actualStep} != {expectedBank}+{expectedStep}");
            checks++;
        }
        var cases = new (int bank, int step, double elapsed, int expectedBank, int expectedStep)[] {
            (20,5,0,20,5), (20,5,3,20,2), (20,5,8,17,0), (20,5,25,0,0),
            (42,11,8,42,3), (42,11,14,39,0), (0,9,7,0,2), (0,5,4,0,1),
            (7,0,3,4,0), (0,0,0,0,0), (20,5,3.2,20,2), (20,5,5.4,20,0),
            (1,5,5.1,1,0), (20,-1,2,18,0),
        };
        foreach (var item in cases)
            Expect(new TuidaoAskClock(), 1, item.bank, item.step, 100,
                   100 + item.elapsed, item.expectedBank, item.expectedStep);
        var repeated = new TuidaoAskClock();
        Expect(repeated,1,20,5,100,100,20,5);
        Expect(repeated,1,20,5,108,108,17,0);
        Expect(repeated,1,17,0,108,108,17,0);
        Expect(repeated,2,17,5,109,109,17,5);
        Expect(repeated,1,20,5,110,110,0,0);
        if (!repeated.ShouldIgnore(1) || repeated.ShouldIgnore(2)) throw new Exception("旧询问不能关闭新窗口");
        checks++;
        repeated.Close();
        Expect(repeated,2,17,5,111,111,0,0);
        if (!repeated.ShouldIgnore(2)) throw new Exception("已操作窗口不能因补发重开");
        checks++;
        Expect(repeated,3,17,5,112,112,17,5);
        repeated.Reset();
        Expect(repeated,1,42,11,200,200,42,11);
        if (repeated.ShouldIgnore(1)) throw new Exception("新局不能沿用上局询问锁");
        checks++;
        return new { passed = true, checks };
    }
}
