using UnityEngine;

public partial class Game3DManager {
    private GameObject changshaSeaBottomConcealedTile;

    public void ShowChangshaSeaBottomConcealedTile() {
        if (changshaSeaBottomConcealedTile != null) return;
        if (selfPosPanel == null || leftPosPanel == null || topPosPanel == null || rightPosPanel == null) return;
        if (MahjongObjectPool.Instance == null) return;

        // 以四家牌河中心作为桌面中央，海底牌在玩家选择前保持盖牌状态。
        Vector3 center = (
            selfPosPanel.discardsPosition.position
            + leftPosPanel.discardsPosition.position
            + topPosPanel.discardsPosition.position
            + rightPosPanel.discardsPosition.position
        ) * 0.25f;
        changshaSeaBottomConcealedTile = MahjongObjectPool.Instance.SpawnBlankTile(
            center + Vector3.up * (cardHeight * 0.5f),
            Quaternion.Euler(90f, 0f, 0f),
            0);
        if (changshaSeaBottomConcealedTile == null) return;
        changshaSeaBottomConcealedTile.name = "ChangshaSeaBottomConcealed";
        Tile3D tile = changshaSeaBottomConcealedTile.GetComponent<Tile3D>();
        tile?.SetConcealedFaceDown(true);
        MahjongObjectPool.Instance.RefreshTileCollider(changshaSeaBottomConcealedTile);
    }

    public void ClearChangshaSeaBottomConcealedTile() {
        if (changshaSeaBottomConcealedTile == null) return;
        MahjongObjectPool.Instance?.Return(-1, changshaSeaBottomConcealedTile);
        changshaSeaBottomConcealedTile = null;
    }

    public void PlaceChangshaSeaBottomDiscardTile(
        int tileId,
        string playerPosition,
        bool isMoqie,
        bool isRiichiHorizontal) {
        ClearChangshaSeaBottomConcealedTile();
        // 海底牌不进入普通摸切动画，翻开后直接放入选择者牌河。
        Change3DTile(
            "SetDiscardWithoutAnimation",
            tileId,
            0,
            playerPosition,
            isMoqie,
            null,
            isRiichiHorizontal);
    }
}
