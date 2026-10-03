using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(ScrollRect))]
public sealed class CreateRoomScrollbarSize : MonoBehaviour {
    private ScrollRect scroll;
    private void OnEnable() {
        scroll = GetComponent<ScrollRect>();
        scroll.onValueChanged.AddListener(Refresh);
        Canvas.willRenderCanvases += BeforeRender;
    }
    private void OnDisable() {
        if (scroll) scroll.onValueChanged.RemoveListener(Refresh);
        Canvas.willRenderCanvases -= BeforeRender;
    }
    private void Refresh(Vector2 _) { BeforeRender(); }
    private void BeforeRender() {
        if (!scroll || !scroll.verticalScrollbar) return;
        var bar = scroll.verticalScrollbar;
        float trackHeight = ((RectTransform)bar.transform).rect.height;
        float minimum = Mathf.Clamp01(88 / Mathf.Max(1, trackHeight));
        if (bar.size < minimum) bar.size = minimum;
    }
}
