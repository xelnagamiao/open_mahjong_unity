using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ScrollThroughTransparentText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    [SerializeField] private ScrollRect targetScrollRect;

    private bool _isPointerInside = false;

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isPointerInside = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isPointerInside = false;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (!_isPointerInside) return;
        if (targetScrollRect == null) return;

        // 直接把滾輪事件傳給 ScrollRect
        targetScrollRect.OnScroll(eventData);
    }
}