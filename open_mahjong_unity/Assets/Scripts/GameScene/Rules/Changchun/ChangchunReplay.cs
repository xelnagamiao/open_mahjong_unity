using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    private int recordCcBaoSlot=-1,recordCcBaoTile,recordCcBaoRevision;
    private readonly Dictionary<int,int> recordCcSeen=new Dictionary<int,int>();
    private readonly Dictionary<int,int> recordCcTailTiles=new Dictionary<int,int>();
    private bool recordCcNoBao,recordCcRevealed;
    private string recordCcWinSource;
    private bool IsChangchunRecordRobWin=>IsChangchunRecord() && recordCcWinSource=="robbing_kong";
    private int? recordCcTailRemaining;
    private Dictionary<int,int[]> recordCcHands;
    private bool IsChangchunRecord()=>RecordRuleManifest?.RuleId=="changchun";
    private List<int> GetChangchunRecordVisibleIndicators() {
        bool visible=recordCcRevealed || (recordPlayer_to_info.TryGetValue("self",out var self)
            && recordCcSeen.TryGetValue(self.playerIndex,out int seen) && seen==recordCcBaoRevision);
        return visible && recordCcBaoTile>0 && !recordCcNoBao ? new List<int>{recordCcBaoTile} : new List<int>();
    }
    private void ResetChangchunRecordState() {
        recordCcBaoSlot=-1;recordCcBaoTile=0;recordCcBaoRevision=0;recordCcSeen.Clear();
        recordCcTailTiles.Clear();
        recordCcNoBao=false;recordCcRevealed=false;recordCcTailRemaining=null;recordCcHands=null;recordCcWinSource=null;
    }
    private RecordPlayer CcPlayer(int index)=>recordPlayerList.FirstOrDefault(p=>p.playerIndex==index);
    private void RestoreCcIndicator() {
        if(recordCcBaoSlot<0) return;
        int position=currentOriginalIndices.FindIndex(i=>i>recordCcBaoSlot);
        if(position<0) position=currentOriginalIndices.Count;
        currentOriginalIndices.Insert(position,recordCcBaoSlot);currentTilesList.Insert(position,recordCcBaoTile);
        consumedBackIndices.Remove(recordCcBaoSlot);recordCcBaoSlot=-1;
    }
    private void RemoveCcWonDiscard(IReadOnlyList<string> tick) {
        if(tick.Count<8 || !tick[0].StartsWith("hu_") || tick[0]=="hu_self") return;
        int field=tick.Count>=9?7:6;
        if(tick[field+1]!="1" || !int.TryParse(tick[field],out int payer) || !int.TryParse(tick[5],out int tile)) return;
        var player=CcPlayer(payer);
        if(player!=null && player.discardTiles.Count>0 && player.discardTiles.Last()==tile)
            RemoveClaimedDiscardFromRecordRiver(player,tile,capturePendingRiichiHorizontal:false);
    }
    private bool ApplyChangchunRecordAction(IReadOnlyList<string> tick) {
        if(!IsChangchunRecord() || tick==null || tick.Count==0) return false;
        RemoveCcWonDiscard(tick);
        if(tick[0]!="cc" || tick.Count<2) return false;
        var e=JObject.Parse(tick[1]);string kind=(string)e["kind"];
        int actor=(int?)e["player"]??-1;var player=CcPlayer(actor);
        if(kind=="bao_reveal" || kind=="bao_change") {
            RestoreCcIndicator();recordCcBaoRevision=(int?)e["revision"]??0;
            // Delayed spectators get redacted ticks until the table closes.
            if(e["slot"]!=null && e["tile"]!=null) {
                recordCcBaoSlot=(int)e["slot"];recordCcBaoTile=(int)e["tile"];
                int position=currentOriginalIndices.IndexOf(recordCcBaoSlot);
                if(position>=0) {currentOriginalIndices.RemoveAt(position);currentTilesList.RemoveAt(position);consumedBackIndices.Add(recordCcBaoSlot);}
                recordCcSeen[actor]=recordCcBaoRevision;
            }
        } else if(kind=="bao_seen") recordCcSeen[actor]=(int)e["revision"];
        else if(kind=="bao_exhausted") {RestoreCcIndicator();recordCcBaoTile=0;recordCcNoBao=true;}
        else if(kind=="special" && player!=null) {
            int[] physical=e["physical"].ToObject<int[]>();
            foreach(int t in physical) player.tileList.Remove(t);
            player.combinationTiles.Add((string)e["code"]);
            player.combinationMasks.Add(physical.SelectMany(t=>new[]{0,t}).ToArray());
            player.showHandDrawSlotActive=(bool?)e["has_draw_slot"]??false;
        } else if(kind=="added_offer" && player!=null) {
            player.tileList.Remove((int)e["tile"]);player.showHandDrawSlotActive=false;
            lastWinnableTileId=(int)e["tile"];lastJiagangPlayerIndex=actor;
        } else if(kind=="added_commit" && player!=null) {
            int pos=(int)e["position"];string code=(string)e["code"];
            player.combinationTiles[pos]=code;
            player.combinationMasks[pos]=ChangchunHandCalculator.MeldTiles(code).SelectMany(t=>new[]{0,t}).ToArray();
        } else if(kind=="added_robbed" && player!=null && (bool?)e["special"]!=true) {
            int pos=player.combinationMasks.FindIndex(m=>m!=null && Enumerable.Range(0,m.Length/2).Any(i=>m[2*i]==3 && m[2*i+1]==(int?)e["tile"]));
            if(pos>=0) {
                var mask=player.combinationMasks[pos].ToList();int offset=Enumerable.Range(0,mask.Count/2).First(i=>mask[2*i]==3)*2;
                int tile=mask[offset+1];mask.RemoveRange(offset,2);player.combinationMasks[pos]=mask.ToArray();player.combinationTiles[pos]="k"+tile;
            }
        } else if(kind=="rob_claim" && player!=null) {
            int tile=(int)e["tile"];player.discardTiles.Add(tile);lastDiscardPlayerIndex=actor;lastDiscardTileId=tile;
        } else if(kind=="tail_pass" && player!=null) {
            int tile=(int?)e["tile"]??0;
            recordCcTailTiles[actor]=tile;
            if(tile>0) player.tileList.Remove(tile);
            player.showHandDrawSlotActive=false;
        } else if(kind=="kong_score") {
            var changes=e["delta"].ToObject<Dictionary<int,int>>();
            ApplyScoreDeltas(changes,out _,out _);
        } else if(kind=="round_reveal") {
            recordCcRevealed=true;recordCcBaoTile=(int?)e["bao_tile"]??0;
            if(e["tail_tiles"] is JArray tail) foreach(var item in tail) recordCcTailTiles[(int)item["player"]]=(int)item["tile"];
            recordCcHands=e["hands"]?.ToObject<Dictionary<int,int[]>>();
            if(recordCcHands!=null) foreach(var item in recordCcHands) {var rp=CcPlayer(item.Key);if(rp!=null) rp.tileList=new List<int>(item.Value);}
        } else if(kind=="settlement") {
            recordCcWinSource=(string)e["source"];
            if(recordCcWinSource=="bao_indicator") {var winner=CcPlayer((int)e["winner"]);winner.tileList.Add((int)e["tile"]);winner.showHandDrawSlotActive=true;}
        }
        return true;
    }
    private int ResolveChangchunRecordNextPlayer(IReadOnlyList<string> tick,int fallback) {
        if(!IsChangchunRecord() || tick.Count<2 || tick[0]!="cc") return fallback;
        var e=JObject.Parse(tick[1]);return (string)e["kind"]=="tail_pass" ? ((int)e["player"]+1)%4 : fallback;
    }
    private void RefreshChangchunRecordReveals() {
        if(IsChangchunRecord() && recordCcRevealed && recordCcHands!=null)
            Game3DManager.Instance?.RevealSichuanLiujuAllHands(recordPlayerList.ToDictionary(p=>p.playerIndex,p=>p.tileList.ToArray()));
    }
    private bool ConsumeChangchunRecordWall(string action) {
        if(!IsChangchunRecord() || (action!="d" && action!="gd") || currentTilesList.Count==0) return false;
        if(action=="d" && !recordCcTailRemaining.HasValue && currentTilesList.Count/2<=9) recordCcTailRemaining=4;
        int pos=action=="d" ? 0 : currentTilesList.Count-(currentTilesList.Count%2==0?2:1);
        int original=currentOriginalIndices[pos];currentTilesList.RemoveAt(pos);currentOriginalIndices.RemoveAt(pos);
        if(action=="d") {consumedFromFront++;if(recordCcTailRemaining.HasValue) recordCcTailRemaining--;}
        else consumedBackIndices.Add(original);
        return true;
    }
    private int ChangchunRecordRemaining()=>recordCcTailRemaining??Math.Max(0,currentTilesList.Count-14-currentTilesList.Count%2);
    private void RefreshChangchunRecordBadge() {
        if(!IsChangchunRecord()) return;
        bool visible=recordCcRevealed || recordCcSeen.TryGetValue(selectedPlayerIndex,out int revision) && revision==recordCcBaoRevision;
        string text=recordCcNoBao ? "无宝" : visible && recordCcBaoTile>0 ? "宝："+ChangchunRuleBootstrap.TileName(recordCcBaoTile) : "宝牌未可见";
        bool finalFour=recordCcTailRemaining.HasValue;
        if(finalFour) text+="\n末四张";
        GameCanvas.Instance?.SetSelfStatusIndicator(GameCanvas.StatusSlotShunhe,true,$"<size={(finalFour ? 16 : 24)}>{text}</size>");
        int self=recordPlayer_to_info.TryGetValue("self",out var player) ? player.playerIndex : -1;
        ChangchunTailTiles.Render(recordCcTailTiles.ToDictionary(t=>t.Key,t=>recordCcRevealed || t.Key==self ? t.Value : 0),indexToPosition,recordCcRevealed);
    }
    private void RefreshChangchunRecordEvent(IReadOnlyList<string> tick) {
        if(!IsChangchunRecord() || tick==null || tick.Count<2 || tick[0]!="cc") return;
        string kind=(string)JObject.Parse(tick[1])["kind"];
        if(new[]{"special","added_offer","added_commit","added_robbed","tail_pass","rob_claim","settlement"}.Contains(kind)) {
            Game3DManager.Instance.Clear3DTile();
            GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info["self"].tileList.ToArray(),null);
            Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
            RebuildRecord3DTableWithoutAnimation();
        }
        RefreshChangchunRecordReveals();
        SyncRecordBoardScores();RefreshChangchunRecordBadge();
    }
    private void PlayChangchunRecordAnnouncement(IReadOnlyList<string> tick) {
        if(!IsChangchunRecord() || tick==null) return;
        if(tick.Count>=5 && tick[0]=="state" && tick[1]=="ready" && tick[4]=="T") {
            int index=int.Parse(tick[2]);var player=CcPlayer(index);
            SoundManager.Instance?.PlayActionSound(indexToPosition[index],"riichi",player.voice_used);
        } else if(tick.Count>=2 && tick[0]=="cc") {
            var e=JObject.Parse(tick[1]);string kind=(string)e["kind"];
            if(kind=="special" || kind=="added_commit") {
                int index=(int)e["player"];SoundManager.Instance?.PlayActionSound(indexToPosition[index],"gang",CcPlayer(index).voice_used);
            }
        }
    }
}
