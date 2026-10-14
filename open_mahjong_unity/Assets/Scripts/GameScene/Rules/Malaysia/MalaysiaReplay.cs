using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private MalaysiaInfo recordMalaysiaInfo;
    private string recordMalaysiaSource;
    private bool recordMalaysiaRecycled;
    internal RuleReplayModule CreateMalaysiaReplayModule() => new RuleReplayModule("malaysia") {
        Reset=()=> {
            recordMalaysiaInfo=gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out Round r)?r.malaysia:null;
            recordMalaysiaSource=null; recordMalaysiaRecycled=false;
        },
        Clear=()=>{recordMalaysiaInfo=null;recordMalaysiaSource=null;recordMalaysiaRecycled=false;EndResultPanel.Instance?.SetOccupiedSeats(null);},
        ApplyBeforeMutation=ApplyMalaysiaRecordAction,
        RefreshAfterAction=RefreshMalaysiaFlowers,
        RefreshAfterSeek=RefreshMalaysiaFlowers,
        AfterSilentMutation=tick=> {
            SyncRecordBoardScores();
            if(tick.Count>2 && tick[0]=="malaysia" && tick[1]=="redeem") {
                string seat=indexToPosition[int.Parse(tick[2])];
                Game3DManager.Instance?.RebuildPlayerMelds(seat);
                if(seat=="self") GameCanvas.Instance?.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info[seat].tileList.ToArray(),null);
            }
            RefreshMalaysiaFlowers();
        },
    };
    private void RefreshMalaysiaFlowers() {
        GameCanvas.Instance?.SetPlayerFlowerCounts(recordPlayerList.ToDictionary(p=>p.playerIndex,p=>p.huapaiList?.Count ?? 0),indexToPosition);
        EndResultPanel.Instance?.SetOccupiedSeats(indexToPosition.Values);
    }
    private bool ApplyMalaysiaRecordAction(IReadOnlyList<string> tick) {
        if(tick==null || tick.Count<2) return false;
        if(!recordMalaysiaRecycled && recordMalaysiaSource=="discard" && tick[0].StartsWith("hu_")) {
            var p=recordPlayerList.First(x=>x.playerIndex==lastDiscardPlayerIndex);int n=p.discardTiles.Count-1;
            if(n>=0 && p.discardTiles[n]==lastWinnableTileId) {
                p.discardTiles.RemoveAt(n);
                if(p.discardIsMoqie.Count>n)p.discardIsMoqie.RemoveAt(n);
                if(p.discardRiichiFlags.Count>n)p.discardRiichiFlags.RemoveAt(n);
            }
            recordMalaysiaRecycled=true;
        }
        if(tick[0]!="malaysia")return false;
        switch(tick[1]) {
            case "payment": {
                int[] changes=JsonConvert.DeserializeObject<int[]>(tick[2]);
                foreach(var p in recordPlayerList){p.score+=changes[p.playerIndex];userIdToScore[p.userId]=p.score;}
                break;
            }
            case "redeem": {
                var p=recordPlayerList.First(x=>x.playerIndex==int.Parse(tick[2]));
                int tile=int.Parse(tick[3]),n=int.Parse(tick[4]),slot=int.Parse(tick[5]);
                p.tileList[p.tileList.Count-1]=81; p.showHandDrawSlotActive=true;
                p.combinationMasks[n][slot*2+1]=tile;
                break;
            }
            case "win_source": {
                int winner=int.Parse(tick[2]),payer=int.Parse(tick[3]);
                recordMalaysiaSource=tick[4];recordMalaysiaRecycled=false;
                recordPlayerList.First(p=>p.playerIndex==winner).isHu=true;
                lastWinnableTileId=int.TryParse(tick[5],out int tile)?tile:0;
                lastJiagangPlayerIndex=recordMalaysiaSource=="rob_kong"?payer:-1;
                if(payer>=0)lastDiscardPlayerIndex=payer;
                if(recordMalaysiaSource=="rob_kong") {
                    var p=recordPlayerList.First(x=>x.playerIndex==payer);
                    p.tileList.Remove(tile);p.showHandDrawSlotActive=false;
                }
                break;
            }
            case "state": {
                recordMalaysiaInfo=JsonConvert.DeserializeObject<MalaysiaInfo>(tick[2]);
                int[] scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach(var p in recordPlayerList){p.score=scores[p.playerIndex];userIdToScore[p.userId]=p.score;}
                break;
            }
        }
        return true;
    }
}
