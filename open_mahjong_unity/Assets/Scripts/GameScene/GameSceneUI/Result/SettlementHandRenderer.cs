using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>普通和牌与川麻逐副结算共用的牌行：暗手、副露、独立和牌张。</summary>
public static class SettlementHandRenderer {
    public static void Populate(GameObject container, GameObject cardPrefab, GameObject splitPrefab,
        int[] hand, int[][] masks, EndResultPanel.EndResultTileLayout layout) {
        bool hasHand = hand != null && hand.Length > 0;
        if (!hasHand && (masks == null || !Array.Exists(masks, HongqueDisplayOrder.IsMeldMask))) return;
        hand = hand ?? Array.Empty<int>();
        SettlementTileRowLayout.Ensure(container);
        bool winning = hasHand && layout == EndResultPanel.EndResultTileLayout.HuWithWinTile;
        int winTile = winning ? hand[hand.Length - 1] : 0;
        int[] closed = (int[])hand.Clone();
        if (winning) Array.Resize(ref closed, closed.Length - 1);
        closed = EndResultPanel.SortSettlementHand(closed);
        foreach (int tile in closed) Tile(container.transform, cardPrefab, tile);
        if (hasHand) Split(container.transform, splitPrefab);
        foreach (var group in SettlementMeldLayoutBuilder.Build(masks)) {
            foreach (var tile in group) {
                Tile(container.transform, cardPrefab, tile.FaceDown ? 0 : tile.TileId);
                if (tile.StackedTileId.HasValue) Tile(container.transform, cardPrefab, tile.StackedTileId.Value);
                if (tile.StackedTiles != null) foreach (var stack in tile.StackedTiles)
                    Tile(container.transform, cardPrefab, stack.FaceDown ? 0 : stack.TileId);
            }
        }
        if (winning) {
            Split(container.transform, splitPrefab);
            Tile(container.transform, cardPrefab, winTile);
        }
    }

    public static void PopulateSnapshot(GameObject container, GameObject cardPrefab, GameObject splitPrefab,
        int[] hand, int[][] masks, int winTile) {
        var ordered = new List<int>(hand ?? Array.Empty<int>());
        int winIndex = winTile > 0 ? ordered.LastIndexOf(winTile) : -1;
        if (winIndex >= 0) {
            ordered.RemoveAt(winIndex);
            ordered.Add(winTile);
        }
        Populate(container, cardPrefab, splitPrefab, ordered.ToArray(), masks, winIndex >= 0
            ? EndResultPanel.EndResultTileLayout.HuWithWinTile : EndResultPanel.EndResultTileLayout.ClosedHandWithMelds);
    }

    static void Tile(Transform parent, GameObject prefab, int id) {
        var card = UnityEngine.Object.Instantiate(prefab, parent, false);
        card.GetComponent<StaticCard>().SetTileOnlyImage(id);
        foreach (var graphic in card.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
    }

    static void Split(Transform parent, GameObject prefab) {
        UnityEngine.Object.Instantiate(prefab, parent, false);
    }
}
