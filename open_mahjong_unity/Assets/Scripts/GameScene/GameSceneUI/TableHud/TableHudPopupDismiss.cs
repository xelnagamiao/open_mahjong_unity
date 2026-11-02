using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TableHudPopupDismiss : MonoBehaviour, IPointerClickHandler {
    public TableHudPopupGroup owner;
    public void OnPointerClick(PointerEventData eventData) => owner.CloseSelectors();
}
