using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CardFacePreviewMagnifierTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public CardFacePreviewMagnifier magnifier;
    public CardFacePreviewSlot slot;
    public bool table;
    public SingleTileAdjustmentPanel editor;
    public string fixedPack;
    public int fixedTileKey;
    public void OnPointerEnter(PointerEventData eventData) { if (editor == null) magnifier.Enter(slot, table, eventData); }
    public void OnPointerExit(PointerEventData eventData) { if (editor == null) magnifier.Exit(slot, table); }
    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (editor != null) {
            if (FixedFacePack.IsFixed(fixedPack)) editor.SelectFixed(fixedPack, fixedTileKey, table);
            else editor.Select(slot.tileId, table);
        } else magnifier.Toggle(slot, table, eventData);
    }
}
