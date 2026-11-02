using UnityEngine;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HomeHeaderPlateGraphic : UnityEngine.UI.MaskableGraphic
    {
        public const int TopLeft = 1, TopRight = 2, BottomLeft = 4, BottomRight = 8;
        public Color borderColor = Color.clear;
        public float borderWidth = 1, cut = 8;
        public int cutCorners = 15;
        readonly Vector2[] outer = new Vector2[8], inner = new Vector2[8];

        public void Paint(Color fill, Color border, float width, float cornerCut, int corners)
        {
            color = fill; borderColor = border; borderWidth = width; cut = cornerCut; cutCorners = corners;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float width = Mathf.Clamp(borderWidth, 0, Mathf.Min(rect.width, rect.height) * .25f);
            Outline(rect, 0, outer);
            Outline(rect, width, inner);
            if (width > 0 && borderColor.a > 0)
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8, start = mesh.currentVertCount;
                    Add(mesh, outer[i], borderColor); Add(mesh, outer[next], borderColor);
                    Add(mesh, inner[next], borderColor); Add(mesh, inner[i], borderColor);
                    mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
                }
            if (color.a <= 0) return;
            int center = mesh.currentVertCount;
            Add(mesh, rect.center, color);
            for (int i = 0; i < 8; i++) Add(mesh, inner[i], color);
            for (int i = 0; i < 8; i++) mesh.AddTriangle(center, center + i + 1, center + (i + 1) % 8 + 1);
        }

        void Outline(Rect rect, float inset, Vector2[] points)
        {
            float l = rect.xMin + inset, r = rect.xMax - inset, b = rect.yMin + inset, t = rect.yMax - inset;
            float c = Mathf.Clamp(cut - inset * (2 - Mathf.Sqrt(2)), 0, Mathf.Min(r - l, t - b) * .4f);
            float tl = (cutCorners & TopLeft) != 0 ? c : 0, tr = (cutCorners & TopRight) != 0 ? c : 0;
            float bl = (cutCorners & BottomLeft) != 0 ? c : 0, br = (cutCorners & BottomRight) != 0 ? c : 0;
            points[0] = new Vector2(r, t - tr); points[1] = new Vector2(r - tr, t);
            points[2] = new Vector2(l + tl, t); points[3] = new Vector2(l, t - tl);
            points[4] = new Vector2(l, b + bl); points[5] = new Vector2(l + bl, b);
            points[6] = new Vector2(r - br, b); points[7] = new Vector2(r, b + br);
        }

        static void Add(UnityEngine.UI.VertexHelper mesh, Vector2 point, Color tint)
        {
            var vertex = UnityEngine.UIVertex.simpleVert; vertex.position = point; vertex.color = tint; mesh.AddVert(vertex);
        }
    }
}
