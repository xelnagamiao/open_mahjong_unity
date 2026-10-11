using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>中心盘上的共享牌按放入顺序展示，编号保留至该张牌被取走。</summary>
public sealed class FreeModeTransferTile : MonoBehaviour {
    private sealed class Presentation {
        public GameObject tile;
        public RectTransform target, number;
        public Canvas canvas;
        public int token, tileId;
        public bool faceDown;
    }
    private FreeGameState state;
    private readonly Dictionary<int, Presentation> displayed = new Dictionary<int, Presentation>();
    private readonly List<int> removed = new List<int>();
    private float nextTakeTime;

    public void Bind(FreeGameState owner) { state = owner; Refresh(); }
    private void LateUpdate() => Refresh();

    public void Refresh() {
        Game3DManager table = Game3DManager.Instance;
        if (!isActiveAndEnabled || state == null || !state.IsActive || FreeGameState.Active != state
            || GameHost.Current.CurrentWindow != "game" || state.TransferTiles.Count == 0 || table == null
            || !table.TryGetTransferTilePlacement(out Vector3 position, out Quaternion rotation,
                out Vector3 hitPosition, out Quaternion hitRotation, out Vector2 hitSize)) {
            ClearTiles();
            return;
        }
        removed.Clear();
        foreach (KeyValuePair<int, Presentation> pair in displayed) {
            bool found = false;
            foreach (FreeTransferTileInfo item in state.TransferTiles)
                if (item.slot == pair.Key && item.token == pair.Value.token && item.tile == pair.Value.tileId
                    && item.face_down == pair.Value.faceDown) { found = true; break; }
            if (!found) removed.Add(pair.Key);
        }
        foreach (int slot in removed) { DestroyPresentation(displayed[slot]); displayed.Remove(slot); }
        for (int index = 0; index < state.TransferTiles.Count; index++) {
            FreeTransferTileInfo item = state.TransferTiles[index];
            if (!displayed.TryGetValue(item.slot, out Presentation view)) {
                view = CreatePresentation(table, item);
                if (view == null) continue;
                displayed[item.slot] = view;
            }
            Vector3 offset = hitRotation * Vector3.right * ((index - (state.TransferTiles.Count - 1) * 0.5f) * hitSize.x * 1.14f);
            view.tile.transform.SetPositionAndRotation(position + offset, rotation);
            view.target.SetPositionAndRotation(hitPosition + offset, hitRotation);
            view.target.sizeDelta = hitSize;
            view.number.anchoredPosition = new Vector2(0f, -hitSize.y * 0.64f);
            view.number.localScale = Vector3.one * (hitSize.y * 0.3f / 70f);
            if (view.canvas.worldCamera == null) view.canvas.worldCamera = Camera.main;
        }
    }

    private Presentation CreatePresentation(Game3DManager table, FreeTransferTileInfo item) {
        MahjongObjectPool pool = MahjongObjectPool.Instance;
        if (pool == null) return null;
        // Unknown tiles never bind the real face, including their hover target.
        GameObject tile = pool.CreatePresentationTile(item.face_down ? ConfigManager.BlankFaceImageId : item.tile);
        if (tile == null) return null;
        tile.GetComponent<Tile3D>()?.SetConcealedFaceDown(item.face_down);
        tile.name = "FreeModeTransferTile" + item.slot;
        SceneManager.MoveGameObjectToScene(tile, table.gameObject.scene);
        if (!item.face_down) Card3DHoverManager.Instance?.RegisterCard(tile, item.tile);
        var root = new GameObject("FreeModeTransferTake" + item.slot, typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image), typeof(FreeModeTransferTileHitTarget));
        SceneManager.MoveGameObjectToScene(root, table.gameObject.scene);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        Canvas board = BoardCanvas.Instance != null ? BoardCanvas.Instance.GetComponent<Canvas>() : null;
        canvas.worldCamera = board != null && board.worldCamera != null ? board.worldCamera : Camera.main;
        if (board != null) { canvas.sortingLayerID = board.sortingLayerID; canvas.sortingOrder = board.sortingOrder + 1; }
        Image hit = root.GetComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        root.GetComponent<FreeModeTransferTileHitTarget>().Bind(this, item.slot, item.token);
        var number = new GameObject("Number", typeof(RectTransform), typeof(Image));
        var numberRect = (RectTransform)number.transform;
        numberRect.SetParent(root.transform, false);
        numberRect.sizeDelta = new Vector2(86f, 70f);
        var backing = number.GetComponent<Image>();
        backing.color = new Color(0.04f, 0.09f, 0.12f, 0.92f);
        backing.raycastTarget = false;
        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(number.transform, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = Resources.Load<Font>("font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular");
        label.fontSize = 56;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = item.slot.ToString();
        return new Presentation { tile = tile, target = (RectTransform)root.transform, number = numberRect,
            canvas = canvas, token = item.token, tileId = item.tile, faceDown = item.face_down };
    }

    internal void TakeTile(int slot, int token) {
        if (!isActiveAndEnabled || state == null || !state.IsActive || FreeGameState.Active != state
            || GameSession.Current.IsRealtimeSpectator || GameHost.Current.CurrentWindow != "game"
            || Time.unscaledTime < nextTakeTime || Game3DManager.Instance == null || !Game3DManager.Instance.isActiveAndEnabled) return;
        foreach (FreeTransferTileInfo item in state.TransferTiles) {
            if (item.slot != slot || item.token != token) continue;
            nextTakeTime = Time.unscaledTime + 0.35f;
            state.SendTransferTake(new[] { item });
            return;
        }
    }

    private void DestroyPresentation(Presentation view) {
        if (view.tile != null) {
            Card3DHoverManager.Instance?.ResetAndUnregisterCard(view.tile);
            view.tile.SetActive(false);
            Destroy(view.tile);
        }
        if (view.target != null) { view.target.gameObject.SetActive(false); Destroy(view.target.gameObject); }
    }
    private void ClearTiles() {
        foreach (Presentation view in displayed.Values) DestroyPresentation(view);
        displayed.Clear();
    }
    private void OnDisable() => ClearTiles();
    private void OnDestroy() => ClearTiles();
}
