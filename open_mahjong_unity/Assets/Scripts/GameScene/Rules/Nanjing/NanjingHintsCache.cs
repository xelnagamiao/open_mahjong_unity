using System;
using System.Collections.Generic;
using System.Linq;

public sealed class NanjingHintsCache {
    private NanjingHints hints;
    public void Reset() { hints=null; }
    public void Accept(NanjingHints value) { if(value?.source_hand_tiles!=null) hints=value; }
    private static bool Same(IEnumerable<int> a,IEnumerable<int> b) => a!=null && b!=null && a.OrderBy(t=>t).SequenceEqual(b.OrderBy(t=>t));
    private bool Rows(List<int> hand,List<string> melds,out int[] waits,out Dictionary<int,NanjingWaitDetail> details) {
        waits=null;details=null;
        if(hints==null || hand==null || !(hints.source_melds??Array.Empty<string>()).OrderBy(x=>x).SequenceEqual((melds??new List<string>()).OrderBy(x=>x))) return false;
        if(Same(hand,hints.source_hand_tiles)) { waits=hints.waiting_tiles;details=hints.waiting_details;return true; }
        if(hand.Count+1!=hints.source_hand_tiles.Length || hints.waiting_by_discard==null) return false;
        foreach(var row in hints.waiting_by_discard) {
            var remaining=new List<int>(hints.source_hand_tiles);
            if(!remaining.Remove(row.Key) || !Same(remaining,hand)) continue;
            waits=row.Value;hints.waiting_details_by_discard?.TryGetValue(row.Key,out details);return true;
        }
        return false;
    }
    public HashSet<int> Waiting(TingpaiQuery query) => query!=null && Rows(query.Hand,query.Melds,out var waits,out _)
        ? new HashSet<int>(waits??Array.Empty<int>()) : new HashSet<int>();
    public WaitTileHint Describe(WaitHintQuery query,bool blocked=false) {
        if(query?.HandWithWin==null) return null;
        var hand=new List<int>(query.HandWithWin);
        if(!hand.Remove(query.HepaiTile) || !Rows(hand,query.Melds,out var waits,out var details)
            || !(waits??Array.Empty<int>()).Contains(query.HepaiTile)) return null;
        if(blocked) return WaitTileHint.None("未满足");
        if(details==null || !details.TryGetValue(query.HepaiTile,out var row) || row==null) return null;
        if(row.kind=="unmet") return WaitTileHint.None("未满足");
        if(row.kind=="not_ready") return WaitTileHint.None("未起和");
        if(row.kind=="self_only") return WaitTileHint.TsumoOnly("仅自摸");
        return row.kind=="ron" && row.points>0 ? WaitTileHint.Ron($"{row.points}分") : WaitTileHint.None("未满足");
    }
}
