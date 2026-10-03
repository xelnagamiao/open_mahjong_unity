using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    private WenzhouInfo recordWenzhouInfo;
    private string recordWenzhouWinSource;
    private bool recordWenzhouRonRecycled;
    private Dictionary<int, Dictionary<string, WenzhouWait[]>> recordWenzhouWaits = new Dictionary<int, Dictionary<string, WenzhouWait[]>>();
    public Dictionary<string, WenzhouWait[]> WenzhouReplayWaitsFor(int? player) {
        int seat = player ?? (recordPlayer_to_info.TryGetValue("self", out var self) ? self.playerIndex : -1);
        return IsWenzhouRecord() && recordWenzhouWaits.TryGetValue(seat, out var snapshot) ? snapshot : null;
    }
    private bool IsWenzhouRecord() => RecordRuleManifest?.RuleId == WenzhouGameState.RuleId;
    private bool IsWenzhouRecordRobWin => IsWenzhouRecord() && recordWenzhouWinSource == "rob_kong";

    private void ResetWenzhouRecordState() {
        recordWenzhouInfo = null; recordWenzhouWinSource = null; recordWenzhouRonRecycled = false;
        recordWenzhouWaits.Clear();
        if (IsWenzhouRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            recordWenzhouInfo = JsonConvert.DeserializeObject<WenzhouInfo>(JsonConvert.SerializeObject(round.wenzhou));
            if (round.wenzhouWaits != null) recordWenzhouWaits = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, WenzhouWait[]>>>(JsonConvert.SerializeObject(round.wenzhouWaits));
        }
        WenzhouPanel.Show(recordWenzhouInfo);
    }

    private List<string> NormalizeWenzhouRecordTick(List<string> source) {
        if (!IsWenzhouRecord() || source == null || source.Count < 2 || recordWenzhouInfo == null) return source;
        // The standard replay codec still needs a logical meld key; physical
        // hand-side IDs and the immediately following Wenzhou mask remain exact.
        if (source[0] != "jg") return source;
        if (source[1] != "46" || recordWenzhouInfo.caishen == 46) return source;
        var result = new List<string>(source); result[1] = recordWenzhouInfo.white_natural.ToString(); return result;
    }

    private bool ApplyWenzhouRecordAction(IReadOnlyList<string> tick) {
        if (!IsWenzhouRecord() || tick == null || tick.Count < 3) return false;
        if (!recordWenzhouRonRecycled && recordWenzhouWinSource == "discard"
            && (tick[0] == "hu_first" || tick[0] == "hu_second" || tick[0] == "hu_third")) {
            var source = recordPlayerList.First(p => p.playerIndex == lastDiscardPlayerIndex);
            int last = source.discardTiles.Count - 1;
            if (last >= 0 && source.discardTiles[last] == lastWinnableTileId) {
                source.discardTiles.RemoveAt(last);
                if (source.discardIsMoqie.Count > last) source.discardIsMoqie.RemoveAt(last);
                if (source.discardRiichiFlags.Count > last) source.discardRiichiFlags.RemoveAt(last);
            }
            recordWenzhouRonRecycled = true;
        }
        if (tick[0] != "wenzhou") return false;
        switch (tick[1]) {
            case "meld": {
                int actor = int.Parse(tick[2]); var player = recordPlayerList.First(p => p.playerIndex == actor);
                int[] mask = JsonConvert.DeserializeObject<int[]>(tick[3]); string code = tick[4];
                int index = player.combinationTiles.FindLastIndex(c => c == code);
                if (index < 0) index = player.combinationTiles.Count - 1;
                if (index >= 0 && index < player.combinationMasks.Count) {
                    player.combinationTiles[index] = code; player.combinationMasks[index] = mask;
                }
                break;
            }
            case "kong_claim_source": {
                int actor = int.Parse(tick[2]), tile = int.Parse(tick[3]);
                var player = recordPlayerList.First(p => p.playerIndex == actor);
                player.tileList.Remove(tile); player.showHandDrawSlotActive = false;
                player.discardTiles.Add(tile); player.discardIsMoqie.Add(tick.Count > 4 && (tick[4] == "T" || tick[4] == "True" || tick[4] == "true")); player.discardRiichiFlags.Add(false);
                lastDiscardPlayerIndex = actor; lastWinnableTileId = tile; lastJiagangPlayerIndex = -1;
                break;
            }
            case "win_source": {
                int actor = int.Parse(tick[2]), payer = int.Parse(tick[3]), tile = int.Parse(tick[5]);
                recordWenzhouWinSource = tick[4]; recordWenzhouRonRecycled = false;
                recordPlayerList.First(p => p.playerIndex == actor).isHu = true; lastWinnableTileId = tile;
                lastJiagangPlayerIndex = recordWenzhouWinSource == "rob_kong" ? payer : -1;
                if (payer >= 0) lastDiscardPlayerIndex = payer;
                if (payer >= 0 && recordWenzhouWinSource == "rob_kong") {
                    var source = recordPlayerList.First(p => p.playerIndex == payer);
                    source.tileList.Remove(tile); source.showHandDrawSlotActive = false;
                }
                if (tick.Count > 6) recordWenzhouInfo = JsonConvert.DeserializeObject<WenzhouInfo>(tick[6]);
                break;
            }
            case "state": {
                recordWenzhouInfo = JsonConvert.DeserializeObject<WenzhouInfo>(tick[2]);
                if (tick.Count > 3) {
                    int[] scores = JsonConvert.DeserializeObject<int[]>(tick[3]);
                    foreach (var player in recordPlayerList) { player.score = scores[player.playerIndex]; userIdToScore[player.userId] = player.score; }
                }
                break;
            }
            case "waits":
                if (tick.Count >= 4) recordWenzhouWaits[int.Parse(tick[2])] = JsonConvert.DeserializeObject<Dictionary<string, WenzhouWait[]>>(tick[3]);
                break;
        }
        WenzhouPanel.Show(recordWenzhouInfo);
        return true;
    }
}
