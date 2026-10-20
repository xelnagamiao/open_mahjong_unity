using System;
using System.Collections.Generic;

public partial class GameRecordManager {
    private RuleReplayModule recordReplayModule;
    private RuleManifest recordReplayManifest;
    private string recordReplaySubRule;
    private int recordDeadWallCount;
    private string recordDeadWallMode;

    private RuleManifest RecordRuleManifest => RuleRegistry.Resolve(
        ReadGameTitleString(gameRecord?.gameTitle, "rule", ""),
        ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", ""));

    // The handler belongs to this manager, not RuleRegistry.Current (a live game
    // can coexist with a record). Changing rule/subrule releases the old caches.
    private RuleReplayModule RecordReplay {
        get {
            RuleManifest manifest = RecordRuleManifest;
            string subRule = ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "");
            if (recordReplayModule == null || recordReplayManifest != manifest || recordReplaySubRule != subRule) {
                recordReplayModule?.Clear?.Invoke();
                recordReplayManifest = manifest;
                recordReplaySubRule = subRule;
                recordReplayModule = manifest?.ReplayFactory?.Invoke(this) ?? CreateStandardReplayModule();
            }
            return recordReplayModule;
        }
    }

    internal RuleReplayModule CreateStandardReplayModule(int reservedWallTiles = 0) =>
        new RuleReplayModule { ReservedWallTiles = reservedWallTiles };

    private void ResetRecordRuleState() {
        RuleReplayModule module = RecordReplay;
        recordDeadWallCount = module.ReservedWallTiles;
        recordDeadWallMode = "";
        module.Reset?.Invoke();
    }

    private List<string> NormalizeRecordRuleTick(List<string> tick) =>
        RecordReplay.NormalizeTick?.Invoke(tick) ?? tick;

    private bool ApplyRecordRuleActionBeforeMutation(IReadOnlyList<string> tick) {
        if (tick == null || tick.Count == 0) return false;
        if (RecordReplay.ApplyBeforeMutation?.Invoke(tick) == true) return true;
        if (RecordRuleManifest?.SupportsRobbedAddedKongSource == true
            || RecordRuleManifest?.PublicReadyStateReplay == true)
            RestoreTaiwanRobbedJiagangForRecord(tick);
        if (RecordRuleManifest?.PublicReadyStateReplay != true || tick[0] != "state") return false;
        ApplyTaiwanRecordStateTick(tick);
        return true;
    }

    private int ResolveRecordRuleNextPlayer(IReadOnlyList<string> tick, int fallback) =>
        RecordReplay.NextPlayer?.Invoke(tick, fallback) ?? fallback;

    private bool TryConsumeRecordRuleWallTile(string action) {
        if (RecordReplay.ConsumeWallTile?.Invoke(action) == true) return true;
        if (RecordRuleManifest?.ReplacementFromTailEnd != true) return false;
        ConsumeTaiwanRecordWallTile(action);
        return true;
    }

    private void RefreshRecordRulePlayerTags() {
        RecordReplay.RefreshPlayerTags?.Invoke();
        if (RecordRuleManifest?.PublicReadyStateReplay == true) RefreshTaiwanRecordPlayerTags();
    }

    private int GetRecordRemainTiles() {
        if (RecordReplay.RemainingTiles != null) return RecordReplay.RemainingTiles();
        if (IsDuplicateReplay) return currentTilesList.Count;
        return Math.Max(0, currentTilesList.Count - recordDeadWallCount);
    }

    internal bool IsOriginalWallIndexNormalDrawable(int originalIndex) {
        if (IsShangraoRecord()) return originalIndex != recordShangraoIndicatorSlot && currentOriginalIndices.Contains(originalIndex);
        if (IsDuplicateReplay) return currentOriginalIndices.Contains(originalIndex);
        if (recordDeadWallCount == 0) return true;
        int currentPosition = currentOriginalIndices != null ? currentOriginalIndices.IndexOf(originalIndex) : -1;
        return currentPosition >= 0 && currentPosition < GetRecordRemainTiles();
    }
}
