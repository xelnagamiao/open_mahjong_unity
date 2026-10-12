using System;
public sealed class FilipinoGameState : TurnBasedGameState {
    public const string RuleId="filipino", Classic16="filipino/classic16";
    public FilipinoInfo Info { get; private set; }
    private void Accept(GameInfo info) { if(info?.filipino_info!=null) Info=info.filipino_info; }
    protected override void OnRoundStarted(GameInfo info) { Accept(info); }
    protected override void OnAskHandAction(Response response) { Accept(response.game_info); base.OnAskHandAction(response); }
    protected override void OnAskClaim(Response response) { Accept(response.game_info); base.OnAskClaim(response); }
    protected override void OnDoAction(Response response) { Accept(response.game_info); base.OnDoAction(response); }
    protected override void OnShowResult(Response response) { Info=response.show_result_info?.filipino_info??Info; base.OnShowResult(response); }
    protected override void OnReadyStatus(Response response) {
        if(response.ready_status_info==null) return;
        Clock.LastAskActionTick=response.ready_status_info.action_tick;
        Info=response.ready_status_info.filipino_info??Info;
        base.OnReadyStatus(response);
    }
    protected override bool TryApplyFamilyWord(TableAction action,string word) {
        if(word=="filipino_score") {
            if(GameCanvas.HasNonZeroGangScoreChanges(action.GangScoreChanges)) {
                Presenter.ApplyScoreDeltas(action.GangScoreChanges);
                GameCanvas.Instance.ShowGangScoreFloats(action.GangScoreChanges);
            }
            return true;
        }
        if(word=="deal_buhua_tile" && action.Seat=="self") {
            int[] tiles=action.ResolveDealTiles();
            if(tiles.Length>0 && (Mirror.SelfHandTiles.Count+tiles.Length)%3!=2) {
                Mirror.RemainTiles-=tiles.Length; Mirror.LastDealTileId=0;
                foreach(int tile in tiles) {
                    Mirror.SelfHandTiles.Add(tile);
                    GameCanvas.Instance.ChangeHandCards("GetCardIntoMainHand",tile,null,null);
                    Game3DManager.Instance.Change3DTile("GetCard",tile,0,"self",false,null);
                }
                return true;
            }
        }
        return false;
    }
    public override void OnSessionReset() { Info=null; }
}
