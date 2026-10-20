using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ShangraoTableIndicators {
    public static ShangraoInfo DisplayedInfo { get; private set; }
    public static IEnumerable<int> VisibleTiles => CountedIndicators(DisplayedInfo);
    public static IEnumerable<int> CountedIndicators(ShangraoInfo info) => info == null ? Array.Empty<int>()
        : new[] {info.indicator}.Concat((info.burnt_tiles ?? Array.Empty<int>()).Where(tile => tile == info.bao));
    public static RoundIndicatorDisplay FieldState(ShangraoInfo info) => info == null ? new RoundIndicatorDisplay()
        : new RoundIndicatorDisplay {Tiles = new[] {info.indicator, info.bao}, Caption = "", Counter = "左指示 右宝牌"};
    public static void Refresh(ShangraoInfo info, Dictionary<int, string> positions = null, Dictionary<int, int[]> rivers = null) {
        DisplayedInfo = info;
        RoundPanel.Instance?.RefreshRuleIndicators(FieldState(info));
        if (Game3DManager.Instance != null) {
            var overlay = Game3DManager.Instance.GetComponent<ShangraoRiverOverlay>() ?? Game3DManager.Instance.gameObject.AddComponent<ShangraoRiverOverlay>();
            overlay.Info = info; overlay.Positions = positions; overlay.Rivers = rivers;
        }
        if (GameCanvas.Instance != null) foreach (var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true)) RuleTileBadge.Apply(card, card.tileId);
    }
    public static string Badge(int tile) => tile == DisplayedInfo?.bao ? "宝" : null;
}

public sealed class ShangraoRiverOverlay : MonoBehaviour {
    public ShangraoInfo Info;
    public Dictionary<int, string> Positions;
    public Dictionary<int, int[]> Rivers;
    private void LateUpdate() {
        var manager = Game3DManager.Instance;
        if (Info?.burnt_river_indices == null || manager == null || manager.HasPendingRecordTableAnimations || Card3DHoverManager.Instance == null) return;
        var positions = Positions ?? TableMirror.Current.IndexToPosition;
        foreach (var seat in positions) {
            if (seat.Key < 0 || seat.Key >= Info.burnt_river_indices.Length) continue;
            IReadOnlyList<int> tiles = Rivers != null && Rivers.TryGetValue(seat.Key, out var recorded)
                ? recorded : TableMirror.Current.Info(seat.Value)?.discard_tiles;
            manager.ShowShangraoBurntRiver(seat.Value, tiles, Info.burnt_river_indices[seat.Key]);
        }
    }
}

public partial class Game3DManager {
    internal void ShowShangraoBurntRiver(string position, IReadOnlyList<int> tiles, int[] indices) {
        var river = GetPosPanel(position)?.discardsPosition;
        if (river == null || tiles == null || indices == null) return;
        foreach (int index in indices) {
            if (index < 0 || index >= tiles.Count) continue;
            if (river.childCount == index) Set3DTile(tiles[index], river, "DiscardWithoutAnimation", position);
            if (river.childCount <= index) continue;
            var card = river.GetChild(index).gameObject;
            if (card.GetComponent<Tile3D>()?.GetTileId() != tiles[index]) continue;
            Card3DHoverManager.Instance.SetCardGrayOverlay(card, new Color(0.15f, 0.48f, 1f), 0.48f);
        }
    }
}
