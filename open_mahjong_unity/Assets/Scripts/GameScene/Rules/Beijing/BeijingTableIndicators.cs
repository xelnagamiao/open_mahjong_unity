using System;
using System.Collections.Generic;
using UnityEngine;

public static class BeijingTableIndicators {
    public static BeijingInfo DisplayedInfo { get; private set; }
    public static IEnumerable<int> VisibleTiles=>DisplayedInfo==null?Array.Empty<int>():new[]{DisplayedInfo.indicator};
    public static RoundIndicatorDisplay FieldState(BeijingInfo info) => new RoundIndicatorDisplay(new[] {
        new RoundIndicatorTile(info?.indicator, "指"), new RoundIndicatorTile(info?.hun, "混")
    });
    public static void Refresh(BeijingInfo info) {
        DisplayedInfo=info;RoundPanel.Instance?.RefreshIndicators(BeijingGameState.RuleId, FieldState(info));
        if(GameCanvas.Instance!=null)foreach(var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true))RuleTileBadge.Apply(card,card.tileId);
    }
    public static string Badge(int tile)=>tile==DisplayedInfo?.hun?"混":null;
}
