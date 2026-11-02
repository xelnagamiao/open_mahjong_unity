using UnityEngine;
using UnityEngine.UI;
namespace Salasasa.ChatLab
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ChatSurface : UnityEngine.UI.Image
    {
        public int silhouette;
        public float cut=9, stroke;
        public Color edge;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            void Polygon(Rect box,Color tint){
                float c=Mathf.Min(cut,Mathf.Min(box.width,box.height)/3);Vector2[] points;
                if(silhouette==1)points=new[]{new Vector2(box.xMin+c,box.yMax),new Vector2(box.xMax,box.yMax),new Vector2(box.xMax,box.yMin+c),new Vector2(box.xMax-c,box.yMin),new Vector2(box.xMin,box.yMin),new Vector2(box.xMin,box.yMax-c)};
                else if(silhouette==2)points=new[]{new Vector2(box.xMin+c,box.yMax),new Vector2(box.xMax,box.yMax),new Vector2(box.xMax-c,box.yMin),new Vector2(box.xMin,box.yMin)};
                else points=new[]{new Vector2(box.xMin,box.yMax),new Vector2(box.xMax,box.yMax),new Vector2(box.xMax,box.yMin),new Vector2(box.xMin,box.yMin)};
                int start=vh.currentVertCount;foreach(var p in points)vh.AddVert(p,tint,Vector2.zero);for(int i=1;i<points.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);
            }
            if(stroke>0){Polygon(r,edge);r.xMin+=stroke;r.xMax-=stroke;r.yMin+=stroke;r.yMax-=stroke;}
            Polygon(r,color);
        }
    }
}
