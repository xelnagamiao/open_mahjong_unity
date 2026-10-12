using System;
using System.Collections.Generic;
using System.Linq;

public sealed class VietnameseGameState : TurnBasedGameState {
    public const string RuleId="vietnamese", Classic160="vietnamese/classic160";
    public static VietnameseGameState Active=>RuleRegistry.ActiveGameState as VietnameseGameState;
    public VietnameseInfo Info {get;private set;}
    private void Accept(GameInfo info) { if(info?.vietnamese_info!=null) Info=info.vietnamese_info; }
    protected override void OnRoundStarted(GameInfo info) {Accept(info);}
    protected override void OnAskHandAction(Response response) {Accept(response.game_info);base.OnAskHandAction(response);}
    protected override void OnAskClaim(Response response) {
        Accept(response.game_info);Clock.PendingAskFromJiagang=Info?.phase=="waiting_action_qianggang";base.OnAskClaim(response);
    }
    protected override void OnDoAction(Response response) {
        Accept(response.game_info);
        if(response.do_action_info?.action_list?.Contains("vn_bounce")==true) {
            // Reuse the tested private reconnection projection for the atomic
            // hand-to-meld / joker-to-bonus exchange, before the supplement draw.
            Manager.InitializeGame(true,"弹鬼",response.game_info);return;
        }
        base.OnDoAction(response);
    }
    protected override bool TryApplyFamilyWord(TableAction action,string word) {
        if(word=="deal_buhua_tile" && action.Seat=="self") {
            int[] tiles=action.ResolveDealTiles();
            if(tiles.Length>0 && (Mirror.SelfHandTiles.Count+tiles.Length)%3!=2) {
                Mirror.RemainTiles-=tiles.Length;Mirror.LastDealTileId=0;
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
    protected override void OnShowResult(Response response) { Info=response.show_result_info?.vietnamese_info??Info;base.OnShowResult(response); }
    protected override void OnReadyStatus(Response response) {
        if(response.ready_status_info==null)return;
        Clock.LastAskActionTick=response.ready_status_info.action_tick;
        Info=response.ready_status_info.vietnamese_info??Info;base.OnReadyStatus(response);
    }
    public override void OnSessionReset() {Info=null;}
    public static IReadOnlyList<ActionCandidate> ClaimChoices(string word) =>
        (Active?.Info?.claim_options??Array.Empty<VietnameseClaimOption>()).Select(o=>new ActionCandidate("vn_claim",o.tiles,o.id,
            (o.kind=="s"?"吃":o.kind=="k"?"碰":"杠")+" · "+VietnameseTileVisual.Name(o.face))).ToArray();
    public static IReadOnlyList<ActionCandidate> BounceChoices(string word) =>
        (Active?.Info?.bounce_candidates??Array.Empty<int>()).Select(t=>new ActionCandidate("vn_bounce",new[]{t},t,"弹"+VietnameseTileVisual.Name(t))).ToArray();
    public static IReadOnlyList<ActionCandidate> ReplacementChoices(string word) {
        var result=new List<ActionCandidate>();var held=Active?.Info?.replacement_candidates??Array.Empty<int>();
        for(int bits=1;bits<(1<<held.Length);bits++) {
            var tiles=held.Where((t,i)=>(bits&(1<<i))!=0).ToArray();
            int mask=tiles.Sum(t=>1<<Array.IndexOf(VietnameseShape.Jokers,t));
            result.Add(new ActionCandidate("vn_replace",tiles,mask,"补"+string.Join("·",tiles.Select(VietnameseTileVisual.Name))));
        }
        return result;
    }
}
