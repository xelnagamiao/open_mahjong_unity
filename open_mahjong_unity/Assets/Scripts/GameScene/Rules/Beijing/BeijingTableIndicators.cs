using System;
using System.Collections.Generic;
using UnityEngine;

public static class BeijingTableIndicators {
    public static BeijingInfo DisplayedInfo { get; private set; }
    public static IEnumerable<int> VisibleTiles=>DisplayedInfo==null?Array.Empty<int>():new[]{DisplayedInfo.indicator};
    public static RoundIndicatorDisplay FieldState(BeijingInfo info)=>info==null?new RoundIndicatorDisplay():new RoundIndicatorDisplay {
        Tiles=new[]{info.indicator,info.hun}, Caption="", Counter="左混儿皮儿  右混儿"
    };
    public static void Refresh(BeijingInfo info) {
        DisplayedInfo=info;RoundPanel.Instance?.RefreshRuleIndicators(FieldState(info));
        if(GameCanvas.Instance!=null)foreach(var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true))RuleTileBadge.Apply(card,card.tileId);
    }
    public static string Badge(int tile)=>tile==DisplayedInfo?.hun?"混":null;
}
