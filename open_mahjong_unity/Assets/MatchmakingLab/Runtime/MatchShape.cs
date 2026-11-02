using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Salasasa.MatchLab
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MatchShape:MaskableGraphic
    {
        public enum Form { Rect,Cut,Parallelogram,Diamond,Line,Ring,Cross,Arrow,Info,Lock,Check,Arc,Frame,RoundedRect,RoundedFrame }
        public Form form;public float cut=12,thickness=2;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;
            void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d){int i=vh.currentVertCount;vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddVert(d,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
            void Line(Vector2 a,Vector2 b,float w){Vector2 n=(b-a).normalized;var t=new Vector2(-n.y,n.x)*w*.5f;Quad(a-t,a+t,b+t,b-t);}
            Vector2 P(float x,float y)=>new Vector2(r.xMin+x*r.width,r.yMin+y*r.height);
            if(form==Form.RoundedRect||form==Form.RoundedFrame)
            {
                float radius=Mathf.Min(cut,Mathf.Min(r.width,r.height)*.5f);var perimeter=new Vector2[36];
                for(int corner=0;corner<4;corner++)
                {
                    Vector2 center=new Vector2(corner<2?r.xMax-radius:r.xMin+radius,corner==0||corner==3?r.yMax-radius:r.yMin+radius);
                    for(int step=0;step<9;step++){float a=(90-corner*90-step*90f/8)*Mathf.Deg2Rad;perimeter[corner*9+step]=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;}
                }
                if(form==Form.RoundedFrame){for(int i=0;i<perimeter.Length;i++)Line(perimeter[i],perimeter[(i+1)%perimeter.Length],thickness);}
                else{vh.AddVert(r.center,color,Vector2.zero);foreach(var p in perimeter)vh.AddVert(p,color,Vector2.zero);for(int i=0;i<perimeter.Length;i++)vh.AddTriangle(0,i+1,(i+1)%perimeter.Length+1);}
                return;
            }
            if(form==Form.Frame)
            {
                float t=Mathf.Clamp(thickness,0,Mathf.Min(r.width,r.height)*.5f);
                float x=t/Mathf.Max(1,r.width),y=t/Mathf.Max(1,r.height);
                Quad(P(0,0),P(0,y),P(1,y),P(1,0));
                Quad(P(0,1-y),P(0,1),P(1,1),P(1,1-y));
                Quad(P(0,y),P(0,1-y),P(x,1-y),P(x,y));
                Quad(P(1-x,y),P(1-x,1-y),P(1,1-y),P(1,y));return;
            }
            if(form==Form.Arc){for(int i=0;i<56;i++){float a=(35+i*5)*Mathf.Deg2Rad,b=(40+i*5)*Mathf.Deg2Rad;Line(P(.5f+.43f*Mathf.Cos(a),.5f+.43f*Mathf.Sin(a)),P(.5f+.43f*Mathf.Cos(b),.5f+.43f*Mathf.Sin(b)),thickness);}return;}
            if(form==Form.Cross){Line(P(.28f,.28f),P(.72f,.72f),thickness);Line(P(.28f,.72f),P(.72f,.28f),thickness);return;}
            if(form==Form.Arrow){Line(P(.65f,.2f),P(.35f,.5f),thickness);Line(P(.35f,.5f),P(.65f,.8f),thickness);return;}
            if(form==Form.Check){Line(P(.18f,.52f),P(.4f,.28f),thickness);Line(P(.4f,.28f),P(.82f,.76f),thickness);return;}
            if(form==Form.Info||form==Form.Ring){for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;var p=P(.5f+.4f*Mathf.Cos(a),.5f+.4f*Mathf.Sin(a));var q=P(.5f+.4f*Mathf.Cos(b),.5f+.4f*Mathf.Sin(b));Line(p,q,thickness);}if(form==Form.Info){Line(P(.5f,.28f),P(.5f,.55f),thickness);Line(P(.5f,.68f),P(.5f,.72f),thickness);}return;}
            if(form==Form.Lock){Quad(P(.23f,.18f),P(.23f,.58f),P(.77f,.58f),P(.77f,.18f));for(int i=0;i<20;i++){float a=i*Mathf.PI/20,b=(i+1)*Mathf.PI/20;Line(P(.5f+.2f*Mathf.Cos(a),.58f+.25f*Mathf.Sin(a)),P(.5f+.2f*Mathf.Cos(b),.58f+.25f*Mathf.Sin(b)),thickness);}return;}
            if(form==Form.Diamond){Quad(P(.5f,0),P(0,.5f),P(.5f,1),P(1,.5f));return;}
            if(form==Form.Parallelogram){float a=Mathf.Min(cut,r.width*.2f);Quad(new Vector2(r.xMin,r.yMin),new Vector2(r.xMin+a,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax-a,r.yMin));return;}
            if(form==Form.Cut){float a=Mathf.Min(cut,Mathf.Min(r.width,r.height)*.25f);Vector2[] ps={new Vector2(r.xMin+a,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin+a),new Vector2(r.xMax-a,r.yMin),new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax-a)};for(int i=0;i<ps.Length;i++)vh.AddVert(ps[i],color,Vector2.zero);for(int i=1;i<ps.Length-1;i++)vh.AddTriangle(0,i,i+1);return;}
            Quad(new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin));
        }
    }

}
