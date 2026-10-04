using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>只拖动牌面，保留原单元格用于命中与布局；丢到另一格时交换两张牌。</summary>
public sealed class FreeModeMeldDragTile : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler {
    public int Key { get; private set; }
    private FreeModeHud owner;
    private RectTransform art, canvas;
    private Transform originalParent;
    private Vector3 originalPosition, originalScale, pointerOffset;
    private Quaternion originalRotation;
    private Vector2 originalSize, originalMin, originalMax;
    private int revision, originalSibling;
    private bool dragging;

    public void Bind(FreeModeHud hud, RectTransform artwork, int key, int selectionRevision) {
        owner = hud;
        art = artwork;
        Key = key;
        revision = selectionRevision;
    }

    public void OnBeginDrag(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left || owner == null || art == null) return;
        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) return;
        canvas = (RectTransform)parentCanvas.rootCanvas.transform;
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(canvas, eventData.position, eventData.pressEventCamera, out Vector3 point)) return;
        eventData.eligibleForClick = false;
        dragging = true;
        originalParent = art.parent;
        originalSibling = art.GetSiblingIndex();
        originalPosition = art.localPosition;
        originalRotation = art.localRotation;
        originalScale = art.localScale;
        originalSize = art.sizeDelta;
        originalMin = art.anchorMin;
        originalMax = art.anchorMax;
        pointerOffset = art.position - point;
        art.SetParent(canvas, true);
        art.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData) {
        if (dragging && art != null && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvas, eventData.position, eventData.pressEventCamera, out Vector3 point)) art.position = point + pointerOffset;
    }

    public void OnEndDrag(PointerEventData eventData) {
        if (!dragging) return;
        RestoreArtwork();
        owner?.DropMeldTile(Key, revision, eventData.position, eventData.pressEventCamera);
    }

    public void OnCancel(BaseEventData eventData) => RestoreArtwork();
    private void OnDisable() => RestoreArtwork();

    private void RestoreArtwork() {
        if (!dragging) return;
        dragging = false;
        if (art == null) return;
        if (originalParent == null) { Destroy(art.gameObject); return; }
        art.SetParent(originalParent, false);
        art.SetSiblingIndex(originalSibling);
        art.anchorMin = originalMin;
        art.anchorMax = originalMax;
        art.sizeDelta = originalSize;
        art.localPosition = originalPosition;
        art.localRotation = originalRotation;
        art.localScale = originalScale;
    }
}
