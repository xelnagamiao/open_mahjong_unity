using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    private DuocaiInfo recordDuocaiInfo;
    private bool recordDuocaiRobbed;
    private bool IsDuocaiRecordRobWin=>RecordRuleManifest?.RuleId=="duocai"&&recordDuocaiRobbed;
    private readonly Dictionary<int,DuocaiHintsCache> recordDuocaiHints=new Dictionary<int,DuocaiHintsCache>();
    private readonly Dictionary<int,int> recordDuocaiWallIndices=new Dictionary<int,int>();
    internal RuleReplayModule CreateDuocaiReplayModule()=>new RuleReplayModule("duocai") {
        Reset=ResetDuocaiRecord,Clear=()=>{recordDuocaiInfo=null;recordDuocaiRobbed=false;recordDuocaiHints.Clear();DuocaiTableIndicators.Refresh(null);},
        ApplyBeforeMutation=ApplyDuocaiTick,ConsumeWallTile=_=>true,
        IsSilentTick=tick=>tick.Count>1&&tick[0]=="duocai"&&tick[1]!="action",
        NextPlayer=(tick,current)=>tick.Count>2&&tick[0]=="duocai"&&tick[1]=="action"?(int)JObject.Parse(tick[2])["player"]:current,
        RemainingTiles=()=>recordDuocaiInfo?.wall?.Count(t=>!t.removed&&!t.dead)??0,
        RefreshAfterAction=RefreshDuocaiRecord,RefreshAfterSeek=RefreshDuocaiRecord,
        AfterSilentMutation=tick=>SyncRecordBoardScores(),
        AfterVisibleMutation=tick=>{if(tick.Count>1&&tick[0]=="duocai"&&tick[1]=="action")RefreshDuocaiRecordMelds();}
    };
    private void ResetDuocaiRecord() {
        recordDuocaiInfo=null;recordDuocaiRobbed=false;recordDuocaiHints.Clear();recordDuocaiWallIndices.Clear();
        if(gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out var round)) {
            recordDuocaiInfo=JsonConvert.DeserializeObject<DuocaiInfo>(JsonConvert.SerializeObject(round.duocai));
            if(round.duocaiHints!=null)foreach(var pair in round.duocaiHints){var cache=new DuocaiHintsCache();cache.Accept(pair.Value);recordDuocaiHints[pair.Key]=cache;}
            if(recordDuocaiInfo?.wall!=null) {
                int index=0;foreach(var tile in recordDuocaiInfo.wall.Where(v=>!v.removed&&!v.dead).OrderBy(v=>(v.position-recordDuocaiInfo.opening+144)%144))recordDuocaiWallIndices[tile.position]=index++;
            }
        }
        RefreshDuocaiRecord();
    }
    private void RefreshDuocaiRecord() {
        var players=recordPlayerList.Select(p=>new PlayerInfo {player_index=p.playerIndex,
            duocai_hand_backs=p.tileList.Select(v=>((recordDuocaiInfo?.deck??0)+(DuocaiTiles.UnusualBack(v)?1:0))%2).ToArray(),
            duocai_meld_backs=p.combinationMasks.Select(m=>m.Where((v,i)=>i%2==1).Select(v=>((recordDuocaiInfo?.deck??0)+(DuocaiTiles.UnusualBack(v)?1:0))%2).ToArray()).ToArray()
        }).ToArray();
        DuocaiTableIndicators.Refresh(recordDuocaiInfo,players);
    }
    private void RefreshDuocaiRecordMelds() {
        if(Game3DManager.Instance==null)return;
        foreach(string seat in indexToPosition.Values)Game3DManager.Instance.RebuildPlayerMelds(seat);
        Game3DManager.Instance.RefreshRecordHandDisplay();
        if(recordPlayer_to_info.TryGetValue("self",out var self))GameCanvas.Instance?.ChangeHandCards("InitHandCardsFromRecord",0,self.tileList.ToArray(),null);
    }
    private DuocaiHintsCache DuocaiCache(int? index) {
        int seat=index??(recordPlayer_to_info.TryGetValue("self",out var player)?player.playerIndex:-1);
        return recordDuocaiHints.TryGetValue(seat,out var cache)?cache:null;
    }
    public HashSet<int> DuocaiWaiting(TingpaiQuery q)=>DuocaiCache(q.RecordPlayerIndex)?.Waiting(q)??new HashSet<int>();
    public WaitTileHint DescribeDuocaiWaiting(WaitHintQuery q) {
        var cache=DuocaiCache(q.Record?.SelfPlayerIndex);
        return cache==null?WaitTileHint.None("未满足"):cache.Describe(q);
    }
    private bool ApplyDuocaiTick(IReadOnlyList<string> tick) {
        if(tick==null||tick.Count<3||tick[0]!="duocai")return false;
        switch(tick[1]) {
            case "waits": {
                int seat=int.Parse(tick[2]);if(!recordDuocaiHints.TryGetValue(seat,out var cache))recordDuocaiHints[seat]=cache=new DuocaiHintsCache();
                cache.Accept(JsonConvert.DeserializeObject<DuocaiHints>(tick[3]));break;
            }
            case "state": {
                recordDuocaiInfo=JsonConvert.DeserializeObject<DuocaiInfo>(tick[2]);var scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach(var p in recordPlayerList){p.score=scores[p.playerIndex];userIdToScore[p.userId]=p.score;}
                foreach(var tile in recordDuocaiInfo.wall??Array.Empty<DuocaiWallTile>())if((tile.removed||tile.dead)&&recordDuocaiWallIndices.TryGetValue(tile.position,out int original)) {
                    int i=currentOriginalIndices.IndexOf(original);if(i>=0){currentOriginalIndices.RemoveAt(i);currentTilesList.RemoveAt(i);consumedBackIndices.Add(original);}
                }
                break;
            }
            case "action":ApplyDuocaiRecordAction(JObject.Parse(tick[2]));break;
        }
        RefreshDuocaiRecord();return true;
    }
    private void ApplyDuocaiRecordAction(JObject e) {
        int actor=(int)e["player"];string action=(string)e["action"];var player=recordPlayerList.First(p=>p.playerIndex==actor);
        currentPlayerIndex=actor;
        if(action=="riichi"){player.isRiichi=true;player.tagList.Add("riichi");return;}
        if(action=="declare_kan"){lastJiagangPlayerIndex=actor;lastWinnableTileId=(int)e["tile"];return;}
        if(action=="rob_kan"){recordDuocaiRobbed=true;player.tileList.Remove((int)e["tile"]);player.showHandDrawSlotActive=false;return;}
        var mask=e["combination_mask"]?.ToObject<int[]>();if(mask==null)return;
        int index=(int?)e["meld_index"]??player.combinationMasks.Count;
        var take=mask.Where((v,i)=>i%2==1).ToList();
        if(index<player.combinationMasks.Count)foreach(int value in player.combinationMasks[index].Where((v,i)=>i%2==1))take.Remove(value);
        if(e["cut_from_player"]!=null) {
            int source=(int)e["cut_from_player"],tile=(int)e["tile"];take.Remove(tile);
            var river=recordPlayerList.First(p=>p.playerIndex==source);int last=river.discardTiles.Count-1;
            if(last>=0){river.discardTiles.RemoveAt(last);if(river.discardIsMoqie.Count>last)river.discardIsMoqie.RemoveAt(last);if(river.discardRiichiFlags.Count>last)river.discardRiichiFlags.RemoveAt(last);}
        }
        foreach(int tile in take)player.tileList.Remove(tile);
        string code=(string)e["meld_code"];
        if(index<player.combinationMasks.Count){player.combinationMasks[index]=mask;player.combinationTiles[index]=code;}
        else {player.combinationMasks.Add(mask);player.combinationTiles.Add(code);}
        player.showHandDrawSlotActive=false;lastJiagangPlayerIndex=-1;lastWinnableTileId=-1;
    }
}
