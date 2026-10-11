using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public sealed class ShenyangGameState : TurnBasedGameState {
    public static ShenyangGameState Active => RuleRegistry.ActiveGameState as ShenyangGameState;
    public ShenyangInfo Info { get; private set; }
    public readonly ShenyangHintsCache Hints=new ShenyangHintsCache();
    private Dictionary<int,int[]> revealedHands;
    private readonly HashSet<int> readyAnnounced=new HashSet<int>();
    private int roundGeneration, eggPublicTile;
    private bool settlementAccepted, pendingSettlement;
    private Response pendingReadyStatus, pendingGameEnd;
    public override bool IsSelfLocked => SeatHasTag("self",t=>t=="declared_ready") || Info?.final_four==true || Info?.egg_offer!=null;
    private void Accept(ShenyangInfo info) {
        if(info==null) return;
        Info=info;Hints.Accept(info.hints);
        RefreshSelfStatusIndicators();
        if(Session.Tips) TipsContainer.Instance?.RefreshTenpaiTipsIfCached(syncHandFromLiveState:true);
    }
    protected override void OnRoundStarted(GameInfo info) {
        roundGeneration++;eggPublicTile=0;settlementAccepted=pendingSettlement=false;pendingReadyStatus=pendingGameEnd=null;
        Hints.Reset();revealedHands=null;readyAnnounced.Clear();Accept(info.shenyang);
        Mirror.LastDealTileId=Info?.self_has_draw_slot==true ? Info.self_last_drawn_tile : 0;
        foreach(var pair in Mirror.IndexToPosition)
            if(SeatHasTag(pair.Value,t=>t=="declared_ready")) readyAnnounced.Add(pair.Key);
        if(Session.Tips && Mirror.Self!=null) TipsBlock.Instance?.ShowTipsBlock(Mirror.SelfHandTiles,Mirror.Self.combination_tiles??new List<string>());
    }
    protected override void OnHandAskReceived(AskHandActionGBInfo info) { Accept(info.shenyang); }
    protected override bool TryApplyFamilyWord(TableAction action,string word) => word=="sy_state" || base.TryApplyFamilyWord(action,word);
    public override void OnPlayerTagsRefreshed() {
        base.OnPlayerTagsRefreshed();
        foreach(var pair in Mirror.IndexToPosition)
            if(SeatHasTag(pair.Value,t=>t=="declared_ready") && readyAnnounced.Add(pair.Key)) SoundManager.Instance?.PlayActionSound(pair.Value,"riichi");
    }
    public override void RefreshSelfStatusIndicators() {
        string text=Info?.final_four==true ? "分张：只和不打" : Info?.egg_offer!=null ? "选择是否公示过蛋" : Info?.long_hair==true ? "长毛" : "";
        if(Info?.passed_fan>=0) text+="\n顺和：仅自摸或更高番点和";
        if(eggPublicTile>0) text+="\n过蛋："+ChangchunRuleBootstrap.TileName(eggPublicTile);
        text=text.Trim();
        GameCanvas.Instance?.SetSelfStatusIndicator(GameCanvas.StatusSlotShunhe,text.Length>0,"<size=18>"+text+"</size>");
        ShenyangTailTiles.Render(Info?.tail_tiles,Mirror.IndexToPosition,revealedHands!=null);
    }
    protected override void OnDoAction(Response response) {
        base.OnDoAction(response);
        Accept(response.do_action_info?.shenyang);
        var e=Info?.@event;
        if(e==null) return;
        string kind=(string)e["kind"];
        int actor=(int?)e["player"]??-1;
        foreach(var snapshot in Info.players??Array.Empty<ShenyangPlayerSnapshot>()) {
            if(snapshot.player!=actor || (kind!="special" && kind!="tail_pass" && kind!="robbed")) continue;
            string seat=Mirror.SeatOf(actor);
            var player=Mirror.Info(seat);
            if(player==null) continue;
            int removed=Math.Max(0,player.hand_tiles_count-snapshot.hand_count);
            var tiles=kind=="special" ? e["physical"].ToObject<int[]>() : new[]{(int?)e["tile"]??0};
            if(seat=="self" && snapshot.hand!=null) {
                Mirror.SelfHandTiles=new List<int>(snapshot.hand);
                int standing=snapshot.hand.Length-(snapshot.has_draw_slot?1:0);
                GameCanvas.Instance?.ChangeHandCards("SyncHandCards",0,snapshot.hand.Take(standing).ToArray(),null);
                if(snapshot.has_draw_slot) GameCanvas.Instance?.ChangeHandCards("GetCard",snapshot.last_drawn_tile,null,null);
                Mirror.LastDealTileId=snapshot.has_draw_slot?snapshot.last_drawn_tile:0;
            }
            player.hand_tiles_count=snapshot.hand_count;
            player.combination_tiles=new List<string>(snapshot.melds??Array.Empty<string>());
            player.combination_masks=new List<int[]>(snapshot.masks??Array.Empty<int[]>());
            if(kind=="special") {
                Game3DManager.Instance?.PlayFreeMeld(seat,player.combination_masks.Last(),player.combination_masks.Count-1,tiles,null,0);
                SoundManager.Instance?.PlayActionSound(seat,"gang");
                GameCanvas.Instance?.ShowActionDisplay(seat,"gang",Session.RoomRule);
            } else if(kind=="tail_pass") for(int i=0;i<removed;i++) Game3DManager.Instance?.RemoveHongqueKongHandTile(seat,i<tiles.Length && tiles[i]>0?tiles[i]:11);
        }
        if(kind=="egg") {
            var changes=e["changes"].ToObject<Dictionary<int,int>>();
            Presenter.ApplyScoreDeltas(changes);GameCanvas.Instance?.ShowGangScoreFloats(changes);
            eggPublicTile=(int)e["tile"];
        }
        RefreshSelfStatusIndicators();
    }
    protected override void OnShowResult(Response response) {
        if(response.show_result_info==null || settlementAccepted) return;
        settlementAccepted=true;
        revealedHands=response.show_result_info?.revealed_hands;
        Accept(response.show_result_info?.shenyang);
        if(Game3DManager.Instance!=null) {
            pendingSettlement=true;Presenter.CloseTableForSettlement();
            Game3DManager.Instance.StartCoroutine(ShowResultWhenIdle(response,roundGeneration));
            return;
        }
        base.OnShowResult(response);
    }
    private IEnumerator ShowResultWhenIdle(Response response,int generation) {
        while(RuleRegistry.ActiveGameState==this && generation==roundGeneration &&
            (Game3DManager.Instance?.HasPendingRecordTableAnimations==true || GameCanvas.Instance?.IsChangeHandCardProcessing==true)) yield return null;
        if(RuleRegistry.ActiveGameState!=this || generation!=roundGeneration) yield break;
        var revealed=response.show_result_info?.revealed_angang_masks;
        if(revealed!=null) foreach(var entry in revealed) {
            var p=Mirror.Info(Mirror.SeatOf(entry.Key));
            if(p?.combination_tiles==null || p.combination_masks==null) continue;
            int kong=0;
            for(int i=0;i<p.combination_tiles.Count && kong<entry.Value.Length;i++) {
                if(!p.combination_tiles[i].StartsWith("G")) continue;
                int[] mask=entry.Value[kong++];
                if(mask?.Length!=8 || i>=p.combination_masks.Count) continue;
                var copy=(int[])mask.Clone();for(int j=0;j<copy.Length;j+=2) copy[j]=0;
                p.combination_tiles[i]="G"+copy[1];p.combination_masks[i]=copy;
            }
        }
        foreach(string seat in Mirror.IndexToPosition.Values) Game3DManager.Instance?.RebuildPlayerMelds(seat);
        pendingSettlement=false;
        var ready=pendingReadyStatus;var end=pendingGameEnd;pendingReadyStatus=pendingGameEnd=null;
        base.OnShowResult(response);
        if(ready!=null) base.OnReadyStatus(ready);
        if(end!=null) base.OnGameEnd(end);
    }
    protected override void OnReadyStatus(Response response) {if(pendingSettlement) pendingReadyStatus=response;else base.OnReadyStatus(response);}
    protected override void OnGameEnd(Response response) {if(pendingSettlement) pendingGameEnd=response;else base.OnGameEnd(response);}
    protected override SettlementEnvelope BuildEnvelope(ShowResultInfo info) {
        var env=base.BuildEnvelope(info);
        if(env.IsHu && revealedHands!=null) env.AfterHandReveal=()=> {
            var other=new Dictionary<int,int[]>(revealedHands);other.Remove(env.WinnerIndex);
            Game3DManager.Instance?.RevealSichuanLiujuAllHands(other);
        };
        return env;
    }
    protected override void OnTableClosedForSettlement(SettlementEnvelope env) {
        if(env.IsLiuju && revealedHands!=null) Game3DManager.Instance?.RevealSichuanLiujuAllHands(revealedHands);
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        var totals=Info?.round_score_changes;
        if(totals==null) {base.AppendScoreboard(env);return;}
        string winnerName=Mirror.IndexToPosition.TryGetValue(env.WinnerIndex,out string pos)?Mirror.Info(pos)?.username??"":"";
        var snapshot=ScoreHistorySettlementHelper.CreateFromShowResult(Session.SubRule,env.HuClass,env.WinnerIndex,winnerName,
            env.HuScore,env.FanLabels,env.WinnerHand,env.WinnerMelds,env.BaseFu,env.FuFanList,null,totals);
        int row=-1;
        foreach(var pair in Mirror.IndexToPosition) {
            var p=Mirror.Info(pair.Value);
            if(p==null) continue;
            p.score_history??=new List<string>();p.round_number_history??=new List<int>();
            ScoreHistorySettlementHelper.AlignRoundNumberHistory(p.score_history,p.round_number_history);
            row=p.round_number_history.IndexOf(Mirror.CurrentRound);
            totals.TryGetValue(p.original_player_index,out int delta);
            string value=delta>0?"+"+delta:delta.ToString();
            if(row>=0 && row<p.score_history.Count) p.score_history[row]=value;
            else {row=p.score_history.Count;p.score_history.Add(value);p.round_number_history.Add(Mirror.CurrentRound);}
        }
        if(row>=0) {while(Mirror.RoundSettlementHistory.Count<=row) Mirror.RoundSettlementHistory.Add(null);Mirror.RoundSettlementHistory[row]=snapshot;}
        Presenter.ApplyScores(env.ScoresAfter);
        GameSceneUIManager.Instance?.UpdateScoreRecord();
    }
    public override void OnSessionReset() {roundGeneration++;eggPublicTile=0;Info=null;Hints.Reset();revealedHands=null;readyAnnounced.Clear();base.OnSessionReset();}
}
