using UnityEngine;

/// <summary>
/// 鼠标悬停于可 peek 的 3D 牌（CanPeekOnHover）时临时翻面，移开恢复。
/// </summary>
public class ConcealedTile3DPeekController : MonoBehaviour {
    public static ConcealedTile3DPeekController Instance { get; private set; }

    [SerializeField] private Camera raycastCamera;
    [SerializeField] private float raycastMaxDistance = 1000f;
    [SerializeField] private bool debugHoverLog;

    private Tile3D currentPeekTile;
    private string lastHoverLogKey = "";
    private readonly RaycastHit[] raycastHitBuffer = new RaycastHit[32];
    private int tilePhysicsLayerMask;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        tilePhysicsLayerMask = 1 << MahjongObjectPool.TilePhysicsLayer;
        ResolveRaycastCamera();
    }

    private void ResolveRaycastCamera() {
        if (raycastCamera != null) return;
        raycastCamera = Camera.main;
    }

    private void Update() {
        ResolveRaycastCamera();
        if (raycastCamera == null) return;

        Physics.SyncTransforms();
        Ray ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
        float maxDistance = raycastMaxDistance > 0f ? raycastMaxDistance : raycastCamera.farClipPlane;
        int hitCount = Physics.RaycastNonAlloc(ray, raycastHitBuffer, maxDistance, tilePhysicsLayerMask);

        Tile3D hoverTile = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++) {
            Tile3D candidate = raycastHitBuffer[i].collider.GetComponentInParent<Tile3D>();
            if (candidate == null) continue;
            if (raycastHitBuffer[i].distance >= bestDistance) continue;
            bestDistance = raycastHitBuffer[i].distance;
            hoverTile = candidate;
        }

        if (hoverTile == null || !hoverTile.CanPeekOnHover) {
            if (hoverTile != null && debugHoverLog) {
                LogHoverOnce($"noPeek:{hoverTile.GetHashCode()}",
                    $"命中牌但不可 peek | tileId={hoverTile.GetTileId()} concealed={hoverTile.IsConcealedFaceDown}");
            } else if (hoverTile == null && debugHoverLog) {
                LogHoverOnce("raycastMiss", $"Physics 射线未命中 Tile3D | hits={hitCount}");
            }
            ClearPeek();
            return;
        }

        if (currentPeekTile != hoverTile) {
            if (currentPeekTile != null) {
                currentPeekTile.SetPeekFaceUp(false);
            }
            currentPeekTile = hoverTile;
            if (debugHoverLog) {
                Debug.Log($"[PeekHover] 开始 peek | tileId={hoverTile.GetTileId()} obj={hoverTile.name}");
            }
        }
        currentPeekTile.SetPeekFaceUp(true);
    }

    private void LateUpdate() {
        if (currentPeekTile != null) {
            currentPeekTile.SetPeekFaceUp(true);
        }
    }

    private void LogHoverOnce(string key, string message) {
        if (!debugHoverLog || lastHoverLogKey == key) return;
        lastHoverLogKey = key;
        Debug.Log($"[PeekHover] {message}");
    }

    private void ClearPeek() {
        if (currentPeekTile == null) return;
        if (debugHoverLog) {
            Debug.Log($"[PeekHover] 结束 peek | tileId={currentPeekTile.GetTileId()} obj={currentPeekTile.name}");
        }
        currentPeekTile.SetPeekFaceUp(false);
        currentPeekTile = null;
        lastHoverLogKey = "";
    }

    private void OnDisable() {
        ClearPeek();
    }
}
