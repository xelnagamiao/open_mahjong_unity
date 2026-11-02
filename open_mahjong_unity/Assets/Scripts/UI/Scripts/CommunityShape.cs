using UnityEngine;

namespace Salasasa.Community {
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CommunityShape : UnityEngine.UI.MaskableGraphic {
        public float cut, border;
        public Color borderColor = Color.gray;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh) {
            vh.Clear(); var r = GetPixelAdjustedRect(); float c = Mathf.Min(cut, r.height*.25f, r.width*.25f);
            Vector2[] Points(float inset) => new[]{new Vector2(r.xMin+inset,r.yMin+inset),new Vector2(r.xMax-c-inset,r.yMin+inset),new Vector2(r.xMax-inset,r.yMin+c+inset),new Vector2(r.xMax-inset,r.yMax-inset),new Vector2(r.xMin+c+inset,r.yMax-inset),new Vector2(r.xMin+inset,r.yMax-c-inset)};
            void Polygon(Vector2[] points,Color tint){int n=vh.currentVertCount;foreach(var p in points)vh.AddVert(p,tint,Vector2.zero);for(int i=1;i<points.Length-1;i++)vh.AddTriangle(n,n+i,n+i+1);}
            if(border>0){Polygon(Points(0),borderColor);Polygon(Points(border),color);}else Polygon(Points(0),color);
        }
    }
}
