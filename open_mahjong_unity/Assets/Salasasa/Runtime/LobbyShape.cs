using System.Collections.Generic;
using UnityEngine;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LobbyShape : UnityEngine.UI.MaskableGraphic
    {
        public enum Kind { Rect, Rounded, Chamfer, Slant, Disc, Diamond }
        public Kind kind;
        public float radius;
        public bool individualCorners;
        public Vector4 cornerRadii;
        public Color outlineColor = Color.clear;
        public float outlineWidth = 1;
        public bool gradient;
        public bool horizontalGradient;
        public Color lowerColor = Color.white;
        readonly List<Vector2> points = new List<Vector2>(48);

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear(); points.Clear();
            Rect r = rectTransform.rect;
            float d = Mathf.Min(radius, Mathf.Min(r.width, r.height) * .49f);
            Vector2 P(float x, float y) => new Vector2(r.xMin + x, r.yMax - y);
            if (kind == Kind.Disc)
            {
                for (int i = 0; i < 64; i++)
                {
                    float a = i * Mathf.PI * 2 / 64;
                    points.Add(r.center + new Vector2(Mathf.Cos(a) * r.width / 2, Mathf.Sin(a) * r.height / 2));
                }
            }
            else if (kind == Kind.Diamond)
                points.AddRange(new[] { P(r.width / 2, 0), P(r.width, r.height / 2), P(r.width / 2, r.height), P(0, r.height / 2) });
            else if (kind == Kind.Slant)
                points.AddRange(new[] { P(d, 0), P(r.width, 0), P(r.width - d, r.height), P(0, r.height) });
            else if (kind == Kind.Chamfer)
                points.AddRange(new[] { P(d, 0), P(r.width - d, 0), P(r.width, d), P(r.width, r.height - d), P(r.width - d, r.height), P(d, r.height), P(0, r.height - d), P(0, d) });
            else if (kind == Kind.Rounded && (d > 0 || individualCorners))
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    float requested = corner == 0 ? cornerRadii.y : corner == 1 ? cornerRadii.z : corner == 2 ? cornerRadii.w : cornerRadii.x;
                    float cr = individualCorners ? Mathf.Clamp(requested, 0, Mathf.Min(r.width, r.height) * .5f) : d;
                    Vector2 center = corner == 0 ? P(r.width - cr, cr) : corner == 1 ? P(r.width - cr, r.height - cr) : corner == 2 ? P(cr, r.height - cr) : P(cr, cr);
                    for (int i = 0; i <= 8; i++)
                    {
                        float a = (90 - corner * 90 - i * 90f / 8) * Mathf.Deg2Rad;
                        points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * cr);
                    }
                }
            }
            else points.AddRange(new[] { P(0, 0), P(r.width, 0), P(r.width, r.height), P(0, r.height) });

            Color At(Vector2 p) => !gradient ? color : horizontalGradient
                ? Color.Lerp(color, lowerColor, Mathf.InverseLerp(r.xMin, r.xMax, p.x))
                : Color.Lerp(lowerColor, color, Mathf.InverseLerp(r.yMin, r.yMax, p.y));
            vh.AddVert(r.center, At(r.center), Vector2.zero);
            foreach (Vector2 p in points) vh.AddVert(p, At(p), Vector2.zero);
            for (int i = 0; i < points.Count; i++) vh.AddTriangle(0, i + 1, (i + 1) % points.Count + 1);
            if (outlineColor.a > 0 && outlineWidth > 0)
                for (int i = 0; i < points.Count; i++) Edge(vh, points[i], points[(i + 1) % points.Count], outlineColor, outlineWidth);
        }

        static void Edge(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < .0001f) return;
            Vector2 n = new Vector2(-direction.y, direction.x).normalized * width * .5f;
            int v = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero); vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero); vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}
