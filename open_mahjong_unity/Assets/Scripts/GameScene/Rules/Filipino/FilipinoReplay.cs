using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
public partial class GameRecordManager {
    internal RuleReplayModule CreateFilipinoReplayModule()=>new RuleReplayModule("filipino") {
        ApplyBeforeMutation=ApplyFilipinoRecordAction,
        RefreshAfterAction=RefreshFilipinoFlowers, RefreshPlayerTags=RefreshFilipinoFlowers,
        AfterSilentMutation=tick=>SyncRecordBoardScores(),
        InitializePlayers=()=>{foreach(var p in recordPlayerList) p.showHandDrawSlotActive=p.tileList.Count==17;},
    };
    private void RefreshFilipinoFlowers() {
        GameCanvas.Instance?.SetPlayerFlowerCounts(RecordRuleManifest?.RuleId=="filipino"?recordPlayerList.ToDictionary(p=>p.playerIndex,p=>p.huapaiList?.Count??0):null,indexToPosition);
    }
    private bool ApplyFilipinoRecordAction(IReadOnlyList<string> tick) {
        if(RecordRuleManifest?.RuleId!="filipino" || tick==null || tick.Count<3 || tick[0]!="filipino") return false;
        if(tick[1]=="win_source") {
            int winner=int.Parse(tick[2]),payer=int.Parse(tick[3]),tile=int.Parse(tick[5]);
            recordPlayerList.First(p=>p.playerIndex==winner).showHandDrawSlotActive=false;
            lastWinnableTileId=tile;
            if(payer>=0) {
                lastDiscardPlayerIndex=payer;
                var source=recordPlayerList.First(p=>p.playerIndex==payer);
                int at=source.discardTiles.Count-1;
                if(at>=0 && source.discardTiles[at]==tile) {
                    source.discardTiles.RemoveAt(at);
                    if(source.discardIsMoqie.Count>at)source.discardIsMoqie.RemoveAt(at);
                    if(source.discardRiichiFlags.Count>at)source.discardRiichiFlags.RemoveAt(at);
                }
            }
        } else if(tick[1]=="score") {
            int[] changes=JsonConvert.DeserializeObject<int[]>(tick[4]);
            foreach(var p in recordPlayerList) { p.score+=changes[p.playerIndex]; userIdToScore[p.userId]=p.score; }
        } else if(tick[1]=="state") {
            int[] scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
            foreach(var p in recordPlayerList) { p.score=scores[p.playerIndex]; userIdToScore[p.userId]=p.score; }
        }
        return true;
    }
}
