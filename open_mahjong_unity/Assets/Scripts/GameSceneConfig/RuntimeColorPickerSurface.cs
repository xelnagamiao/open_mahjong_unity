using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>运行时 HSV 选色面板；渐变直接由 UI 网格绘制。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RuntimeColorPickerSurface : MaskableGraphic, IPointerDownHandler, IDragHandler
{
    public RuntimeColorPicker picker;
    public bool hueStrip;
    float hue;

    public void SetHue(float value)
    {
        if (Mathf.Approximately(hue, value)) return;
        hue = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        int columns = hueStrip ? 6 : 16;
        int rows = hueStrip ? 1 : 16;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++) {
                int start = mesh.currentVertCount;
                Add(mesh, rect, x / (float)columns, y / (float)rows);
                Add(mesh, rect, x / (float)columns, (y + 1f) / rows);
                Add(mesh, rect, (x + 1f) / columns, (y + 1f) / rows);
                Add(mesh, rect, (x + 1f) / columns, y / (float)rows);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start + 2, start + 3, start);
            }
    }

    void Add(VertexHelper mesh, Rect rect, float x, float y)
    {
        Color value = hueStrip ? Color.HSVToRGB(x, 1, 1) : Color.HSVToRGB(hue, x, y);
        mesh.AddVert(new Vector3(rect.xMin + x * rect.width, rect.yMin + y * rect.height), value, new Vector2(x, y));
    }

    public void OnPointerDown(PointerEventData data) => Pick(data);
    public void OnDrag(PointerEventData data) => Pick(data);

    void Pick(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || picker == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, data.position, data.pressEventCamera, out var local)) return;
        var rect = rectTransform.rect;
        float x = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
        float y = Mathf.Clamp01((local.y - rect.yMin) / rect.height);
        if (hueStrip) picker.SetHue(x);
        else picker.SetSaturationValue(x, y);
    }
}
