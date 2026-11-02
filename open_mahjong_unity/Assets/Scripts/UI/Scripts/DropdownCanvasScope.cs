using TMPro;
using UnityEngine;

/// <summary>Keeps the TMP-generated popup and its blocker in the owning window's layer.</summary>
[DisallowMultipleComponent]
public sealed class DropdownCanvasScope : MonoBehaviour {
    public TMP_Dropdown owner;

    void OnEnable() => Apply();
    void OnTransformParentChanged() => Apply();

    void LateUpdate() {
        if (!Application.isPlaying || !owner || transform == owner.template) return;
        Canvas popup = GetComponent<Canvas>();
        if (!popup) return;
        // TMP creates this blocker after enabling the cloned dropdown list.
        foreach (Transform child in popup.rootCanvas.transform) {
            if (child.name != "Blocker") continue;
            Canvas blocker = child.GetComponent<Canvas>();
            if (!blocker || blocker.sortingOrder != popup.sortingOrder - 1) continue;
            UiCanvasLayers.Apply(blocker, popup.sortingLayerName, popup.sortingOrder - 1);
            enabled = false;
            break;
        }
    }

    void Apply() {
        if (!Application.isPlaying || !owner || transform == owner.template) return;
        Canvas window = UiCanvasLayers.SortingCanvas(owner.transform);
        Canvas popup = GetComponent<Canvas>();
        if (!window || !popup) return;
        UiCanvasLayers.Apply(popup, window.sortingLayerName, window.sortingOrder + 20);
    }
}
