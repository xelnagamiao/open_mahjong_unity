using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal SanhuaInfo SanhuaRecordInfo {get; private set;}
    internal RuleReplayModule CreateSanhuaReplayModule() => new RuleReplayModule("sanhua") {
        Reset=()=>SanhuaRecordInfo=gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out var r)?r.sanhua:null,
        Clear = () => {
            SanhuaRecordInfo = null;
            SanhuaGameState.ShowFans(GameCanvas.Instance, null, indexToPosition);
            Game3DManager.Instance?.ClearSanhuaWall();
        },
        RefreshPlayerTags=RefreshSanhua,
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
        SanhuaGameState.ShowFans(GameCanvas.Instance, SanhuaRecordInfo, indexToPosition);
        RoundPanel.Instance?.RefreshIndicators(SanhuaGameState.SubRule, SanhuaGameState.Indicators(SanhuaRecordInfo));
        // 以当前节点已经执行的摸牌和杠补为准，旧谱及遮蔽牌面的观战牌谱也保留这些动作。
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round))
            Game3DManager.Instance?.ShowSanhuaWall(SanhuaRecordInfo, round.dealerIndex, indexToPosition,
                consumedFromFront, consumedBackIndices.Count);
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
