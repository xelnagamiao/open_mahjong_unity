using UnityEngine;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class CreateRoomDropdownArrow : UnityEngine.UI.MaskableGraphic {
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh) {
        mesh.Clear();
        var rect = rectTransform.rect;
        mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.center.x, rect.yMin), color, Vector2.zero);
        mesh.AddTriangle(0, 1, 2);
    }
}
