using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Match authoritative hints to the exact physical hand and melds. No local wildcard solver.</summary>
public sealed class HangzhouHintsCache {
    private HangzhouHints hints;
    public void Reset() { hints = null; }
    public void Accept(HangzhouHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private static bool Same(IEnumerable<int> a, IEnumerable<int> b) => a != null && b != null && a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x));
    public HashSet<int> Waiting(TingpaiQuery query) {
        if (hints == null || query?.Hand == null) return new HashSet<int>();
        if (!(hints.source_melds ?? Array.Empty<string>()).OrderBy(x => x).SequenceEqual((query.Melds ?? new List<string>()).OrderBy(x => x))) return new HashSet<int>();
        if (Same(query.Hand, hints.source_hand_tiles)) return new HashSet<int>(hints.waiting_tiles ?? Array.Empty<int>());
        if (query.Hand.Count + 1 != hints.source_hand_tiles.Length || hints.waiting_by_discard == null) return new HashSet<int>();
        foreach (var pair in hints.waiting_by_discard) {
            var hand = new List<int>(hints.source_hand_tiles);
            if (hand.Remove(pair.Key) && Same(hand, query.Hand)) return new HashSet<int>(pair.Value ?? Array.Empty<int>());
        }
        return new HashSet<int>();
    }
}
