using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>只展示与当前实体手牌、副露完全匹配的权威听牌；不另写癞子求解器。</summary>
public sealed class HongzhongHintsCache {
    private HongzhongHints hints;
    public void Reset() { hints = null; }
    public void Accept(HongzhongHints value) { if (value?.source_hand_tiles != null) hints = value; }
    private static bool Same(IEnumerable<int> a, IEnumerable<int> b) => a != null && b != null
        && a.OrderBy(t=>t).SequenceEqual(b.OrderBy(t=>t));
    public HashSet<int> Waiting(TingpaiQuery query) {
        if (hints == null || query?.Hand == null) return new HashSet<int>();
        if (!(hints.source_melds ?? Array.Empty<string>()).OrderBy(t=>t)
            .SequenceEqual((query.Melds ?? new List<string>()).OrderBy(t=>t))) return new HashSet<int>();
        if (Same(query.Hand,hints.source_hand_tiles)) return new HashSet<int>(hints.waiting_tiles ?? Array.Empty<int>());
        if (query.Hand.Count+1 != hints.source_hand_tiles.Length || hints.waiting_by_discard == null) return new HashSet<int>();
        foreach (var entry in hints.waiting_by_discard) {
            var remaining = new List<int>(hints.source_hand_tiles);
            if (remaining.Remove(entry.Key) && Same(remaining,query.Hand))
                return new HashSet<int>(entry.Value ?? Array.Empty<int>());
        }
        return new HashSet<int>();
    }
}
