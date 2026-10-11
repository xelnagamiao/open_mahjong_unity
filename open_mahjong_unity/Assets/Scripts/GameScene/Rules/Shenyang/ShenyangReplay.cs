using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    private readonly ShenyangHintsCache[] recordSyHints={new ShenyangHintsCache(),new ShenyangHintsCache(),new ShenyangHintsCache(),new ShenyangHintsCache()};
    private readonly Dictionary<int,int> recordSyTail=new Dictionary<int,int>();
    private bool recordSyRevealed;
    private bool recordSyRobbed;
    private bool IsShenyangRecordRobWin=>RecordRuleManifest?.RuleId=="shenyang" && recordSyRobbed;
    internal RuleReplayModule CreateShenyangReplayModule() => new RuleReplayModule {
        ReservedWallTiles=0, StateActions={"sy"},
        Reset=ResetShenyangRecord, Clear=ResetShenyangRecord,
        ApplyBeforeMutation=ApplyShenyangRecord,
        AfterVisibleMutation=RefreshShenyangRecord,
        RefreshAfterSeek=()=>RefreshShenyangRecord(null),
        NextPlayer=(tick,fallback)=>tick.Count>=2 && tick[0]=="sy" && (string)JObject.Parse(tick[1])["kind"]=="tail_pass" ? ((int)JObject.Parse(tick[1])["player"]+1)%4 : fallback,
        ConsumeWallTile=action=> {
            if((action!="d" && action!="gd") || currentTilesList.Count==0) return false;
            int pos=action=="d"?0:currentTilesList.Count-1;
            int original=currentOriginalIndices[pos];currentTilesList.RemoveAt(pos);currentOriginalIndices.RemoveAt(pos);
            if(action=="d") consumedFromFront++;else consumedBackIndices.Add(original);
            return true;
        }
    };
    private void ResetShenyangRecord() {foreach(var h in recordSyHints) h.Reset();recordSyTail.Clear();recordSyRevealed=recordSyRobbed=false;}
    public HashSet<int> ShenyangWaiting(TingpaiQuery q) {
        var result=new HashSet<int>();
        if(q?.RecordPlayerIndex is int seat && seat>=0 && seat<4) return recordSyHints[seat].Waiting(q);
        foreach(var h in recordSyHints) result.UnionWith(h.Waiting(q));
        return result;
    }
    public WaitTileHint DescribeShenyangWaiting(WaitHintQuery q) {
        int seat=q?.Record?.SelfPlayerIndex??q?.SelfIndex??-1;
        return seat>=0 && seat<4?recordSyHints[seat].Describe(q):null;
    }
    private bool ApplyShenyangRecord(IReadOnlyList<string> tick) {
        if(tick==null || tick.Count<2) return false;
        if(tick[0].StartsWith("hu_") && tick[0]!="hu_self" && tick.Count>=8) {
            int field=tick.Count>=9?7:6;
            if(tick[field+1]=="1" && int.TryParse(tick[field],out int payer) && int.TryParse(tick[5],out int tile)) {
                var source=recordPlayerList.FirstOrDefault(p=>p.playerIndex==payer);
                if(source?.discardTiles?.LastOrDefault()==tile) RemoveClaimedDiscardFromRecordRiver(source,tile,capturePendingRiichiHorizontal:false);
            }
        }
        if(tick[0]!="sy") return false;
        var e=JObject.Parse(tick[1]);string kind=(string)e["kind"];
        int actor=(int?)e["player"]??-1;
        var player=recordPlayerList.FirstOrDefault(p=>p.playerIndex==actor);
        if(kind=="hints" && actor>=0 && actor<4) recordSyHints[actor].Accept(e["hints"].ToObject<ShenyangHints>());
        else if(kind=="special" && player!=null) {
            foreach(int t in e["physical"].ToObject<int[]>()) player.tileList.Remove(t);
            player.combinationTiles.Add((string)e["code"]);player.combinationMasks.Add(e["mask"].ToObject<int[]>());
            player.showHandDrawSlotActive=(string)e["code"]=="Ydragon";
        } else if(kind=="robbed" && player!=null) {
            recordSyRobbed=true;
            player.combinationTiles=e["melds"].ToObject<List<string>>();
            player.combinationMasks=e["masks"].ToObject<List<int[]>>();
        } else if(kind=="tail_pass" && player!=null) {
            int tile=(int)e["tile"];player.tileList.Remove(tile);player.showHandDrawSlotActive=false;recordSyTail[actor]=tile;
        } else if(kind=="egg") ApplyScoreDeltas(e["changes"].ToObject<Dictionary<int,int>>(),out _,out _);
        else if(kind=="settlement") {recordSyRevealed=true;foreach(var item in e["tail_tiles"].ToObject<Dictionary<int,int>>())recordSyTail[item.Key]=item.Value;}
        return true;
    }
    private void RefreshShenyangRecord(IReadOnlyList<string> tick) {
        if(RecordRuleManifest?.RuleId!="shenyang") return;
        if(tick!=null && tick.Count>=2 && tick[0]=="sy") {
            string kind=(string)JObject.Parse(tick[1])["kind"];
            if(kind=="special" || kind=="tail_pass" || kind=="robbed") {
                Game3DManager.Instance?.Clear3DTile();
                GameCanvas.Instance?.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info["self"].tileList.ToArray(),null);
                Game3DManager.Instance?.Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
                RebuildRecord3DTableWithoutAnimation();
            }
        }
        int self=recordPlayer_to_info.TryGetValue("self",out var p)?p.playerIndex:-1;
        ShenyangTailTiles.Render(recordSyTail.ToDictionary(t=>t.Key,t=>recordSyRevealed || t.Key==self?t.Value:0),indexToPosition,recordSyRevealed);
        SyncRecordBoardScores();
    }
}
