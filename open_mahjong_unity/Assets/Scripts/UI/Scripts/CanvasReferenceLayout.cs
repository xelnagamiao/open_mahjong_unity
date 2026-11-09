using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps authored UI units when a former root canvas joins a page or overlay canvas.</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(200)]
public sealed class CanvasReferenceLayout : MonoBehaviour {
    public RectTransform referenceArea;
    public CanvasScaler scaleSettings;
    public bool includeTopInset;
    [System.NonSerialized] public bool keepMobilePopupSize;
    RectTransform rect;
    Vector2 previousSize, previousCenter;
    float previousScale;

    void OnEnable() { Apply(true); }
    void LateUpdate() { Apply(); }

    public void Apply(bool force = false) {
        if (!referenceArea || !scaleSettings || !(transform.parent is RectTransform parent)) return;
        if (!rect) rect = (RectTransform)transform;
        float pixels = MobileViewportLayout.Enabled && keepMobilePopupSize
            ? Screen.height / Mathf.Max(1,scaleSettings.referenceResolution.y) : DesiredPixels(scaleSettings);
        float parentPixels = PixelsPerUnit(parent);
        if (pixels <= 0 || parentPixels <= 0) return;
        float scale = pixels / parentPixels;
        Vector2 size = referenceArea.rect.size * (PixelsPerUnit(referenceArea) / pixels);
        Vector2 center = parent.InverseTransformPoint(referenceArea.TransformPoint(referenceArea.rect.center));
        if (includeTopInset && referenceArea.parent is RectTransform areaParent) {
            float extra=Mathf.Max(0,-referenceArea.offsetMax.y)*PixelsPerUnit(areaParent);
            size.y+=extra/pixels;center.y+=extra/(parentPixels*2);
        }
        Vector2 position = center + Vector2.Scale(rect.pivot - Vector2.one * .5f, size) * scale;
        if (!force && size == previousSize && position == previousCenter && Mathf.Approximately(scale, previousScale)) return;
        previousSize = size; previousCenter = position; previousScale = scale;
        rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
        rect.sizeDelta = size;
        rect.localScale = new Vector3(scale, scale, 1);
        rect.anchoredPosition = position;
    }

    public static float PixelsPerUnit(RectTransform rect) {
        var canvas = rect.GetComponentInParent<Canvas>();
        if (!canvas) return 1;
        var root = canvas.rootCanvas;
        return root.scaleFactor * Mathf.Abs(rect.lossyScale.x / root.transform.lossyScale.x);
    }

    public static float EffectiveScale(Canvas canvas) => canvas ? PixelsPerUnit((RectTransform)canvas.transform) : 1;

    static float DesiredPixels(CanvasScaler settings) {
        if (settings.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return settings.scaleFactor;
        if (settings.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return settings.GetComponent<Canvas>().rootCanvas.scaleFactor;
        var reference = settings.referenceResolution;
        var root = settings.GetComponent<Canvas>().rootCanvas;
        Vector2 pixels = Application.isPlaying ? new Vector2(Screen.width, Screen.height) : root.pixelRect.size;
        if (pixels.x <= 0 || pixels.y <= 0) return 0;
        float width = pixels.x / Mathf.Max(1, reference.x), height = pixels.y / Mathf.Max(1, reference.y);
        if (settings.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand) return Mathf.Min(width, height);
        if (settings.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink) return Mathf.Max(width, height);
        return Mathf.Pow(2, Mathf.Lerp(Mathf.Log(width, 2), Mathf.Log(height, 2), settings.matchWidthOrHeight));
    }
}
