using System.Linq;
using UnityEngine;

public static class WuhanPresentation {
    public static WuhanInfo DisplayedInfo { get; private set; }
    public static string TileName(int tile) {
        if (tile < 40) return (tile % 10) + (tile < 20 ? "万" : tile < 30 ? "筒" : "条");
        string[] names = { "东", "南", "西", "北", "中", "白", "发" };
        return tile >= 41 && tile <= 47 ? names[tile - 41] : "?";
    }
    public static void Apply(WuhanInfo info, bool refreshBadges) {
        bool jokerChanged = DisplayedInfo?.joker != info?.joker;
        DisplayedInfo = info;
        if (jokerChanged) {
            GameCanvas.Instance?.RefreshRuleHandOrder();
            Game3DManager.Instance?.RefreshRecordJokerHandOrder();
            GameRecordManager.Instance?.RefreshInitialHandJokerOrder();
        }
        var panel = Object.FindFirstObjectByType<SimpleRoundPanel>();
        if (panel != null) {
            string first = info?.joker is int joker ? "癞：" + TileName(joker) : "待庄家首打";
            string second = "皮：" + string.Join(" ", (info?.skins ?? new[] {45}).Select(TileName));
            string third = info?.sea == true ? "海底：仅自摸或过" : info?.push_gold == true ? "推金：须杠出皮子" : "皮癞：杠补牌 / 打出过";
            panel.UpdateSupplementalLines(first, second, third);
        }
        if (!refreshBadges) return;
        foreach (var card in Object.FindObjectsByType<TileCard>(FindObjectsSortMode.None)) RuleTileBadge.Apply(card, card.tileId);
        foreach (var card in Object.FindObjectsByType<StaticCard>(FindObjectsSortMode.None)) RuleTileBadge.Apply(card.transform, card.TileId);
        foreach (var tile in Object.FindObjectsByType<Tile3D>(FindObjectsSortMode.None)) tile.RefreshRuleBadge();
    }
}
