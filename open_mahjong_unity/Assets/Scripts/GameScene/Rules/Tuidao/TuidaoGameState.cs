/// <summary>MIL 推倒和：公开报听锁手，杠分由服务端独立记账。</summary>
public sealed class TuidaoGameState : TurnBasedGameState {
    private readonly TuidaoAskClock askClock = new TuidaoAskClock();
    private int clockRoundNumber = -1;
    private string clockGameId;
    private System.Collections.Generic.Dictionary<int, int[]> revealedHands;
    public override bool IsSelfLocked => SeatHasTag("self", tag => tag == "declared_ready");

    private readonly System.Collections.Generic.HashSet<int> announcedReady = new System.Collections.Generic.HashSet<int>();

    protected override void OnRoundStarted(GameInfo gameInfo) {
        int nextRound = gameInfo?.current_round ?? 0;
        string nextGame = gameInfo?.gamestate_id ?? Session.GamestateId;
        if (nextRound != clockRoundNumber || nextGame != clockGameId) {
            askClock.Reset();
            clockRoundNumber = nextRound;
            clockGameId = nextGame;
        }
        announcedReady.Clear();
        // 重连快照已有的声明只恢复标记；不重播报声。
        foreach (var pair in Mirror.IndexToPosition)
            if (SeatHasTag(pair.Value, tag => tag == "declared_ready")) announcedReady.Add(pair.Key);
    }


    protected override void OnAskHandAction(Response response) {
        var info = response.ask_hand_action_info;
        if (info == null || askClock.ShouldIgnore(info.action_tick)) return;
        if (info.player_index != Session.SelfIndex) { base.OnAskHandAction(response); return; }
        int bank = info.remaining_time;
        int? step = info.step_remaining;
        double exactBankMs = info.remaining_time_ms ?? System.Math.Max(0, bank) * 1000d;
        double exactStepMs = info.step_remaining_ms ?? System.Math.Max(0, step ?? Session.RoomStepTime) * 1000d;
        askClock.ProjectExact(info.action_tick,
            exactBankMs + exactStepMs, exactStepMs,
            response.received_monotonic ?? TuidaoAskClock.Now, TuidaoAskClock.Now,
            out double exactBank, out double exactStep);
        info.remaining_time = (int)System.Math.Ceiling(System.Math.Max(0, exactBank - 1e-9));
        int projectedStep = (int)System.Math.Ceiling(System.Math.Max(0, exactStep - 1e-9));
        info.step_remaining = projectedStep;
        try {
            if (info.remaining_time == 0 && projectedStep == 0) { askClock.Close(); GameCanvas.Instance?.StopTimeRunning(); Clock.Timeout(); return; }
            base.OnAskHandAction(response);
            RebaseAskClock();
        } finally { info.remaining_time = bank; info.step_remaining = step; }
    }

    protected override void OnAskClaim(Response response) {
        var info = response.ask_other_action_info;
        if (info == null || askClock.ShouldIgnore(info.action_tick)) return;
        int bank = info.remaining_time;
        int? step = info.step_remaining;
        int effectiveStep = info.is_tactical_recheck == true ? 0 : step ?? Session.RoomStepTime;
        double exactBankMs = info.remaining_time_ms ?? System.Math.Max(0, bank) * 1000d;
        double exactStepMs = info.is_tactical_recheck == true ? 0
            : info.step_remaining_ms ?? System.Math.Max(0, effectiveStep) * 1000d;
        askClock.ProjectExact(info.action_tick,
            exactBankMs + exactStepMs, exactStepMs,
            response.received_monotonic ?? TuidaoAskClock.Now, TuidaoAskClock.Now,
            out double exactBank, out double exactStep);
        info.remaining_time = (int)System.Math.Ceiling(System.Math.Max(0, exactBank - 1e-9));
        int projectedStep = (int)System.Math.Ceiling(System.Math.Max(0, exactStep - 1e-9));
        info.step_remaining = projectedStep;
        try {
            if (info.remaining_time == 0 && projectedStep == 0) { askClock.Close(); GameCanvas.Instance?.StopTimeRunning(); Clock.Timeout(); return; }
            base.OnAskClaim(response);
            RebaseAskClock();
        } finally { info.remaining_time = bank; info.step_remaining = step; }
    }

    private void RebaseAskClock() {
        if (!Clock.IsSelfActionRequired) return;
        askClock.RemainingSeconds(TuidaoAskClock.Now, out double bank, out double step);
        GameCanvas.Instance?.RebaseDecisionClock(bank, step);
    }

    public override void OnAskWindowClosed(AskCloseReason reason) {
        askClock.Close();
        RiichiCutSelectionController.Instance?.ExitRiichiCutMode();
        base.OnAskWindowClosed(reason);
    }

    public override void OnSessionReset() {
        askClock.Reset();
        clockRoundNumber = -1;
        clockGameId = null;
        base.OnSessionReset();
        RiichiCutSelectionController.Instance?.ExitRiichiCutMode();
    }

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (!IsActive) return;
        foreach (var pair in Mirror.IndexToPosition) {
            if (SeatHasTag(pair.Value, tag => tag == "declared_ready") && announcedReady.Add(pair.Key))
                SoundManager.Instance?.PlayActionSound(pair.Value, "riichi");
        }
    }

    protected override void OnShowResult(Response response) {
        revealedHands = response.show_result_info?.revealed_hands;
        base.OnShowResult(response);
    }

    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        if (revealedHands == null) return;
        var others = new System.Collections.Generic.Dictionary<int, int[]>(revealedHands);
        // 和牌者沿用公共取牌/倒牌演出，其余玩家同时明手；流局全员明手。
        if (env.IsHu) others.Remove(env.WinnerIndex);
        // 复用现有多家明手渲染器，本方法不包含四川规则的计分逻辑。
        Game3DManager.Instance?.RevealSichuanLiujuAllHands(others);
    }

    protected override void OnBeforeHuPresented(SettlementEnvelope env) {
        if (!env.IsQianggang || !env.RonDiscarderIndex.HasValue
            || !Mirror.IndexToPosition.TryGetValue(env.RonDiscarderIndex.Value, out string source)) return;
        var player = Mirror.Info(source);
        if (player?.combination_tiles == null || player.combination_masks == null) return;
        // 公共倒牌演出会回收加杠第四张；同步数据镜像，防止随后重建副露又显示四张。
        int index = player.combination_tiles.IndexOf($"g{env.WinTile}");
        if (index < 0 || index >= player.combination_masks.Count || player.combination_masks[index] == null) return;
        var mask = new System.Collections.Generic.List<int>(player.combination_masks[index]);
        for (int i = 0; i + 1 < mask.Count; i += 2) {
            if (mask[i] != 3 || mask[i + 1] != env.WinTile) continue;
            mask.RemoveRange(i, 2);
            TableMirror.ReplaceMeldMaskAt(player, index, mask.ToArray());
            player.combination_tiles[index] = $"k{env.WinTile}";
            break;
        }
    }

    protected override void OnActionPlayed(TableAction action) {
        // 加杠只在无人抢杠后支付；其计分帧没有动作词，不能依赖 gang 词触发。
        if (action.Silent || !GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) return;
        Presenter.ApplyScoreDeltas(action.GangScoreChanges);
        GameCanvas.Instance?.ShowGangScoreFloats(action.GangScoreChanges);
    }
}
