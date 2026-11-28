using System;
using System.Collections.Generic;
using System.Linq;

public sealed class WuhanHintsCache {
    private WuhanHints hints;
    private System.Action onLocalReady;
    private bool RememberReady(TingpaiQuery q) { onLocalReady=q.OnReady; return true; }
    public void Reset() { hints = null; onLocalReady = null; }
    public void Accept(WuhanHints value) { if (value?.source_hand_tiles != null) hints = value; }

    private bool TryRows(List<int> hand, List<string> melds, out int[] waits,
        out Dictionary<int, WuhanWaitDetail> details) {
        waits = null; details = null;
        if(hints?.local_request!=null) { var local=LocalHintSnapshots.Read<WuhanHints>(hints.local_request,onLocalReady); if(local==null)return false; hints=local; }
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

    public WaitTileHint Describe(WaitHintQuery query, bool blocked = false) {
        if (query?.HandWithWin == null) return null;
        var hand = new List<int>(query.HandWithWin);
        if (!hand.Remove(query.HepaiTile) || !TryRows(hand, query.Melds, out var waits, out var details)
            || !(waits ?? Array.Empty<int>()).Contains(query.HepaiTile)) return null;
        if (blocked || hints.win_blocked) return WaitTileHint.None("未满足");
        if (details == null || !details.TryGetValue(query.HepaiTile, out var row) || row == null) return null;
        switch (row.kind) {
            case "none": return WaitTileHint.None("未满足");
            case "below_minimum": return WaitTileHint.None("未起和");
            case "tsumo_only": return WaitTileHint.TsumoOnly("仅自摸");
            case "ron": return row.points > 0 ? WaitTileHint.Ron(row.label) : null;
            default: return null;
        }
    }
}
