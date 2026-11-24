using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal NewChineseInfo NewChineseRecordInfo {get; private set;}
    private const string NewChineseRecordChoicePhase = "showing_exchange_choices";
    private bool newChineseRecordShowingChoices, newChineseRecordSettled;
    private int NewChineseRecordSettlementBao => newChineseRecordSettled
        ? NewChineseGameState.GetSettlementBao(NewChineseRecordInfo)
        : 0;

    internal RuleReplayModule CreateNewChineseReplayModule() => new RuleReplayModule("new_chinese") {
        Reset = () => {
            newChineseRecordSettled = false;
            GameCanvas.Instance?.FinishNewChineseExchangeChoices();
            GameCanvas.Instance?.FinishNewChineseReceipt();
            SetNewChineseRecordInfo(gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out var round) ? round.newChinese : null);
        },
        Clear = () => {
            newChineseRecordSettled = false;
            GameCanvas.Instance?.FinishNewChineseReceipt();
            SetNewChineseRecordInfo(null);
            RefreshNewChineseRecord();
        },
        IsSilentTick = IsNewChineseSilentRecordTick,
        AfterSilentMutation = tick => {
            if (!newChineseRecordShowingChoices) GameCanvas.Instance?.FinishNewChineseExchangeChoices();
        },
        ApplyBeforeMutation=ApplyNewChineseTick, RefreshAfterSeek=RefreshNewChineseRecord,
        RefreshAfterAction=RefreshNewChineseRecord, RefreshPlayerTags=RefreshNewChineseRecord,
        ConsumeWallTile=action=>action=="gd",
        AnnounceBeforeAction=(tick,seat,voice)=>{
            if (tick.Count > 3 && tick[0] == "c" && tick[3] == "H")
                SoundManager.Instance.PlayActionSound(seat, "riichi", voice);
        },
        AfterVisibleMutation=tick=>{
            if (tick.Count < 2 || tick[0] != "new_chinese" || (tick[1] != "exchange" && tick[1] != "rob_kan")) return;
            GameCanvas.Instance?.FinishNewChineseExchangeChoices();
            Game3DManager.Instance?.RefreshRecordHandDisplay();
            if (tick[1] == "exchange" && recordPlayer_to_info.TryGetValue("self", out var player) &&
                NewChineseRecordInfo?.exchange_packets is int[][] packets && packets.Length == 4 && player.playerIndex >= 0 && player.playerIndex < 4 &&
                packets[player.playerIndex]?.Length == 3) {
                for (int offset = 1; offset < 4; offset++) {
                    var incoming = packets[(player.playerIndex - offset + 4) % 4];
                    if (incoming?.Length != 3) continue;
                    GameCanvas.Instance?.ShowSbrReceived(incoming, 0);
                    break;
                }
            }
        }
    };

    private static bool IsNewChineseSilentRecordTick(IReadOnlyList<string> tick) {
        if (tick[0] != "new_chinese") return false;
        if (tick.Count < 2) return true;
        if (tick[1] == "exchange") return false;
        return tick[1] != "state" || tick.Count < 3 || !tick[2].Contains("\"" + NewChineseRecordChoicePhase + "\"");
    }

    private void SetNewChineseRecordInfo(NewChineseInfo info) {
        NewChineseRecordInfo = info;
        newChineseRecordShowingChoices = info?.phase == NewChineseRecordChoicePhase && info.exchange_choices?.Length == 4;
    }

    private void RefreshNewChineseRecord() {
        int viewer = recordPlayer_to_info.TryGetValue("self",out var p) ? p.playerIndex : -1;
        GameCanvas.Instance?.ShowNewChineseStatus(NewChineseRecordInfo, indexToPosition, viewer, newChineseRecordSettled);
        if (newChineseRecordShowingChoices)
            GameCanvas.Instance?.ShowNewChineseExchangeChoices(NewChineseRecordInfo, indexToPosition,
                viewer >= 0 && viewer < (NewChineseRecordInfo.exchange_packets?.Length ?? 0) ? NewChineseRecordInfo.exchange_packets[viewer] : null);
        else
            GameCanvas.Instance?.FinishNewChineseExchangeChoices();
    }

    private bool ApplyNewChineseTick(IReadOnlyList<string> tick) {
        newChineseRecordSettled |= tick[0] is "hu_self" or "hu_first" or "hu_second" or "hu_third" or "liuju" or "ryuukyoku" or "end";
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
        else if (tick[1]=="state" && tick.Count>2) SetNewChineseRecordInfo(JsonConvert.DeserializeObject<NewChineseInfo>(tick[2]));
        else if (tick[1]=="exchange" && tick.Count>2) {
            newChineseRecordShowingChoices = false;
            var hands=JsonConvert.DeserializeObject<Dictionary<int,List<int>>>(tick[2]);
            foreach(var item in hands)
                if(indexToPosition.TryGetValue(item.Key,out var pos) && recordPlayer_to_info.TryGetValue(pos,out var player))
                    player.tileList=new List<int>(item.Value);
        }
        return true;
    }
}
