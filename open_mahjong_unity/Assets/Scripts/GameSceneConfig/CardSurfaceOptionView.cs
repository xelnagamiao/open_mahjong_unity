using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CardSurfaceOptionView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public CardLayoutRoundedGraphic frame;
    bool selected, hovered, focused;

    public void SetSelected(bool value) { selected = value; Refresh(); }
    public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
    public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
    public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
    public void OnDeselect(BaseEventData data) { focused = false; Refresh(); }
    void OnDisable() { hovered = focused = false; }
    void OnEnable() => Refresh();

    void Refresh()
    {
        if (frame == null) return;
        var fill = selected ? new Color(.37f, .46f, .51f, .88f) : new Color(.27f, .35f, .43f, .72f);
        var border = selected ? new Color(1f, .81f, .34f) : new Color(.62f, .72f, .81f, hovered || focused ? 1 : .55f);
        if (hovered || focused) fill += new Color(.045f, .045f, .045f, 0);
        frame.SetAppearance(fill, border, 13, selected ? 2 : 1.4f);
    }
}
