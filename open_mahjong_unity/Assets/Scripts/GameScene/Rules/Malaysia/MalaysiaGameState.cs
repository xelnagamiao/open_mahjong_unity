using System.Collections.Generic;
using System.Linq;

public sealed class MalaysiaGameState : TurnBasedGameState {
    public const string RuleId="malaysia", SubRule="malaysia/traditional84";
    public static MalaysiaGameState Active => RuleRegistry.ActiveGameState as MalaysiaGameState;
    public MalaysiaInfo Info { get; private set; }
    public readonly MalaysiaHintsCache Hints = new MalaysiaHintsCache();
    private DoActionInfo actionInfo;
    private void Accept(MalaysiaInfo info, MalaysiaHints hints=null) {
        if (info != null) Info=info;
        Hints.Accept(hints);
    }
    protected override void OnRoundStarted(GameInfo info) {
        Info=null; Hints.Reset(); Accept(info.malaysia_info,info.malaysia_hints);
        RefreshFlowers();
    }
    protected override void OnAskHandAction(Response r) {
        Accept(r.ask_hand_action_info?.malaysia_info,r.ask_hand_action_info?.malaysia_hints);
        base.OnAskHandAction(r);
    }
    protected override void OnAskClaim(Response r) {
        Accept(r.game_info?.malaysia_info,r.game_info?.malaysia_hints);
        Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";
        base.OnAskClaim(r);
    }
    protected override void OnDoAction(Response r) {
        actionInfo=r.do_action_info;
        Accept(actionInfo?.malaysia_info,actionInfo?.malaysia_hints);
        base.OnDoAction(r); actionInfo=null;
        RefreshFlowers();
    }
    protected override bool TryApplyFamilyWord(TableAction a,string word) {
        if (word=="deal_buhua_tile" && a.Seat=="self" && Info?.self_has_draw_slot==false) {
            var tiles=a.ResolveDealTiles(); Mirror.RemainTiles-=tiles.Length; Mirror.LastDealTileId=0;
            foreach(int drawnTile in tiles) {
                Mirror.SelfHandTiles.Add(drawnTile);
                GameCanvas.Instance.ChangeHandCards("GetCardIntoMainHand",drawnTile,null,null);
                Game3DManager.Instance.Change3DTile("GetCard",drawnTile,0,"self",false,null);
            }
            return true;
        }
        if (word=="malaysia_payment") return true;
        if (word!="malaysia_redeem") return false;
        int n=actionInfo.malaysia_meld_index.Value, tile=actionInfo.malaysia_redeemed_tile.Value;
        var p=Mirror.Info(a.Seat);
        var mask=p.combination_masks[n];
        for (int k=1;k<mask.Length;k+=2) if(mask[k]==81) { mask[k]=tile; break; }
        if (a.Seat=="self") {
            Mirror.SelfHandTiles[Mirror.SelfHandTiles.Count-1]=81;
            Mirror.LastDealTileId=81;
            GameCanvas.Instance.ChangeHandCards("RemoveGetCard",tile,null,null);
            GameCanvas.Instance.ChangeHandCards("GetCard",81,null,null);
        }
        Game3DManager.Instance?.RebuildPlayerMelds(a.Seat);
        return true;
    }
    protected override void OnActionPlayed(TableAction a) {
        if (a.Silent || !GameCanvas.HasNonZeroGangScoreChanges(a.GangScoreChanges)) return;
        Presenter.ApplyScoreDeltas(a.GangScoreChanges);
        GameCanvas.Instance?.ShowGangScoreFloats(a.GangScoreChanges);
    }
    protected override void OnShowResult(Response r) {
        Accept(r.show_result_info?.malaysia_info); base.OnShowResult(r);
    }
    protected override void PresentSettlement(SettlementEnvelope env) {
        // Four opening flies end this hand and need the normal result/ready panel.
        // The common initial_hu presentation is for nonterminal side awards.
        if (env.HuClass=="initial_hu") env.HuClass="hu_self";
        base.PresentSettlement(env);
    }
    protected override void AppendScoreboard(SettlementEnvelope env) {
        var payment=env.ScoreChanges;
        if (Info?.round_changes!=null && Info.start_scores!=null) {
            int rows=0; bool replacing=false;
            foreach (var seat in Mirror.IndexToPosition) {
                var p=Mirror.Info(seat.Value); if(p==null) continue;
                if(Info.start_scores.TryGetValue(p.original_player_index,out int before)) p.score=before;
                if(p.round_number_history?.Count>0 && p.score_history?.Count>0 && p.round_number_history.Last()==Mirror.CurrentRound) {
                    replacing=true; rows=System.Math.Max(rows,p.score_history.Count);
                    p.round_number_history.RemoveAt(p.round_number_history.Count-1);
                    p.score_history.RemoveAt(p.score_history.Count-1);
                }
            }
            if(replacing && Mirror.RoundSettlementHistory.Count==rows) Mirror.RoundSettlementHistory.RemoveAt(rows-1);
            env.ScoreChanges=Info.round_changes;
        }
        base.AppendScoreboard(env); env.ScoreChanges=payment;
    }
    protected override void OnReadyStatus(Response r) {
        if(r.ready_status_info==null) return;
        Clock.LastAskActionTick=r.ready_status_info.action_tick;
        Accept(r.ready_status_info.malaysia_info); base.OnReadyStatus(r);
    }
    private static void RefreshFlowers() {
        GameCanvas.Instance?.SetPlayerFlowerCounts(Mirror.IndexToPosition.ToDictionary(s=>s.Key,s=>Mirror.Info(s.Value)?.huapai_list?.Count ?? 0),Mirror.IndexToPosition);
        EndResultPanel.Instance?.SetOccupiedSeats(Mirror.IndexToPosition.Values);
    }
    public override void OnSessionReset() { Info=null; Hints.Reset(); GameCanvas.Instance?.SetPlayerFlowerCounts(null,null); EndResultPanel.Instance?.SetOccupiedSeats(null); }
}
