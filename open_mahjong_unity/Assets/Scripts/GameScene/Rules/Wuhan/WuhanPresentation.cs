using System.Collections.Generic;
using UnityEngine;

public static class WuhanPresentation {
    public static WuhanInfo DisplayedInfo { get; private set; }
    internal static RoundIndicatorDisplay Indicators(WuhanInfo info) {
        var tiles = new List<RoundIndicatorTile> { new RoundIndicatorTile(info?.indicator, "指") };
        var shown = new HashSet<int> { tiles[0].TileId };
        foreach (int skin in info?.skins ?? new[] {45}) {
            if (skin > 0 && shown.Add(skin)) tiles.Add(new RoundIndicatorTile(skin, "皮"));
        }
        if (info?.joker > 0) tiles.Add(new RoundIndicatorTile(info.joker, "赖"));
        return new RoundIndicatorDisplay(tiles);
    }
    public static void Apply(WuhanInfo info, bool refreshBadges) {
        bool jokerChanged = DisplayedInfo?.joker != info?.joker;
        DisplayedInfo = info;
        if (jokerChanged) {
            GameCanvas.Instance?.RefreshRuleHandOrder();
            Game3DManager.Instance?.RefreshRecordJokerHandOrder();
            GameRecordManager.Instance?.RefreshInitialHandJokerOrder();
        }
        RoundPanel.Instance?.RefreshIndicators(WuhanGameState.RuleId, Indicators(info));
        if (!refreshBadges) return;
        TileInstances<TileCard>.ForEach(card => RuleTileBadge.Apply(card, card.tileId));
        TileInstances<StaticCard>.ForEach(card => RuleTileBadge.Apply(card.transform, card.TileId));
        TileInstances<Tile3D>.ForEach(tile => tile.RefreshRuleBadge());
    }
}
