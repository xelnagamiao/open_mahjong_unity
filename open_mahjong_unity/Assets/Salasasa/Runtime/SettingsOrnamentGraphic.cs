using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SettingsOrnamentGraphic : MaskableGraphic
    {
        public int style;
        public void Refresh(int value,Color tint){style=value;color=tint;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=GetPixelAdjustedRect();float w=r.width,h=r.height;
            if(style==0)
            {
                for(int i=0;i<12;i++)Line(mesh,new Vector2(r.xMax-10-i*13,r.yMax-10),new Vector2(r.xMax-36-i*13,r.yMax-36),1);
            }
            else if(style==1)
            {
                Line(mesh,new Vector2(r.xMin+24,r.yMin+10),new Vector2(r.xMax-24,r.yMin+10),1);
                Diamond(mesh,new Vector2(r.xMax-26,r.yMin+10),3);
            }
            else
            {
                Line(mesh,new Vector2(r.xMin+9,r.yMax-8),new Vector2(r.xMin+110,r.yMax-8),2);
                Line(mesh,new Vector2(r.xMin+9,r.yMin+8),new Vector2(r.xMax-9,r.yMin+8),1);
                Line(mesh,new Vector2(r.xMax-9,r.yMin+8),new Vector2(r.xMax-9,r.yMin+32),1);
            }
        }
        void Diamond(VertexHelper mesh,Vector2 center,float size)
        {int v=mesh.currentVertCount;Vertex(mesh,center+Vector2.up*size);Vertex(mesh,center+Vector2.left*size);Vertex(mesh,center+Vector2.down*size);Vertex(mesh,center+Vector2.right*size);mesh.AddTriangle(v,v+1,v+2);mesh.AddTriangle(v,v+2,v+3);}
        void Line(VertexHelper mesh,Vector2 a,Vector2 b,float width)
        {var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int v=mesh.currentVertCount;Vertex(mesh,a+n);Vertex(mesh,b+n);Vertex(mesh,b-n);Vertex(mesh,a-n);mesh.AddTriangle(v,v+1,v+2);mesh.AddTriangle(v,v+2,v+3);}
        void Vertex(VertexHelper mesh,Vector2 point){var v=UIVertex.simpleVert;v.position=point;v.color=color;mesh.AddVert(v);}
    }
}
