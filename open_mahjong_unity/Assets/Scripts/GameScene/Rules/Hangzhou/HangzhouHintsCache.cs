using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Matches local calculations or saved legacy rows to the exact physical hand.</summary>
public sealed class HangzhouHintsCache {
    private HangzhouHints hints;
    private System.Action onLocalReady;
    private bool RememberReady(TingpaiQuery q) { onLocalReady=q.OnReady; return true; }
    public void Reset() { hints = null; onLocalReady = null; }
    public void Accept(HangzhouHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private bool TryRows(List<int> hand, List<string> melds, out int[] waits,
        out Dictionary<int, HangzhouWaitDetail> details) {
        waits = null; details = null;
        if(hints?.local_request!=null) { var local=LocalHintSnapshots.Read<HangzhouHints>(hints.local_request,onLocalReady); if(local==null)return false; hints=local; }
        if (hints == null || hand == null || !LocalHintSnapshots.SameMelds(hints.source_melds, melds)) return false;
        if (LocalHintSnapshots.SameTiles(hand, hints.source_hand_tiles)) {
            waits = hints.waiting_tiles; details = hints.waiting_details;
            return true;
        }
        if (hints.waiting_by_discard != null && LocalHintSnapshots.TryFindSingleCut(hints.source_hand_tiles, hand, out int cut)
            && hints.waiting_by_discard.TryGetValue(cut, out waits)) {
            hints.waiting_details_by_discard?.TryGetValue(cut, out details);
            return true;
        }
        return false;
    }
    public HashSet<int> Waiting(TingpaiQuery query) => query != null && RememberReady(query) && TryRows(query.Hand, query.Melds, out var waits, out _)
        ? new HashSet<int>(waits ?? Array.Empty<int>()) : new HashSet<int>();

    public WaitTileHint Describe(WaitHintQuery query) {
        if (query?.HandWithWin == null) return null;
        var hand = new List<int>(query.HandWithWin);
        if (!hand.Remove(query.HepaiTile)
            || !TryRows(hand, query.Melds, out var waits, out var details)
            || !(waits ?? Array.Empty<int>()).Contains(query.HepaiTile)) return null;
        if (details != null && details.TryGetValue(query.HepaiTile, out var score)
            && score != null && score.fan >= 0 && score.fan <= 4 && score.base_score == (1 << score.fan))
            return WaitTileHint.TsumoOnly($"自摸{score.fan}番{score.base_score}分");
        // Legacy replay snapshots contain waits only; never invent their score.
        return WaitTileHint.TsumoOnly("自摸");
    }
}
