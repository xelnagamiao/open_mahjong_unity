using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class NanjingGameState : TurnBasedGameState {
    public const string RuleId="nanjing",SubRule="nanjing/mil2024",RuleVersion="mil-nanjing-2024-v1";
    public static NanjingGameState Active=>RuleRegistry.ActiveGameState as NanjingGameState;
    public NanjingInfo Info { get; private set; }
    public readonly NanjingHintsCache Hints=new NanjingHintsCache();
    private readonly List<Response> settlements=new List<Response>();
    private readonly HashSet<int> acceptedWinners=new HashSet<int>();
    private bool pendingSettlement;
    private int generation;
    private Response pendingReady,pendingEnd;
    public WaitTileHint DescribeWaiting(WaitHintQuery query)=>Hints.Describe(query,SeatHasTag("self",tag=>tag=="peida"));
    private void Accept(GameInfo info) {
        if(info==null) return;
        Info=info.nanjing_info??Info;Hints.Accept(info.nanjing_hints);
    }
    protected override void OnRoundStarted(GameInfo info) {
        OnSessionReset();Accept(info);RefreshFlowers();
        if(Session.Tips && Info?.final_hands==null && Mirror.Self!=null)
            TipsBlock.Instance?.ShowTipsBlock(Mirror.SelfHandTiles,Mirror.Self.combination_tiles??new List<string>());
    }
    protected override void OnAskHandAction(Response response) {Accept(response.game_info);base.OnAskHandAction(response);}
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";base.OnAskClaim(response);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.game_info);base.OnDoAction(response);RefreshFlowers();
        if(response.do_action_info?.action_list?.Contains("nanjing_score")==true && Info?.start_scores?.Length==4 && Info.round_changes?.Length==4)
            Presenter.ApplyScores(Enumerable.Range(0,4).ToDictionary(i=>i,i=>Info.start_scores[i]+Info.round_changes[i]));
    }
    protected override bool TryApplyFamilyWord(TableAction action,string word)=>word=="nanjing_score";
    private void RefreshFlowers() {
        GameCanvas.Instance?.SetPlayerFlowerCounts(Mirror.IndexToPosition.ToDictionary(x=>x.Key,x=>Mirror.Info(x.Value)?.huapai_list?.Count??0),Mirror.IndexToPosition);
    }
    public override void OnPlayerTagsRefreshed() {base.OnPlayerTagsRefreshed();if(Session.Tips) TipsContainer.Instance?.RefreshTenpaiTipsIfCached();}
    protected override void OnShowResult(Response response) {
        var info=response.show_result_info;
        if(info==null || !acceptedWinners.Add(info.hepai_player_index)) return;
        Accept(response.game_info);settlements.Add(response);pendingSettlement=true;
        if(info.next_status=="round_continue") return;
        if(Game3DManager.Instance!=null) Game3DManager.Instance.StartCoroutine(PresentResults(generation));
        else {pendingSettlement=false;base.OnShowResult(response);}
    }
    private IEnumerator PresentResults(int token) {
        int idle=0;
        while(token==generation && Active==this && idle<2) {
            idle=Game3DManager.Instance.HasPendingRecordTableAnimations || GameCanvas.Instance?.IsChangeHandCardProcessing==true ? 0 : idle+1;
            yield return null;
        }
        if(token!=generation || Active!=this) yield break;
        Manager.BeginSettlement();Presenter.CloseTableForSettlement();
        var panels=settlements.ToArray();
        var winners=panels.Where(r=>r.show_result_info.hepai_player_index>=0).ToDictionary(r=>r.show_result_info.hepai_player_index,r=>r.show_result_info.hepai_player_hand);
        for(int n=0;n<panels.Length;n++) {
            var env=BuildEnvelope(panels[n].show_result_info);
            bool final=n==panels.Length-1;
            Presenter.BeginLifecycle(env);
            if(final) AppendScoreboard(env);
            if(env.IsLiuju) {RevealFinalHands();Presenter.ApplyScores(env.ScoresAfter);Presenter.PresentLiuju("流局");}
            else {
                if(env.IsMatchEnd) Manager.MarkAwaitingMatchEnd();
                yield return RoundEndPresentation.Instance.PresentHuResultSequenceCoroutine(
                    env.WinnerIndex,env.ScoresAfter,env.HuScore,env.FanLabels,env.HuClass,env.WinnerHand,env.WinnerFlowers,env.WinnerMelds,
                    env.BaseFu,env.FuFanList,null,env.ScoreChanges,hepaiTile:env.WinTile,multiRon:env.MultiRon,
                    ronDiscarderIndex:env.RonDiscarderIndex,recycleDiscard:env.RecycleDiscard==true,isQianggang:env.IsQianggang,
                    finalPanel:final,simultaneousHuHands:n==0 && winners.Count>1 ? winners : null,
                    skipHandReveal:env.SkipHandReveal || n>0,afterHandReveal:RevealFinalHands);
            }
            if(token!=generation || Active!=this) yield break;
        }
        pendingSettlement=false;
        var ready=pendingReady;var end=pendingEnd;pendingReady=pendingEnd=null;
        if(ready!=null) base.OnReadyStatus(ready);
        if(end!=null) base.OnGameEnd(end);
    }
    private void RevealFinalHands() {
        if(Info?.final_hands==null) return;
        RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
        Game3DManager.Instance?.RevealSichuanLiujuAllHands(Info.final_hands);
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        if(Info?.round_changes?.Length!=4) {base.AppendScoreboard(env);return;}
        int previousRows=0;
        foreach(var player in Mirror.PlayerToInfo.Values) {
            if(player.round_number_history?.Count>0 && player.round_number_history.Last()==Mirror.CurrentRound) {
                previousRows=System.Math.Max(previousRows,player.score_history?.Count??0);
                player.round_number_history.RemoveAt(player.round_number_history.Count-1);
                if(player.score_history?.Count>0) player.score_history.RemoveAt(player.score_history.Count-1);
            }
        }
        if(previousRows>0 && Mirror.RoundSettlementHistory.Count==previousRows) Mirror.RoundSettlementHistory.RemoveAt(previousRows-1);
        var total=Enumerable.Range(0,4).ToDictionary(i=>Info.seat_to_original[i],i=>Info.round_changes[i]);
        Presenter.AppendScoreboard(env,null,total);
    }
    protected override void OnReadyStatus(Response response) {
        if(response.ready_status_info==null) return;
        Clock.LastAskActionTick=response.ready_status_info.action_tick;
        Info=response.ready_status_info.nanjing_info??Info;
        if(pendingSettlement) pendingReady=response;
        else base.OnReadyStatus(response);
    }
    protected override void OnGameEnd(Response response) {
        if(pendingSettlement) {LocalRecordStore.SavePushedDetail(response.game_end_info?.record_detail);pendingEnd=response;}
        else base.OnGameEnd(response);
    }
    public override void OnSessionReset() {
        generation++;Info=null;Hints.Reset();settlements.Clear();acceptedWinners.Clear();pendingSettlement=false;pendingReady=pendingEnd=null;
    }
}
