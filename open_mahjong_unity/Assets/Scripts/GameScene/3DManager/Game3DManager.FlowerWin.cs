using System.Collections;
using UnityEngine;

public partial class Game3DManager {
    /// <summary>花胡成立时，将已经公开的花牌移到花胡者花牌区。</summary>
    public void TransferFlowerWinTile(
        int tileId,
        string sourcePlayerPosition,
        string recipientPlayerPosition) {
        if (sourcePlayerPosition == recipientPlayerPosition) return;
        StartCoroutine(TransferFlowerWinTileCoroutine(
            tileId, sourcePlayerPosition, recipientPlayerPosition));
    }

    private IEnumerator TransferFlowerWinTileCoroutine(
        int tileId,
        string sourcePlayerPosition,
        string recipientPlayerPosition) {
        PosPanel3D sourcePanel = GetPosPanel(sourcePlayerPosition);
        PosPanel3D recipientPanel = GetPosPanel(recipientPlayerPosition);
        if (sourcePanel?.buhuaPosition == null || recipientPanel?.buhuaPosition == null) yield break;

        GameObject sourceTile = null;
        float deadline = Time.realtimeSinceStartup + 3f;
        while (sourceTile == null && Time.realtimeSinceStartup < deadline) {
            for (int i = 0; i < sourcePanel.buhuaPosition.childCount; i++) {
                GameObject candidate = sourcePanel.buhuaPosition.GetChild(i).gameObject;
                Tile3D tile = candidate.GetComponent<Tile3D>();
                if (tile != null && tile.GetTileId() == tileId) {
                    sourceTile = candidate;
                    break;
                }
            }
            if (sourceTile == null) yield return null;
        }
        if (sourceTile == null) {
            Debug.LogWarning(
                $"花胡花牌移交失败: tile={tileId}, from={sourcePlayerPosition}, to={recipientPlayerPosition}");
            yield break;
        }

        yield return WaitForFlowerTileSettleCoroutine(
            sourceTile, sourcePanel.buhuaPosition);
        if (sourceTile == null
            || !sourceTile.activeInHierarchy
            || sourceTile.transform.parent != sourcePanel.buhuaPosition) {
            yield break;
        }

        Vector3 startPosition = sourceTile.transform.position;
        sourceTile.transform.SetParent(null, worldPositionStays: true);
        MahjongObjectPool.Instance.Return(-1, sourceTile);
        yield return PlaceTransferredFlowerTileCoroutine(
            tileId, recipientPlayerPosition, recipientPanel.buhuaPosition, startPosition);
    }

    /// <summary>等待普通补花落桌，避免两个动画协程同时驱动同一个对象。</summary>
    private IEnumerator WaitForFlowerTileSettleCoroutine(
        GameObject flowerTile,
        Transform expectedParent) {
        const float settledDuration = 0.05f;
        const int settledFrameCount = 2;
        float settledTime = 0f;
        int settledFrames = 0;
        Vector3 previousPosition = flowerTile.transform.position;

        while (flowerTile != null
            && flowerTile.activeInHierarchy
            && flowerTile.transform.parent == expectedParent) {
            yield return null;

            Vector3 currentPosition = flowerTile.transform.position;
            if (currentPosition.Equals(previousPosition)) {
                settledTime += Time.deltaTime;
                settledFrames++;
                if (settledTime >= settledDuration
                    && settledFrames >= settledFrameCount) {
                    yield break;
                }
            } else {
                previousPosition = currentPosition;
                settledTime = 0f;
                settledFrames = 0;
            }
        }
    }

    private IEnumerator PlaceTransferredFlowerTileCoroutine(
        int tileId,
        string recipientPlayerPosition,
        Transform recipientFlowerPosition,
        Vector3 startPosition) {
        int childIndex = recipientFlowerPosition.childCount;
        Set3DTile(
            tileId,
            recipientFlowerPosition,
            "BuhuaWithoutAnimation",
            recipientPlayerPosition);
        if (recipientFlowerPosition.childCount <= childIndex) yield break;

        GameObject transferredTile = recipientFlowerPosition.GetChild(childIndex).gameObject;
        Vector3 targetPosition = transferredTile.transform.position;
        yield return MoveCardFromRemovePosition(
            transferredTile, targetPosition, startPosition);
    }
}
