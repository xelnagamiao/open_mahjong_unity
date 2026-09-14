using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>拖动设置窗口的标题或空白处；控件和滚动区域保留各自的操作。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SceneSettingsPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform rect;
    private Vector2 lastLocalPoint;
    private bool dragging;
    private readonly Vector3[] corners = new Vector3[4];

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = false;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        // Buttons do not implement IDragHandler: their drag would otherwise bubble to us.
        var hit = eventData.pointerPressRaycast.gameObject;
        for (Transform t = hit != null ? hit.transform : null; t != null && t != transform; t = t.parent)
            if (t.GetComponent<Selectable>() != null || t.GetComponent<ScrollRect>() != null) return;
        rect = (RectTransform)transform;
        var parent = rect.parent as RectTransform;
        dragging = parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, eventData.position, eventData.pressEventCamera, out lastLocalPoint);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || eventData.button != PointerEventData.InputButton.Left) return;
        var parent = rect.parent as RectTransform;
        if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, eventData.position, eventData.pressEventCamera, out var point)) return;
        rect.anchoredPosition += point - lastLocalPoint;
        lastLocalPoint = point;
        ClampInside(parent);
    }

    private void ClampInside(RectTransform parent)
    {
        rect.GetWorldCorners(corners);
        var min = (Vector2)parent.InverseTransformPoint(corners[0]);
        var max = (Vector2)parent.InverseTransformPoint(corners[2]);
        var bounds = parent.rect;
        // Keep the complete window visible when it fits; for small viewports keep its top reachable.
        float dx = max.x - min.x > bounds.width ? bounds.xMin - min.x
            : min.x < bounds.xMin ? bounds.xMin - min.x : max.x > bounds.xMax ? bounds.xMax - max.x : 0;
        float dy = max.y - min.y > bounds.height ? bounds.yMax - max.y
            : min.y < bounds.yMin ? bounds.yMin - min.y : max.y > bounds.yMax ? bounds.yMax - max.y : 0;
        rect.anchoredPosition += new Vector2(dx, dy);
    }

    public void OnEndDrag(PointerEventData eventData) => dragging = false;
    private void OnDisable() => dragging = false;
}
