/// <summary>山西公开报听锁手；合法杠、强制和牌由服务端决定。</summary>
public sealed class ShanxiGameState : TurnBasedGameState {
    private readonly ShanxiAskClock askClock = new ShanxiAskClock();

    public override bool IsSelfLocked => SeatHasTag("self", tag => tag == "declared_ready");
    protected override void OnRoundStarted(GameInfo gameInfo) {
        askClock.StartHand(gameInfo.gamestate_id, gameInfo.current_round, gameInfo.action_tick);
        var known = Manager.player_to_info["self"].known_concealed_discards;
        known.Clear();
        foreach (var player in gameInfo.players_info)
            if (player.known_concealed_discards != null) known.AddRange(player.known_concealed_discards);
    }

    protected override void OnAskHandAction(Response response) {
        var info = response.ask_hand_action_info;
        if (info == null) return;
        // Other seats' hand broadcasts update the shared table indicator only.
        if (info.player_index != Session.SelfIndex || info.action_list == null || info.action_list.Length == 0) {
            askClock.ObserveOtherHand(info.action_tick);
            base.OnAskHandAction(response);
            return;
        }
        if (!askClock.CanPresent(info.action_tick)) return;
        AnchorAsk(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime);
        base.OnAskHandAction(response);
        ProjectAsk(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime,
            AutoActionPolicy.Current.TryResolveHand(info.deal_tile_type, out _, out _));
    }

    protected override void OnAskClaim(Response response) {
        var info = response.ask_other_action_info;
        if (info == null || !askClock.CanPresent(info.action_tick)) return;
        AnchorAsk(response, info.action_tick, info.remaining_time,
            info.is_tactical_recheck == true ? 0 : info.step_remaining ?? Session.RoomStepTime);
        base.OnAskClaim(response);
        ProjectAsk(response, info.action_tick, info.remaining_time,
            info.is_tactical_recheck == true ? 0 : info.step_remaining ?? Session.RoomStepTime,
            AutoActionPolicy.Current.TryResolveClaim(out _, out _));
    }

    private void AnchorAsk(Response response, int tick, int bank, int step) {
        // The base path can time out a zero-budget ask immediately. Anchor
        // its tick before that callback so presentation cannot reopen it.
        double now = ShanxiAskClock.Now;
        askClock.Project(tick, bank, step, response.received_monotonic ?? now, now, out _, out _);
    }

    private void ProjectAsk(Response response, int tick, int bank, int step, bool automatic) {
        double now = ShanxiAskClock.Now;
        askClock.Project(tick, bank, step, response.received_monotonic ?? now, now,
            out double bankSeconds, out double stepSeconds);
        if (automatic || !Clock.IsSelfActionRequired) return;
        if (bankSeconds + stepSeconds <= 0) {
            GameCanvas.Instance.StopTimeRunning();
            Clock.Timeout();
            return;
        }
        if (Clock.AllowActionList.Count > 0)
            GameCanvas.Instance.LoadingRemianTime(bankSeconds, stepSeconds);
    }

    public override void OnAskWindowClosed(AskCloseReason reason) {
        if (reason == AskCloseReason.Acted || reason == AskCloseReason.TimedOut) askClock.Close();
        base.OnAskWindowClosed(reason);
    }

    public override void OnSessionReset() {
        askClock.Reset();
        base.OnSessionReset();
    }

    protected override void OnActionPlayed(TableAction action) {
        if (action.ConcealedDiscard && action.HasWord("cut")) {
            if (action.Seat == "self" && action.CutTile.HasValue)
                Manager.player_to_info["self"].known_concealed_discards.Add(action.CutTile.Value);
            if (!action.Silent) {
                GameCanvas.Instance.ShowActionDisplay(action.Seat, "riichi", "shanxi");
                SoundManager.Instance.PlayActionSound(action.Seat, "riichi");
            }
        }
    }
}
