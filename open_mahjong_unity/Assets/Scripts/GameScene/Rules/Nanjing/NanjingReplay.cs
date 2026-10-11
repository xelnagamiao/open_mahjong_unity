using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateNanjingReplayModule()=>new RuleReplayModule("nanjing") {
        Reset=ResetNanjingRecordState,Clear=ResetNanjingRecordState,
        ApplyBeforeMutation=ApplyNanjingRecordAction,
        RefreshAfterAction=RefreshNanjingRecordFlowers,
        RefreshPlayerTags=RefreshNanjingRecordFlowers,
        RefreshAfterSeek=()=>{
            if(recordNanjingSourceRemoved || recordNanjingWinSource=="self_draw") {
                lastWinnableTileId=-1;lastJiagangPlayerIndex=-1;
            }
        },
        AfterSilentMutation=tick=>SyncRecordBoardScores(),
        InitializePlayers=()=>{foreach(var player in recordPlayerList) player.showHandDrawSlotActive=player.tileList.Count==14;},
    };
    private NanjingInfo recordNanjingInfo;
    private string recordNanjingWinSource;
    private bool recordNanjingForced,recordNanjingSourceRemoved;
    private readonly NanjingHintsCache[] recordNanjingHints={new NanjingHintsCache(),new NanjingHintsCache(),new NanjingHintsCache(),new NanjingHintsCache()};
    private bool IsNanjingRecord()=>RecordRuleManifest?.RuleId==NanjingGameState.RuleId;
    private bool IsNanjingRecordRobWin=>IsNanjingRecord() && recordNanjingWinSource=="rob_kong";
    private bool IsNanjingRecordForcedWin=>IsNanjingRecord() && recordNanjingForced;
    private void ResetNanjingRecordState() {
        recordNanjingInfo=null;recordNanjingWinSource=null;recordNanjingForced=recordNanjingSourceRemoved=false;
        foreach(var cache in recordNanjingHints) cache.Reset();
        if(IsNanjingRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out Round round)) recordNanjingInfo=round.nanjing;
    }
    private void RefreshNanjingRecordFlowers() {
        GameCanvas.Instance?.SetPlayerFlowerCounts(IsNanjingRecord()?recordPlayerList.ToDictionary(p=>p.playerIndex,p=>p.huapaiList?.Count??0):null,indexToPosition);
    }
    public HashSet<int> NanjingWaiting(TingpaiQuery query) {
        if(!IsNanjingRecord() || query==null) return new HashSet<int>();
        int? seat=query.RecordPlayerIndex;
        if(!seat.HasValue && recordPlayer_to_info.TryGetValue("self",out RecordPlayer self)) seat=self.playerIndex;
        return seat.HasValue && seat.Value>=0 && seat.Value<4 ? recordNanjingHints[seat.Value].Waiting(query) : new HashSet<int>();
    }
    public WaitTileHint DescribeNanjingWaiting(WaitHintQuery query) {
        if(!IsNanjingRecord() || query==null) return null;
        int seat=query.Record?.SelfPlayerIndex??query.SelfIndex;
        return seat>=0 && seat<4 ? recordNanjingHints[seat].Describe(query) : null;
    }
    private bool ApplyNanjingRecordAction(IReadOnlyList<string> tick) {
        if(!IsNanjingRecord() || tick==null || tick.Count<3 || tick[0]!="nanjing") return false;
        switch(tick[1]) {
            case "hints": {
                var hints=JsonConvert.DeserializeObject<NanjingHints[]>(tick[2]);
                if(hints?.Length==4) for(int i=0;i<4;i++) recordNanjingHints[i].Accept(hints[i]);
                break;
            }
            case "win_source": {
                int actor=int.Parse(tick[2]),payer=int.Parse(tick[3]),tile=int.Parse(tick[5]);
                recordNanjingWinSource=tick[4];recordNanjingForced=bool.Parse(tick[7]);
                recordNanjingInfo=JsonConvert.DeserializeObject<NanjingInfo>(tick[6]);
                recordPlayerList.First(p=>p.playerIndex==actor).isHu=true;
                lastWinnableTileId=recordNanjingForced?-1:tile;
                lastJiagangPlayerIndex=recordNanjingWinSource=="rob_kong"?payer:-1;
                lastDiscardPlayerIndex=payer>=0?payer:-1;
                if(payer>=0 && !recordNanjingForced && !recordNanjingSourceRemoved) {
                    var source=recordPlayerList.First(p=>p.playerIndex==payer);
                    if(recordNanjingWinSource=="rob_kong") {source.tileList.Remove(tile);source.showHandDrawSlotActive=false;}
                    else if(recordNanjingWinSource=="discard" && bool.Parse(tick[8])) {
                        int at=source.discardTiles.Count-1;
                        if(at>=0 && source.discardTiles[at]==tile) {
                            source.discardTiles.RemoveAt(at);
                            if(source.discardIsMoqie.Count>at) source.discardIsMoqie.RemoveAt(at);
                            if(source.discardRiichiFlags.Count>at) source.discardRiichiFlags.RemoveAt(at);
                        }
                    }
                    recordNanjingSourceRemoved=true;
                }
                break;
            }
            case "score": {
                var entry=JsonConvert.DeserializeObject<NanjingScoreEvent>(tick[2]);
                if(entry?.changes?.Length==4) foreach(var player in recordPlayerList) {
                    player.score+=entry.changes[player.playerIndex];userIdToScore[player.userId]=player.score;
                }
                break;
            }
            case "state": {
                recordNanjingInfo=JsonConvert.DeserializeObject<NanjingInfo>(tick[2]);
                var scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                if(scores?.Length==4) foreach(var player in recordPlayerList) {player.score=scores[player.playerIndex];userIdToScore[player.userId]=player.score;}
                break;
            }
        }
        return true;
    }
}
