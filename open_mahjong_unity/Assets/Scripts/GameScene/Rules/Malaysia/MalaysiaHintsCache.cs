using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Server structural hints are valid only for their exact physical hand.</summary>
public sealed class MalaysiaHintsCache {
    private MalaysiaHints hints;
    public void Reset() { hints = null; }
    public void Accept(MalaysiaHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private static bool Same(IEnumerable<int> a, IEnumerable<int> b) => a != null && b != null && a.OrderBy(x=>x).SequenceEqual(b.OrderBy(x=>x));
    public HashSet<int> Waiting(TingpaiQuery q) {
        if (q?.Hand == null || hints == null || !(q.Melds ?? new List<string>()).OrderBy(x=>x).SequenceEqual((hints.source_melds ?? Array.Empty<string>()).OrderBy(x=>x))) return new HashSet<int>();
        if (Same(q.Hand,hints.source_hand_tiles)) return new HashSet<int>(hints.waiting_tiles ?? Array.Empty<int>());
        if (q.Hand.Count + 1 == hints.source_hand_tiles.Length && hints.waiting_by_discard != null)
            foreach (var row in hints.waiting_by_discard) {
                var remaining = new List<int>(hints.source_hand_tiles);
                if (remaining.Remove(row.Key) && Same(q.Hand,remaining)) return new HashSet<int>(row.Value);
            }
        return new HashSet<int>();
    }
    public WaitTileHint Describe(WaitHintQuery q) {
        if (q?.HandWithWin == null) return null;
        var hand = new List<int>(q.HandWithWin);
        if (!hand.Remove(q.HepaiTile) || !Waiting(new TingpaiQuery { Hand=hand, Melds=q.Melds }).Contains(q.HepaiTile)) return null;
        return WaitTileHint.None("结构听牌 · 须满5番或满番");
    }
}
