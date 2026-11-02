using Salasasa.Home;
using UnityEngine;

/// <summary>Fits an authored floating panel without changing its own popup animation transform.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(300)]
public sealed class ViewportPanelFit : MonoBehaviour {
    public RectTransform panel;
    public FourSideHeader navigation;
    public Vector2 authoredPosition;
    public float margin = 12;
    RectTransform rect;
    Canvas root;

    void OnEnable() => Apply();
    void LateUpdate() => Apply();

    public void Apply() {
        if (!panel || !(transform.parent is RectTransform parent)) return;
        if (!rect) rect = (RectTransform)transform;
        if (!root) root = GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        Rect pixels = root.pixelRect;
        Vector2 min = Vector2.Max(pixels.min, Screen.safeArea.min);
        Vector2 max = Vector2.Min(pixels.max, Screen.safeArea.max);
        if (max.x <= min.x || max.y <= min.y) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, min, camera, out var low) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, max, camera, out var high)) return;
        Rect available = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        Vector4 insets = navigation ? navigation.Insets * CanvasReferenceLayout.EffectiveScale(navigation.canvas)
            / CanvasReferenceLayout.PixelsPerUnit(parent) : Vector4.zero;
        available.xMin += insets.x + margin; available.xMax -= insets.z + margin;
        available.yMin += insets.w + margin; available.yMax -= insets.y + margin;
        // The child animates its localScale; its unscaled authored rectangle remains the fitting reference.
        Rect bounds = new Rect((Vector2)panel.localPosition + panel.rect.min, panel.rect.size);
        Vector2 anchor = parent.rect.min + Vector2.Scale(parent.rect.size, rect.anchorMin);
        Vector2 origin = anchor + authoredPosition;
        float left = Mathf.Max(available.xMin, origin.x + bounds.xMin);
        float top = Mathf.Min(available.yMax, origin.y + bounds.yMax);
        float scale = Mathf.Max(.1f, Mathf.Min(1, (available.xMax-left)/bounds.width, (top-available.yMin)/bounds.height));
        Vector3 desiredScale = new Vector3(scale, scale, 1);
        Vector2 position = new Vector2(left-bounds.xMin*scale, top-bounds.yMax*scale)-anchor;
        if (rect.localScale != desiredScale) rect.localScale = desiredScale;
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
    }
}
