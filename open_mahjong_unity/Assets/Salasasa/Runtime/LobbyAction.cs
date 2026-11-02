using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home
{
    public sealed class LobbyAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public string action;
        public int index;
        public string label;
        public GameObject hover;
        bool pointer;
        bool focused;

        public void OnPointerEnter(PointerEventData eventData) { pointer = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { pointer = false; Refresh(); }
        public void OnSelect(BaseEventData eventData) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { focused = false; Refresh(); }
        void OnDisable() { pointer = focused = false; Refresh(); }
        void Refresh() { if (hover != null) hover.SetActive(pointer || focused); }
    }
}
