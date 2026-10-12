using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateVietnameseReplayModule()=>new RuleReplayModule("vietnamese") {
        ReservedWallTiles=26,
        Reset=()=>{recordVietnameseSpecial=false;},Clear=()=>{recordVietnameseSpecial=false;},
        ApplyBeforeMutation=ApplyVietnameseRecord,
        NextPlayer=(tick,fallback)=>tick.Count>1 && tick[0]=="vietnamese" && tick[1]=="opening_complete"?0:fallback,
        AfterSilentMutation=tick=>{
            SyncRecordBoardScores();
            if(tick.Count>1 && (tick[1]=="bounce" || tick[1]=="opening_complete")) {
                Game3DManager.Instance.Clear3DTile();
                GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord",0,recordPlayer_to_info["self"].tileList.ToArray(),null);
                Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord",0,0,null,false,null);
                RebuildRecord3DTableWithoutAnimation();
            }
        },
        InitializePlayers=()=>{foreach(var p in recordPlayerList)p.showHandDrawSlotActive=p.tileList.Count==14;},
    };
    private bool recordVietnameseSpecial;
    private bool ApplyVietnameseRecord(IReadOnlyList<string> tick) {
        if(RecordRuleManifest?.RuleId!="vietnamese" || tick==null || tick.Count<3 || tick[0]!="vietnamese")return false;
        switch(tick[1]) {
            case "opening_complete": {
                var hands=JsonConvert.DeserializeObject<int[][]>(tick[2]);var slots=JsonConvert.DeserializeObject<bool[]>(tick[3]);
                foreach(var p in recordPlayerList){p.tileList=new List<int>(hands[p.playerIndex]);p.showHandDrawSlotActive=slots[p.playerIndex];}
                currentPlayerIndex=0;break;
            }
            case "meld_face": {
                var p=recordPlayerList.First(x=>x.playerIndex==int.Parse(tick[2]));int at=int.Parse(tick[3]);
                p.combinationTiles[at]=tick[4];p.combinationMasks[at]=JsonConvert.DeserializeObject<int[]>(tick[5]);break;
            }
            case "bounce": {
                var p=recordPlayerList.First(x=>x.playerIndex==int.Parse(tick[2]));
                p.tileList.RemoveAt(p.tileList.Count-1);p.huapaiList.Add(int.Parse(tick[4]));
                p.combinationMasks[int.Parse(tick[5])]=JsonConvert.DeserializeObject<int[]>(tick[6]);
                p.showHandDrawSlotActive=false;break;
            }
            case "win_source": {
                int winner=int.Parse(tick[2]),payer=int.Parse(tick[3]),tile=int.Parse(tick[5]);
                recordVietnameseSpecial=bool.Parse(tick[6]);
                var won=recordPlayerList.First(p=>p.playerIndex==winner);
                won.isHu=true;won.showHandDrawSlotActive=false;
                lastWinnableTileId=recordVietnameseSpecial?-1:tile;
                lastJiagangPlayerIndex=tick[4]=="rob_kong"?payer:-1;
                if(payer>=0) {
                    lastDiscardPlayerIndex=payer;var source=recordPlayerList.First(p=>p.playerIndex==payer);
                    if(tick[4]=="rob_kong") {source.tileList.Remove(tile);source.showHandDrawSlotActive=false;}
                    else if(source.discardTiles.Count>0 && source.discardTiles.Last()==tile) {
                        int at=source.discardTiles.Count-1;source.discardTiles.RemoveAt(at);
                        if(source.discardIsMoqie.Count>at)source.discardIsMoqie.RemoveAt(at);
                        if(source.discardRiichiFlags.Count>at)source.discardRiichiFlags.RemoveAt(at);
                    }
                }
                break;
            }
            case "state": {
                int[] scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach(var p in recordPlayerList){p.score=scores[p.playerIndex];userIdToScore[p.userId]=p.score;}break;
            }
        }
        return true;
    }
}
