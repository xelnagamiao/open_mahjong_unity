using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CreateRoomHelpLink : MonoBehaviour, IPointerClickHandler {
    [SerializeField] private CreatePanel owner;
    [SerializeField] private TMP_Text text;

#if UNITY_EDITOR
    public void Configure(CreatePanel panel, TMP_Text label) { owner = panel; text = label; }
#endif

    public void OnPointerClick(PointerEventData eventData) {
        if (!owner || !text || eventData.button != PointerEventData.InputButton.Left) return;
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, eventData.pressEventCamera);
        if (linkIndex >= 0) owner.ConfirmRoomHelpNavigation(text.textInfo.linkInfo[linkIndex].GetLinkID());
    }
}
