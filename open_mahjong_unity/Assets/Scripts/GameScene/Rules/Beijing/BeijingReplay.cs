using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    private BeijingInfo recordBeijingInfo;
    private bool recordBeijingRobbed;
    private bool recordBeijingRecycle,recordBeijingMulti,recordBeijingEnded;
    private bool IsBeijingRecord()=>RecordRuleManifest?.RuleId=="beijing";
    private bool IsBeijingRecordRobWin=>IsBeijingRecord()&&recordBeijingRobbed;
    private readonly Dictionary<int,BeijingHintsCache> recordBeijingHints=new Dictionary<int,BeijingHintsCache>();
    internal RuleReplayModule CreateBeijingReplayModule()=>new RuleReplayModule("beijing") {
        Reset=ResetBeijingRecord,Clear=()=>{recordBeijingInfo=null;recordBeijingRobbed=recordBeijingRecycle=recordBeijingMulti=recordBeijingEnded=false;recordBeijingHints.Clear();BeijingTableIndicators.Refresh(null);},
        ApplyBeforeMutation=ApplyBeijingTick,ConsumeWallTile=ConsumeBeijingWall,
        IsSilentTick=tick=>tick.Count>1&&tick[0]=="beijing"&&tick[1]!="action",
        NextPlayer=(tick,current)=>tick.Count>2&&tick[0]=="beijing"&&tick[1]=="action"?(int)JObject.Parse(tick[2])["player"]:current,
        RefreshAfterAction=RefreshBeijingRecord,RefreshAfterSeek=RefreshBeijingRecord,
        AfterSilentMutation=_=>{SyncRecordBoardScores();if(recordBeijingEnded)RefreshBeijingRecordMelds();},
        AfterVisibleMutation=tick=>{if(tick.Count>1&&tick[0]=="beijing"&&tick[1]=="action")Game3DManager.Instance?.RefreshRecordHandDisplay();}
    };
    private void ResetBeijingRecord() {
        recordBeijingInfo=null;recordBeijingRobbed=recordBeijingRecycle=recordBeijingMulti=recordBeijingEnded=false;recordBeijingHints.Clear();
        if(gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out var round)) {
            recordBeijingInfo=JsonConvert.DeserializeObject<BeijingInfo>(JsonConvert.SerializeObject(round.beijing));
            if(round.beijingHints!=null)foreach(var pair in round.beijingHints){var cache=new BeijingHintsCache();cache.Accept(pair.Value);recordBeijingHints[pair.Key]=cache;}
        }
        RefreshBeijingRecord();
    }
    private void RefreshBeijingRecord()=>BeijingTableIndicators.Refresh(recordBeijingInfo);
    private void RefreshBeijingRecordMelds() {
        if(Game3DManager.Instance==null)return;
        foreach(var player in recordPlayerList) {
            for(int n=0;n<player.combinationMasks.Count;n++)if(player.combinationTiles[n].StartsWith("G")) {
                var mask=player.combinationMasks[n];for(int i=0;i<mask.Length;i+=2)mask[i]=0;
            }
            if(indexToPosition.TryGetValue(player.playerIndex,out string seat))Game3DManager.Instance.RebuildPlayerMelds(seat);
        }
    }
    private bool ConsumeBeijingWall(string action) {
        if(currentTilesList.Count==0)return true;
        int position=action=="d"?0:currentTilesList.Count-1,original=currentOriginalIndices[position];
        currentTilesList.RemoveAt(position);currentOriginalIndices.RemoveAt(position);
        if(original==consumedFromFront)consumedFromFront++;else consumedBackIndices.Add(original);
        return true;
    }
    private BeijingHintsCache BeijingCache(int? index) {
        int seat=index??(recordPlayer_to_info.TryGetValue("self",out var p)?p.playerIndex:-1);
        return recordBeijingHints.TryGetValue(seat,out var cache)?cache:null;
    }
    public HashSet<int> BeijingWaiting(TingpaiQuery q)=>BeijingCache(q.RecordPlayerIndex)?.Waiting(q)??new HashSet<int>();
    public WaitTileHint DescribeBeijingWaiting(WaitHintQuery q){var cache=BeijingCache(q.Record?.SelfPlayerIndex);return cache==null?WaitTileHint.None("未满足"):cache.Describe(q);}
    private bool ApplyBeijingTick(IReadOnlyList<string> tick) {
        if(!IsBeijingRecord()||tick==null||tick.Count<3)return false;
        if(tick[0].StartsWith("hu_")) {
            recordBeijingRecycle=tick.Count>8&&tick[8]=="1";recordBeijingMulti=tick.Count>6&&tick[6]=="1";
            if(recordBeijingRecycle&&!recordBeijingRobbed&&tick[0]!="hu_self")SyncRecordRonDiscardRemoved(ResolveRecordRonDiscarderPosition(int.Parse(tick[7])),int.Parse(tick[5]));
            return false;
        }
        if(tick[0]!="beijing")return false;
        switch(tick[1]) {
            case "payment": {
                var changes=JObject.Parse(tick[2])["changes"].ToObject<int[]>();
                foreach(var p in recordPlayerList){p.score+=changes[p.playerIndex];userIdToScore[p.userId]=p.score;}break;
            }
            case "waits": {
                int seat=int.Parse(tick[2]);if(!recordBeijingHints.TryGetValue(seat,out var cache))recordBeijingHints[seat]=cache=new BeijingHintsCache();
                cache.Accept(JsonConvert.DeserializeObject<BeijingHints>(tick[3]));break;
            }
            case "state": {
                recordBeijingInfo=JsonConvert.DeserializeObject<BeijingInfo>(tick[2]);var scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach(var p in recordPlayerList){p.score=scores[p.playerIndex];userIdToScore[p.userId]=p.score;}
                recordBeijingEnded=new[]{"END","waiting_ready","finished"}.Contains(recordBeijingInfo?.phase);
                if(recordBeijingEnded)foreach(var p in recordPlayerList)for(int n=0;n<p.combinationMasks.Count;n++)if(p.combinationTiles[n].StartsWith("G"))for(int i=0;i<p.combinationMasks[n].Length;i+=2)p.combinationMasks[n][i]=0;
                break;
            }
            case "win_source": {
                int actor=int.Parse(tick[2]),payer=int.TryParse(tick[3],out var v)?v:-1;lastWinnableTileId=int.Parse(tick[5]);
                recordBeijingRobbed=tick[4]=="rob_kong";lastJiagangPlayerIndex=recordBeijingRobbed?payer:-1;
                if(payer>=0)lastDiscardPlayerIndex=payer;
                if(tick.Count>6)recordBeijingInfo=JsonConvert.DeserializeObject<BeijingInfo>(tick[6]);break;
            }
            case "action": {
                var e=JObject.Parse(tick[2]);int actor=(int)e["player"];string action=(string)e["action"];
                var player=recordPlayerList.First(p=>p.playerIndex==actor);currentPlayerIndex=actor;
                if(action=="tianting"){if(!player.tagList.Contains("tianting"))player.tagList.Add("tianting");}
                else if(action=="declare_kan"){lastJiagangPlayerIndex=actor;lastWinnableTileId=(int)e["tile"];}
                else if(action=="rob_kan"){recordBeijingRobbed=true;player.tileList.Remove((int)e["tile"]);player.showHandDrawSlotActive=false;}
                break;
            }
        }
        RefreshBeijingRecord();return true;
    }
}
