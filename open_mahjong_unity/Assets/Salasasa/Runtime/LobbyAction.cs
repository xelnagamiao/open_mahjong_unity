using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home
{
    public sealed class LobbyAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IPointerClickHandler
    {
        public string action;
        public int index;
        public string label;
        public GameObject hover;
        bool pointer;
        bool focused;

        bool IsDockAction => transform.parent && transform.parent.name == "MetroBottomNavigation";

        void OnEnable() {
            pointer = focused = false;
            if (IsDockAction) {
                var button = GetComponent<UnityEngine.UI.Button>();
                if (button) {
                    button.transition = UnityEngine.UI.Selectable.Transition.None;
                    if (button.targetGraphic) button.targetGraphic.CrossFadeColor(Color.white, 0, true, true);
                }
            }
            Refresh();
        }
        public void OnPointerClick(PointerEventData eventData) {
            if (!IsDockAction || eventData.button != PointerEventData.InputButton.Left) return;
            pointer = focused = false;
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData) { pointer = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { pointer = false; Refresh(); }
        public void OnSelect(BaseEventData eventData) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { focused = false; Refresh(); }
        void OnDisable() { pointer = focused = false; Refresh(); }
        void Refresh() { if (hover != null) hover.SetActive(pointer || focused); }
    }
}
