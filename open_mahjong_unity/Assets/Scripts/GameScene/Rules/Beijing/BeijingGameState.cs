using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class BeijingGameState : TurnBasedGameState {
    public const string RuleId="beijing",SubRule="beijing/chipengti",RuleVersion="beijing-v1";
    public static BeijingGameState Active=>RuleRegistry.ActiveGameState as BeijingGameState;
    public BeijingInfo Info {get;private set;}
    public readonly BeijingHintsCache Hints=new BeijingHintsCache();
    private readonly BeijingAskClock askClock=new BeijingAskClock();
    private readonly Queue<(string suffix,Response response)> deferred=new Queue<(string,Response)>();
    private int generation,clockHand;
    private string clockGame;
    private bool settlementAccepted,transitionHeld;
    private GameInfo latestInfo;
    private DoActionInfo latestAction;
    private Response pendingReady;
    private bool pendingSettlement;
    private UnityEngine.Coroutine sequence;
    private Game3DManager host;
    private Dictionary<int,int[]> endHands;

    public override bool HandleMessage(string suffix,Response response) {
        if(deferred.Count>0||transitionHeld&&(suffix=="game_start"||suffix=="game_end")){deferred.Enqueue((suffix,response));return true;}
        return base.HandleMessage(suffix,response);
    }
    private void Accept(GameInfo info) {
        if(info==null)return;latestInfo=info;Info=info.beijing_info??Info;Hints.Accept(info.beijing_hints);BeijingTableIndicators.Refresh(Info);
    }
    protected override void OnRoundStarted(GameInfo info) {
        if(clockHand!=info.beijing_info?.hand_number||clockGame!=Session.GamestateId){askClock.Reset();clockHand=info.beijing_info?.hand_number??0;clockGame=Session.GamestateId;}
        ResetSettlement();Info=null;endHands=null;Hints.Reset();Accept(info);
        if(Session.Tips&&Mirror.Self!=null)TipsBlock.Instance?.ShowTipsBlock(Mirror.SelfHandTiles,Mirror.Self.combination_tiles??new List<string>());
    }
    protected override void OnAskHandAction(Response response) {
        Accept(response.game_info);var info=response.ask_hand_action_info;if(info==null||askClock.IsClosed(info.action_tick))return;
        base.OnAskHandAction(response);PresentClock(response,info.action_tick,info.remaining_time,info.step_remaining??Session.RoomStepTime);
    }
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);var info=response.ask_other_action_info;if(info==null||askClock.IsClosed(info.action_tick))return;
        Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";
        base.OnAskClaim(response);PresentClock(response,info.action_tick,info.remaining_time,info.step_remaining??Session.RoomStepTime);
    }
    private void PresentClock(Response response,int tick,int bank,int step) {
        if(!Clock.IsSelfActionRequired)return;
        if(Info?.clock_active==false||Clock.AllowActionList.Count==0){Clock.Clear("beijingAnswered");return;}
        double now=BeijingAskClock.Now;
        askClock.Project(tick,Info?.clock_remaining_ms??(bank+(double)step)*1000,Info?.clock_step_remaining_ms??step*1000d,
            response.received_monotonic??now,now,out double remaining,out double stepLeft);
        GameCanvas.Instance?.LoadingRemianTime(remaining,stepLeft);
    }
    public override void OnAskWindowClosed(AskCloseReason reason){if(reason==AskCloseReason.Acted||reason==AskCloseReason.TimedOut)askClock.Close();base.OnAskWindowClosed(reason);}
    protected override void OnDoAction(Response response){Accept(response.game_info);latestAction=response.do_action_info;base.OnDoAction(response);}
    protected override bool TryApplyFamilyWord(TableAction action,string word) {
        if(word=="declare_kan"||word=="tianting"||word=="beijing_payment")return true;
        if(word!="rob_kan")return false;
        var source=latestInfo?.players_info?.FirstOrDefault(p=>p.player_index==action.PlayerIndex);var target=Mirror.Info(action.Seat);
        if(source!=null&&target!=null){if(action.Seat=="self") {
            var remaining=(source.hand_tiles??Array.Empty<int>()).ToList();var removed=new List<int>();
            foreach(int tile in Mirror.SelfHandTiles)if(!remaining.Remove(tile))removed.Add(tile);
            Mirror.SelfHandTiles=(source.hand_tiles??Array.Empty<int>()).ToList();
            GameCanvas.Instance?.ChangeHandCards("RemoveCombinationCard",0,removed.ToArray(),null);
        }target.hand_tiles_count=source.hand_tiles_count;}
        Game3DManager.Instance?.RemoveHongqueKongHandTile(action.Seat,latestAction?.cut_tile??0);return true;
    }
    protected override void OnActionPlayed(TableAction action) {
        if(action.Silent||!GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges))return;
        Presenter.ApplyScoreDeltas(action.GangScoreChanges);GameCanvas.Instance?.ShowGangScoreFloats(action.GangScoreChanges);
    }
    public override void OnPlayerTagsRefreshed(){base.OnPlayerTagsRefreshed();if(Session.Tips)TipsContainer.Instance?.RefreshTenpaiTipsIfCached();}
    public WaitTileHint DescribeWaiting(WaitHintQuery q)=>Hints.Describe(q,SeatHasTag("self",tag=>tag=="peida"||tag=="discarded_hun"));
    protected override void OnShowResult(Response response) {
        if(settlementAccepted||response.show_result_info==null)return;
        settlementAccepted=transitionHeld=pendingSettlement=true;Accept(response.game_info);Info=response.show_result_info.beijing_info??Info;
        endHands=response.show_result_info.beijing_end_hands;Presenter.CloseTableForSettlement();
        host=Game3DManager.Instance;
        if(host!=null)sequence=host.StartCoroutine(SettlementSync.PreserveContext(PresentResults(response,generation)));
        else {base.OnShowResult(response);ReleaseTransition();}
    }
    private IEnumerator PresentResults(Response response,int token) {
        yield return null;int idle=0;
        while(Active==this&&generation==token&&host!=null&&idle<2){idle=host.HasPendingRecordTableAnimations||GameCanvas.Instance?.IsChangeHandCardProcessing==true?0:idle+1;yield return null;}
        if(Active!=this||generation!=token)yield break;
        pendingSettlement=false;
        foreach(var player in latestInfo?.players_info??Array.Empty<PlayerInfo>()) {
            var target=Mirror.Info(Mirror.SeatOf(player.player_index));if(target==null)continue;
            target.combination_tiles=(player.combination_tiles??Array.Empty<string>()).ToList();
            target.combination_masks=(player.combination_mask??Array.Empty<int[]>()).Select(m=>(int[])m.Clone()).ToList();
            target.discard_tiles=(player.discard_tiles??Array.Empty<int>()).ToList();
            Game3DManager.Instance?.RebuildPlayerMelds(Mirror.SeatOf(player.player_index));
        }
        var panels=response.show_result_info.beijing_settlements??new[]{response.show_result_info};
        var winners=panels.Where(p=>p.hepai_player_index>=0).ToDictionary(p=>p.hepai_player_index,p=>p.hepai_player_hand);
        Manager.BeginSettlement();
        if(pendingReady!=null){base.OnReadyStatus(pendingReady);pendingReady=null;}
        for(int n=0;n<panels.Length;n++) {
            var env=BuildEnvelope(panels[n]);bool final=n==panels.Length-1;
            Presenter.BeginLifecycle(env);
            if(final){var total=BuildEnvelope(response.show_result_info);AppendScoreboard(total);}
            if(env.IsLiuju){Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands);Presenter.ApplyScores(env.ScoresAfter);Presenter.PresentLiuju("流局");
                yield return new UnityEngine.WaitForSeconds(RoundEndTiming.RoundEndPresentationFadeSeconds+RoundEndPresentation.DrawCaptionHoldSeconds);}
            else {
                if(env.IsMatchEnd)Manager.MarkAwaitingMatchEnd();
                yield return RoundEndPresentation.Instance.PresentHuResultSequenceCoroutine(
                    env.WinnerIndex,env.ScoresAfter,env.HuScore,env.FanLabels,env.HuClass,env.WinnerHand,env.WinnerFlowers,env.WinnerMelds,
                    env.BaseFu,env.FuFanList,null,env.ScoreChanges,hepaiTile:env.WinTile,multiRon:env.MultiRon,
                    ronDiscarderIndex:env.RonDiscarderIndex,recycleDiscard:env.RecycleDiscard==true,isQianggang:env.IsQianggang,
                    finalPanel:final,simultaneousHuHands:n==0&&winners.Count>1?winners:null,skipHandReveal:n>0,
                    afterHandReveal:()=>Game3DManager.Instance?.RevealSichuanLiujuAllHands(endHands?.Where(p=>!winners.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>p.Value)));
            }
            if(Active!=this||generation!=token)yield break;
        }
        if(panels.Last().hu_class!="liuju") {
            double until=UnityEngine.Time.realtimeSinceStartupAsDouble+RoundEndTiming.HuConfirmCountdownSeconds;
            while(Active==this&&generation==token&&EndResultPanel.Instance!=null&&!EndResultPanel.Instance.HasConfirmedResult&&UnityEngine.Time.realtimeSinceStartupAsDouble<until)yield return null;
        }
        if(Active==this&&generation==token)ReleaseTransition();
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        if(SettlementSync.IsRestoring)return;
        if(Info?.start_scores?.Length==4) {
            int rows=0;bool replaced=false;
            foreach(var pair in Mirror.IndexToPosition){var p=Mirror.Info(pair.Value);if(p==null)continue;p.score=Info.start_scores[pair.Key];
                if(Info.hand_number>0&&p.round_number_history?.Count>0&&p.score_history?.Count>=Info.hand_number&&p.round_number_history.Last()==Mirror.CurrentRound){replaced=true;rows=Math.Max(rows,p.score_history.Count);p.round_number_history.RemoveAt(p.round_number_history.Count-1);p.score_history.RemoveAt(p.score_history.Count-1);}}
            if(replaced&&Mirror.RoundSettlementHistory.Count==rows)Mirror.RoundSettlementHistory.RemoveAt(rows-1);
        }
        base.AppendScoreboard(env);
    }
    protected override void OnReadyStatus(Response response){if(response.ready_status_info==null)return;Clock.LastAskActionTick=response.ready_status_info.action_tick;Info=response.ready_status_info.beijing_info??Info;BeijingTableIndicators.Refresh(Info);if(pendingSettlement)pendingReady=response;else base.OnReadyStatus(response);}
    private void ReleaseTransition(){sequence=null;host=null;transitionHeld=false;var copy=deferred.ToArray();deferred.Clear();foreach(var message in copy)HandleMessage(message.suffix,message.response);}
    private void ResetSettlement(){generation++;if(sequence!=null&&host!=null)host.StopCoroutine(sequence);sequence=null;host=null;settlementAccepted=transitionHeld=pendingSettlement=false;pendingReady=null;deferred.Clear();RoundEndPresentation.Instance?.StopActiveSequence();}
    public override void OnSessionReset(){ResetSettlement();askClock.Reset();clockHand=0;clockGame=null;Info=null;latestInfo=null;endHands=null;Hints.Reset();BeijingTableIndicators.Refresh(null);}
}
