using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>复用回合制客户端骨架，只投影贵州的公开报听、开局亮杠与延迟结算。</summary>
public sealed class GuizhouGameState : TurnBasedGameState {
    public const string RuleId = "guizhou";
    public const string SubRule = "guizhou/standard";
    public const string RuleVersion = "mil-guizhou-2023-om1";
    public static GuizhouGameState Active => RuleRegistry.ActiveGameState as GuizhouGameState;
    public GuizhouInfo Info { get; private set; }
    private string legacyReadyQualification;
    private bool legacyReadyPending;
    public string SelfReadyQualification => (Info?.self_ready_pending ?? legacyReadyPending) ? "soft_ready"
        : Info?.self_ready_qualification ?? legacyReadyQualification;
    private Dictionary<int, int[]> endHands;
    private readonly GuizhouAskClock askClock = new GuizhouAskClock();
    private int clockHandNumber;
    public override bool IsSelfLocked => SeatHasTag("self", tag => tag == "declared_ready");

    private void Accept(GameInfo info) {
        if (info == null) return;
        Info = info.guizhou_info ?? Info;
        if (info.players_info != null && IsActive)
            RefreshTags(info.players_info.ToDictionary(p => p.player_index, p => p.tag_list ?? Array.Empty<string>()));
        GuizhouLedgerPanel.Show(Info);
    }

