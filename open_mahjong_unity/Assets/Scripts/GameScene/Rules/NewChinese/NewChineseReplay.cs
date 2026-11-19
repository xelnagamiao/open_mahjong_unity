using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal NewChineseInfo NewChineseRecordInfo {get; private set;}

    internal RuleReplayModule CreateNewChineseReplayModule() => new RuleReplayModule("new_chinese") {
        Reset = () => {
            NewChineseRecordInfo = gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out var round) ? round.newChinese : null;
        },
        Clear = () => { NewChineseRecordInfo=null; RefreshNewChineseRecord(); },
        ApplyBeforeMutation=ApplyNewChineseTick, RefreshAfterSeek=RefreshNewChineseRecord,
        RefreshAfterAction=RefreshNewChineseRecord, RefreshPlayerTags=RefreshNewChineseRecord,
        ConsumeWallTile=action=>action=="gd",
        AnnounceBeforeAction=(tick,seat,voice)=>{
            if (tick.Count > 3 && tick[0] == "c" && tick[3] == "H")
                SoundManager.Instance.PlayActionSound(seat, "riichi", voice);
        },
        AfterVisibleMutation=tick=>{
            if(tick.Count>1 && tick[0]=="new_chinese" && (tick[1]=="exchange" || tick[1]=="rob_kan")) Game3DManager.Instance?.RefreshRecordHandDisplay();
        }
    };

    private void RefreshNewChineseRecord() {
        int viewer = recordPlayer_to_info.TryGetValue("self",out var p) ? p.playerIndex : -1;
        GameCanvas.Instance?.ShowNewChineseStatus(NewChineseRecordInfo,indexToPosition,viewer);
    }

    private bool ApplyNewChineseTick(IReadOnlyList<string> tick) {
        if (tick.Count < 2) return false;
        if (tick[0].StartsWith("hu")) {
            if (tick.Count > 8 && tick[8] == "1" && tick[0]!="hu_self" && tick.Count>7 && int.TryParse(tick[7],out var source) && int.TryParse(tick[5],out var face))
                SyncRecordRonDiscardRemoved(ResolveRecordRonDiscarderPosition(source),face);
            return false;
        }
        if (tick[0]!="new_chinese") return false;
        if ((tick[1]=="declare_kan" || tick[1]=="rob_kan") && tick.Count>=7) {
            int actor = int.Parse(tick[2]);
            int tile = int.Parse(tick[3]);
            var player = recordPlayerList.First(p=>p.playerIndex==actor);
            lastJiagangPlayerIndex = actor;
            lastWinnableTileId = tile;
            if (tick[1]=="rob_kan") {
                if (tick[4]=="jiagang") {
                    player.tileList.Remove(tile);
                    player.showHandDrawSlotActive=false;
                } else if (int.TryParse(tick[5],out var originalSource)) {
                    SyncRecordRonDiscardRemoved(ResolveRecordRonDiscarderPosition(originalSource),tile);
                }
                if (tick[6]=="1") {
                    player.discardTiles.Add(tile);
                    player.discardRiichiFlags.Add(false);
                    player.discardIsMoqie.Add(false);
                    lastDiscardPlayerIndex=actor;
                }
            }
        }
        else if (tick[1]=="seen_win" && tick.Count>=4) {
            var player = recordPlayerList.First(p=>p.playerIndex==int.Parse(tick[2]));
            player.tileList.Add(int.Parse(tick[3]));
            player.showHandDrawSlotActive=true;
        }
        else if (tick[1]=="state" && tick.Count>2) NewChineseRecordInfo=JsonConvert.DeserializeObject<NewChineseInfo>(tick[2]);
        else if (tick[1]=="exchange" && tick.Count>2) {
            var hands=JsonConvert.DeserializeObject<Dictionary<int,List<int>>>(tick[2]);
            foreach(var item in hands)
                if(indexToPosition.TryGetValue(item.Key,out var pos) && recordPlayer_to_info.TryGetValue(pos,out var player))
                    player.tileList=new List<int>(item.Value);
        }
        return true;
    }
}
