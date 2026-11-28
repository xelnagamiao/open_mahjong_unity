using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ShenyangHintsCache {
    private ShenyangHints current;
    public void Reset() { current=null; }
    public void Accept(ShenyangHints hints) { if(hints?.source_hand_tiles!=null) current=hints; }
    private static bool Same(IEnumerable<int> a,IEnumerable<int> b) => a!=null && b!=null && a.OrderBy(t=>t).SequenceEqual(b.OrderBy(t=>t));
    private static bool TryFindSingleCut(int[] source, List<int> current, out int cutTile) {
        cutTile = -1;
        if (source == null || current == null || current.Count + 1 != source.Length) return false;
        Span<int> diff = stackalloc int[48];
        for (int i = 0; i < source.Length; i++) {
            int t = source[i];
            if (t < 0 || t >= 48) return false;
            diff[t]++;
        }
        for (int i = 0; i < current.Count; i++) {
            int t = current[i];
            if (t < 0 || t >= 48 || --diff[t] < 0) return false;
        }
        for (int t = 0; t < 48; t++) {
            if (diff[t] == 1) {
                if (cutTile >= 0) return false;
                cutTile = t;
            } else if (diff[t] != 0) {
                return false;
            }
        }
        return cutTile >= 0;
    }
    private Dictionary<int,ShenyangWaitDetail> Rows(List<int> hand,List<string> melds,Action ready=null) {
        if (current?.local_request != null) {
            var local = LocalHintSnapshots.Read<ShenyangHints>(current.local_request, ready);
            if (local == null) return null;
            current = local;
        }
        if(current==null || hand==null || !(melds??new List<string>()).OrderBy(c=>c).SequenceEqual((current.source_melds??Array.Empty<string>()).OrderBy(c=>c))) return null;
        if(Same(hand,current.source_hand_tiles)) return current.waiting;
        if(current.by_discard!=null && TryFindSingleCut(current.source_hand_tiles, hand, out int cut)
           && current.by_discard.TryGetValue(cut, out var cutRows)) return cutRows;
        return null;
    }
    public HashSet<int> Waiting(TingpaiQuery q) => new HashSet<int>(Rows(q?.Hand,q?.Melds,q?.OnReady)?.Keys.AsEnumerable() ?? Enumerable.Empty<int>());
    public WaitTileHint Describe(WaitHintQuery q) {
        if(q?.HandWithWin==null) return null;
        var before=new List<int>(q.HandWithWin);
        if(!before.Remove(q.HepaiTile)) return null;
        var rows=Rows(before,q.Melds);
        if(rows==null || !rows.TryGetValue(q.HepaiTile,out var hint)) return null;
        if(hint.status=="unmet") return WaitTileHint.None("未满足");
        if(hint.status=="below") return WaitTileHint.None("未起和");
        if(hint.status=="self_draw") return WaitTileHint.TsumoOnly("仅自摸");
        return hint.status=="win" ? WaitTileHint.Ron(hint.label) : WaitTileHint.None("未满足");
    }
}
