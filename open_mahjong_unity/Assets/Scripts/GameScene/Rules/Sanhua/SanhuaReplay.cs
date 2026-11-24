using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal SanhuaInfo SanhuaRecordInfo {get; private set;}
    internal RuleReplayModule CreateSanhuaReplayModule() => new RuleReplayModule("sanhua") {
        Reset=()=>SanhuaRecordInfo=gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out var r)?r.sanhua:null,
        Clear=()=>{SanhuaRecordInfo=null; GameCanvas.Instance?.ShowSanhuaFans(null,indexToPosition);},
        RefreshPlayerTags=()=>GameCanvas.Instance?.ShowSanhuaFans(SanhuaRecordInfo,indexToPosition),
        RefreshAfterAction=RefreshSanhua,RefreshAfterSeek=RefreshSanhua,AfterSilentMutation=_=>RefreshSanhua(),
        ApplyBeforeMutation=ApplySanhua,
        ConsumeWallTile=action=>{
            if(action!="gd") return false;
            int i=currentTilesList.Count-1;
            if(i>=0) {
                consumedBackIndices.Add(currentOriginalIndices[i]);
                currentTilesList.RemoveAt(i);
                currentOriginalIndices.RemoveAt(i);
            }
            return true;
        }
    };
    private void RefreshSanhua() {
        GameCanvas.Instance?.ShowSanhuaFans(SanhuaRecordInfo,indexToPosition);
        RoundPanel.Instance?.RefreshIndicators(SanhuaGameState.SubRule, SanhuaGameState.Indicators(SanhuaRecordInfo));
    }
    private bool ApplySanhua(IReadOnlyList<string> tick) {
        if(tick.Count<3 || tick[0]!="sanhua") return false;
        if(tick[1]=="state") SanhuaRecordInfo=JsonConvert.DeserializeObject<SanhuaInfo>(tick[2]);
        else if(tick[1]=="rob" && tick.Count>=4) {
            lastJiagangPlayerIndex=int.Parse(tick[2]);
            lastWinnableTileId=int.Parse(tick[3]);
            var player=recordPlayerList.First(p=>p.playerIndex==lastJiagangPlayerIndex);
            player.tileList.Remove(lastWinnableTileId);
            player.showHandDrawSlotActive=false;
        }
        return true;
    }
}
