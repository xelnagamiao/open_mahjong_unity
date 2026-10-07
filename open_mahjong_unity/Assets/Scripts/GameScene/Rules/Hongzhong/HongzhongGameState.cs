using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>MIL 红中客户端只投影权威事件。实体牌面不因最高番替代而变化。</summary>
public sealed class HongzhongGameState : TurnBasedGameState {
    public const string RuleId = "hongzhong";
    public const string SubRule = "hongzhong/mil2024";
    public static HongzhongGameState Active => RuleRegistry.ActiveGameState as HongzhongGameState;
    public HongzhongInfo Info { get; private set; }
    public readonly HongzhongHintsCache Hints = new HongzhongHintsCache();
    private Dictionary<int,int[]> revealedHands;
    private int settlementGeneration;
    private UnityEngine.Coroutine settlementCoroutine;
    private Game3DManager settlementHost;

    public WaitTileHint DescribeWaiting(WaitHintQuery query) => Hints.Describe(query, SeatHasTag("self", tag => tag == "peida"));

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (Manager?.tips == true) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
    }

    protected override void OnRoundStarted(GameInfo info) {
        CancelPendingSettlement();
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info = info.hongzhong_info; revealedHands = null; Hints.Reset(); Hints.Accept(info.hongzhong_hints);
        // game_start builds every tile as a held tile, including reconnects.
        // Restore the authoritative physical draw identity. Hand size alone
        // cannot distinguish a draw from the mandatory discard after a pong.
        if (Info?.self_has_draw_slot == true && Mirror.SelfHandTiles.Count > 0) {
            var tiles = Mirror.SelfHandTiles.ToArray();
            GameCanvas.Instance.ChangeHandCards("SyncHandCards", 0, tiles.Take(tiles.Length - 1).ToArray(), null);
            GameCanvas.Instance.ChangeHandCards("GetCardNoAnimation", tiles[tiles.Length - 1], null, null);
        }
        // Spectator initialization queries tips before this hook loads private hints.
        // Rebuild the stable marker only after the matching authoritative cache is ready.
        if (Manager?.tips == true && Mirror?.Self != null && TipsBlock.Instance != null && TipsContainer.Instance != null)
            TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles, Mirror.Self.combination_tiles ?? new List<string>());
    }
    protected override void OnAskHandAction(Response response) {
        Hints.Accept(response.ask_hand_action_info?.hongzhong_hints);
        base.OnAskHandAction(response);
    }
    protected override void OnDoAction(Response response) {
        Hints.Accept(response.do_action_info?.hongzhong_hints);
        base.OnDoAction(response);
    }
    protected override void OnShowResult(Response response) {
        Info = response.show_result_info?.hongzhong_info;
        revealedHands = response.show_result_info?.revealed_hands;
        base.OnShowResult(response);
    }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var env = base.BuildEnvelope(info);
        env.BirdTiles = info.hongzhong_info?.bird_tiles;
        return env;
    }
    protected override void PresentSettlement(SettlementEnvelope env) {
        CancelPendingSettlement();
        RoundEndPresentation.Instance?.StopActiveSequence();
        // Close input and record the authoritative net result immediately. The visual
        // hand must wait: live ankan/replacement queues still own cardsPosition.
        Manager.BeginSettlement();
        if (env.IsHu && env.IsMatchEnd) Manager.MarkAwaitingMatchEnd();
        Presenter.BeginLifecycle(env);
        Presenter.CloseTableForSettlement();
        RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
        AppendScoreboard(env);
        var hands = new Dictionary<int,int[]>();
        if (revealedHands != null) foreach (var hand in revealedHands)
            hands[hand.Key] = hand.Value == null ? null : (int[])hand.Value.Clone();
        settlementHost = Game3DManager.Instance;
        if (settlementHost != null)
            settlementCoroutine = settlementHost.StartCoroutine(PresentSettlementWhenIdle(env, hands, settlementHost, settlementGeneration));
    }
    private bool IsCurrentSettlement(int generation) => Active == this && generation == settlementGeneration;
    private IEnumerator PresentSettlementWhenIdle(SettlementEnvelope env, Dictionary<int,int[]> hands, Game3DManager table, int generation) {
        // Allow every nested operation from the final broadcast to register first.
        yield return null;
        int idleFrames = 0;
        while (IsCurrentSettlement(generation) && table != null) {
            bool pending = table.HasPendingRecordTableAnimations
                || (GameCanvas.Instance != null && GameCanvas.Instance.IsChangeHandCardProcessing);
            idleFrames = pending ? 0 : idleFrames + 1;
            // A normal live Get3DTileCoroutine is not queue-counted and spawns after
            // one yielded frame. Two idle frames also drain that existing path.
            if (idleFrames >= 2) break;
            yield return null;
        }
        if (!IsCurrentSettlement(generation) || table == null) yield break;
        settlementCoroutine = null; settlementHost = null;
        if (env.IsLiuju) {
            RevealFinalHands(hands, table, generation);
            Presenter.PresentLiuju("流局");
            yield break;
        }
        // The winner's existing reveal resets all reveal animators. Expose the other
        // three hands afterwards, with the same generation guard as the queued wait.
        hands.Remove(env.WinnerIndex);
        env.AfterHandReveal = () => RevealFinalHands(hands, table, generation);
        Presenter.PresentHu(env);
    }
    private void RevealFinalHands(Dictionary<int,int[]> hands, Game3DManager table, int generation) {
        if (!IsCurrentSettlement(generation) || table == null) return;
        RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
        table.RevealSichuanLiujuAllHands(hands);
    }
    private void CancelPendingSettlement() {
        settlementGeneration++;
        if (settlementCoroutine != null && settlementHost != null) settlementHost.StopCoroutine(settlementCoroutine);
        settlementCoroutine = null; settlementHost = null;
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        var payment = env.ScoreChanges;
        if (Info?.round_score_changes != null && Info.start_scores != null) {
            // 杠分已即时加到镜像。计分板要记整副净变化，先恢复局初再走公共历史逻辑。
            int restoredRows = 0;
            bool replacingLastRow = false;
            foreach (var seat in Mirror.IndexToPosition) {
                var player = Mirror.Info(seat.Value);
                if (player == null) continue;
                if (Info.start_scores.TryGetValue(player.original_player_index,out int before)) player.score = before;
                // 终局重连的快照可能已经包含本副历史，替换该项而不是重复追加。
                if (player.round_number_history?.Count > 0 && player.score_history?.Count > 0
                    && player.round_number_history[player.round_number_history.Count-1] == Mirror.CurrentRound) {
                    replacingLastRow = true;
                    restoredRows = System.Math.Max(restoredRows,player.score_history.Count);
                    player.round_number_history.RemoveAt(player.round_number_history.Count-1);
                    player.score_history.RemoveAt(player.score_history.Count-1);
                }
            }
            if (replacingLastRow && Mirror.RoundSettlementHistory.Count == restoredRows)
                Mirror.RoundSettlementHistory.RemoveAt(restoredRows-1);
            env.ScoreChanges = Info.round_score_changes;
        }
        base.AppendScoreboard(env);
        env.ScoreChanges = payment;
    }
    protected override void OnActionPlayed(TableAction action) {
        if (action.Silent || !GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) return;
        Presenter.ApplyScoreDeltas(action.GangScoreChanges);
        GameCanvas.Instance?.ShowGangScoreFloats(action.GangScoreChanges);
    }
    public override void OnSessionReset() {
        // A reconnect or table exit can arrive while the old settlement is still fading.
        CancelPendingSettlement();
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info = null; revealedHands = null; Hints.Reset();
    }
}
