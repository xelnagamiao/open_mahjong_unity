using System;
using System.Collections.Generic;
using System.Linq;

public sealed class NanjingHintsCache {
    private NanjingHints hints;
    private System.Action onLocalReady;
    private bool RememberReady(TingpaiQuery q) { onLocalReady=q.OnReady; return true; }
    public void Reset() { hints=null; onLocalReady=null; }
    public void Accept(NanjingHints value) { if(value?.source_hand_tiles!=null) hints=value; }
    private bool Rows(List<int> hand,List<string> melds,out int[] waits,out Dictionary<int,NanjingWaitDetail> details) {
        waits=null;details=null;
        if(hints?.local_request!=null) { var local=LocalHintSnapshots.Read<NanjingHints>(hints.local_request,onLocalReady); if(local==null)return false; hints=local; }
        if(hints==null || hand==null || !LocalHintSnapshots.SameMelds(hints.source_melds,melds)) return false;
        if(LocalHintSnapshots.SameTiles(hand,hints.source_hand_tiles)) { waits=hints.waiting_tiles;details=hints.waiting_details;return true; }
        if(hints.waiting_by_discard!=null && LocalHintSnapshots.TryFindSingleCut(hints.source_hand_tiles,hand,out int cut) && hints.waiting_by_discard.TryGetValue(cut,out waits)) {
            hints.waiting_details_by_discard?.TryGetValue(cut,out details);
            return true;
        }
        return false;
    }
    public HashSet<int> Waiting(TingpaiQuery query) => query!=null && RememberReady(query) && Rows(query.Hand,query.Melds,out var waits,out _)
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
