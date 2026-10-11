using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public partial class GameRecordManager {
    internal RuleReplayModule CreateNanchangReplayModule() => new RuleReplayModule("nanchang") {
        ReservedWallTiles = 0,
        Reset = ResetNanchangRecordState,
        Clear = () => {
            recordNanchangInfo = null; recordNanchangWinSource = null; recordNanchangRonRecycled = false;
            recordNanchangWaits.Clear(); NanchangTableIndicators.Refresh(null);
        },
        ApplyBeforeMutation = ApplyNanchangRecordAction,
        RefreshPlayerTags = () => NanchangTableIndicators.Refresh(recordNanchangInfo, indexToPosition),
        RefreshAfterAction = () => NanchangTableIndicators.Refresh(recordNanchangInfo, indexToPosition),
        RefreshAfterSeek = () => {
            // A completed ron already owns its river tile; self-draw never has
            // a river source. Keep the candidate only when seeking before ron.
            if (recordNanchangRonRecycled || recordNanchangWinSource == "self_draw") {
                lastWinnableTileId = -1;
                lastJiagangPlayerIndex = -1;
            }
        },
        AfterSilentMutation = tick => {
            SyncRecordBoardScores();
            if (tick.Count > 1 && tick[1] == "meld") RefreshGuizhouRecordMelds();
        },
    };

    private NanchangInfo recordNanchangInfo;
    private string recordNanchangWinSource;
    private bool recordNanchangRonRecycled;
    private Dictionary<int, Dictionary<string, NanchangWait[]>> recordNanchangWaits = new Dictionary<int, Dictionary<string, NanchangWait[]>>();
    public Dictionary<string, NanchangWait[]> NanchangReplayWaitsFor(int? player) {
        int seat = player ?? (recordPlayer_to_info.TryGetValue("self", out var self) ? self.playerIndex : -1);
        return IsNanchangRecord() && recordNanchangWaits.TryGetValue(seat, out var snapshot) ? snapshot : null;
    }
    private bool IsNanchangRecord() => RecordRuleManifest?.RuleId == NanchangGameState.RuleId;

    private void ResetNanchangRecordState() {
        recordNanchangInfo = null; recordNanchangWinSource = null; recordNanchangRonRecycled = false;
        recordNanchangWaits.Clear();
        if (IsNanchangRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            recordNanchangInfo = JsonConvert.DeserializeObject<NanchangInfo>(JsonConvert.SerializeObject(round.nanchang));
            if (round.nanchangHints != null) recordNanchangWaits = JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, NanchangWait[]>>>(JsonConvert.SerializeObject(round.nanchangHints));
        }
        NanchangTableIndicators.Refresh(recordNanchangInfo, indexToPosition);
    }

    private bool ApplyNanchangRecordAction(IReadOnlyList<string> tick) {
        if (!IsNanchangRecord() || tick == null || tick.Count < 3) return false;
        if (!recordNanchangRonRecycled && recordNanchangWinSource == "discard"
            && (tick[0] == "hu_first" || tick[0] == "hu_second" || tick[0] == "hu_third")) {
            var source = recordPlayerList.First(p => p.playerIndex == lastDiscardPlayerIndex);
            int last = source.discardTiles.Count - 1;
            if (last >= 0 && source.discardTiles[last] == lastWinnableTileId) {
                source.discardTiles.RemoveAt(last);
                if (source.discardIsMoqie.Count > last) source.discardIsMoqie.RemoveAt(last);
                if (source.discardRiichiFlags.Count > last) source.discardRiichiFlags.RemoveAt(last);
            }
            recordNanchangRonRecycled = true;
        }
        if (tick[0] != "nanchang") return false;
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
            case "jingdiao":
                if(recordNanchangInfo?.declared_jingdiao?.Length==4) recordNanchangInfo.declared_jingdiao[int.Parse(tick[2])]=true;
                break;
            case "win_source": {
                int actor = int.Parse(tick[2]), payer = int.TryParse(tick[3], out int sourceIndex) ? sourceIndex : -1, tile = int.Parse(tick[5]);
                recordNanchangWinSource = tick[4]; recordNanchangRonRecycled = false;
                recordPlayerList.First(p => p.playerIndex == actor).isHu = true; lastWinnableTileId = tile;
                lastJiagangPlayerIndex = -1;
                lastDiscardPlayerIndex = payer >= 0 ? payer : -1;
                if (tick.Count > 6) recordNanchangInfo = JsonConvert.DeserializeObject<NanchangInfo>(tick[6]);
                break;
            }
            case "state": {
                recordNanchangInfo = JsonConvert.DeserializeObject<NanchangInfo>(tick[2]);
                if (tick.Count > 3) {
                    int[] scores = JsonConvert.DeserializeObject<int[]>(tick[3]);
                    foreach (var player in recordPlayerList) { player.score = scores[player.playerIndex]; userIdToScore[player.userId] = player.score; }
                }
                break;
            }
            case "waits":
                if (tick.Count >= 4) recordNanchangWaits[int.Parse(tick[2])] = JsonConvert.DeserializeObject<Dictionary<string, NanchangWait[]>>(tick[3]);
                break;
        }
        NanchangTableIndicators.Refresh(recordNanchangInfo, indexToPosition);
        return true;
    }
}
