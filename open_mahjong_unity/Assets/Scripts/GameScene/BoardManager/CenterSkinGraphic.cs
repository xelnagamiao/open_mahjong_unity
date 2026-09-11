using System;
using UnityEngine;
using UnityEngine.UI;

// The approved v4 face: editable solid-colour convex geometry with inward AA.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CenterSkinGraphic : MaskableGraphic
{
    [SerializeField] Vector2[] outline = Array.Empty<Vector2>();
    [SerializeField] Color stroke = Color.clear;
    [SerializeField] float strokeWidth;
    const float Feather = .08f;

    public void SetShape(Vector2[] polygon, Color fill, Color edge, float width)
    {
        outline = polygon; color = fill; stroke = edge; strokeWidth = width;
        raycastTarget = false; SetVerticesDirty();
    }

    public static Vector2[] CutRect(float width, float height, float cut)
    {
        float x = width / 2, y = height / 2;
        cut = Mathf.Clamp(cut, .001f, Mathf.Min(x, y));
        return new[] { new Vector2(-x + cut, -y), new Vector2(x - cut, -y), new Vector2(x, -y + cut),
            new Vector2(x, y - cut), new Vector2(x - cut, y), new Vector2(-x + cut, y),
            new Vector2(-x, y - cut), new Vector2(-x, -y + cut) };
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (outline == null || outline.Length < 3) return;
        float aa = Mathf.Min(Feather, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .2f);
        Color edge = strokeWidth > aa ? stroke : color, transparent = edge;
        transparent.a = 0;
        var solid = Inset(outline, aa);
        Ring(mesh, outline, solid, transparent, edge);
        var fill = solid;
        if (strokeWidth > aa)
        {
            fill = Inset(outline, strokeWidth);
            Ring(mesh, solid, fill, stroke, stroke);
        }
        Fan(mesh, fill, color);
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static Vector2[] Inset(Vector2[] points, float width)
    {
        var result = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = (points[i] - points[(i + points.Length - 1) % points.Length]).normalized;
            Vector2 b = (points[(i + 1) % points.Length] - points[i]).normalized;
            Vector2 pa = points[i] + new Vector2(-a.y, a.x) * width, pb = points[i] + new Vector2(-b.y, b.x) * width;
            float cross = Cross(a, b);
            result[i] = Mathf.Abs(cross) < .00001f ? pa : pa + a * (Cross(pb - pa, b) / cross);
        }
        return result;
    }
    static void Vertex(VertexHelper mesh, Vector2 point, Color color)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = point; vertex.color = color; vertex.uv0 = Vector2.zero;
        mesh.AddVert(vertex);
    }
    static void Ring(VertexHelper mesh, Vector2[] outer, Vector2[] inner, Color a, Color b)
    {
        int start = mesh.currentVertCount, count = outer.Length;
        for (int i = 0; i < count; i++) { Vertex(mesh, outer[i], a); Vertex(mesh, inner[i], b); }
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count, x = start + i * 2, y = start + j * 2;
            mesh.AddTriangle(x, y, x + 1); mesh.AddTriangle(x + 1, y, y + 1);
        }
    }
    static void Fan(VertexHelper mesh, Vector2[] points, Color color)
    {
        int start = mesh.currentVertCount; Vector2 center = Vector2.zero;
        foreach (var point in points) center += point;
        Vertex(mesh, center / points.Length, color);
        foreach (var point in points) Vertex(mesh, point, color);
        for (int i = 0; i < points.Length; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
    }
}
