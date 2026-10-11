using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class WuhanGameState : TurnBasedGameState {
    public const string RuleId = "wuhan";
    public const string SubRule = "wuhan/mil2025";
    public static WuhanGameState Active => RuleRegistry.ActiveGameState as WuhanGameState;
    public WuhanInfo Info { get; private set; }
    public readonly WuhanHintsCache Hints = new WuhanHintsCache();
    private Dictionary<int,int[]> revealedHands;
    private int settlementGeneration;
    private UnityEngine.Coroutine settlementCoroutine;
    private Game3DManager settlementHost;

    private void Accept(WuhanInfo info) {
        if (info == null) return;
        bool changed = Info?.indicator != info.indicator;
        Info = info;
        WuhanPresentation.Apply(info, changed);
    }

    public WaitTileHint DescribeWaiting(WaitHintQuery query) => Hints.Describe(query, SeatHasTag("self", tag => tag == "peida"));

    public static List<(int targetTile, List<int> displayTiles)> CollectAngangOptions(List<int> hand) {
        var result = new List<(int, List<int>)>();
        var candidates = TurnClock.Current.KongCandidates;
        if (candidates == null || !candidates.TryGetValue("angang", out var tiles) || tiles == null) return result;
        foreach (int tile in tiles.Distinct()) {
            if (!hand.Contains(tile)) continue;
            bool single = Active?.Info?.joker == tile || (Active?.Info?.skins ?? new[] {45}).Contains(tile);
            if (single) result.Add((tile, new List<int> {tile}));
            else if (hand.Count(t => t == tile) == 4) result.Add((tile, hand.Where(t => t == tile).ToList()));
        }
        return result;
    }

    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (Manager?.tips == true) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
    }

    protected override void OnRoundStarted(GameInfo info) {
        CancelPendingSettlement();
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info = null; Accept(info.wuhan_info); revealedHands = null; Hints.Reset(); Hints.Accept(info.wuhan_hints);
        if (Manager?.tips == true && Mirror?.Self != null && TipsBlock.Instance != null && TipsContainer.Instance != null)
            TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles, Mirror.Self.combination_tiles ?? new List<string>());
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.ask_hand_action_info?.wuhan_info);
        Hints.Accept(response.ask_hand_action_info?.wuhan_hints);
        base.OnAskHandAction(response);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.do_action_info?.wuhan_info);
        Hints.Accept(response.do_action_info?.wuhan_hints);
        base.OnDoAction(response);
    }
    protected override void OnShowResult(Response response) {
        Accept(response.show_result_info?.wuhan_info);
        revealedHands = response.show_result_info?.revealed_hands;
        base.OnShowResult(response);
    }
    protected override void PresentSettlement(SettlementEnvelope env) {
        CancelPendingSettlement();
        RoundEndPresentation.Instance?.StopActiveSequence();
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
        yield return null;
        int idleFrames = 0;
        while (IsCurrentSettlement(generation) && table != null) {
            bool pending = table.HasPendingRecordTableAnimations
                || (GameCanvas.Instance != null && GameCanvas.Instance.IsChangeHandCardProcessing);
            idleFrames = pending ? 0 : idleFrames + 1;
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
            int restoredRows = 0;
            bool replacingLastRow = false;
            foreach (var seat in Mirror.IndexToPosition) {
                var player = Mirror.Info(seat.Value);
                if (player == null) continue;
                if (Info.start_scores.TryGetValue(player.original_player_index,out int before)) player.score = before;
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
    public static WuhanInfo InfoFromIndicator(int indicator) {
        int joker, previous, effective = indicator;
        if (indicator < 40) { joker = indicator / 10 * 10 + indicator % 10 % 9 + 1; previous = indicator / 10 * 10 + (indicator % 10 + 7) % 9 + 1; }
        else { int[] order = {41,42,43,44,47,46}; if (effective == 45) effective = 44; int i = System.Array.IndexOf(order,effective); joker = order[(i+1)%6]; previous=order[(i+5)%6]; }
        return new WuhanInfo { indicator=indicator,joker=joker,skins=new[]{previous,effective,45} };
    }
    public override void OnSessionReset() {
        CancelPendingSettlement();
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info = null; revealedHands = null; Hints.Reset();
    }
}
