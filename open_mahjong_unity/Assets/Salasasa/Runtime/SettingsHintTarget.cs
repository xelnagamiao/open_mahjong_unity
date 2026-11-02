using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class SettingsHintTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public SettingsPagePresentation owner;
        public string key;
        [TextArea] public string description;
        public RectTransform Rect => (RectTransform)transform;
        public string Message => key == "MeldSpacingRow" && RuleRegistry.Current?.MeldSpacingEnabled == true
            ? "当前规则固定开启副露间距。" : description;

        public void OnPointerEnter(PointerEventData eventData) { if (owner) owner.PointerEnter(this); }
        public void OnPointerExit(PointerEventData eventData) { if (owner) owner.PointerExit(this); }
        void OnDisable() { if (owner) owner.PointerExit(this); }
    }
}
