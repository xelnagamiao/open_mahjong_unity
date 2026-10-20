using UnityEngine;
using UnityEngine.UI;

/// <summary>Keep the status content backed by its main color, then fade to transparent.</summary>
[RequireComponent(typeof(UnityEngine.UI.Image))]
public sealed class SelfStatusFade : BaseMeshEffect {
    private float solidWidth;

    public void SetSolidWidth(float value) {
        if (Mathf.Approximately(solidWidth, value)) return;
        solidWidth = value;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vertices) {
        if (!IsActive()) return;
        Rect rect = graphic.rectTransform.rect;
        float contentEnd = Mathf.Clamp(rect.xMin + solidWidth, rect.xMin, rect.xMax);
        Color color = graphic.color;
        vertices.Clear();
        AddEdge(vertices, rect.xMin, rect, color, 1f);
        AddEdge(vertices, contentEnd, rect, color, 0.94f);
        AddEdge(vertices, Mathf.Lerp(contentEnd, rect.xMax, 0.4f), rect, color, 0.62f);
        AddEdge(vertices, Mathf.Lerp(contentEnd, rect.xMax, 0.75f), rect, color, 0.22f);
        AddEdge(vertices, rect.xMax, rect, color, 0f);
        for (int i = 0; i < 4; i++) {
            int n = i * 2;
            vertices.AddTriangle(n, n + 1, n + 3);
            vertices.AddTriangle(n + 3, n + 2, n);
        }
    }

    private static void AddEdge(VertexHelper vertices, float x, Rect rect, Color color, float opacity) {
        color.a *= opacity;
        vertices.AddVert(new Vector3(x, rect.yMin), color, Vector2.zero);
        vertices.AddVert(new Vector3(x, rect.yMax), color, Vector2.zero);
    }
}
