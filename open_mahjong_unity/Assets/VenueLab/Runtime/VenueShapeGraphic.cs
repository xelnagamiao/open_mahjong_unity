using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Venues
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class VenueShapeGraphic : MaskableGraphic
    {
        public enum Shape { Square, Round, Cut, Slant, JoinedSlant }
        [System.Flags] public enum Corners { None = 0, TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8, All = 15 }
        public Shape shape;
        public Corners corners = Corners.All;
        public float radius=8, borderWidth;
        public Color borderColor=Color.clear;
        readonly List<Vector2> outer=new List<Vector2>(40), inner=new List<Vector2>(40);

        public void Paint(Color fill,Color border,float stroke,Shape form,float rounding=8)
        {color=fill;borderColor=border;borderWidth=stroke;shape=form;radius=rounding;SetVerticesDirty();}

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var rect=GetPixelAdjustedRect();if(rect.width<=0||rect.height<=0)return;
            Outline(rect,0,outer);
            if(borderWidth>0)
            {
                Outline(rect,Mathf.Min(borderWidth,Mathf.Min(rect.width,rect.height)/2),inner);
                for(int i=0;i<outer.Count;i++)
                {
                    int n=(i+1)%outer.Count,v=mesh.currentVertCount;
                    Add(mesh,outer[i],borderColor);Add(mesh,outer[n],borderColor);Add(mesh,inner[n],borderColor);Add(mesh,inner[i],borderColor);
                    mesh.AddTriangle(v,v+1,v+2);mesh.AddTriangle(v,v+2,v+3);
                }
            }
            else {inner.Clear();inner.AddRange(outer);}
            int center=mesh.currentVertCount;Add(mesh,rect.center,color);
            for(int i=0;i<inner.Count;i++)Add(mesh,inner[i],color);
            for(int i=0;i<inner.Count;i++)mesh.AddTriangle(center,center+1+i,center+1+(i+1)%inner.Count);
        }
        static void Add(VertexHelper mesh,Vector2 position,Color tint)
        {var vertex=UIVertex.simpleVert;vertex.position=position;vertex.color=tint;mesh.AddVert(vertex);}
        void Outline(Rect rect,float inset,List<Vector2> points)
        {
            points.Clear();float l=rect.xMin+inset,r=rect.xMax-inset,b=rect.yMin+inset,t=rect.yMax-inset;
            float corner=Mathf.Clamp(radius-inset,0,Mathf.Min(r-l,t-b)*.5f);
            if(shape==Shape.Round)
            {
                Vector2[] centers={new Vector2(r-corner,t-corner),new Vector2(l+corner,t-corner),new Vector2(l+corner,b+corner),new Vector2(r-corner,b+corner)};
                for(int c=0;c<4;c++)for(int step=0;step<=8;step++)
                {float angle=(c*90+step*90f/8)*Mathf.Deg2Rad;points.Add(centers[c]+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*corner);}
            }
            else if(shape==Shape.Cut)
            {
                // Offset the diagonal by the same perpendicular distance as the straight edges.
                corner=Mathf.Clamp(radius-inset*(2-Mathf.Sqrt(2)),0,Mathf.Min(r-l,t-b)*.5f);
                float tl=(corners&Corners.TopLeft)!=0?corner:0, tr=(corners&Corners.TopRight)!=0?corner:0;
                float bl=(corners&Corners.BottomLeft)!=0?corner:0, br=(corners&Corners.BottomRight)!=0?corner:0;
                points.Add(new Vector2(r,t-tr));points.Add(new Vector2(r-tr,t));points.Add(new Vector2(l+tl,t));points.Add(new Vector2(l,t-tl));
                points.Add(new Vector2(l,b+bl));points.Add(new Vector2(l+bl,b));points.Add(new Vector2(r-br,b));points.Add(new Vector2(r,b+br));
            }
            else if(shape==Shape.JoinedSlant)
            {
                float tr=(corners&Corners.TopRight)!=0?corner:0, br=(corners&Corners.BottomRight)!=0?corner:0;
                points.Add(new Vector2(r,t-tr));points.Add(new Vector2(r-tr,t));points.Add(new Vector2(l+corner,t));
                points.Add(new Vector2(l,b));points.Add(new Vector2(r-br,b));points.Add(new Vector2(r,b+br));
            }
            else if(shape==Shape.Slant)
            {
                float slope=Mathf.Min(radius,rect.height*.5f)/rect.height;
                float offset=inset*Mathf.Sqrt(1+slope*slope);
                float shift=slope*inset,cut=slope*rect.height;
                points.Add(new Vector2(rect.xMax-shift-offset,t));
                points.Add(new Vector2(rect.xMin+cut-shift+offset,t));
                points.Add(new Vector2(rect.xMin+shift+offset,b));
                points.Add(new Vector2(rect.xMax-cut+shift-offset,b));
            }
            else {points.Add(new Vector2(r,t));points.Add(new Vector2(l,t));points.Add(new Vector2(l,b));points.Add(new Vector2(r,b));}
        }
    }

}

