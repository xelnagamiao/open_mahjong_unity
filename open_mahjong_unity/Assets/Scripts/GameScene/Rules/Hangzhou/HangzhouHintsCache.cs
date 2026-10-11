using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Match authoritative hints to the exact physical hand and melds. No local wildcard solver.</summary>
public sealed class HangzhouHintsCache {
    private HangzhouHints hints;
    public void Reset() { hints = null; }
    public void Accept(HangzhouHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private static bool Same(IEnumerable<int> a, IEnumerable<int> b) => a != null && b != null && a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x));
    private bool TryRows(List<int> hand, List<string> melds, out int[] waits,
        out Dictionary<int, HangzhouWaitDetail> details) {
        waits = null; details = null;
        if (hints == null || hand == null) return false;
        if (!(hints.source_melds ?? Array.Empty<string>()).OrderBy(x => x).SequenceEqual((melds ?? new List<string>()).OrderBy(x => x))) return false;
        if (Same(hand, hints.source_hand_tiles)) {
            waits = hints.waiting_tiles; details = hints.waiting_details;
            return true;
        }
        if (hand.Count + 1 != hints.source_hand_tiles.Length || hints.waiting_by_discard == null) return false;
        foreach (var pair in hints.waiting_by_discard) {
            var remaining = new List<int>(hints.source_hand_tiles);
            if (!remaining.Remove(pair.Key) || !Same(remaining, hand)) continue;
            waits = pair.Value;
            hints.waiting_details_by_discard?.TryGetValue(pair.Key, out details);
            return true;
        }
        return false;
    }
    public HashSet<int> Waiting(TingpaiQuery query) => query != null
        && TryRows(query.Hand, query.Melds, out var waits, out _)
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
