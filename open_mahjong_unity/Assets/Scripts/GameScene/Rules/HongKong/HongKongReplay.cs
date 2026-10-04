using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private HongKongInfo recordHongKongInfo;
    private string recordHongKongWinSource;
    private string recordHongKongFlowerWin;

    private bool IsHongKongRecord() => RecordRuleManifest?.RuleId==HongKongGameState.RuleId;

    private void PlayHongKongRecordAnnouncement(IReadOnlyList<string> tick) {
        if (!IsHongKongRecord() || tick == null || tick.Count < 4
            || tick[0] != "hongkong" || tick[1] != "ready"
            || ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") != HongKongGameState.New16)
            return;
        if (!int.TryParse(tick[2], out int seat)
            || !indexToPosition.TryGetValue(seat, out string position)
            || !recordPlayer_to_info.TryGetValue(position, out RecordPlayer player)) return;
        SoundManager.Instance?.PlayActionSound(position, "riichi", player.voice_used);
    }

    private void ResetHongKongRecordState() {
        recordHongKongWinSource=null;
        recordHongKongFlowerWin=null;
        recordHongKongInfo=null;
        if (IsHongKongRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex,out Round round))
            recordHongKongInfo=JsonConvert.DeserializeObject<HongKongInfo>(JsonConvert.SerializeObject(round.hongkong));
        HongKongLedgerPanel.Show(recordHongKongInfo,false);
    }

    private bool ApplyHongKongRecordAction(IReadOnlyList<string> tick) {
        if (!IsHongKongRecord() || tick.Count<2 || tick[0]!="hongkong") return false;
        switch (tick[1]) {
            case "opening_complete": {
                var hands=JsonConvert.DeserializeObject<int[][]>(tick[2]);
                var slots=JsonConvert.DeserializeObject<bool[]>(tick[3]);
                foreach (var p in recordPlayerList) {
                    p.tileList=new List<int>(hands[p.playerIndex]);
                    p.showHandDrawSlotActive=slots[p.playerIndex];
                }
                currentPlayerIndex=0;
                break;
            }
            case "ready": {
                int seat=int.Parse(tick[2]);
                var player=recordPlayerList.First(p=>p.playerIndex==seat);
                player.readyQualification=tick[3];
                if (!player.tagList.Contains("declared_ready")) player.tagList.Add("declared_ready");
                break;
            }
            case "score": {
                int[] changes=JsonConvert.DeserializeObject<int[]>(tick[2]);
                var deltas=new Dictionary<int,int>();
                MapTickScoreChangesToDeltas(changes,deltas);
                ApplyScoreDeltas(deltas,out _,out _);
                if (recordHongKongInfo!=null) recordHongKongInfo.pulls=JsonConvert.DeserializeObject<HongKongPull[]>(tick[4]);
                break;
            }
            case "win_source": {
                int payer=int.Parse(tick[3]), tile=int.Parse(tick[5]);
                recordPlayerList.First(p=>p.playerIndex==int.Parse(tick[2])).isHu=true;
                recordHongKongWinSource=tick[4];
                recordHongKongFlowerWin=tick[6];
                if (tick.Count>8) recordHongKongInfo=JsonConvert.DeserializeObject<HongKongInfo>(tick[8]);
                lastWinnableTileId=tile;
                lastJiagangPlayerIndex=recordHongKongWinSource=="rob_kong" ? payer : -1;
                if (payer>=0) lastDiscardPlayerIndex=payer;
                if (payer>=0 && string.IsNullOrEmpty(recordHongKongFlowerWin) && bool.Parse(tick[7])) {
                    var source=recordPlayerList.First(p=>p.playerIndex==payer);
                    if (recordHongKongWinSource=="rob_kong") {
                        // Kongs are committed only after the rob window. Preserve the original pung / three concealed tiles.
                        source.tileList.Remove(tile);
                        source.showHandDrawSlotActive=false;
                    } else if (recordHongKongWinSource=="discard" && source.discardTiles.Count>0) {
                        int last=source.discardTiles.Count-1;
                        source.discardTiles.RemoveAt(last);
                        if (source.discardIsMoqie.Count>last) source.discardIsMoqie.RemoveAt(last);
                        if (source.discardRiichiFlags.Count>last) source.discardRiichiFlags.RemoveAt(last);
                    }
                }
                break;
            }
            case "state": {
                recordHongKongInfo=JsonConvert.DeserializeObject<HongKongInfo>(tick[2]);
                int[] scores=JsonConvert.DeserializeObject<int[]>(tick[3]);
                // Absolute snapshot is also the checkpoint for joining a delayed spectator stream.
                foreach (var p in recordPlayerList) {
                    p.score=scores[p.playerIndex];
                    userIdToScore[p.userId]=p.score;
                }
                break;
            }
        }
        HongKongLedgerPanel.Show(recordHongKongInfo,false);
        return true;
    }

    private bool IsHongKongRecordRobWin => IsHongKongRecord() && recordHongKongWinSource=="rob_kong";
    private bool IsHongKongRecordFlowerWin => IsHongKongRecord() && !string.IsNullOrEmpty(recordHongKongFlowerWin);
}
