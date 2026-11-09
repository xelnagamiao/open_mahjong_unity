using UnityEngine;

/// <summary>A small, resolution-independent captain badge for the party portrait.</summary>
public sealed class PartyLeaderCrown : UnityEngine.UI.MaskableGraphic {
    static readonly Vector2[] Outline = {
        new Vector2(.08f, .92f), new Vector2(.29f, .55f), new Vector2(.5f, 1),
        new Vector2(.71f, .55f), new Vector2(.92f, .92f),
        new Vector2(.81f, .24f), new Vector2(.19f, .24f)
    };
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh) {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        Add(mesh, rect, new Vector2(.5f, .32f));
        foreach (var point in Outline) Add(mesh, rect, point);
        for (int i = 0; i < Outline.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % Outline.Length + 1);
        int start = mesh.currentVertCount;
        Add(mesh, rect, new Vector2(.19f, .05f)); Add(mesh, rect, new Vector2(.81f, .05f));
        Add(mesh, rect, new Vector2(.81f, .17f)); Add(mesh, rect, new Vector2(.19f, .17f));
        mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
    }
    void Add(UnityEngine.UI.VertexHelper mesh, Rect rect, Vector2 point) {
        var vertex = UnityEngine.UIVertex.simpleVert;
        vertex.position = new Vector3(rect.xMin + point.x * rect.width, rect.yMin + point.y * rect.height);
        vertex.color = color; mesh.AddVert(vertex);
    }
}
