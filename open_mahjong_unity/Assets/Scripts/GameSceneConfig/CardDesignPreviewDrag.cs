using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Each preview card owns its drag gesture; the other cards keep their angles.</summary>
public sealed class CardDesignPreviewDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public CardDesignModelPreview preview;
    public int cardIndex;
    int? pointerId;
    Vector2 previous;

    public void OnBeginDrag(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || pointerId.HasValue) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, data.position, data.pressEventCamera, out previous)) return;
        pointerId = data.pointerId;
    }
    public void OnDrag(PointerEventData data)
    {
        if (pointerId != data.pointerId) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, data.position, data.pressEventCamera, out var position)) return;
        preview.RotateCard(cardIndex, position - previous);
        previous = position;
    }
    public void OnEndDrag(PointerEventData data) { if (pointerId == data.pointerId) pointerId = null; }
    void OnDisable() => pointerId = null;
}
