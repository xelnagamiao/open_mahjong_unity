using System.Collections.Generic;

/// <summary>广东花鬼沿用回合制状态机，合法行动、提示和支付均由服务端裁定。</summary>
public sealed class GuangdongMilGameState : TurnBasedGameState {
    private readonly GuangdongAskClock askClock = new GuangdongAskClock();
    private Dictionary<int, int[]> revealedHands;
    public GuangdongPublicState Info { get; private set; }
    public GuangdongResult Result { get; private set; }
    private void Accept(GuangdongPublicState state, GuangdongTips tips) {
        if (state != null) Info = state;
        if (tips != null) GuangdongServerTips.Accept(tips);
        GuangdongLedgerPanel.Show(Info, Result);
    }
    protected override void OnGameStartMessage(Response response) {
        GuangdongServerTips.Clear();
        if (response.game_info != null) GuangdongServerTips.Accept(response.game_info.guangdong_tips);
        base.OnGameStartMessage(response);
    }
    protected override void OnRoundStarted(GameInfo info) {
        askClock.EnterRound(info?.gamestate_id ?? Session.GamestateId, info?.current_round ?? Mirror.CurrentRound,
            Session.IsRealtimeSpectator ? Session.SelfIndex : (int?)null);
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info = null; Result = null; revealedHands = null;
        Accept(info?.guangdong_state, info?.guangdong_tips);
    }
    protected override void OnAskHandAction(Response response) {
        var info = response.ask_hand_action_info;
        bool ownAsk = info != null && info.action_list != null && info.action_list.Length > 0
            && info.player_index == Session.SelfIndex;
        if (ownAsk && askClock.IsClosed(info.action_tick)) return;
        Accept(response.game_info?.guangdong_state, info?.guangdong_tips ?? response.game_info?.guangdong_tips);
        if (!ownAsk) { base.OnAskHandAction(response); return; }
        int bank = info.remaining_time;
        int? step = info.step_remaining;
        askClock.ApplyHand(response, Session.RoomStepTime);
        try {
            base.OnAskHandAction(response);
            if (Clock.IsSelfActionRequired && !AutoActionPolicy.Current.TryResolveHand(info.deal_tile_type, out _, out _))
                ApplyExactTimer();
        } finally { info.remaining_time = bank; info.step_remaining = step; }
    }
    protected override void OnAskClaim(Response response) {
        var info = response.ask_other_action_info;
        if (info?.action_list == null || info.action_list.Length == 0 || askClock.IsClosed(info.action_tick)) return;
        int bank = info.remaining_time;
        int? step = info.step_remaining;
        askClock.ApplyClaim(response, Session.RoomStepTime);
        try {
            base.OnAskClaim(response);
            if (Clock.IsSelfActionRequired && !AutoActionPolicy.Current.TryResolveClaim(out _, out _))
                ApplyExactTimer();
        } finally { info.remaining_time = bank; info.step_remaining = step; }
    }
    private void ApplyExactTimer() {
        askClock.Remaining(GuangdongAskClock.Now, out double bank, out double step);
        GameCanvas.Instance?.LoadingRemianTime(bank, step);
    }
    public override void OnAskWindowClosed(AskCloseReason reason) {
        askClock.Close();
        base.OnAskWindowClosed(reason);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.do_action_info?.guangdong_state ?? response.game_info?.guangdong_state, response.do_action_info?.guangdong_tips ?? response.game_info?.guangdong_tips);
        base.OnDoAction(response);
    }
    protected override void OnActionPlayed(TableAction action) {
        if (action.Silent || !GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) return;
        Presenter.ApplyScoreDeltas(action.GangScoreChanges);
        GameCanvas.Instance?.ShowGangScoreFloats(action.GangScoreChanges);
    }
    protected override void OnShowResult(Response response) {
        revealedHands = response.show_result_info?.revealed_hands;
        Result = response.show_result_info?.guangdong_result;
        GuangdongLedgerPanel.Show(Info, Result);
        base.OnShowResult(response);
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        if (revealedHands == null) return;
        var others = new Dictionary<int, int[]>(revealedHands);
        if (env.IsHu) others.Remove(env.WinnerIndex);
        Game3DManager.Instance?.RevealSichuanLiujuAllHands(others);
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        var totals = GuangdongMilRules.RoundChanges(Result, env.ScoreChanges);
        if (totals == null) { base.AppendScoreboard(env); return; }
        string winnerName = Mirror.IndexToPosition.TryGetValue(env.WinnerIndex, out string winnerPosition)
            ? Mirror.Info(winnerPosition)?.username ?? "" : "";
        var byOriginal = new Dictionary<int,int>();
        foreach (var pair in Mirror.IndexToPosition) {
            var player = Mirror.Info(pair.Value);
            if (player != null && totals.TryGetValue(pair.Key, out int delta)) byOriginal[player.original_player_index] = delta;
        }
        var snapshot = ScoreHistorySettlementHelper.CreateFromShowResult(Session.SubRule,
            env.HuClass, env.WinnerIndex, winnerName, env.HuScore, env.FanLabels,
            env.WinnerHand, env.WinnerMelds, env.BaseFu, env.FuFanList, null, byOriginal);
        int snapshotRow = -1;
        foreach (var pair in Mirror.IndexToPosition) {
            var player = Mirror.Info(pair.Value);
            if (player == null) continue;
            player.score_history ??= new List<string>();
            player.round_number_history ??= new List<int>();
            ScoreHistorySettlementHelper.AlignRoundNumberHistory(player.score_history, player.round_number_history);
            int row = player.round_number_history.IndexOf(Mirror.CurrentRound);
            totals.TryGetValue(pair.Key, out int delta);
            string value = delta > 0 ? "+" + delta : delta.ToString();
            // A reconnect snapshot may already include this round. Replace its row, never append it twice.
            if (row >= 0 && row < player.score_history.Count) player.score_history[row] = value;
            else { row = player.score_history.Count; player.score_history.Add(value); player.round_number_history.Add(Mirror.CurrentRound); }
            snapshotRow = row;
        }
        if (snapshotRow >= 0) {
            while (Mirror.RoundSettlementHistory.Count <= snapshotRow) Mirror.RoundSettlementHistory.Add(null);
            Mirror.RoundSettlementHistory[snapshotRow] = snapshot;
        }
        // Kong deltas were applied at the actual action. Only restore the authoritative absolute total here.
        Presenter.ApplyScores(env.ScoresAfter);
        GameSceneUIManager.Instance?.UpdateScoreRecord();
    }
    protected override void OnBeforeHuPresented(SettlementEnvelope env) {
        if (!env.IsQianggang || !env.RonDiscarderIndex.HasValue || !Mirror.IndexToPosition.TryGetValue(env.RonDiscarderIndex.Value, out string source)) return;
        var player = Mirror.Info(source);
        if (player?.combination_tiles == null || player.combination_masks == null) return;
        int index = player.combination_tiles.IndexOf($"g{env.WinTile}");
        if (index < 0 || index >= player.combination_masks.Count || player.combination_masks[index] == null) return;
        var mask = new List<int>(player.combination_masks[index]);
        for (int i = 0; i + 1 < mask.Count; i += 2) {
            if (mask[i] != 3 || mask[i + 1] != env.WinTile) continue;
            mask.RemoveRange(i, 2); TableMirror.ReplaceMeldMaskAt(player, index, mask.ToArray());
            player.combination_tiles[index] = $"k{env.WinTile}"; break;
        }
    }
    protected override void OnSettlementPresented(SettlementEnvelope env, ShowResultInfo info) {
        if (env.IsLiuju) Presenter.ApplyScores(env.ScoresAfter);
    }
    public override void OnSessionReset() {
        askClock.Reset();
        // Cancel a still-fading draw result before its panel is cleared for the next table.
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info = null; Result = null; revealedHands = null; GuangdongServerTips.Clear(); GuangdongServerTips.Clear(true); GuangdongLedgerPanel.Hide();
    }
}
