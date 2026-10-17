using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>只展示与当前实体手牌、副露完全匹配的权威听牌。</summary>
public sealed class JiujiangHintsCache {
    private JiujiangHints hints;
    private System.Action onLocalReady;
    private bool RememberReady(TingpaiQuery q) { onLocalReady=q.OnReady; return true; }
    public void Reset() { hints = null; }
    public void Accept(JiujiangHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private static bool Same(IEnumerable<int> a, IEnumerable<int> b) => a != null && b != null
        && a.OrderBy(t => t).SequenceEqual(b.OrderBy(t => t));

    private bool TryRows(List<int> hand, List<string> melds, out int[] waits,
        out Dictionary<int, JiujiangWaitDetail> details) {
        waits = null; details = null;
        if(hints?.local_request!=null) { var local=LocalHintSnapshots.Read<JiujiangHints>(hints.local_request,onLocalReady); if(local==null)return false; hints=local; }
        if (hints == null || hand == null) return false;
        if (!(hints.source_melds ?? Array.Empty<string>()).OrderBy(t => t)
            .SequenceEqual((melds ?? new List<string>()).OrderBy(t => t))) return false;
        if (Same(hand, hints.source_hand_tiles)) {
            waits = hints.waiting_tiles; details = hints.waiting_details;
            return true;
        }
        if (hand.Count + 1 != hints.source_hand_tiles.Length || hints.waiting_by_discard == null) return false;
        foreach (var entry in hints.waiting_by_discard) {
            var remaining = new List<int>(hints.source_hand_tiles);
            if (!remaining.Remove(entry.Key) || !Same(remaining, hand)) continue;
            waits = entry.Value;
            hints.waiting_details_by_discard?.TryGetValue(entry.Key, out details);
            return true;
        }
        return false;
    }

    public HashSet<int> Waiting(TingpaiQuery query) => query != null && RememberReady(query) && TryRows(query.Hand, query.Melds, out var waits, out _)
        ? new HashSet<int>(waits ?? Array.Empty<int>()) : new HashSet<int>();

    public WaitTileHint Describe(WaitHintQuery query, bool blocked = false) {
        if (query?.HandWithWin == null) return null;
        var hand = new List<int>(query.HandWithWin);
        if (!hand.Remove(query.HepaiTile)
            || !TryRows(hand, query.Melds, out var waits, out var details)
            || !(waits ?? Array.Empty<int>()).Contains(query.HepaiTile)) return null;
        if (blocked) return WaitTileHint.None("0分");
        if (details == null || !details.TryGetValue(query.HepaiTile,out var score) || score == null) return null;
        string label = $"{System.Math.Max(0,score.points)}分";
        return score.points <= 0 || score.kind == "wuyi" ? WaitTileHint.None(label)
            : score.kind == "zimo" ? WaitTileHint.TsumoOnly(label) : WaitTileHint.Ron(label);
    }
}
