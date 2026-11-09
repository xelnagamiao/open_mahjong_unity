using System.Collections.Generic;
using System.Linq;

public partial class GameRecordManager {
    private List<int> RecordDrawingSeats() => Enumerable.Range(0, RecordPlayerCount)
        .Where(seat => !IsBloodBattleRecord() || recordPlayerList.Find(p => p.playerIndex == seat)?.isHu != true)
        .ToList();

    // currentPlayerIndex already points to the next player after a discard or
    // a continuing win. At a drawn/claimed hand the next ordinary draw is one
    // seat later. Do not infer turn ownership from physical wall indices.
    private int NextForecastDrawingSeat(List<int> seats) {
        if (seats.Count == 0 || (IsBloodBattleRecord() && seats.Count <= 1)) return -1;
        bool? discardPending = null;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            for (int i = System.Math.Min(currentNode, round.actionTicks.Count) - 1; i >= 0; i--) {
                var tick = round.actionTicks[i];
                if (tick == null || tick.Count == 0) continue;
                string action = tick[0];
                if (action == "end" || action == "ryuukyoku" || action == "liuju" || action == "blood" || action == "jiuzhongjiupai") return -1;
                if (action.StartsWith("hu")) {
                    if (!IsBloodBattleRecord() && !IsXueliuRecord()) return -1;
                    discardPending = false;
                    break;
                }
                // A committed replacement is still pending. Its source may be
                // front, tail, paired tail, or a batch; resume after its draw tick.
                if (action == "ag" || action == "g" || action == "jg" || action == "nuki" || action == "bh") return -1;
                if (action == "d" || action == "gd" || action == "bd" || action == "nd"
                    || action == "p" || action == "cl" || action == "cm" || action == "cr") {
                    discardPending = true;
                    break;
                }
                if (action == "c") { discardPending = ContinuesChangshaRecordDiscards(i); break; }
                if (action == "reset") break;
            }
        }
        int next = seats.FindIndex(seat => seat >= currentPlayerIndex);
        if (next < 0) next = 0;
        RecordPlayer current = recordPlayerList.Find(p => p.playerIndex == currentPlayerIndex);
        bool mustDiscard = discardPending ?? (!waitingForDrawAfterCut && current != null
            && (current.showHandDrawSlotActive || current.tileList.Count % 3 == 2));
        if (mustDiscard && seats[next] == currentPlayerIndex) next = (next + 1) % seats.Count;
        return next;
    }

    internal void ComputeRecordDrawForecast(ICollection<int> indices, int count) {
        var seats = RecordDrawingSeats();
        int next = NextForecastDrawingSeat(seats);
        int selected = seats.IndexOf(selectedPlayerIndex);
        if (next < 0 || selected < 0) return;
        if (TryGetDuplicateDrawIndices(indices, count, seats, next)) return;
        int offset = (selected - next + seats.Count) % seats.Count;
        int position = 0;
        foreach (int index in currentOriginalIndices) {
            if (!IsOriginalWallIndexNormalDrawable(index)) continue;
            if (position++ % seats.Count != offset) continue;
            indices.Add(index);
            if (--count <= 0) break;
        }
    }

    // Keep physical slots stable when rewinding. The secondary section includes
    // initial replacement/indicator slots plus the current rule's reserved tail.
    // Sanma has eight replacement slots around ten indicators, but still stops
    // ordinary draws with fourteen tiles left (not eighteen).
    private int initialRecordReservedWallCount;
    internal HashSet<int> RecordReservedWallIndices() {
        var result = new HashSet<int>();
        if (IsDuplicateReplay) return result;
        if (IsShangraoRecord()) {
            if (recordShangraoIndicatorSlot >= 0) result.Add(recordShangraoIndicatorSlot);
            return result;
        }
        int initial = IsSanmaRecord() ? 18 : initialRecordReservedWallCount;
        if (IsWashizuRecord() && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round))
            initial = round.washizu?.dead_wall_count ?? 13;
        for (int i = System.Math.Max(0, originalTilesList.Count - initial); i < originalTilesList.Count; i++) result.Add(i);
        foreach (int index in currentOriginalIndices)
            if (!IsOriginalWallIndexNormalDrawable(index)) result.Add(index);
        return result;
    }
}
