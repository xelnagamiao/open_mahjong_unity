using UnityEngine;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FourSideHeaderSelection : UnityEngine.UI.MaskableGraphic
    {
        public UnityEngine.UI.Graphic[] artwork;
        public Color normalInk = new Color32(244, 247, 251, 255);
        public float cornerLength = 8;
        public float lineWidth = 2;
        public float washOpacity = .09f;

        public bool IsSelected { get; private set; }
        float hoverOpacity;

        public void Apply(bool selected, bool hovered, bool pressed)
        {
            float nextHover = pressed ? .16f : hovered ? .08f : 0;
            if (IsSelected != selected || !Mathf.Approximately(hoverOpacity, nextHover))
            {
                IsSelected = selected;
                hoverOpacity = nextHover;
                SetVerticesDirty();
            }
            Color ink = selected ? color : normalInk;
            if (artwork == null) return;
            foreach (var graphic in artwork)
                if (graphic != null && graphic.color != ink) graphic.color = ink;
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0 || r.height <= 0) return;
            if (hoverOpacity > 0)
                Quad(mesh, r.xMin, r.yMin, r.width, r.height, new Color(1, 1, 1, hoverOpacity));
            if (!IsSelected) return;

            float thickness = Mathf.Clamp(lineWidth, 0, Mathf.Min(r.width, r.height) * .5f);
            float arm = Mathf.Clamp(cornerLength, thickness, Mathf.Min(r.width, r.height) * .45f);
            Color wash = color;
            wash.a *= washOpacity;
            Quad(mesh, r.xMin + thickness, r.yMin + thickness,
                r.width - thickness * 2, r.height - thickness * 2, wash);

            float l = r.xMin, b = r.yMin, t = r.yMax, q = r.xMax;
            Quad(mesh, l, t - thickness, arm, thickness, color);
            Quad(mesh, l, t - arm, thickness, arm, color);
            Quad(mesh, q - arm, t - thickness, arm, thickness, color);
            Quad(mesh, q - thickness, t - arm, thickness, arm, color);
            Quad(mesh, l, b, arm, thickness, color);
            Quad(mesh, l, b, thickness, arm, color);
            Quad(mesh, q - arm, b, arm, thickness, color);
            Quad(mesh, q - thickness, b, thickness, arm, color);
        }

        static void Quad(UnityEngine.UI.VertexHelper mesh, float x, float y, float width, float height, Color tint)
        {
            int start = mesh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = new Vector2(x, y); mesh.AddVert(vertex);
            vertex.position = new Vector2(x + width, y); mesh.AddVert(vertex);
            vertex.position = new Vector2(x + width, y + height); mesh.AddVert(vertex);
            vertex.position = new Vector2(x, y + height); mesh.AddVert(vertex);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
