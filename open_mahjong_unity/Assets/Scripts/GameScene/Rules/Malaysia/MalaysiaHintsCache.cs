using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Matches local calculations or saved legacy rows to the exact physical hand.</summary>
public sealed class MalaysiaHintsCache {
    private MalaysiaHints hints;
    public void Reset() { hints = null; }
    public void Accept(MalaysiaHints value) { if (value?.source_hand_tiles != null) hints = value; }
    public HashSet<int> Waiting(TingpaiQuery q) {
        if(hints?.local_request!=null) { var local=LocalHintSnapshots.Read<MalaysiaHints>(hints.local_request,q?.OnReady); if(local==null)return new HashSet<int>(); hints=local; }
        if (q?.Hand == null || hints == null || !LocalHintSnapshots.SameMelds(hints.source_melds, q.Melds)) return new HashSet<int>();
        if (LocalHintSnapshots.SameTiles(q.Hand, hints.source_hand_tiles)) return new HashSet<int>(hints.waiting_tiles ?? Array.Empty<int>());
        if (hints.waiting_by_discard != null && LocalHintSnapshots.TryFindSingleCut(hints.source_hand_tiles, q.Hand, out int cut)
            && hints.waiting_by_discard.TryGetValue(cut, out var waits))
            return new HashSet<int>(waits ?? Array.Empty<int>());
        return new HashSet<int>();
    }
    public WaitTileHint Describe(WaitHintQuery q) {
        if (q?.HandWithWin == null) return null;
        var hand = new List<int>(q.HandWithWin);
        if (!hand.Remove(q.HepaiTile) || !Waiting(new TingpaiQuery { Hand=hand, Melds=q.Melds }).Contains(q.HepaiTile)) return null;
        return WaitTileHint.None("牌形听牌");
    }
}
