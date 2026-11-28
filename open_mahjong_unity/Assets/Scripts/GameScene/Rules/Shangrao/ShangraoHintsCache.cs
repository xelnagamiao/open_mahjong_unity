using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ShangraoHintsCache {
    private ShangraoHints hints;
    private Action onReady;
    public void Reset() { hints = null; onReady = null; }
    public void Accept(ShangraoHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private bool TryRows(List<int> hand, List<string> melds, out int[] waits, out Dictionary<int, ShangraoWait> details) {
        waits = null; details = null;
        if (hints?.local_request != null) {
            var local = LocalHintSnapshots.Read<ShangraoHints>(hints.local_request, onReady);
            if (local == null) return false;
            hints = local;
        }
        if (hints == null || hand == null || !LocalHintSnapshots.SameMelds(hints.source_melds, melds)) return false;
        if (LocalHintSnapshots.SameTiles(hand, hints.source_hand_tiles)) { waits = hints.waiting_tiles; details = hints.waiting_details; return true; }
        if (hints.waiting_by_discard != null && LocalHintSnapshots.TryFindSingleCut(hints.source_hand_tiles, hand, out int cut)
            && hints.waiting_by_discard.TryGetValue(cut, out waits)) {
            hints.waiting_details_by_discard?.TryGetValue(cut, out details);
            return true;
        }
        return false;
    }
    public HashSet<int> Waiting(TingpaiQuery query) {
        if (query == null) return new HashSet<int>();
        onReady = query.OnReady;
        return TryRows(query.Hand, query.Melds, out var waits, out _) ? new HashSet<int>(waits ?? Array.Empty<int>()) : new HashSet<int>();
    }
    public WaitTileHint Describe(WaitHintQuery query, bool blocked = false) {
        if (query?.HandWithWin == null) return WaitTileHint.None("未满足");
        var hand = new List<int>(query.HandWithWin);
        if (!hand.Remove(query.HepaiTile) || !TryRows(hand, query.Melds, out var waits, out var details)
            || !(waits ?? Array.Empty<int>()).Contains(query.HepaiTile)) return WaitTileHint.None("未满足");
        if (blocked || hints.win_blocked) return WaitTileHint.None("未满足");
        return details != null && details.TryGetValue(query.HepaiTile, out var row) && row?.kind == "zimo" && row.points > 0
            ? WaitTileHint.TsumoOnly($"仅自摸\n{row.points}分") : WaitTileHint.None("未满足");
    }
}