    protected override void OnRoundStarted(GameInfo info) {
        int nextHand = info?.guizhou_info?.hand_number ?? 0;
        if (nextHand != clockHandNumber) { askClock.Reset(); clockHandNumber = nextHand; }
        Info = null; endHands = null; legacyReadyQualification = null; legacyReadyPending = false; Accept(info);
        // game_start (including reconnect) builds all tiles as held tiles.
        // Restore the authoritative draw slot before accepting a tile click.
        if (Info?.self_has_draw_slot == true && Mirror.SelfHandTiles.Count > 0) {
            var tiles = Mirror.SelfHandTiles.ToArray();
            GameCanvas.Instance.ChangeHandCards("SyncHandCards", 0, tiles.Take(tiles.Length - 1).ToArray(), null);
            GameCanvas.Instance.ChangeHandCards("GetCardNoAnimation", tiles[tiles.Length - 1], null, null);
        }
        // Restore player and spectator hints after viewer-local ready state, even without a subsequent ask.
        if (Session.Tips && Info?.phase != "END" && Info?.phase != "waiting_ready" && Info?.phase != "finished") {
            var hand = new List<int>(Mirror.SelfHandTiles);
            if (Info?.self_has_draw_slot == true && hand.Count % 3 == 2) hand.RemoveAt(hand.Count - 1);
            TipsBlock.Instance?.ShowTipsBlock(hand, Mirror.Info("self")?.combination_tiles ?? new List<string>());
        } else TipsBlock.Instance?.HideTipsBlock();
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);
        var info = response.ask_hand_action_info;
        if (info == null || (info.player_index == Session.SelfIndex && askClock.IsClosed(info.action_tick))) return;
        if (info.player_index == Session.SelfIndex && (info.action_list == null || info.action_list.Length == 0)) {
            Clock.Clear("guizhouNoDecision");
            return;
        }
        base.OnAskHandAction(response);
        PresentAskClock(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime);
    }
    protected override void OnHandAskReceived(AskHandActionGBInfo info) {
        if (info.player_index != Session.SelfIndex) return;
        if (info.ready_qualification != null) legacyReadyQualification = info.ready_qualification;
        legacyReadyPending = info.action_list?.Contains("guizhou_ready_cancel") == true;
    }
    protected override void OnBeforeActionPlayed(TableAction action) {
        if (action.PlayerIndex != Session.SelfIndex || action.ReadyQualification == null) return;
        legacyReadyQualification = action.ReadyQualification;
        legacyReadyPending = false;
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        var info = response.ask_other_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        Clock.PendingAskFromJiagang = Info?.phase == "waiting_action_qianggang";
        base.OnAskClaim(response);
        PresentAskClock(response, info.action_tick, info.remaining_time, info.is_tactical_recheck == true ? 0 : info.step_remaining ?? Session.RoomStepTime);
    }
    private void PresentAskClock(Response response, int tick, int bank, int step) {
        if (!Clock.IsSelfActionRequired || Clock.AllowActionList.Count == 0) return;
        var precise = Info?.action_clock;
        double now = GuizhouAskClock.Now;
        askClock.Project(tick,
            precise?.action_tick == tick ? precise.remaining_time : bank,
            precise?.action_tick == tick ? precise.step_remaining : step,
            response.received_monotonic ?? now, now, out double bankSeconds, out double stepSeconds);
        GameCanvas.Instance.LoadingRemianTime(bankSeconds, stepSeconds);
    }
    public override void OnAskWindowClosed(AskCloseReason reason) {
        if (reason == AskCloseReason.Acted || reason == AskCloseReason.TimedOut) askClock.Close();
        base.OnAskWindowClosed(reason);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.game_info);
        if (response.do_action_info?.action_list?.Contains("guizhou_reveal_kongs") == true) {
            // This is a reveal of existing melds, not four new kongs.
            RefreshMelds(response.game_info);
            return;
        }
        base.OnDoAction(response);
    }
    protected override bool TryApplyFamilyWord(TableAction action, string word) {
        if (word != "riichi") return false;
        var player = Mirror.Info(action.Seat);
        if (player != null)
            player.tag_list = (player.tag_list ?? Array.Empty<string>()).Concat(new[] { "declared_ready" }).Distinct().ToArray();
        return true; // No 1000-point deposit or riichi stick in this ruleset.
    }
    private static void RefreshMelds(GameInfo info) {
        if (info?.players_info == null) return;
        foreach (var server in info.players_info) {
            string seat = Mirror.SeatOf(server.player_index);
            var player = Mirror.Info(seat);
            if (player == null) continue;
            player.combination_tiles = new List<string>(server.combination_tiles ?? Array.Empty<string>());
            player.combination_masks = new List<int[]>(server.combination_mask ?? Array.Empty<int[]>());
            Game3DManager.Instance?.RebuildPlayerMelds(seat);
        }
    }
    protected override void OnShowResult(Response response) {
        Accept(response.game_info);
        endHands = response.show_result_info?.guizhou_end_hands;
        Info = response.show_result_info?.guizhou_info ?? Info;
        GuizhouLedgerPanel.Show(Info);
        base.OnShowResult(response);
    }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var envelope = base.BuildEnvelope(info);
        // Wait until the physical ron/rob-kong tile has been moved, then show
        // the other three hands for checking chicken/kong/ready accounting.
        if (envelope.FinalPanel && !envelope.IsLiuju && endHands != null) {
            var remaining = endHands.Where(p => p.Key != envelope.WinnerIndex)
                .ToDictionary(p => p.Key, p => p.Value);
            envelope.AfterHandReveal = () => {
                if (Active != this) return;
                RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
                Game3DManager.Instance?.RevealSichuanLiujuAllHands(remaining);
            };
        }
        return envelope;
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope envelope) {
        if (envelope.IsLiuju) {
            RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
            Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands);
        }
    }
    protected override void OnSettlementPresented(SettlementEnvelope envelope, ShowResultInfo info) {
        // AppendScoreboard first records the delta against the pre-hand score.
        // Then restore the authoritative total; doing this before that append
        // would charge the draw's ready-hand payment twice.
        if (envelope.IsLiuju) Presenter.ApplyScores(envelope.ScoresAfter);
    }
    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info == null) return;
        Clock.LastAskActionTick = response.ready_status_info.action_tick;
        Info = response.ready_status_info.guizhou_info ?? Info;
        GuizhouLedgerPanel.Show(Info);
        base.OnReadyStatus(response);
    }
    public override void OnSessionReset() { askClock.Reset(); clockHandNumber = 0; Info = null; endHands = null; legacyReadyQualification = null; legacyReadyPending = false; GuizhouLedgerPanel.Hide(); }
}
