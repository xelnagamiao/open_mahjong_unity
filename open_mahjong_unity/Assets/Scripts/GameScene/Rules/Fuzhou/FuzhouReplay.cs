using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateFuzhouReplayModule()=>new RuleReplayModule("fuzhou") {
        ReservedWallTiles=18, Reset=ResetFuzhouRecordState,
        Clear=()=>{recordFuzhouInfo=null;recordFuzhouWinSource=null;recordFuzhouForced=false;recordFuzhouWinTick=null;recordFuzhouWinApplied=false;FuzhouTableIndicators.Refresh(null);GameCanvas.Instance?.SetFuzhouPlayerStatus(null,null);},
        ApplyBeforeMutation=ApplyFuzhouRecordAction,
        RefreshAfterAction=RefreshFuzhouRecordPresentation,
        RefreshPlayerTags=RefreshFuzhouRecordPresentation,
        RefreshAfterSeek=()=>{RefreshFuzhouRecordPresentation();if(recordFuzhouWinApplied){lastWinnableTileId=-1;lastJiagangPlayerIndex=-1;}},
        AfterSilentMutation=PresentFuzhouSilentAction,
        InitializePlayers=()=>{foreach(var player in recordPlayerList) player.showHandDrawSlotActive=player.tileList.Count==17;},
    };
    private FuzhouInfo recordFuzhouInfo;
    private string recordFuzhouWinSource;
    private bool recordFuzhouForced;
    private string[] recordFuzhouWinTick;
    private bool recordFuzhouWinApplied;
    private readonly FuzhouHintsCache[] recordFuzhouHints={new FuzhouHintsCache(),new FuzhouHintsCache(),new FuzhouHintsCache(),new FuzhouHintsCache()};
    private bool IsFuzhouRecord()=>RecordRuleManifest?.RuleId==FuzhouGameState.RuleId;
    private bool IsFuzhouV2Record=>recordFuzhouInfo?.rule_version==FuzhouGameState.RuleVersion;
    private string FuzhouRecordRoundName(int number) {
        var rounds=gameRecord?.gameRound?.rounds;
        bool legacy=rounds!=null && rounds.TryGetValue(currentRoundIndex,out Round round)
            && round.fuzhou?.rule_version=="fuzhou-user-20261014-v1";
        return legacy?FuzhouRuleBootstrap.LegacyRoundName(number):FuzhouRuleBootstrap.RoundName(number);
    }
    private bool IsFuzhouRecordRobWin=>IsFuzhouRecord() && recordFuzhouWinSource=="rob_kong";
    private bool IsFuzhouRecordForcedWin=>IsFuzhouRecord() && recordFuzhouForced;
    private List<int> FuzhouRecordVisibleIndicators()=>recordFuzhouInfo?.gold_tile>0 && !recordFuzhouInfo.indicator_claimed
        ?new List<int>{recordFuzhouInfo.gold_tile}:new List<int>();
    private void ResetFuzhouRecordState() {
        recordFuzhouInfo=null;recordFuzhouWinSource=null;recordFuzhouForced=false;recordFuzhouWinTick=null;recordFuzhouWinApplied=false;
        foreach(var cache in recordFuzhouHints) cache.Reset();
        if(IsFuzhouRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out Round round))
            recordFuzhouInfo=JsonConvert.DeserializeObject<FuzhouInfo>(JsonConvert.SerializeObject(round.fuzhou));
        FuzhouTableIndicators.Refresh(recordFuzhouInfo);
        GameCanvas.Instance?.SetFuzhouPlayerStatus(recordFuzhouInfo,indexToPosition);
    }
    private void RefreshFuzhouRecordPresentation() {
        if(!IsFuzhouRecord()) return;
        // A normal meld/buhua tick also changes the reserve before the next
        // custom snapshot. Recompute from the public physical tiles.
        recordDeadWallCount=18+recordPlayerList.Sum(p=>
            (p.combinationTiles?.Sum(m=>m.StartsWith("G")?2:m.StartsWith("g")?1:0)??0)
            +(p.huapaiList?.Where(t=>t>=41 && t<=47).GroupBy(t=>t).Count(g=>g.Count()==4)??0)*2
            +(IsFuzhouV2Record && p.huapaiList!=null
                ?new[]{51,55}.Count(start=>Enumerable.Range(start,4).All(p.huapaiList.Contains))*2:0));
        GameCanvas.Instance?.SetPlayerFlowerCounts(recordPlayerList.ToDictionary(p=>p.playerIndex,p=>p.huapaiList?.Count??0),indexToPosition);
        GameCanvas.Instance?.SetFuzhouPlayerStatus(recordFuzhouInfo,indexToPosition);
        if(recordFuzhouInfo!=null) recordFuzhouInfo.reserved_tiles=recordDeadWallCount;
        FuzhouTableIndicators.Refresh(recordFuzhouInfo);
    }
    private void PresentFuzhouSilentAction(IReadOnlyList<string> tick) {
        SyncRecordBoardScores();RefreshFuzhouRecordPresentation();RefreshCurrentRecordTexts();RefreshTileListViewIfVisible();
        if(tick.Count<4 || tick[0]!="fuzhou") return;
        if(tick[1]!="kaijin_flower" && tick[1]!="qiangjin_take") return;
        int actor=int.Parse(tick[2]),tile=int.Parse(tick[3]);
        if(!indexToPosition.TryGetValue(actor,out string seat)) return;
        if(tick[1]=="kaijin_flower") Game3DManager.Instance?.Change3DTile("SetBuhuacardWithoutAnimation",tile,0,seat,false,null);
        else {
            if(seat=="self") GameCanvas.Instance?.ChangeHandCards("GetCard",tile,null,null);
            Game3DManager.Instance?.Change3DTile("GetCard",tile,0,seat,false,null);
        }
    }
    public HashSet<int> FuzhouWaiting(TingpaiQuery query) {
        if(!IsFuzhouRecord() || query==null) return new HashSet<int>();
        int? seat=query.RecordPlayerIndex;
        if(!seat.HasValue && recordPlayer_to_info.TryGetValue("self",out RecordPlayer self)) seat=self.playerIndex;
        return seat.HasValue && seat.Value>=0 && seat.Value<4?recordFuzhouHints[seat.Value].Waiting(query):new HashSet<int>();
    }
    public WaitTileHint DescribeFuzhouWaiting(WaitHintQuery query) {
        if(!IsFuzhouRecord() || query==null) return null;
        int seat=query.Record?.SelfPlayerIndex??query.SelfIndex;
        return seat>=0 && seat<4?recordFuzhouHints[seat].Describe(query):null;
    }
    private bool ApplyFuzhouRecordAction(IReadOnlyList<string> tick) {
        if(!IsFuzhouRecord() || tick==null || tick.Count==0) return false;
        if(tick[0]=="hu_self" || tick[0]=="hu_first" || tick[0]=="hu_second" || tick[0]=="hu_third") {
            ApplyFuzhouRecordWin();
            return false;
        }
        if(tick.Count<3 || tick[0]!="fuzhou") return false;
        switch(tick[1]) {
            case "hints": {
                var hints=JsonConvert.DeserializeObject<FuzhouHints[]>(tick[2]);
                if(hints?.Length==4) for(int i=0;i<4;i++) recordFuzhouHints[i].Accept(hints[i]);
                break;
            }
            case "kaijin": case "kaijin_flower": case "qiangjin_take": case "youjin": case "tingjin": {
                int actor=int.Parse(tick[2]),tile=int.Parse(tick[3]);
                var player=recordPlayerList.First(p=>p.playerIndex==actor);
                recordFuzhouInfo=JsonConvert.DeserializeObject<FuzhouInfo>(tick[4]);
                if(tick[1]=="kaijin" || tick[1]=="kaijin_flower") ConsumeTaiwanRecordWallTile(IsFuzhouV2Record?"d":"bd");
                if(tick[1]=="kaijin_flower") player.huapaiList.Add(tile);
                if(tick[1]=="qiangjin_take") {player.tileList.Add(tile);player.showHandDrawSlotActive=true;}
                break;
            }
            case "win_source": {
                int payer=int.Parse(tick[3]),tile=int.Parse(tick[5]);
                recordFuzhouWinSource=tick[4];recordFuzhouForced=bool.Parse(tick[7]);
                recordFuzhouInfo=JsonConvert.DeserializeObject<FuzhouInfo>(tick[6]);
                // This is metadata immediately before the actual hu action.
                // Seeking here must keep the winning tile at its donor so
                // the following animation can still take the real tile.
                recordFuzhouWinTick=tick.ToArray();recordFuzhouWinApplied=false;
                lastWinnableTileId=recordFuzhouForced?-1:tile;
                lastJiagangPlayerIndex=recordFuzhouWinSource=="rob_kong"?payer:-1;
                lastDiscardPlayerIndex=payer;
                break;
            }
            case "state": {
                recordFuzhouInfo=JsonConvert.DeserializeObject<FuzhouInfo>(tick[2]);
                var scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                if(scores?.Length==4) foreach(var player in recordPlayerList) {player.score=scores[player.playerIndex];userIdToScore[player.userId]=player.score;}
                break;
            }
        }
        return true;
    }
    private void ApplyFuzhouRecordWin() {
        if(recordFuzhouWinTick==null || recordFuzhouWinApplied) return;
        var tick=recordFuzhouWinTick;
        int actor=int.Parse(tick[2]),payer=int.Parse(tick[3]),tile=int.Parse(tick[5]);
        var winner=recordPlayerList.First(p=>p.playerIndex==actor);
        winner.isHu=true;
        if(payer>=0 && !recordFuzhouForced) {
            var source=recordPlayerList.First(p=>p.playerIndex==payer);
            if(recordFuzhouWinSource=="rob_kong") {source.tileList.Remove(tile);source.showHandDrawSlotActive=false;}
            else if(recordFuzhouWinSource=="discard" && bool.Parse(tick[8])) {
                int at=source.discardTiles.Count-1;
                if(at>=0 && source.discardTiles[at]==tile) {
                    source.discardTiles.RemoveAt(at);
                    if(source.discardIsMoqie.Count>at) source.discardIsMoqie.RemoveAt(at);
                    if(source.discardRiichiFlags.Count>at) source.discardRiichiFlags.RemoveAt(at);
                }
            }
        }
        // Robbed-kong payments use hu_self, so the generic ron codec cannot
        // append its winning tile. Restore the authoritative hand once here.
        if(recordFuzhouInfo?.final_hands!=null && recordFuzhouInfo.final_hands.TryGetValue(actor,out int[] hand)) {
            winner.tileList=hand.ToList();winner.showHandDrawSlotActive=false;
            winner.hasRonWinningTile=recordFuzhouWinSource=="discard" || recordFuzhouWinSource=="rob_kong";
        }
        recordFuzhouWinApplied=true;
    }
}
