using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Close a page's detached dialogs and subpages before the owning page fades out.</summary>
[DisallowMultipleComponent]
public sealed class PageOwnedOverlays : MonoBehaviour {
    public GameObject[] overlays;
    public void Close() {
        if (overlays == null) return;
        foreach (var overlay in overlays) {
            if (!overlay) continue;
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected && selected.transform.IsChildOf(overlay.transform)) {
                selected.GetComponent<TMPro.TMP_InputField>()?.DeactivateInputField();
                EventSystem.current.SetSelectedGameObject(null);
            }
            if (overlay.TryGetComponent<TipsContainer>(out var tips)) tips.HideTips();
            else if (overlay.TryGetComponent<ScoreHistoryFanTooltip>(out var fans)) fans.Hide();
            else if (overlay.TryGetComponent<RulebookPanelController>(out var rules)) rules.CloseAll();
            else if (overlay.TryGetComponent<PanelPopupTransition>(out var popup)) popup.HideImmediate();
            else overlay.SetActive(false);
        }
    }
    void OnDisable() { Close(); }
}
