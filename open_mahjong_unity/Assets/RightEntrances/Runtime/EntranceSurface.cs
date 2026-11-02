using UnityEngine;
using System.Collections.Generic;

namespace Salasasa.RightEntrances
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EntranceSurface : UnityEngine.UI.MaskableGraphic
    {
        public enum Form { Rectangle, Cut, Slash, Outline, Circle }
        public Form form;
        public bool strokeOnly;
        public float cut = 18, thickness = 1;
        readonly List<Vector2> points = new List<Vector2>(64);
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear(); points.Clear(); var r=rectTransform.rect;
            Vector2 P(float x,float y)=>new Vector2(r.xMin+x,r.yMax-y);
            float c=Mathf.Min(cut,Mathf.Min(r.width,r.height)*.45f);
            if(form==Form.Circle){for(int i=0;i<64;i++){float a=i*Mathf.PI/32;points.Add(r.center+new Vector2(Mathf.Cos(a)*r.width*.5f,Mathf.Sin(a)*r.height*.5f));}}
            else if(form==Form.Cut)points.AddRange(new[]{P(0,0),P(r.width-c,0),P(r.width,c),P(r.width,r.height),P(c,r.height),P(0,r.height-c)});
            else if(form==Form.Slash)points.AddRange(new[]{P(c,0),P(r.width,0),P(r.width-c,r.height),P(0,r.height)});
            else points.AddRange(new[]{P(0,0),P(r.width,0),P(r.width,r.height),P(0,r.height)});
            if(strokeOnly||form==Form.Outline){for(int i=0;i<points.Count;i++)Line(vh,points[i],points[(i+1)%points.Count],color,thickness);return;}
            vh.AddVert(r.center,color,Vector2.zero);
            foreach(var p in points)vh.AddVert(p,color,Vector2.zero);
            for(int i=0;i<points.Count;i++)vh.AddTriangle(0,i+1,(i+1)%points.Count+1);
        }
        public static void Line(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,Color color,float width)
        {
            Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int i=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
