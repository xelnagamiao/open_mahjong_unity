using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas {
    private bool HandCardsMatchSnapshot(int[] tiles) {
        var counts = CountSnapshotTiles(tiles);
        int matched = 0;
        foreach (Transform child in handCardsContainer) {
            TileCard card = child.GetComponent<TileCard>();
            if (card == null) continue;
            if (!counts.TryGetValue(card.tileId, out int remaining) || remaining == 0) return false;
            counts[card.tileId] = remaining - 1;
            matched++;
        }
        return matched == tiles.Length;
    }

    private static Dictionary<int, int> CountSnapshotTiles(int[] tiles) {
        var counts = new Dictionary<int, int>();
        foreach (int tile in tiles) {
            counts.TryGetValue(tile, out int count);
            counts[tile] = count + 1;
        }
        return counts;
    }

    // 丢包/重连补帧仍以服务端牌数为准，只增删差额，保留已有牌的对象和相对顺序。
    // 同牌有多张时优先保留主列，避免自摸和牌张尚未移除时删掉玩家手排的那一张。
    private void ReconcileHandCardsPreservingOrder(int[] tiles) {
        var remaining = CountSnapshotTiles(tiles);
        var cards = GetMainHandCardsOrdered();
        TileCard draw = GetPinnedDrawTile();
        if (draw != null) cards.Add(draw);
        int sortIndex = 0;
        foreach (TileCard card in cards) {
            if (remaining.TryGetValue(card.tileId, out int count) && count > 0) {
                remaining[card.tileId] = count - 1;
                card.handSortIndex = sortIndex++;
            } else {
                Destroyer.Instance.AddToDestroyer(card.transform);
            }
        }
        foreach (int tile in tiles) {
            if (remaining[tile] == 0) continue;
            TileCard card = Instantiate(tileCardPrefab, handCardsContainer).GetComponent<TileCard>();
            card.SetTile(tile, false);
            card.handSortIndex = sortIndex++;
            remaining[tile]--;
        }
        LayoutHandCardsFromCurrentOrder();
        isArranged = true;
        RefreshHandTileSelectability();
    }
}
