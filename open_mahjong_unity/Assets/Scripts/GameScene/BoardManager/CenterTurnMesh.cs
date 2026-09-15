using UnityEngine;
using UnityEngine.UI;

/// <summary>Cut ends for the original turn Image, preserving its live colour and uniform opacity.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class CenterTurnMesh : BaseMeshEffect
{
    float cut = .55f;
    bool taper;
    float highlight;
    public bool Taper { get => taper; set { taper = value; if (graphic) graphic.SetVerticesDirty(); } }
    public float Highlight { get => highlight; set { highlight = value; if (graphic) graphic.SetVerticesDirty(); } }
    public float Cut
    {
        get => cut;
        set { cut = value; if (graphic) graphic.SetVerticesDirty(); }
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        UIVertex source = UIVertex.simpleVert;
        mesh.PopulateUIVertex(ref source, 0);
        // UIVertex stores Color32: convert once before using its alpha as a float.
        Color sourceColor = source.color;
        Rect rect = graphic.rectTransform.rect;
        Vector2[] points = taper ? new[] {
            new Vector2(-rect.width / 2 + 1.4f, -rect.height / 2), new Vector2(rect.width / 2 - 1.4f, -rect.height / 2),
            new Vector2(rect.width / 2, rect.height / 2), new Vector2(-rect.width / 2, rect.height / 2)
        } : CenterSkinGraphic.CutRect(rect.width, rect.height, cut);
        Vector2 center = rect.center;
        mesh.Clear();

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = source.color;
        // A sprite-less Image samples the shared white texture. No Sprite or texture is created.
        vertex.uv0 = source.uv0;
        vertex.position = center;
        mesh.AddVert(vertex);
        foreach (Vector2 point in points)
        {
            Color lit = Color.Lerp(sourceColor, Color.white, point.y > 0 ? highlight : 0);
            lit.a = sourceColor.a; vertex.color = lit;
            vertex.position = center + point;
            mesh.AddVert(vertex);
        }
        for (int i = 0; i < points.Length; i++)
            mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
    }
}
