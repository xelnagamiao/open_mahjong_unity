using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class ShangraoGameState : TurnBasedGameState {
    public const string RuleId = "shangrao", SubRule = "shangrao/standard", RuleVersion = "shangrao-v0.2";
    public static ShangraoGameState Active => RuleRegistry.ActiveGameState as ShangraoGameState;
    public ShangraoInfo Info { get; private set; }
    public readonly ShangraoHintsCache Hints = new ShangraoHintsCache();
    private readonly ShangraoAskClock askClock = new ShangraoAskClock();
    private Dictionary<int, int[]> endHands;
    private int generation, clockHand;
    private string clockGame;
    private bool settlementAccepted, pendingSettlement;
    private Response pendingReady, pendingGameEnd;
    private UnityEngine.Coroutine settlementCoroutine;
    private Game3DManager settlementHost;

    private void Accept(GameInfo info) {
        if (info == null) return;
        Info = info.shangrao_info ?? Info;
        Hints.Accept(info.shangrao_hints);
        ShangraoTableIndicators.Refresh(Info);
    }
    protected override void OnRoundStarted(GameInfo info) {
        if (clockHand != info.shangrao_info?.hand_number || clockGame != Session.GamestateId) {
            askClock.Reset(); clockHand = info.shangrao_info?.hand_number ?? 0; clockGame = Session.GamestateId;
        }
        ResetSettlement(); Info = null; endHands = null; Hints.Reset(); Accept(info);
        if (Session.Tips && Mirror.Self != null && TipsBlock.Instance != null)
            TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles, Mirror.Self.combination_tiles ?? new List<string>());
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);
        var info = response.ask_hand_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        base.OnAskHandAction(response);
        PresentClock(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime);
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);
        var info = response.ask_other_action_info;
        if (info == null || askClock.IsClosed(info.action_tick)) return;
        Clock.PendingAskFromJiagang = false;
        base.OnAskClaim(response);
        PresentClock(response, info.action_tick, info.remaining_time, info.step_remaining ?? Session.RoomStepTime);
    }
    private void PresentClock(Response response, int tick, int bank, int step) {
        if (!Clock.IsSelfActionRequired) return;
        if (Info?.clock_active == false || Clock.AllowActionList.Count == 0) { Clock.Clear("shangraoAnswered"); return; }
        double now = ShangraoAskClock.Now;
        askClock.Project(tick, Info?.clock_remaining_ms ?? (bank + (double)step) * 1000,
            Info?.clock_step_remaining_ms ?? step * 1000d, response.received_monotonic ?? now, now, out double remaining, out double stepLeft);
        GameCanvas.Instance?.LoadingRemianTime(remaining, stepLeft);
    }
    public override void OnAskWindowClosed(AskCloseReason reason) {
        if (reason == AskCloseReason.Acted || reason == AskCloseReason.TimedOut) askClock.Close();
        base.OnAskWindowClosed(reason);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.game_info); base.OnDoAction(response);
        int? burnt = response.do_action_info?.shangrao_burnt_tile;
        int actor = response.do_action_info?.action_player ?? -1;
        if (burnt.HasValue && burnt != Info?.bao && actor >= 0 && Info?.burnt_river_indices?.Length > actor) {
            string seat = Mirror.SeatOf(actor);
            var indices = Info.burnt_river_indices[actor];
            if (indices?.Length > 0 && Mirror.Info(seat)?.discard_tiles.Count == indices.Last()) Mirror.AddDiscard(seat, burnt.Value, false);
        }
    }
    protected override bool TryApplyFamilyWord(TableAction action, string word) => word == "shangrao_payment";
    protected override void OnActionPlayed(TableAction action) {
        if (action.Silent || !GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) return;
        Presenter.ApplyScoreDeltas(action.GangScoreChanges);
        GameCanvas.Instance?.ShowGangScoreFloats(action.GangScoreChanges);
    }
    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (Session.Tips) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
    }
    public WaitTileHint DescribeWaiting(WaitHintQuery q) => Hints.Describe(q, SeatHasTag("self", tag => tag == "peida"));
    protected override void OnShowResult(Response response) {
        if (settlementAccepted || response.show_result_info == null) return;
        settlementAccepted = true; Accept(response.game_info);
        Info = response.show_result_info.shangrao_info ?? Info;
        endHands = response.show_result_info.shangrao_end_hands;
        pendingSettlement = true; Presenter.CloseTableForSettlement();
        settlementHost = Game3DManager.Instance;
        if (settlementHost != null) settlementCoroutine = settlementHost.StartCoroutine(WaitForTable(response, generation));
        else CompleteSettlement(response, generation);
    }
    private IEnumerator WaitForTable(Response response, int expected) {
        yield return null;
        int idle = 0;
        while (Active == this && generation == expected && settlementHost != null) {
            bool pending = settlementHost.HasPendingRecordTableAnimations || GameCanvas.Instance?.IsChangeHandCardProcessing == true;
            idle = pending ? 0 : idle + 1;
            if (idle >= 2) break;
            yield return null;
        }
        if (Active == this && generation == expected) CompleteSettlement(response, expected);
    }
    private void CompleteSettlement(Response response, int expected) {
        if (expected != generation) return;
        settlementCoroutine = null; settlementHost = null; pendingSettlement = false;
        var ready = pendingReady; var end = pendingGameEnd; pendingReady = pendingGameEnd = null;
        base.OnShowResult(response);
        if (ready != null) base.OnReadyStatus(ready);
        if (end != null) base.OnGameEnd(end);
    }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var env = base.BuildEnvelope(info);
        int expected = generation;
        if (!env.IsLiuju) env.AfterHandReveal = () => {
            if (Active == this && generation == expected)
                Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands?.Where(p => p.Key != env.WinnerIndex).ToDictionary(p => p.Key, p => p.Value));
        };
        return env;
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        if (env.IsLiuju) Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands);
    }
    protected override void OnSettlementPresented(SettlementEnvelope env, ShowResultInfo info) {
        if (env.IsLiuju) Presenter.ApplyScores(env.ScoresAfter);
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        if (Info?.start_scores?.Length == 4) {
            int rows = 0; bool replaced = false;
            foreach (var seat in Mirror.IndexToPosition) {
                var player = Mirror.Info(seat.Value); if (player == null) continue;
                player.score = Info.start_scores[seat.Key];
                if (player.round_number_history?.Count > 0 && player.score_history?.Count > 0
                    && player.round_number_history.Last() == Mirror.CurrentRound) {
                    replaced = true; rows = Math.Max(rows, player.score_history.Count);
                    player.round_number_history.RemoveAt(player.round_number_history.Count - 1);
                    player.score_history.RemoveAt(player.score_history.Count - 1);
                }
            }
            if (replaced && Mirror.RoundSettlementHistory.Count == rows) Mirror.RoundSettlementHistory.RemoveAt(rows - 1);
        }
        base.AppendScoreboard(env);
    }
    protected override void OnReadyStatus(Response response) {
        if (response.ready_status_info == null) return;
        Clock.LastAskActionTick = response.ready_status_info.action_tick;
        Info = response.ready_status_info.shangrao_info ?? Info;
        ShangraoTableIndicators.Refresh(Info);
        if (pendingSettlement) pendingReady = response; else base.OnReadyStatus(response);
    }
    protected override void OnGameEnd(Response response) {
        if (!pendingSettlement) { base.OnGameEnd(response); return; }
        if (!Session.IsRealtimeSpectator && !Session.IsDuplicate) LocalRecordStore.SavePushedDetail(response.game_end_info?.record_detail);
        pendingGameEnd = response;
    }
    private void ResetSettlement() {
        generation++;
        if (settlementCoroutine != null && settlementHost != null) settlementHost.StopCoroutine(settlementCoroutine);
        settlementCoroutine = null; settlementHost = null; settlementAccepted = pendingSettlement = false;
        pendingReady = pendingGameEnd = null; RoundEndPresentation.Instance?.StopActiveSequence();
    }
    public override void OnSessionReset() {
        ResetSettlement(); askClock.Reset(); clockHand = 0; clockGame = null;
        Info = null; endHands = null; Hints.Reset(); ShangraoTableIndicators.Refresh(null);
    }
}
