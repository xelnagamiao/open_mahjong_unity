using UnityEngine;

public partial class NormalGameStateManager {
    private void RefreshChangshaSeaBottomConcealedTile(int remainTiles) {
        if (roomRule != "changsha") return;
        if (remainTiles == 1) {
            Game3DManager.Instance?.ShowChangshaSeaBottomConcealedTile();
        } else {
            Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
        }
    }

    private void ClearChangshaSeaBottomVisual() {
        Game3DManager.Instance?.ClearChangshaSeaBottomConcealedTile();
    }

    private void HandleChangshaSeaBottomDiscard(
        string playerPosition,
        int? tileId,
        bool isMoqie,
        bool isRiichiHorizontal) {
        if (!tileId.HasValue || tileId.Value <= 0) {
            Debug.LogError("长沙海底弃牌缺少牌值");
            return;
        }

        int tile = tileId.Value;
        Game3DManager.Instance?.PlaceChangshaSeaBottomDiscardTile(
            tile,
            playerPosition,
            isMoqie,
            isRiichiHorizontal);
        lastCutCardID = tile;
        lastDiscardPlayerPosition = playerPosition;
        if (player_to_info.TryGetValue(playerPosition, out PlayerInfoClass playerInfo)) {
            playerInfo.discard_tiles.Add(tile);
            playerInfo.discard_riichi_flags.Add(isRiichiHorizontal);
        }
    }
}
