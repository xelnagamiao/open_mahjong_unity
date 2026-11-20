using System;
using System.Collections.Generic;
using System.Linq;

internal static class DuocaiHintPresentation {
    internal static int DisplayTile(int tile)=>tile>=1000&&tile<3000?DuocaiTiles.Face(tile):tile;
    internal static int[] DisplayTiles(IEnumerable<int> tiles)=>tiles.Select(DisplayTile).Distinct().OrderBy(t=>t).ToArray();
    internal static int[] Variants(IEnumerable<int> tiles,int display)=>tiles.Where(t=>DisplayTile(t)==display).Distinct().OrderBy(t=>t).ToArray();
    internal static WaitTileHint Merge(IReadOnlyList<(int tile,WaitTileHint hint)> rows) {
        if(rows.Count==0)return WaitTileHint.None("未满足");
        var first=rows[0].hint;
        if(rows.All(r=>r.hint.Kind==first.Kind&&r.hint.Label==first.Label))return new WaitTileHint{Kind=first.Kind,Label=first.Label};
        string kind=rows.Any(r=>r.hint.CanRon)?WaitTileHint.KindRon:rows.Any(r=>r.hint.Kind==WaitTileHint.KindTsumoOnly)?WaitTileHint.KindTsumoOnly:WaitTileHint.KindNone;
        return new WaitTileHint{Kind=kind,Label=string.Join("\n",rows.Select(r=>(r.tile>=2000&&r.tile<3000?"银":r.tile>=1000&&r.tile<2000?"金":"普")+" "+r.hint.Label.Replace("\n"," ")))};
    }
}
