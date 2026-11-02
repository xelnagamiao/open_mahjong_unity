using System.Collections.Generic;
using UnityEngine;

/// <summary>Resolution-independent HUD surface. Theme changes do not allocate textures.</summary>
[RequireComponent(typeof(CanvasRenderer)), AddComponentMenu("Mahjong/UI/Table HUD Surface")]
public sealed class TableHudSurface : UnityEngine.UI.MaskableGraphic {
    public TableHudTheme.SurfaceRole role;
    public Color edgeColor = Color.white;
    public Color bottomColor = Color.black;
    public float edgeWidth = 1.2f;
    public float corner = 10f;
    public bool chamfer = true;

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh) {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        float border = Mathf.Clamp(edgeWidth, 0, Mathf.Min(rect.width, rect.height) * .25f);
        var outside = Points(rect, corner);
        var inside = Points(new Rect(rect.x + border, rect.y + border, rect.width - border * 2, rect.height - border * 2), Mathf.Max(0, corner - border));
        int count = outside.Count;
        for (int i = 0; i < count; i++) {
            vh.AddVert(outside[i], edgeColor, Vector2.zero);
            vh.AddVert(inside[i], edgeColor, Vector2.zero);
        }
        if (border > 0) for (int i = 0; i < count; i++) {
            int j = (i + 1) % count;
            vh.AddTriangle(i * 2, j * 2, i * 2 + 1);
            vh.AddTriangle(j * 2, j * 2 + 1, i * 2 + 1);
        }
        int start = vh.currentVertCount;
        vh.AddVert(rect.center, Color.Lerp(bottomColor, color, .5f), Vector2.zero);
        foreach (var point in inside) vh.AddVert(point, Color.Lerp(bottomColor, color, Mathf.InverseLerp(rect.yMin, rect.yMax, point.y)), Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % count + 1);
    }

    private List<Vector2> Points(Rect r, float radius) {
        radius = Mathf.Clamp(radius, 0, Mathf.Min(r.width, r.height) * .5f);
        int steps = chamfer ? 1 : 6;
        var points = new List<Vector2>(4 * (steps + 1));
        Vector2[] centers = { new Vector2(r.xMax-radius,r.yMax-radius), new Vector2(r.xMin+radius,r.yMax-radius), new Vector2(r.xMin+radius,r.yMin+radius), new Vector2(r.xMax-radius,r.yMin+radius) };
        for (int c = 0; c < 4; c++) for (int i = 0; i <= steps; i++) {
            float a = (c * 90f + i * 90f / steps) * Mathf.Deg2Rad;
            points.Add(centers[c] + new Vector2(Mathf.Cos(a),Mathf.Sin(a)) * radius);
        }
        return points;
    }
}
