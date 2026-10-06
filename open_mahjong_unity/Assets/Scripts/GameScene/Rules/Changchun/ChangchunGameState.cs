using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public sealed class ChangchunGameState : TurnBasedGameState {
    private readonly ChangchunAskClock askClock = new ChangchunAskClock();
    private int clockRound = -1;
    private string clockGame;
    public ChangchunInfo Info { get; private set; }
    private readonly HashSet<int> announcedReady = new HashSet<int>();
    private Dictionary<int,int[]> revealedHands;
    private int roundGeneration;
    private bool settlementAccepted,pendingSettlement;
    private Response pendingReadyStatus,pendingGameEnd;
    public override bool IsSelfLocked => SeatHasTag("self",t=>t=="declared_ready") || Info?.window=="before_draw" || Info?.window=="final_four";
    protected override void OnRoundStarted(GameInfo info) {
        if(clockRound!=info.current_round || clockGame!=Session.GamestateId) {
            askClock.Reset();clockRound=info.current_round;clockGame=Session.GamestateId;
        }
        roundGeneration++;
        settlementAccepted=pendingSettlement=false;
        pendingReadyStatus=pendingGameEnd=null;
        RoundEndPresentation.Instance?.StopActiveSequence();
        Info=info.changchun;
        Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";
        if(Info!=null) {
            var tiles=Mirror.SelfHandTiles.ToArray();
            int standing=tiles.Length-(Info.self_has_draw_slot && tiles.Length>0 ? 1 : 0);
            GameCanvas.Instance?.ChangeHandCards("SyncHandCards",0,tiles.Take(standing).ToArray(),null);
            if(Info.self_has_draw_slot && tiles.Length>0) GameCanvas.Instance?.ChangeHandCards("GetCardNoAnimation",Info.self_last_drawn_tile,null,null);
            Mirror.LastDealTileId=Info.self_has_draw_slot ? Info.self_last_drawn_tile : 0;
            foreach(string seat in Mirror.IndexToPosition.Values)
                if(Mirror.Info(seat)?.combination_masks?.Count>0) Game3DManager.Instance?.RebuildPlayerMelds(seat);
        }
        revealedHands=null;
        announcedReady.Clear();
        foreach(var pair in Mirror.IndexToPosition)
            if(SeatHasTag(pair.Value,t=>t=="declared_ready")) announcedReady.Add(pair.Key);
        RefreshSelfStatusIndicators();
    }
    protected override void OnHandAskReceived(AskHandActionGBInfo info) {Info=info.changchun;RefreshSelfStatusIndicators();}
    protected override void OnAskHandAction(Response response) {
        var info=response.ask_hand_action_info;
        if(info==null || askClock.ShouldIgnore(info.action_tick)) return;
        int bank=info.remaining_time;int? step=info.step_remaining;
        double exactBank=info.remaining_time_ms.HasValue ? info.remaining_time_ms.Value/1000d : bank;
        double exactStep=info.step_remaining_ms.HasValue ? info.step_remaining_ms.Value/1000d : step??Session.RoomStepTime;
        double received=response.received_monotonic??ChangchunAskClock.Now;
        askClock.Project(info.action_tick,exactBank,exactStep,received,ChangchunAskClock.Now,
            out double b,out double s);
        info.remaining_time=ChangchunAskClock.Seconds(b);info.step_remaining=ChangchunAskClock.Seconds(s);
        try {
            base.OnAskHandAction(response);
            if(!AutoActionPolicy.Current.TryResolveHand(info.deal_tile_type,out _,out _))
                PresentAskClock(info.action_tick,exactBank,exactStep,received);
        } finally {info.remaining_time=bank;info.step_remaining=step;}
    }
    protected override void OnAskClaim(Response response) {
        var info=response.ask_other_action_info;
        if(info==null || askClock.ShouldIgnore(info.action_tick)) return;
        if(response.game_info?.changchun!=null) {Info=response.game_info.changchun;Clock.PendingAskFromJiagang=Info.phase=="waiting_action_qianggang";}
        int bank=info.remaining_time;int? step=info.step_remaining;
        double exactBank=info.remaining_time_ms.HasValue ? info.remaining_time_ms.Value/1000d : bank;
        double exactStep=info.is_tactical_recheck==true ? 0d : info.step_remaining_ms.HasValue ? info.step_remaining_ms.Value/1000d : step??Session.RoomStepTime;
        double received=response.received_monotonic??ChangchunAskClock.Now;
        askClock.Project(info.action_tick,exactBank,exactStep,received,ChangchunAskClock.Now,
            out double b,out double s);
        info.remaining_time=ChangchunAskClock.Seconds(b);info.step_remaining=ChangchunAskClock.Seconds(s);
        try {
            base.OnAskClaim(response);
            if(!AutoActionPolicy.Current.TryResolveClaim(out _,out _))
                PresentAskClock(info.action_tick,exactBank,exactStep,received);
        } finally {info.remaining_time=bank;info.step_remaining=step;}
    }
    private void PresentAskClock(int tick,double bank,double step,double received) {
        if(!Clock.IsSelfActionRequired || GameCanvas.Instance==null) return;
        askClock.Project(tick,bank,step,received,ChangchunAskClock.Now,out double b,out double s);
        if(b+s<=0) {
            Clock.Timeout();GameCanvas.Instance.StopTimeRunning();
        } else if(GameCanvas.Instance.IsCountdownRunning)
            GameCanvas.Instance.CorrectCountdownBudget(b,s);
    }
    public override void OnAskWindowClosed(AskCloseReason reason) {
        if(reason==AskCloseReason.Acted || reason==AskCloseReason.TimedOut) askClock.Close();
        base.OnAskWindowClosed(reason);
    }
    protected override bool HandleExtraMessage(string suffix,Response response) {
        if(suffix!="ask_closed") return base.HandleExtraMessage(suffix,response);
        var info=response.ask_other_action_info;
        if(info!=null &&
           askClock.AcceptsClosure(info.action_tick,info.player_index,Session.SelfIndex)) {
            askClock.Close();Clock.Clear("changchunAnswered");
        }
        return true;
    }
    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        foreach(var pair in Mirror.IndexToPosition)
            if(SeatHasTag(pair.Value,t=>t=="declared_ready") && announcedReady.Add(pair.Key)) SoundManager.Instance?.PlayActionSound(pair.Value,"riichi");
        RefreshSelfStatusIndicators();
    }
    public override void RefreshSelfStatusIndicators() {
        string text=Info?.bao_exhausted==true ? "无宝" : Info?.bao_visible==true ? (Info.bao_tile>0 ? "宝："+ChangchunRuleBootstrap.TileName(Info.bao_tile) : "未翻宝") : "宝牌未可见";
        bool finalFour=Info?.window=="final_four";
        if(finalFour) text+="\n末四张";
        GameCanvas.Instance?.SetSelfStatusIndicator(GameCanvas.StatusSlotShunhe,true,$"<size={(finalFour ? 16 : 24)}>{text}</size>");
        if(Info?.tail_tiles!=null) ChangchunTailTiles.Render(Info.tail_tiles.ToDictionary(t=>(int)t["player"],t=>(int)t["tile"]),Mirror.IndexToPosition,revealedHands!=null);
    }
    protected override void OnDoAction(Response response) {
        base.OnDoAction(response);
        Info=response.do_action_info?.changchun ?? Info;
        if(Info?.Event!=null) ApplyEvent(Info);
        RefreshSelfStatusIndicators();
    }
    private void ApplyEvent(ChangchunInfo info) {
        string kind=(string)info.Event["kind"];
        if(kind=="added_offer") Clock.PendingAskFromJiagang=true;
        int actor=(int?)info.Event["player"]??-1;
        var deltas=new Dictionary<int,int>();
        foreach(var snapshot in info.players??Array.Empty<ChangchunPlayerSnapshot>()) {
            string seat=Mirror.SeatOf(snapshot.player);
            var player=Mirror.Info(seat);
            if(player==null) continue;
            if(kind=="kong_score") deltas[snapshot.player]=snapshot.score-player.score;
            if(snapshot.player!=actor || (kind!="special" && kind!="added_offer" && kind!="added_commit" && kind!="added_robbed" && kind!="tail_pass")) continue;
            int removed=Math.Max(0,player.hand_tiles_count-snapshot.hand_count);
            int[] removedTiles=(kind=="special" ? info.Event["physical"]?.ToObject<int[]>() : new[]{(int?)info.Event["tile"]??0})??Array.Empty<int>();
            if(seat=="self") {
                if(snapshot.hand!=null) {
                    var previous=new List<int>(Mirror.SelfHandTiles);
                    foreach(int t in snapshot.hand) previous.Remove(t);
                    removedTiles=previous.ToArray();removed=removedTiles.Length;
                    Mirror.SelfHandTiles=new List<int>(snapshot.hand);
                    if(removed>0) {
                        int standing=snapshot.hand.Length-(snapshot.has_draw_slot ? 1 : 0);
                        GameCanvas.Instance?.ChangeHandCards("SyncHandCards",0,snapshot.hand.Take(standing).ToArray(),null);
                        if(snapshot.has_draw_slot) GameCanvas.Instance?.ChangeHandCards("GetCard",snapshot.last_drawn_tile,null,null);
                        Mirror.LastDealTileId=snapshot.has_draw_slot ? snapshot.last_drawn_tile : 0;
                    }
                }
            }
            player.hand_tiles_count=snapshot.hand_count;
            player.combination_tiles=new List<string>(snapshot.melds??Array.Empty<string>());
            player.combination_masks=new List<int[]>(snapshot.masks??Array.Empty<int[]>());
            if(kind=="special" && player.combination_masks.Count>0) {
                Game3DManager.Instance?.PlayFreeMeld(seat,player.combination_masks.Last(),player.combination_masks.Count-1,removedTiles,null,0);
                SoundManager.Instance?.PlayActionSound(seat,"gang");
                GameCanvas.Instance?.ShowActionDisplay(seat,"gang",Session.RoomRule);
            } else {
                for(int i=0;i<removed;i++) Game3DManager.Instance?.RemoveHongqueKongHandTile(seat,i<removedTiles.Length && removedTiles[i]>0 ? removedTiles[i] : 11);
                if(kind=="added_commit" || kind=="added_robbed") Game3DManager.Instance?.RebuildPlayerMelds(seat);
                if(kind=="added_commit") {SoundManager.Instance?.PlayActionSound(seat,"gang");GameCanvas.Instance?.ShowActionDisplay(seat,"gang",Session.RoomRule);}
            }
        }
        if(kind=="kong_score" && deltas.Values.Any(d=>d!=0)) {
            Presenter.ApplyScoreDeltas(deltas);GameCanvas.Instance?.ShowGangScoreFloats(deltas);
        }
        if(kind=="rob_claim" && actor>=0) {
            // A robbed added tile is a transient discard source, consumed by
            // the immediately following standard pung/kong frame.
            string source=Mirror.SeatOf(actor);int tile=(int)info.Event["tile"];
            Mirror.Info(source).discard_tiles.Add(tile);
            Mirror.LastCutCardID=tile;Mirror.LastDiscardPlayerPosition=source;
            Game3DManager.Instance?.Change3DTile("SetDiscardWithoutAnimation",tile,0,source,false,null);
        }
    }
    protected override void OnShowResult(Response response) {
        if(response.show_result_info==null || settlementAccepted) return;
        settlementAccepted=true;
        Info=response.show_result_info?.changchun??Info;
        revealedHands=response.show_result_info?.revealed_hands;
        RefreshSelfStatusIndicators();
        AcceptFinalConcealedMelds(response.show_result_info?.revealed_angang_masks);
        if(Game3DManager.Instance!=null) {
            pendingSettlement=true;
            Presenter.CloseTableForSettlement();
            Game3DManager.Instance.StartCoroutine(ShowResultWhenTableIdle(response,roundGeneration));
            return;
        }
        base.OnShowResult(response);
    }
    private IEnumerator ShowResultWhenTableIdle(Response response,int generation) {
        // A fast win can arrive while the preceding kong's supplement is still
        // queued. Finish that draw before the normal winner reveal clears the
        // standing hand, otherwise a late hidden draw adds a placeholder tile.
        while(RuleRegistry.ActiveGameState==this && generation==roundGeneration &&
              (Game3DManager.Instance?.HasPendingRecordTableAnimations==true || GameCanvas.Instance?.IsChangeHandCardProcessing==true)) yield return null;
        if(RuleRegistry.ActiveGameState!=this || generation!=roundGeneration) yield break;
        // A robbed ordinary addition can already be animating when the server
        // restores the pung. Rebuild only after that animation finishes so its
        // late fourth tile cannot overwrite the authoritative three-tile meld.
        foreach(string seat in Mirror.IndexToPosition.Values)
            if(Mirror.Info(seat)?.combination_masks?.Count>0) Game3DManager.Instance?.RebuildPlayerMelds(seat);
        pendingSettlement=false;
        var ready=pendingReadyStatus;var end=pendingGameEnd;
        pendingReadyStatus=pendingGameEnd=null;
        base.OnShowResult(response);
        if(ready!=null) base.OnReadyStatus(ready);
        if(end!=null) base.OnGameEnd(end);
    }
    protected override void OnReadyStatus(Response response) {
        if(pendingSettlement) pendingReadyStatus=response; else base.OnReadyStatus(response);
    }
    protected override void OnGameEnd(Response response) {
        if(pendingSettlement) pendingGameEnd=response; else base.OnGameEnd(response);
    }
    private void AcceptFinalConcealedMelds(Dictionary<int,int[][]> revealed) {
        if(revealed==null) return;
        foreach(var entry in revealed) {
            var player=Mirror.Info(Mirror.SeatOf(entry.Key));
            if(player?.combination_tiles==null || player.combination_masks==null) continue;
            int kong=0;
            for(int i=0;i<player.combination_tiles.Count && kong<entry.Value.Length;i++) {
                if(!player.combination_tiles[i].StartsWith("G")) continue;
                int[] mask=entry.Value[kong++];
                if(mask==null || mask.Length!=8 || i>=player.combination_masks.Count) continue;
                player.combination_tiles[i]="G"+mask[1];
                player.combination_masks[i]=(int[])mask.Clone();
            }
        }
    }
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var env=base.BuildEnvelope(info);
        if(env.IsHu) env.AfterHandReveal=()=>RevealFinalTable(env.WinnerIndex);
        return env;
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        if(env.IsLiuju) RevealFinalTable(-1);
    }
    private void RevealFinalTable(int winner) {
        var hands=revealedHands==null ? null : new Dictionary<int,int[]>(revealedHands);
        hands?.Remove(winner);
        Game3DManager.Instance?.StartCoroutine(RevealFinalTableWhenIdle(roundGeneration,hands));
    }
    private IEnumerator RevealFinalTableWhenIdle(int generation,Dictionary<int,int[]> hands) {
        var table=Game3DManager.Instance;
        while(RuleRegistry.ActiveGameState==this && generation==roundGeneration && table!=null && table.HasPendingRecordTableAnimations) yield return null;
        if(RuleRegistry.ActiveGameState!=this || generation!=roundGeneration || table==null) yield break;
        RoundEndPresentation.Instance?.HideSelfGameplayControl(false);
        table.RevealSichuanLiujuAllHands(hands);
        foreach(string seat in Mirror.IndexToPosition.Values) {
            var masks=Mirror.Info(seat)?.combination_masks;
            var groups=table.GetPosPanel(seat)?.combination3DObjects;
            if(masks==null || groups==null) continue;
            for(int meld=0;meld<masks.Count && meld<groups.Length;meld++) {
                int[] mask=masks[meld];var group=groups[meld];
                if(mask==null || mask.Length!=8 || mask.Where((_,i)=>i%2==0).Any(flag=>flag!=2)) continue;
                if(group==null || group.childCount!=4) {UnityEngine.Debug.LogError($"长春终局暗杠显示缺失：{seat}/{meld}");continue;}
                for(int i=0;i<4;i++) {
                    int physical=mask[(3-i)*2+1];var old=group.GetChild(i);
                    if(physical<10 || old.GetComponent<Tile3D>()?.GetTileId()==physical) continue;
                    // Keep the concealed pose and all other melds, including stacked added kongs.
                    var replacement=MahjongObjectPool.Instance.Spawn(physical,old.position,old.rotation);
                    if(replacement==null) continue;
                    replacement.transform.SetParent(group,true);replacement.transform.SetSiblingIndex(i);
                    replacement.GetComponent<Tile3D>()?.ApplyCombinationPeekState(physical,2);
                    Card3DHoverManager.Instance?.RegisterCard(replacement,physical);
                    MahjongObjectPool.Instance.Return(-1,old.gameObject);
                }
            }
        }
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        var totals=Info?.round_score_changes;
        if(totals==null) {base.AppendScoreboard(env);return;}
        string winnerName=Mirror.IndexToPosition.TryGetValue(env.WinnerIndex,out string winnerPosition) ? Mirror.Info(winnerPosition)?.username??"" : "";
        var snapshot=ScoreHistorySettlementHelper.CreateFromShowResult(Session.SubRule,env.HuClass,env.WinnerIndex,winnerName,
            env.HuScore,env.FanLabels,env.WinnerHand,env.WinnerMelds,env.BaseFu,env.FuFanList,null,totals);
        int snapshotRow=-1;
        foreach(var pair in Mirror.IndexToPosition) {
            var player=Mirror.Info(pair.Value);if(player==null) continue;
            player.score_history??=new List<string>();player.round_number_history??=new List<int>();
            ScoreHistorySettlementHelper.AlignRoundNumberHistory(player.score_history,player.round_number_history);
            int row=player.round_number_history.IndexOf(Mirror.CurrentRound);
            totals.TryGetValue(player.original_player_index,out int delta);
            string value=delta>0 ? "+"+delta : delta.ToString();
            if(row>=0 && row<player.score_history.Count) player.score_history[row]=value;
            else {row=player.score_history.Count;player.score_history.Add(value);player.round_number_history.Add(Mirror.CurrentRound);}
            snapshotRow=row;
        }
        if(snapshotRow>=0) {
            while(Mirror.RoundSettlementHistory.Count<=snapshotRow) Mirror.RoundSettlementHistory.Add(null);
            Mirror.RoundSettlementHistory[snapshotRow]=snapshot;
        }
        Presenter.ApplyScores(env.ScoresAfter);GameSceneUIManager.Instance?.UpdateScoreRecord();
    }
    public override void OnSessionReset() {askClock.Reset();clockRound=-1;clockGame=null;roundGeneration++;settlementAccepted=pendingSettlement=false;pendingReadyStatus=pendingGameEnd=null;RoundEndPresentation.Instance?.StopActiveSequence();Info=null;revealedHands=null;announcedReady.Clear();GameCanvas.Instance?.SetSelfStatusIndicator(GameCanvas.StatusSlotShunhe,false,"");}
}
