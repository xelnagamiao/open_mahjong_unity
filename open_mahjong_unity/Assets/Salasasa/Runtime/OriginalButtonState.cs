using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [RequireComponent(typeof(Button))]
    public sealed class OriginalButtonState : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public Color resting;
        public Graphic target;
        bool hovering;
        bool selected;
        Coroutine tween;
        static readonly Color Pressed = new Color(.42075467f, .73697346f, 1, 1);
        Color Rest => selected ? new Color(.9547169f, .6821753f, .30082586f, 1) : resting;
        public void OnPointerEnter(PointerEventData _) { hovering = true; Move(Color.Lerp(Rest, Color.white, .15f), .1f); }
        public void OnPointerExit(PointerEventData _) { hovering = false; Move(Rest, .1f); }
        public void OnPointerDown(PointerEventData _) => Move(Pressed, .06f);
        public void OnPointerUp(PointerEventData _) => Move(hovering ? Color.Lerp(Rest, Color.white, .15f) : Rest, .1f);
        public void OnSelect(BaseEventData _) { selected = true; Move(Rest, .1f); }
        public void OnDeselect(BaseEventData _) { selected = false; Move(Rest, .1f); }
        void Move(Color color, float duration) { if (tween != null) StopCoroutine(tween); tween = StartCoroutine(Blend(color, duration)); }
        IEnumerator Blend(Color color, float duration)
        {
            var start = target.color;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime) { target.color = Color.Lerp(start, color, t / duration); yield return null; }
            target.color = color; tween = null;
        }
        void OnDisable() { if (target != null) target.color = resting; tween = null; hovering = selected = false; }
    }
}
