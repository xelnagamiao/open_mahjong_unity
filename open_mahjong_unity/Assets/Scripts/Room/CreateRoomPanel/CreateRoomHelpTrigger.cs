using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CreateRoomHelpTrigger : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, ISelectHandler {
    [SerializeField] private CreatePanel owner;
    [SerializeField, TextArea] private string message;

#if UNITY_EDITOR
    public void Configure(CreatePanel panel, string text) { owner = panel; message = text; }
#endif

    internal bool IsOwnedBy(CreatePanel panel) => owner == panel;
    public void OnPointerEnter(PointerEventData eventData) {
        if (owner && eventData.pointerId < 0) owner.ShowRoomHelp(message, this);
    }
    public void OnPointerDown(PointerEventData eventData) {
        if (owner && eventData.button == PointerEventData.InputButton.Left)
            owner.ShowRoomHelp(message, this);
    }
    public void OnSelect(BaseEventData eventData) { if (owner) owner.ShowRoomHelp(message, this); }
}
