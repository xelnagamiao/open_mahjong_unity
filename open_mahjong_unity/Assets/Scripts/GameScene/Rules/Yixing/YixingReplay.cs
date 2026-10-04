using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private YixingInfo recordYixingInfo;
    private string recordYixingWinSource;
    private bool recordYixingRonRecycled;
    private bool IsYixingRecord() => RecordRuleManifest?.RuleId == YixingGameState.RuleId;
    private bool IsYixingRecordRobWin => IsYixingRecord() && recordYixingWinSource == "rob_kong";
    private void ResetYixingRecordState() {
        recordYixingInfo = null; recordYixingWinSource = null; recordYixingRonRecycled = false;
        if (IsYixingRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round))
            recordYixingInfo = JsonConvert.DeserializeObject<YixingInfo>(JsonConvert.SerializeObject(round.yixing));
    }
    private bool ApplyYixingRecordAction(IReadOnlyList<string> tick) {
        if (!IsYixingRecord() || tick == null || tick.Count < 3) return false;
        if (!recordYixingRonRecycled && recordYixingWinSource == "discard"
            && (tick[0] == "hu_first" || tick[0] == "hu_second" || tick[0] == "hu_third")) {
            var source = recordPlayerList.First(p => p.playerIndex == lastDiscardPlayerIndex);
            int last = source.discardTiles.Count - 1;
            if (last >= 0 && source.discardTiles[last] == lastWinnableTileId) {
                source.discardTiles.RemoveAt(last);
                if (source.discardIsMoqie.Count > last) source.discardIsMoqie.RemoveAt(last);
                if (source.discardRiichiFlags.Count > last) source.discardRiichiFlags.RemoveAt(last);
            }
            recordYixingRonRecycled = true;
        }
        if (tick[0] != "yixing") return false;
        switch (tick[1]) {
            case "win_source": {
                int actor = int.Parse(tick[2]), payer = int.Parse(tick[3]), tile = int.Parse(tick[5]);
                recordYixingWinSource = tick[4];
                recordYixingRonRecycled = false;
                recordPlayerList.First(p => p.playerIndex == actor).isHu = true;
                lastWinnableTileId = tile;
                lastJiagangPlayerIndex = recordYixingWinSource == "rob_kong" ? payer : -1;
                if (payer >= 0) lastDiscardPlayerIndex = payer;
                // Recycle a normal discard at its hu tick above. A robbed added
                // kong has not been committed, so remove its hand tile here.
                if (payer >= 0 && recordYixingWinSource == "rob_kong") {
                    var source = recordPlayerList.First(p => p.playerIndex == payer);
                    source.tileList.Remove(tile); source.showHandDrawSlotActive = false;
                }
                recordYixingInfo = JsonConvert.DeserializeObject<YixingInfo>(tick[6]);
                break;
            }
            case "state": {
                recordYixingInfo = JsonConvert.DeserializeObject<YixingInfo>(tick[2]);
                int[] scores = JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach (var player in recordPlayerList) {
                    player.score = scores[player.playerIndex]; userIdToScore[player.userId] = player.score;
                }
                break;
            }
        }
        return true;
    }
}
