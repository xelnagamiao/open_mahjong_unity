using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent, RequireComponent(typeof(TMP_InputField))]
public sealed class InputFieldFeedback : MonoBehaviour, ISelectHandler, IDeselectHandler {
    public Outline focusOutline;
    public void SetPalette(Color accent) {
        var input = GetComponent<TMP_InputField>();
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1);
        colors.pressedColor = new Color(.78f, .78f, .78f, 1);
        colors.selectedColor = Color.Lerp(new Color(1.18f, 1.18f, 1.18f, 1), accent, .18f);
        colors.fadeDuration = .1f;
        input.transition = Selectable.Transition.ColorTint;
        input.colors = colors;
        if (focusOutline) {
            accent.a = 1;
            focusOutline.effectColor = accent;
            focusOutline.enabled = input.isFocused;
        }
    }
    public void OnSelect(BaseEventData _) { if (focusOutline) focusOutline.enabled = true; }
    public void OnDeselect(BaseEventData _) { if (focusOutline) focusOutline.enabled = false; }
    void OnDisable() { if (focusOutline) focusOutline.enabled = false; }
}
