using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    // 只在备用 sign 出现时分配索引表；索引始终对应原始 mask，不能用倒序后的相邻牌判断归属。
    private static Transform[] CreateMeldStackAnchors(int[] mask) {
        return MeldStackLayout.HasStacks(mask) ? new Transform[mask.Length / 2] : null;
    }

    private void SpawnMeldStacks(int[] mask, Transform[] anchors, Transform parent) {
        if (anchors == null) return;
        InitializeTilePlacement();
        GetTileSurface(out _, out Vector3 normal);
        var tops = new Dictionary<int, Transform>();
        foreach (var stack in MeldStackLayout.Build(mask)) {
            Transform anchor = anchors[stack.AnchorIndex];
            if (anchor == null) continue;
            Transform below = tops.TryGetValue(stack.AnchorIndex, out Transform top) ? top : anchor;
            MeshFilter belowMesh = below.GetComponentInChildren<MeshFilter>(true);
            if (belowMesh == null || belowMesh.sharedMesh == null) continue;
            Quaternion rotation = anchor.GetComponent<Tile3D>()?.FaceUpRootRotation ?? anchor.rotation;
            GameObject card = stack.TileId == 0
                ? MahjongObjectPool.Instance.SpawnBlankTile(anchor.position, rotation, 0)
                : MahjongObjectPool.Instance.SpawnPresentationTile(stack.TileId, anchor.position, rotation);
            if (card == null) continue;
            card.transform.SetParent(parent, true);
            card.GetComponent<Tile3D>()?.ApplyCombinationPeekState(stack.TileId, stack.Sign);
            MeshFilter mesh = card.GetComponentInChildren<MeshFilter>(true);
            if (mesh == null || mesh.sharedMesh == null) {
                MahjongObjectPool.Instance.Return(-1, card);
                continue;
            }
            // 使用实际模型的上下表面；翻面、四家朝向和缩放不会改变桌面内的槽位。
            float upper = TileSurfacePlacement.MaximumProjection(belowMesh.sharedMesh.bounds,
                belowMesh.transform.localToWorldMatrix, normal);
            card.transform.position = TileSurfacePlacement.PlaceOnPlane(card.transform.position,
                mesh.sharedMesh.bounds, mesh.transform.localToWorldMatrix, normal.normalized * upper,
                normal, tileSurfaceClearance);
            MahjongObjectPool.Instance.RefreshTileCollider(card);
            Card3DHoverManager.Instance.RegisterCard(card, stack.TileId);
            tops[stack.AnchorIndex] = card.transform;
        }
    }
}
