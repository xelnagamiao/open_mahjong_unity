using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>中心盘上的单张共享牌；服务器拥有牌值，展示实例不占用任何玩家的牌容器或数量池。</summary>
public sealed class FreeModeTransferTile : MonoBehaviour {
    private FreeGameState state;
    private GameObject tile;
    private int displayedTile;
    private bool displayedFaceDown;
    private RectTransform clickTarget;
    private Canvas clickCanvas;
    private float nextTakeTime;

    public void Bind(FreeGameState owner) {
        state = owner;
        Refresh();
    }

    private void LateUpdate() => Refresh();

    public void Refresh() {
        Game3DManager table = Game3DManager.Instance;
        if (!isActiveAndEnabled || state == null || !state.IsActive || FreeGameState.Active != state
            || GameHost.Current.CurrentWindow != "game" || !state.TransferTile.HasValue || table == null
            || !table.TryGetTransferTilePlacement(out Vector3 position, out Quaternion rotation,
                out Vector3 hitPosition, out Quaternion hitRotation, out Vector2 hitSize)) {
            ClearTile();
            return;
        }
        if (tile != null && (displayedTile != state.TransferTile.Value || displayedFaceDown != state.TransferFaceDown)) ClearTile();
        if (tile == null) {
            MahjongObjectPool pool = MahjongObjectPool.Instance;
            if (pool == null) return;
            // 暗牌用空白实体翻到牌背；从未绑定真实牌值，因此悬停也不能偷看。
            tile = pool.CreatePresentationTile(state.TransferFaceDown ? ConfigManager.BlankFaceImageId : state.TransferTile.Value);
            if (tile == null) return;
            displayedTile = state.TransferTile.Value;
            displayedFaceDown = state.TransferFaceDown;
            tile.GetComponent<Tile3D>()?.SetConcealedFaceDown(displayedFaceDown);
            tile.name = "FreeModeTransferTile";
            SceneManager.MoveGameObjectToScene(tile, table.gameObject.scene);
            if (!displayedFaceDown) Card3DHoverManager.Instance?.RegisterCard(tile, displayedTile);
            EnsureClickTarget(table);
        }
        tile.transform.SetPositionAndRotation(position, rotation);
        clickTarget.SetPositionAndRotation(hitPosition, hitRotation);
        clickTarget.sizeDelta = hitSize;
        bool canTake = !GameSession.Current.IsRealtimeSpectator;
        clickTarget.gameObject.SetActive(canTake);
        if (clickCanvas.worldCamera == null) clickCanvas.worldCamera = Camera.main;
    }

    private void EnsureClickTarget(Game3DManager table) {
        if (clickTarget != null) return;
        var root = new GameObject("FreeModeTransferTake", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image), typeof(FreeModeTransferTileHitTarget));
        SceneManager.MoveGameObjectToScene(root, table.gameObject.scene);
        clickTarget = root.GetComponent<RectTransform>();
        clickTarget.localScale = Vector3.one;
        clickCanvas = root.GetComponent<Canvas>();
        clickCanvas.renderMode = RenderMode.WorldSpace;
        Canvas board = BoardCanvas.Instance != null ? BoardCanvas.Instance.GetComponent<Canvas>() : null;
        clickCanvas.worldCamera = board != null && board.worldCamera != null ? board.worldCamera : Camera.main;
        if (board != null) {
            clickCanvas.sortingLayerID = board.sortingLayerID;
            clickCanvas.sortingOrder = board.sortingOrder + 1;
        }
        // 只有牌顶大小的透明点击面；世界 Canvas 保持低于屏幕抽屉、弹窗的事件优先级。
        Image image = root.GetComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        root.GetComponent<FreeModeTransferTileHitTarget>().Bind(this);
    }

    internal void TakeTile() {
        if (!isActiveAndEnabled || tile == null || state == null || !state.IsActive
            || FreeGameState.Active != state || GameSession.Current.IsRealtimeSpectator
            || GameHost.Current.CurrentWindow != "game"
            || state.TransferTile != displayedTile || Time.unscaledTime < nextTakeTime
            || Game3DManager.Instance == null || !Game3DManager.Instance.isActiveAndEnabled) return;
        nextTakeTime = Time.unscaledTime + 0.35f;
        state.SendTransferTake();
    }

    private void ClearTile() {
        if (tile != null) {
            Card3DHoverManager.Instance?.ResetAndUnregisterCard(tile);
            tile.SetActive(false);
            Destroy(tile);
            tile = null;
        }
        if (clickTarget != null) clickTarget.gameObject.SetActive(false);
        displayedTile = 0;
        displayedFaceDown = false;
    }

    private void OnDisable() => ClearTile();

    private void OnDestroy() {
        ClearTile();
        if (clickTarget != null) Destroy(clickTarget.gameObject);
    }
}
