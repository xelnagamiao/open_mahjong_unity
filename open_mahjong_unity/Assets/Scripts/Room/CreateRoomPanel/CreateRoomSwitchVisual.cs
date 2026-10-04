using TMPro;
using UnityEngine;

[ExecuteAlways]
public sealed class CreateRoomSwitchVisual : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.Toggle toggle;
    [SerializeField] private UnityEngine.UI.Image track;
    [SerializeField] private RectTransform knob;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color enabledLabelColor = Color.black;
    [SerializeField] private Color disabledLabelColor = new Color32(83, 86, 99, 255);
    private bool previousOn, previousEnabled, initialized;
    private float position, from, target, started;
    private static readonly Color Active = new Color32(44, 58, 116, 255);
    private static readonly Color Inactive = new Color32(136, 141, 156, 255);

    public void Configure(UnityEngine.UI.Toggle control, UnityEngine.UI.Image background, RectTransform dot, TMP_Text text) {
        toggle = control; track = background; knob = dot; label = text; initialized = false;
        Refresh();
    }
    public void SetLabelColors(Color enabled, Color disabled) {
        enabledLabelColor = enabled; disabledLabelColor = disabled; initialized = false; Refresh();
    }
    private void OnEnable() { initialized = false; }
    private void LateUpdate() { Refresh(); }
    private void Refresh() {
        if (!toggle || !track || !knob) return;
        bool enabled = toggle.IsInteractable();
        if (!initialized || previousOn != toggle.isOn || previousEnabled != enabled) {
            from = position; target = toggle.isOn ? 1 : 0; started = Time.unscaledTime;
            if (!initialized || !Application.isPlaying) position = from = target;
            previousOn = toggle.isOn; previousEnabled = enabled; initialized = true;
            if (label) label.color = enabled ? enabledLabelColor : disabledLabelColor;
        }
        float t = Mathf.Clamp01((Time.unscaledTime - started) / .13f);
        position = Mathf.Lerp(from, target, t * t * (3 - 2 * t));
        track.color = enabled ? Color.Lerp(Inactive, Active, position) : Inactive;
        float radius = track.rectTransform.rect.height / 2;
        knob.anchoredPosition = new Vector2(Mathf.Lerp(radius, track.rectTransform.rect.width - radius, position), 0);
    }
}
