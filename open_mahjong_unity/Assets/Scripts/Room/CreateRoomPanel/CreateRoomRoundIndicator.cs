using TMPro;
using UnityEngine;

[ExecuteAlways]
public sealed class CreateRoomRoundIndicator : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.Toggle[] choices;
    [SerializeField] private RectTransform indicator;
    [SerializeField] private CanvasGroup group;
    private static readonly Color SelectedColor = new Color32(44, 58, 116, 255);
    private UnityEngine.UI.Image[] backgrounds;
    private TMP_Text[] labels;

    public void Configure(UnityEngine.UI.Toggle[] controls, RectTransform fill, CanvasGroup state) {
        choices = controls; indicator = fill; group = state;
        CacheGraphics(); RefreshSelection();
    }
    private void OnEnable() { CacheGraphics(); RefreshSelection(); }
    private void CacheGraphics() {
        if (choices == null) return;
        backgrounds = new UnityEngine.UI.Image[choices.Length];
        labels = new TMP_Text[choices.Length];
        for (int i = 0; i < choices.Length; i++) {
            if (!choices[i]) continue;
            backgrounds[i] = choices[i].GetComponent<UnityEngine.UI.Image>();
            labels[i] = choices[i].GetComponentInChildren<TMP_Text>(true);
        }
    }
    private void LateUpdate() { RefreshSelection(); }
    private void RefreshSelection() {
        if (indicator && indicator.gameObject.activeSelf) indicator.gameObject.SetActive(false);
        if (choices == null) return;
        if (backgrounds == null || backgrounds.Length != choices.Length) CacheGraphics();
        UnityEngine.UI.Toggle selected = null;
        for (int i = 0; i < choices.Length; i++) {
            var option = choices[i];
            if (!option || !option.gameObject.activeSelf) continue;
            if (backgrounds[i]) backgrounds[i].color = option.isOn ? SelectedColor : Color.white;
            var label = labels[i];
            if (label) {
                label.fontStyle = FontStyles.Normal;
                label.color = option.isOn ? Color.white : Color.black;
            }
            if (option.isOn) selected = option;
        }
        if (group && selected) { group.alpha = selected.interactable ? 1 : .42f; group.interactable = selected.interactable; }
    }
}
