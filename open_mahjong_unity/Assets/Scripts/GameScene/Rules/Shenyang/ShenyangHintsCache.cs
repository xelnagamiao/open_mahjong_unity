using System;
using System.Collections.Generic;
using System.Linq;

public sealed class ShenyangHintsCache {
    private ShenyangHints current;
    public void Reset() { current=null; }
    public void Accept(ShenyangHints hints) { if(hints?.source_hand_tiles!=null) current=hints; }
    private static bool Same(IEnumerable<int> a,IEnumerable<int> b) => a!=null && b!=null && a.OrderBy(t=>t).SequenceEqual(b.OrderBy(t=>t));
    private Dictionary<int,ShenyangWaitDetail> Rows(List<int> hand,List<string> melds,Action ready=null) {
        if(current?.local_request!=null)return LocalHintSnapshots.Rows(current.local_request,hand,ready)?.ToObject<Dictionary<int,ShenyangWaitDetail>>();
        if(current==null || hand==null || !(melds??new List<string>()).OrderBy(c=>c).SequenceEqual((current.source_melds??Array.Empty<string>()).OrderBy(c=>c))) return null;
        if(Same(hand,current.source_hand_tiles)) return current.waiting;
        if(hand.Count+1!=current.source_hand_tiles.Length || current.by_discard==null) return null;
        foreach(var entry in current.by_discard) {
            var before=new List<int>(current.source_hand_tiles);
            if(before.Remove(entry.Key) && Same(before,hand)) return entry.Value;
        }
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
