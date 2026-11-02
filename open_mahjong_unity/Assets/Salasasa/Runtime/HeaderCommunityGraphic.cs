using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HeaderCommunityGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            void Polygon(params Vector2[] points)
            {
                int first = mesh.currentVertCount;
                foreach (var p in points) mesh.AddVert(new Vector3(rect.xMin + p.x * rect.width, rect.yMin + p.y * rect.height), color, Vector2.zero);
                for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
            }
            // Two solid speech bubbles. Negative space is geometry, so no background colour is baked into the symbol.
            Polygon(new Vector2(.06f,.92f),new Vector2(.76f,.92f),new Vector2(.76f,.46f),new Vector2(.06f,.46f));
            Polygon(new Vector2(.14f,.47f),new Vector2(.37f,.47f),new Vector2(.14f,.27f));
            Polygon(new Vector2(.84f,.7f),new Vector2(.96f,.7f),new Vector2(.96f,.18f),new Vector2(.84f,.18f));
            Polygon(new Vector2(.40f,.34f),new Vector2(.96f,.34f),new Vector2(.96f,.18f),new Vector2(.40f,.18f));
            Polygon(new Vector2(.69f,.19f),new Vector2(.88f,.19f),new Vector2(.88f,.03f));
        }
    }
}
