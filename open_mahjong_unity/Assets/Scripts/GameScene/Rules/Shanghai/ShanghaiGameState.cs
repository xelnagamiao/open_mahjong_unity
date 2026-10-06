/// <summary>敲牌标签驱动锁手；出牌、补花与结算沿用回合制表现。</summary>
public sealed class ShanghaiGameState : TurnBasedGameState {
    private readonly ShanghaiAskClock askClock = new ShanghaiAskClock();
    private readonly System.Collections.Generic.HashSet<int> announcedReady = new System.Collections.Generic.HashSet<int>();

    public override bool IsSelfLocked => SeatHasTag("self", tag => tag == "declared_ready");

    protected override void OnRoundStarted(GameInfo info) {
        announcedReady.Clear();
        // 开局/重连快照仅恢复已有报听，后续新增的公开声明才发声。
        foreach (var pair in Mirror.IndexToPosition)
            if (SeatHasTag(pair.Value, tag => tag == "declared_ready")) announcedReady.Add(pair.Key);
        askClock.Bind(info.gamestate_id, info.current_round, Session.SelfIndex);
        base.OnRoundStarted(info);
    }

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (!IsActive) return;
        foreach (var pair in Mirror.IndexToPosition) {
            if (SeatHasTag(pair.Value, tag => tag == "declared_ready") && announcedReady.Add(pair.Key))
                SoundManager.Instance?.PlayActionSound(pair.Value, "riichi");
        }
    }

    protected override void OnAskHandAction(Response response) {
        var info = response.ask_hand_action_info;
        if (info == null) return;
        int bank = info.remaining_time;
        int? step = info.step_remaining;
        double exactBank = info.remaining_time_ms.HasValue ? info.remaining_time_ms.Value / 1000d : bank;
        double exactStep = info.step_remaining_ms.HasValue ? info.step_remaining_ms.Value / 1000d : step ?? Session.RoomStepTime;
        double received = response.received_monotonic ?? ShanghaiAskClock.Now;
        askClock.ProjectExact(info.action_tick, exactBank, exactStep, received, ShanghaiAskClock.Now,
            out double projectedBank, out double projectedStep);
        info.remaining_time = (int)System.Math.Ceiling(projectedBank);
        info.step_remaining = (int)System.Math.Ceiling(projectedStep);
        try {
            base.OnAskHandAction(response);
            RestoreExactCountdown(info.action_tick, exactBank, exactStep, received);
        }
        finally { info.remaining_time = bank; info.step_remaining = step; }
    }

    protected override void OnAskClaim(Response response) {
        var info = response.ask_other_action_info;
        if (info == null) return;
        int bank = info.remaining_time;
        int? step = info.step_remaining;
        double exactBank = info.remaining_time_ms.HasValue ? info.remaining_time_ms.Value / 1000d : bank;
        double exactStep = info.is_tactical_recheck == true ? 0
            : info.step_remaining_ms.HasValue ? info.step_remaining_ms.Value / 1000d : step ?? Session.RoomStepTime;
        double received = response.received_monotonic ?? ShanghaiAskClock.Now;
        askClock.ProjectExact(info.action_tick, exactBank, exactStep, received, ShanghaiAskClock.Now,
            out double projectedBank, out double projectedStep);
        info.remaining_time = (int)System.Math.Ceiling(projectedBank);
        info.step_remaining = (int)System.Math.Ceiling(projectedStep);
        try {
            base.OnAskClaim(response);
            RestoreExactCountdown(info.action_tick, exactBank, exactStep, received);
        }
        finally { info.remaining_time = bank; info.step_remaining = step; }
    }

    private void RestoreExactCountdown(int tick, double bank, double step, double received) {
        if (GameCanvas.Instance == null || !GameCanvas.Instance.IsCountdownRunning) return;
        askClock.ProjectExact(tick, bank, step, received, ShanghaiAskClock.Now,
            out double bankRemaining, out double stepRemaining);
        GameCanvas.Instance.CorrectCountdownBudget(bankRemaining, stepRemaining);
    }

    public override void OnAskWindowClosed(AskCloseReason reason) {
        RiichiCutSelectionController.Instance?.ExitRiichiCutMode();
    }

    public override void OnSessionReset() {
        announcedReady.Clear();
        askClock.Reset();
        base.OnSessionReset();
        RiichiCutSelectionController.Instance?.ExitRiichiCutMode();
    }
}
