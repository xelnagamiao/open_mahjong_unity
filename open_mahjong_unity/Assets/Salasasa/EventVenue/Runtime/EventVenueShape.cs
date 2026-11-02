using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Events
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EventVenueShape : MaskableGraphic
    {
        public float cut = 18;
        public bool slash;
        public Color borderColor;
        [Min(0)] public float borderWidth;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = GetPixelAdjustedRect(); float c = Mathf.Min(cut, r.height * 0.4f);
            var p=Points(r,c);
            float width=Mathf.Clamp(borderWidth,0,Mathf.Min(r.width,r.height)*.25f);
            if(width>0 && borderColor.a>0)
            {
                var inner=Rect.MinMaxRect(r.xMin+width,r.yMin+width,r.xMax-width,r.yMax-width);
                var q=Points(inner,Mathf.Max(0,c-width*.6f));
                for(int i=0;i<p.Length;i++)vh.AddVert(p[i],borderColor,Vector2.zero);
                for(int i=0;i<q.Length;i++)vh.AddVert(q[i],borderColor,Vector2.zero);
                for(int i=0;i<p.Length;i++)
                {
                    int next=(i+1)%p.Length;
                    vh.AddTriangle(i,next,p.Length+i);vh.AddTriangle(next,p.Length+next,p.Length+i);
                }
                int start=vh.currentVertCount;
                for(int i=0;i<q.Length;i++)vh.AddVert(q[i],color,Vector2.zero);
                for(int i=1;i<q.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);
                return;
            }
            for(int i=0;i<p.Length;i++) vh.AddVert(p[i],color,Vector2.zero);
            for(int i=1;i<p.Length-1;i++) vh.AddTriangle(0,i,i+1);
        }
        Vector2[] Points(Rect r,float c)=>slash
            ?new[]{new Vector2(r.xMin+c,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax-c,r.yMax),new Vector2(r.xMin,r.yMax)}
            :new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMax-c,r.yMin),new Vector2(r.xMax,r.yMin+c),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin+c,r.yMax),new Vector2(r.xMin,r.yMax-c)};
    }
}
