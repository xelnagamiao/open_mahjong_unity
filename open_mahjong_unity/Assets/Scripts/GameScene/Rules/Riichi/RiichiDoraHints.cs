using System.Collections.Generic;

/// <summary>Use only currently public indicators. Ura indicators and future replay ticks never enter here.</summary>
public static class RiichiDoraHints {
    public static bool Matches(int physicalTile, IEnumerable<int> indicators, bool sanma, bool redDora) {
        int tile = TileIdOrder.Normalize(physicalTile);
        if (tile < 11 || tile > 47 || tile % 10 == 0) return false;
        if (redDora && Riichi.RiichiTileUtil.IsRedFive(physicalTile)) return true;
        if (indicators == null) return false;
        foreach (int physicalIndicator in indicators) {
            int indicator = TileIdOrder.Normalize(physicalIndicator);
            if (indicator < 11 || indicator > 47 || indicator % 10 == 0) continue;
            int dora = sanma && indicator == 11 ? 19 : Riichi.RiichiTileUtil.DoraFromIndicator(indicator);
            if (dora == tile) return true;
        }
        return false;
    }

    public static bool IsDora(int tile) {
        if (!ConfigManager.Instance || !ConfigManager.Instance.DoraHintsEnabled) return false;
        var record = GameRecordManager.Instance;
        if (record && record.isActiveAndEnabled) return record.IsVisibleRecordDora(tile);
        if (RuleRegistry.Current?.RuleId != RiichiGameState.RuleId) return false;
        var state = RiichiGameState.Active;
        if (state == null) return false;
        bool sanma = RuleRegistry.CurrentSubRule == "riichi/sanma";
        return Matches(tile, state.DoraIndicators, sanma, state.RedDora)
            || Matches(tile, state.KanDoraIndicators, sanma, false);
    }
}
