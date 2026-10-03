using UnityEngine;
using UnityEngine.UI;

/// <summary>Clips the portrait geometry to the frame opening, leaving transparent corners.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.UI.Image))]
public sealed class AvatarPortraitCorners : BaseMeshEffect {
    [SerializeField] private float cornerRadius;

    public void SetRadius(float radius) {
        radius = Mathf.Max(0, radius);
        if (Mathf.Approximately(cornerRadius, radius)) return;
        cornerRadius = radius;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh) {
        // Portraits use simple Image quads. Work from their actual vertices so
        // sprite UVs, preserveAspect and non-centered pivots remain unchanged.
        if (!IsActive() || cornerRadius <= 0 || vh.currentVertCount != 4) return;
        UIVertex bottomLeft = default, topLeft = default, topRight = default, bottomRight = default;
        vh.PopulateUIVertex(ref bottomLeft, 0);
        vh.PopulateUIVertex(ref topLeft, 1);
        vh.PopulateUIVertex(ref topRight, 2);
        vh.PopulateUIVertex(ref bottomRight, 3);
        var bounds = Rect.MinMaxRect(bottomLeft.position.x, bottomLeft.position.y, topRight.position.x, topRight.position.y);
        if (bounds.width <= 0 || bounds.height <= 0) return;
        float radius = Mathf.Min(cornerRadius, Mathf.Min(bounds.width, bounds.height) * .5f);
        const int segments = 12;
        const int count = 4 * (segments + 1);
        vh.Clear();
        AddVertex(vh, bounds.center, bounds, bottomLeft, topLeft, topRight, bottomRight);
        for (int i = 0; i < count; i++) {
            var point = AvatarFrameGraphic.RoundedPoint(bounds, radius, i, segments);
            AddVertex(vh, point, bounds, bottomLeft, topLeft, topRight, bottomRight);
        }
        for (int i = 0; i < count; i++) vh.AddTriangle(0, i + 1, (i + 1) % count + 1);
    }

    private static void AddVertex(VertexHelper vh, Vector2 point, Rect bounds, UIVertex bl, UIVertex tl, UIVertex tr, UIVertex br) {
        float x = (point.x - bounds.xMin) / bounds.width;
        float y = (point.y - bounds.yMin) / bounds.height;
        var vertex = bl;
        vertex.position = new Vector3(point.x, point.y, bl.position.z);
        vertex.uv0 = Vector4.Lerp(Vector4.Lerp(bl.uv0, br.uv0, x), Vector4.Lerp(tl.uv0, tr.uv0, x), y);
        vh.AddVert(vertex);
    }
}
