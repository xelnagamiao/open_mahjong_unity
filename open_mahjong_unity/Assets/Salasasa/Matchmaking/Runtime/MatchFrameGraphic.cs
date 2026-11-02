using System;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Matchmaking
{
    /// <summary>Renders the authored card frame in one graphic without separate edge objects.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MatchFrameGraphic : MaskableGraphic
    {
        [Serializable]
        public struct Band
        {
            public Vector2 anchorMin, anchorMax, pivot, position, sizeDelta;
            public Color color;
            public Rect GetRect(Rect parent)
            {
                var size = Vector2.Scale(parent.size, anchorMax - anchorMin) + sizeDelta;
                var anchor = anchorMin + Vector2.Scale(anchorMax - anchorMin, pivot);
                var origin = parent.min + Vector2.Scale(parent.size, anchor) + position - Vector2.Scale(size, pivot);
                return new Rect(origin, size);
            }
        }

        public Band[] bands = Array.Empty<Band>();

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            foreach (var band in bands)
            {
                var rect = band.GetRect(rectTransform.rect);
                int start = mesh.currentVertCount;
                Color32 tint = band.color * color;
                mesh.AddVert(new Vector3(rect.xMin, rect.yMin), tint, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMin, rect.yMax), tint, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, rect.yMax), tint, Vector2.zero);
                mesh.AddVert(new Vector3(rect.xMax, rect.yMin), tint, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
