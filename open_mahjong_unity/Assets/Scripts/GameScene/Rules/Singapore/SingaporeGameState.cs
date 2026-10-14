using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class SingaporeGameState : TurnBasedGameState {
    public const string RuleId = "singapore";
    public const string SubRule = "singapore/spgg2024";
    public static SingaporeGameState Active => RuleRegistry.ActiveGameState as SingaporeGameState;
    public readonly SingaporeHintsCache Hints = new SingaporeHintsCache();
    private int roundGeneration;
    private bool settlementAccepted, pendingSettlement;
    private bool snapshotContainsSettlement;
    public SingaporeInfo Info {get; private set;}
    private Response pendingReady, pendingEnd;
    public override bool IsSelfLocked => false;
    public WaitTileHint DescribeWaiting(WaitHintQuery query) => Hints.Describe(query,SeatHasTag("self",tag=>tag=="peida"));
    protected override void OnRoundStarted(GameInfo info) {
        roundGeneration++;settlementAccepted=pendingSettlement=false;pendingReady=pendingEnd=null;
        Hints.Reset(); Hints.Accept(info.singapore_hints);
        snapshotContainsSettlement=info.singapore_info?.settled==true;
        AcceptInfo(info.singapore_info);
        if (Manager?.tips == true && Mirror?.Self != null && TipsBlock.Instance != null && TipsContainer.Instance != null)
            TipsBlock.Instance.ShowTipsBlock(Mirror.SelfHandTiles,Mirror.Self.combination_tiles ?? new List<string>());
    }
    protected override void OnAskHandAction(Response response) {
        Hints.Accept(response.ask_hand_action_info?.singapore_hints); AcceptInfo(response.ask_hand_action_info?.singapore_info); base.OnAskHandAction(response);
    }
    protected override void OnDoAction(Response response) {
        Hints.Accept(response.do_action_info?.singapore_hints); AcceptInfo(response.do_action_info?.singapore_info); base.OnDoAction(response);
    }
    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        if (Manager?.tips == true) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();
    }
    protected override void OnShowResult(Response response) {
        if(response.show_result_info==null || settlementAccepted) return;
        settlementAccepted=true;
        Info=response.show_result_info.singapore_info;
        if(Game3DManager.Instance!=null) {
            pendingSettlement=true;
            Presenter.CloseTableForSettlement();
            Game3DManager.Instance.StartCoroutine(FinishSettlement(response,roundGeneration));
        } else base.OnShowResult(response);
    }
    private IEnumerator FinishSettlement(Response response,int generation) {
        int idle=0;
        while(Active==this && generation==roundGeneration) {
            bool busy=Game3DManager.Instance?.HasPendingRecordTableAnimations==true || GameCanvas.Instance?.IsChangeHandCardProcessing==true;
            idle=busy?0:idle+1;
            if(idle>=2) break;
            yield return null;
        }
        if(Active!=this || generation!=roundGeneration) yield break;
        var revealed=response.show_result_info.revealed_angang_masks;
        if(revealed!=null) foreach(var entry in revealed) {
            string seat=Mirror.SeatOf(entry.Key);
            RevealConcealedMasks(Mirror.Info(seat),entry.Value);
            Game3DManager.Instance?.RebuildPlayerMelds(seat);
        }
        pendingSettlement=false;
        var ready=pendingReady;var end=pendingEnd;pendingReady=pendingEnd=null;
        base.OnShowResult(response);
        if(ready!=null) base.OnReadyStatus(ready);
        if(end!=null) base.OnGameEnd(end);
    }
    internal static void RevealConcealedMasks(PlayerInfoClass player,int[][] revealed) {
        if(player?.combination_tiles==null || player.combination_masks==null || revealed==null) return;
        int kong=0;
        for(int i=0;i<player.combination_tiles.Count && kong<revealed.Length;i++) {
            if(!player.combination_tiles[i].StartsWith("G")) continue;
            int[] mask=revealed[kong++];
            if(mask==null || mask.Length!=8 || i>=player.combination_masks.Count) continue;
            player.combination_tiles[i]="G"+mask[1];
            var copy=(int[])mask.Clone();for(int j=0;j<8;j+=2) copy[j]=0;
            player.combination_masks[i]=copy;
        }
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        int previousRows=0;
        foreach(var seat in Mirror.IndexToPosition) {
            var player=Mirror.Info(seat.Value);
            if(player==null) continue;
            if(env.ScoresAfter!=null && ShowResultPlayerScoreResolver.TryGetAfterScore(env.ScoresAfter,seat.Key,player.original_player_index,out int after)
                && ShowResultPlayerScoreResolver.TryGetDelta(env.ScoreChanges,seat.Key,player.original_player_index,out int delta))
                player.score=after-delta;
            if(snapshotContainsSettlement && player.round_number_history?.Count>0 && player.score_history?.Count>0) {
                previousRows=System.Math.Max(previousRows,player.score_history.Count);
                player.round_number_history.RemoveAt(player.round_number_history.Count-1);
                player.score_history.RemoveAt(player.score_history.Count-1);
            }
        }
        if(previousRows>0 && Mirror.RoundSettlementHistory.Count==previousRows) Mirror.RoundSettlementHistory.RemoveAt(previousRows-1);
        base.AppendScoreboard(env);
        snapshotContainsSettlement=false;
    }
    protected override void OnReadyStatus(Response response) {if(pendingSettlement) pendingReady=response;else base.OnReadyStatus(response);}
    protected override void OnGameEnd(Response response) {if(pendingSettlement) pendingEnd=response;else base.OnGameEnd(response);}
    protected override bool TryApplyFamilyWord(TableAction action,string word) => word=="singapore_payment";
    public void AcceptInfo(SingaporeInfo info) {
        if(info==null) return;
        Info=info;
        if(info.scores==null) return;
        foreach(var seat in Mirror.IndexToPosition)
            if(info.scores.TryGetValue(seat.Key,out int score) && Mirror.Info(seat.Value)!=null) Mirror.Info(seat.Value).score=score;
        BoardCanvas.Instance?.UpdatePlayerScores(info.scores,Mirror.IndexToPosition);
    }
    public string PaymentSummary() {
        if(Info?.payments==null || Info.payments.Count==0) return "";
        return "本局即时支付（已计入总分）："+string.Join("；",Info.payments.Select(p=>$"{"东南西北"[p.winner]}家{p.reason} +{p.changes.GetValueOrDefault(p.winner)}"));
    }
    public override void OnSessionReset() {
        Hints.Reset();Info=null;snapshotContainsSettlement=false;roundGeneration++;settlementAccepted=pendingSettlement=false;pendingReady=pendingEnd=null;
    }
}
