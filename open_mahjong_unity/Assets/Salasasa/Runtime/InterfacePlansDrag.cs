using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home
{
    public sealed class InterfacePlansDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public InterfacePlansController owner;
        Vector2 startPointer, startPosition;
        public void OnBeginDrag(PointerEventData data)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)owner.frame.parent, data.position, data.pressEventCamera, out startPointer);
            startPosition = owner.frame.anchoredPosition;
        }
        public void OnDrag(PointerEventData data)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)owner.frame.parent, data.position, data.pressEventCamera, out var point))
            { owner.frame.anchoredPosition = startPosition + point - startPointer; owner.ClampPosition(); }
        }
    }
}
