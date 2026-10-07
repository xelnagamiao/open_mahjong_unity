using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>The final four passed tiles remain separate from the river and hand.</summary>
internal static class ChangchunTailTiles {
    private const string MarkerName="ChangchunTail";
    public static void Render(Dictionary<int,int> visibleTiles,Dictionary<int,string> seats,bool revealed) {
        var table=Game3DManager.Instance;
        if(table==null || visibleTiles==null || seats==null) return;
        foreach(var seat in seats) {
            var parent=table.GetPosPanel(seat.Value)?.buhuaPosition;
            if(parent==null) continue;
            var existing=parent.Cast<Transform>().Where(t=>t.name==MarkerName).ToArray();
            bool wanted=visibleTiles.TryGetValue(seat.Key,out int physical);
            int displayTile=physical>0 ? physical : 2; // The shared 3D back tile.
            if(wanted && existing.Length==1) {
                var tile=existing[0].GetComponent<Tile3D>();
                if(tile!=null && tile.GetTileId()==displayTile) {ApplyPresentation(tile,seat.Value,revealed);continue;}
            }
            foreach(var old in existing) MahjongObjectPool.Instance.Return(-1,old.gameObject);
            if(!wanted) continue;
            int before=parent.childCount;
            table.Change3DTile("SetBuhuacardWithoutAnimation",physical,0,seat.Value,false,null);
            if(parent.childCount<=before) continue;
            var marker=parent.GetChild(parent.childCount-1);marker.name=MarkerName;
            ApplyPresentation(marker.GetComponent<Tile3D>(),seat.Value,revealed);
        }
    }
    private static void ApplyPresentation(Tile3D tile,string seat,bool revealed) {
        if(tile==null) return;
        tile.SetConcealedFaceDown(!revealed);
        // 末四张仅本人可查看；局终公开信息提前到达也不能让对手盖牌进入悬停翻面。
        tile.SetHoverPeekAllowed(seat=="self");
    }
}
