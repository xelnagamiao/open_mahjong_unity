using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Shows an authored, non-interactive tooltip; never creates UI at runtime.</summary>
public sealed class RankedEloHelp : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler {
    [SerializeField] private GameObject tooltip;
    public bool IsVisible => tooltip != null && tooltip.activeInHierarchy;
    public void OnPointerEnter(PointerEventData eventData) { Show(); }
    public void OnPointerExit(PointerEventData eventData) { Hide(); }
    public void OnSelect(BaseEventData eventData) { Show(); }
    public void OnDeselect(BaseEventData eventData) { Hide(); }
    private void OnDisable() { Hide(); }
    private void Show() { if (tooltip != null) tooltip.SetActive(true); }
    private void Hide() { if (tooltip != null) tooltip.SetActive(false); }
}
