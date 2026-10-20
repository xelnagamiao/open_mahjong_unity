using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public partial class GameRecordManager {
    private ShangraoInfo recordShangraoInfo;
    private int recordShangraoIndicatorSlot;
    private readonly Dictionary<int, ShangraoHintsCache> recordShangraoHints = new Dictionary<int, ShangraoHintsCache>();
    private bool IsShangraoRecord() => RecordRuleManifest?.RuleId == ShangraoGameState.RuleId;
    internal RuleReplayModule CreateShangraoReplayModule() => new RuleReplayModule("shangrao") {
        ReservedWallTiles = 1,
        Reset = ResetShangraoRecord,
        Clear = () => { recordShangraoInfo = null; recordShangraoHints.Clear(); ShangraoTableIndicators.Refresh(null); },
        ApplyBeforeMutation = ApplyShangraoTick,
        ConsumeWallTile = ConsumeShangraoWall,
        RefreshAfterAction = RefreshShangraoTable,
        RefreshAfterSeek = () => { lastWinnableTileId = -1; lastJiagangPlayerIndex = -1; RefreshShangraoTable(); },
        AfterSilentMutation = _ => SyncRecordBoardScores(),
    };
    private void ResetShangraoRecord() {
        recordShangraoInfo = null; recordShangraoHints.Clear(); recordShangraoIndicatorSlot = -1;
        if (IsShangraoRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            recordShangraoInfo = JsonConvert.DeserializeObject<ShangraoInfo>(JsonConvert.SerializeObject(round.shangrao));
            recordShangraoIndicatorSlot = round.shangrao?.indicator_index ?? -1;
            if (round.shangraoHints != null) foreach (var pair in round.shangraoHints) {
                var cache = new ShangraoHintsCache(); cache.Accept(pair.Value); recordShangraoHints[pair.Key] = cache;
            }
        }
        RefreshShangraoTable();
    }
    private void RefreshShangraoTable() => ShangraoTableIndicators.Refresh(recordShangraoInfo, indexToPosition,
        recordPlayerList.ToDictionary(p => p.playerIndex, p => p.discardTiles.ToArray()));
    private ShangraoHintsCache ShangraoCache(int? index) {
        int seat = index ?? (recordPlayer_to_info.TryGetValue("self", out var player) ? player.playerIndex : -1);
        return recordShangraoHints.TryGetValue(seat, out var cache) ? cache : null;
    }
    public HashSet<int> ShangraoWaiting(TingpaiQuery q) => ShangraoCache(q.RecordPlayerIndex)?.Waiting(q) ?? new HashSet<int>();
    public WaitTileHint DescribeShangraoWaiting(WaitHintQuery q) => ShangraoCache(q.Record?.SelfPlayerIndex)?.Describe(q) ?? WaitTileHint.None("未满足");
    private List<int> ShangraoRecordVisibleTiles() => ShangraoTableIndicators.CountedIndicators(recordShangraoInfo).ToList();
    private bool ConsumeShangraoWall(string action) {
        if (currentTilesList.Count <= 1) return true;
        int position = action == "d" ? currentOriginalIndices.FindIndex(i => i != recordShangraoIndicatorSlot)
            : currentOriginalIndices.FindLastIndex(i => i != recordShangraoIndicatorSlot);
        if (position < 0) return true;
        int original = currentOriginalIndices[position];
        RemoveShangraoWallIndex(original);
        if (action != "d" && original - 1 != recordShangraoIndicatorSlot) RemoveShangraoWallIndex(original - 1);
        return true;
    }
    private void RemoveShangraoWallIndex(int original) {
        int position = currentOriginalIndices.IndexOf(original);
        if (position < 0) return;
        currentTilesList.RemoveAt(position); currentOriginalIndices.RemoveAt(position);
        if (original == consumedFromFront) consumedFromFront++; else consumedBackIndices.Add(original);
    }
    private bool ApplyShangraoTick(IReadOnlyList<string> tick) {
        if (!IsShangraoRecord() || tick == null || tick.Count < 3 || tick[0] != "shangrao") return false;
        switch (tick[1]) {
            case "payment": {
                int[] changes = JObject.Parse(tick[2])["changes"].ToObject<int[]>();
                foreach (var p in recordPlayerList) { p.score += changes[p.playerIndex]; userIdToScore[p.userId] = p.score; }
                break;
            }
            case "burnt": {
                if (recordShangraoInfo == null) break;
                int tile = int.Parse(tick[2]);
                recordShangraoInfo.burnt_tiles = (recordShangraoInfo.burnt_tiles ?? Array.Empty<int>()).Concat(new[] {tile}).ToArray();
                if (tile == recordShangraoInfo.bao || tick.Count < 4) break;
                int actor = int.Parse(tick[3]); var player = recordPlayerList.First(p => p.playerIndex == actor);
                recordShangraoInfo.burnt_river_indices ??= new[] {Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>()};
                recordShangraoInfo.burnt_river_indices[actor] = recordShangraoInfo.burnt_river_indices[actor].Concat(new[] {player.discardTiles.Count}).ToArray();
                player.discardTiles.Add(tile); player.discardIsMoqie.Add(false); player.discardRiichiFlags.Add(false);
                break;
            }
            case "waits": {
                int seat = int.Parse(tick[2]);
                if (!recordShangraoHints.TryGetValue(seat, out var cache)) recordShangraoHints[seat] = cache = new ShangraoHintsCache();
                cache.Accept(JsonConvert.DeserializeObject<ShangraoHints>(tick[3]));
                break;
            }
            case "win_source":
                recordPlayerList.First(p => p.playerIndex == int.Parse(tick[2])).isHu = true;
                lastWinnableTileId = int.Parse(tick[5]); lastJiagangPlayerIndex = lastDiscardPlayerIndex = -1;
                if (tick.Count > 6) recordShangraoInfo = JsonConvert.DeserializeObject<ShangraoInfo>(tick[6]);
                break;
            case "state": {
                recordShangraoInfo = JsonConvert.DeserializeObject<ShangraoInfo>(tick[2]);
                int[] scores = JsonConvert.DeserializeObject<int[]>(tick[3]);
                foreach (var p in recordPlayerList) { p.score = scores[p.playerIndex]; userIdToScore[p.userId] = p.score; }
                break;
            }
        }
        RefreshShangraoTable();
        return true;
    }
}
