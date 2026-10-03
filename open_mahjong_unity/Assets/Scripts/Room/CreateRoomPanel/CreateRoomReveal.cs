using UnityEngine;

public sealed class CreateRoomReveal : MonoBehaviour {
    [SerializeField] private UnityEngine.UI.LayoutElement layout;
    [SerializeField] private CanvasGroup group;
    private float from, target, started;
    private bool initialized;
    public void SetExpanded(bool expanded, bool animate) {
        if (!layout) layout = GetComponent<UnityEngine.UI.LayoutElement>();
        if (!group) group = GetComponent<CanvasGroup>();
        float next = expanded ? 136 : 0;
        if (initialized && Mathf.Approximately(next, target)) return;
        from = initialized ? layout.preferredHeight : next; target = next; started = Time.unscaledTime;
        if (!animate || !initialized) from = target;
        initialized = true;
        group.interactable = group.blocksRaycasts = expanded;
        Paint(from);
    }
    private void Update() {
        if (!initialized) return;
        float t = Mathf.Clamp01((Time.unscaledTime - started) / .24f);
        Paint(Mathf.Lerp(from, target, t * t * (3 - 2 * t)));
    }
    private void Paint(float height) {
        if (!Mathf.Approximately(layout.preferredHeight, height)) layout.preferredHeight = height;
        group.alpha = height / 136;
    }
}
