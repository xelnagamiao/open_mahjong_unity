using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    [Header("牌与桌布的接触面")]
    [Tooltip("可选：位置是接触面，上方向是法线。留空时自动读取 Desktop 的桌布子网格，不使用旧手牌/弃牌锚点的高度。")]
    [SerializeField] private Transform tileSurfacePlane;
    [Tooltip("没有指定平面时读取桌布网格的这个子网格；默认 0 对应桌布，排除桌框。")]
    [SerializeField, Min(0)] private int tileSurfaceSubMesh;
    [Tooltip("世界单位。保留极小间隙避免数值穿插；牌宽约 12，默认间隙为 0.02。")]
    [SerializeField, Min(0f)] private float tileSurfaceClearance = 0.02f;

    private MeshFilter tileBodyMesh;
    private Bounds tileBodyBounds;
    private Matrix4x4 tileBodyFromRoot;
    private Vector3 tileWorldScale;
    private MeshFilter tableSurfaceMesh;
    private int tableSurfaceLookupFrame = -1;
    private bool placementInitialized;

    private sealed class RevealingHandPlacement {
        public PosPanel3D panel;
        public readonly List<Transform> tiles = new List<Transform>();
        public readonly List<Tile3D> tileStates = new List<Tile3D>();
        public readonly List<uint> leases = new List<uint>();
        public readonly List<Vector3> initialLocalPositions = new List<Vector3>();
        public bool active;
        public bool enteredExpand;
    }
    private readonly List<RevealingHandPlacement> revealingHandPlacements = new List<RevealingHandPlacement>(4);

    private void InitializeTilePlacement() {
        if (placementInitialized) return;
        tileBodyMesh = tile3DPrefab.GetComponent<MeshFilter>() ?? tile3DPrefab.GetComponentInChildren<MeshFilter>(true);
        if (tileBodyMesh == null || tileBodyMesh.sharedMesh == null) {
            Debug.LogError("Game3DManager: 卡牌缺少主体 MeshFilter，无法按模型计算接触位置。", this);
            return;
        }
        tileBodyBounds = tileBodyMesh.sharedMesh.bounds;
        tileBodyFromRoot = tile3DPrefab.transform.worldToLocalMatrix * tileBodyMesh.transform.localToWorldMatrix;
        tileWorldScale = tile3DPrefab.transform.lossyScale;
        ResolveTableSurfaceMesh();
        placementInitialized = true;
    }

    private void ResolveTableSurfaceMesh() {
        if (tableSurfaceMesh != null || tableSurfaceLookupFrame == Time.frameCount) return;
        tableSurfaceLookupFrame = Time.frameCount;
        Desktop desktop = Desktop.Instance != null ? Desktop.Instance
            : FindFirstObjectByType<Desktop>(FindObjectsInactive.Include);
        if (desktop != null) tableSurfaceMesh = desktop.GetComponent<MeshFilter>();
    }

    private void GetTileSurface(out Vector3 point, out Vector3 normal) {
        if (tileSurfacePlane != null) {
            point = tileSurfacePlane.position;
            normal = tileSurfacePlane.up;
            return;
        }
        // 桌面可以稍后激活/载入；一次初始化没找到时，不永久锁死在世界零平面。
        ResolveTableSurfaceMesh();
        if (tableSurfaceMesh != null && tableSurfaceMesh.sharedMesh != null && tableSurfaceMesh.sharedMesh.subMeshCount > 0) {
            Mesh mesh = tableSurfaceMesh.sharedMesh;
            int subMesh = Mathf.Clamp(tileSurfaceSubMesh, 0, mesh.subMeshCount - 1);
            Bounds bounds = mesh.GetSubMesh(subMesh).bounds;
            TileSurfacePlacement.PlaneFromSurfaceBounds(bounds, tableSurfaceMesh.transform.localToWorldMatrix, out point, out normal);
            return;
        }
        // 老场景/无桌布验证场景的桌面基准是世界 Y=0；可用上面的显式平面覆盖。
        point = Vector3.zero;
        normal = Vector3.up;
    }

    private Matrix4x4 TileBodyAt(Vector3 rootPosition, Quaternion worldRotation) {
        return Matrix4x4.TRS(rootPosition, worldRotation, tileWorldScale) * tileBodyFromRoot;
    }

    /// <summary>布局锚点只提供沿桌面的槽位置，中心高度由当前模型和最终姿态决定。</summary>
    private Vector3 PlaceTileOnTable(Vector3 candidate, Quaternion worldRotation) {
        InitializeTilePlacement();
        if (!placementInitialized) return candidate;
        GetTileSurface(out Vector3 point, out Vector3 normal);
        return TileSurfacePlacement.PlaceOnPlane(candidate, tileBodyBounds, TileBodyAt(candidate, worldRotation),
            point, normal, tileSurfaceClearance);
    }

    private Vector3 KeepMovingTileAboveTable(Transform tile, Vector3 candidate, Quaternion worldRotation) {
        if (!placementInitialized) return candidate;
        GetTileSurface(out Vector3 point, out Vector3 normal);
        // 保留完整父级矩阵，不能以 lossyScale 重建带非均匀缩放的层级（会丢失剪切）。
        Transform parent = tile.parent;
        Matrix4x4 rootMatrix = parent == null
            ? Matrix4x4.TRS(candidate, worldRotation, tile.localScale)
            : parent.localToWorldMatrix * Matrix4x4.TRS(parent.InverseTransformPoint(candidate),
                Quaternion.Inverse(parent.rotation) * worldRotation, tile.localScale);
        Matrix4x4 matrix = rootMatrix * tileBodyFromRoot;
        return TileSurfacePlacement.KeepAbovePlane(candidate, tileBodyBounds, matrix, point, normal, tileSurfaceClearance);
    }

    private void BeginHandSurfacePlacement(PosPanel3D panel) {
        ResetHandSurfacePlacement(panel);
        var placement = new RevealingHandPlacement { panel = panel, active = true };
        for (int i = 0; i < panel.cardsPosition.childCount; i++) {
            Transform tile = panel.cardsPosition.GetChild(i);
            Tile3D state = tile.GetComponent<Tile3D>();
            if (state == null) continue;
            placement.tiles.Add(tile);
            placement.tileStates.Add(state);
            placement.leases.Add(state.PoolLeaseVersion);
            placement.initialLocalPositions.Add(tile.localPosition);
        }
        revealingHandPlacements.Add(placement);
    }

    private void ResetHandSurfacePlacement(PosPanel3D panel) {
        for (int i = revealingHandPlacements.Count - 1; i >= 0; i--) {
            RevealingHandPlacement placement = revealingHandPlacements[i];
            if (placement.panel != panel) continue;
            for (int j = 0; j < placement.tiles.Count; j++) {
                Transform tile = placement.tiles[j];
                Tile3D state = placement.tileStates[j];
                if (tile != null && panel != null && tile.parent == panel.cardsPosition && tile.gameObject.activeSelf
                    && state != null && state.PoolLeaseVersion == placement.leases[j])
                    tile.localPosition = placement.initialLocalPositions[j];
            }
            revealingHandPlacements.RemoveAt(i);
        }
    }

    // Animator 在 Update 后采样，这里仅处理正在倒牌的组。静止牌没有每帧放置开销。
    private void LateUpdate() {
        bool anyActive = false;
        foreach (RevealingHandPlacement placement in revealingHandPlacements) {
            if (placement.active) { anyActive = true; break; }
        }
        if (!anyActive) return;
        GetTileSurface(out Vector3 point, out Vector3 normal);
        for (int i = 0; i < revealingHandPlacements.Count; i++) {
            RevealingHandPlacement placement = revealingHandPlacements[i];
            if (!placement.active) continue;
            PosPanel3D panel = placement.panel;
            if (panel == null || panel.cardsPosition == null || panel.handRevealAnimator == null
                || !panel.handRevealAnimator.isActiveAndEnabled) {
                placement.active = false;
                continue;
            }
            Animator anim = panel.handRevealAnimator;
            AnimatorStateInfo state = anim.GetCurrentAnimatorStateInfo(0);
            bool isExpand = state.IsName("Expand");
            bool transitioning = anim.IsInTransition(0);
            if (isExpand || (transitioning && anim.GetNextAnimatorStateInfo(0).IsName("Expand")))
                placement.enteredExpand = true;
            bool canceled = placement.enteredExpand && !isExpand && !transitioning;

            bool hasSupport = false;
            float support = 0f;
            for (int j = 0; j < placement.tiles.Count; j++) {
                Transform tile = placement.tiles[j];
                Tile3D stateForTile = placement.tileStates[j];
                if (!IsCardDrivable(tile, panel.cardsPosition) || stateForTile == null
                    || stateForTile.PoolLeaseVersion != placement.leases[j]) continue;
                // 每帧从无补偿的局部位置重新计算，防止上一帧高度修正被父级旋转累积到水平轴。
                tile.localPosition = placement.initialLocalPositions[j];
                if (!hasSupport) {
                    Matrix4x4 matrix = tile.localToWorldMatrix * tileBodyFromRoot;
                    support = Vector3.Dot(normal, tile.position)
                        - TileSurfacePlacement.MinimumProjection(tileBodyBounds, matrix, normal);
                    hasSupport = true;
                }
                float delta = Vector3.Dot(normal, point) + tileSurfaceClearance + support - Vector3.Dot(normal, tile.position);
                tile.position += normal * delta;
            }
            // 先投影实际末帧再停止；Animator只写Cube，不会覆盖子牌最终局部位置。
            if (canceled || !hasSupport || (isExpand && state.normalizedTime >= 1f && !transitioning)) placement.active = false;
        }
    }

    private void OnDisable() {
        // 组件被禁用也视为取消倒牌，Animator与补偿一起复位，不能让Cube继续播到错误落点。
        while (revealingHandPlacements.Count > 0) {
            PosPanel3D panel = revealingHandPlacements[revealingHandPlacements.Count - 1].panel;
            if (panel != null && panel.handRevealAnimator != null) ForceHandRevealIdle(panel);
            else revealingHandPlacements.RemoveAt(revealingHandPlacements.Count - 1);
        }
    }
}
