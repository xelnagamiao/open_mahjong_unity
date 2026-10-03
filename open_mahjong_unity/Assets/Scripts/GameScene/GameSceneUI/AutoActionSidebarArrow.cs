using UnityEngine;

/// <summary>侧栏的实心三角，按 UI 尺寸绘制，避免缩放贴图时变模糊。</summary>
public sealed class AutoActionSidebarArrow : UnityEngine.UI.MaskableGraphic {
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh) {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        mesh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMax, rect.center.y), color, Vector2.zero);
        mesh.AddTriangle(0, 1, 2);
    }
}
