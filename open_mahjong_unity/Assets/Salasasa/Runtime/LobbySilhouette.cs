using UnityEngine;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LobbySilhouette : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 P(float x, float y) => new Vector2(r.xMin + x * r.width, r.yMax - y * r.height);
            void Polygon(Vector2 center, Vector2[] points)
            {
                int start = vh.currentVertCount;
                foreach (var p in points) vh.AddVert(p, color, Vector2.zero);
                var remaining = new System.Collections.Generic.List<int>();
                float area = 0;
                for (int i = 0; i < points.Length; i++) { remaining.Add(i); var a = points[i]; var b = points[(i + 1) % points.Length]; area += a.x * b.y - b.x * a.y; }
                float sign = Mathf.Sign(area);
                float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                int guard = 0;
                while (remaining.Count > 2 && guard++ < points.Length * points.Length)
                {
                    bool clipped = false;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                        if (Cross(points[a], points[b], points[c]) * sign <= .0001f) continue;
                        bool contains = false;
                        foreach (int p in remaining)
                        {
                            if (p == a || p == b || p == c) continue;
                            if (Cross(points[a], points[b], points[p]) * sign >= 0 && Cross(points[b], points[c], points[p]) * sign >= 0 && Cross(points[c], points[a], points[p]) * sign >= 0) { contains = true; break; }
                        }
                        if (contains) continue;
                        vh.AddTriangle(start + a, start + b, start + c); remaining.RemoveAt(i); clipped = true; break;
                    }
                    if (!clipped) break;
                }
            }
            var outline = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i <= 56; i++)
            {
                float a = Mathf.Lerp(60, -240, i / 56f) * Mathf.Deg2Rad;
                outline.Add(P(.5f + Mathf.Cos(a) * .12f, .16f + Mathf.Sin(a) * .11f));
            }
            outline.AddRange(new[] {
                P(.43f,.29f), P(.34f,.31f), P(.27f,.35f), P(.21f,.47f), P(.17f,.63f),
                P(.21f,.70f), P(.27f,.65f), P(.33f,.48f), P(.36f,.63f), P(.34f,.74f),
                P(.28f,1.04f), P(.47f,1.04f), P(.5f,.8f), P(.53f,1.04f), P(.72f,1.04f),
                P(.66f,.74f), P(.64f,.63f), P(.67f,.48f), P(.73f,.65f), P(.79f,.70f),
                P(.83f,.63f), P(.79f,.47f), P(.73f,.35f), P(.66f,.31f), P(.57f,.29f)
            });
            Polygon(P(.5f, .49f), outline.ToArray());
        }
    }
}
