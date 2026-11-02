using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.ChatLab
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ChatArtMotif : MaskableGraphic
    {
        public int style;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(style==0)return;var r=rectTransform.rect;
            void Line(Vector2 a,Vector2 b,float width,float alpha){
                var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
                var tint=color;tint.a*=alpha;int index=vh.currentVertCount;
                vh.AddVert(a-n,tint,Vector2.zero);vh.AddVert(a+n,tint,Vector2.zero);vh.AddVert(b+n,tint,Vector2.zero);vh.AddVert(b-n,tint,Vector2.zero);
                vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
            }
            if(style==1){
                Line(new Vector2(r.xMin,r.center.y-3),new Vector2(r.xMax-22,r.center.y-3),1,.55f);
                Line(new Vector2(r.xMin+24,r.center.y+2),new Vector2(r.xMax-22,r.center.y+2),1,.3f);
                var c=new Vector2(r.xMax-9,r.center.y);var d=new Vector2(7,0);var v=new Vector2(0,7);
                Line(c-d,c+v,1,.9f);Line(c+v,c+d,1,.9f);Line(c+d,c-v,1,.9f);Line(c-v,c-d,1,.9f);
            }
            if(style==2){
                Line(new Vector2(r.xMin,r.yMin+5),new Vector2(r.xMax-55,r.yMin+5),3,.8f);
                for(int i=0;i<5;i++)Line(new Vector2(r.xMax-49+i*10,r.yMin+5),new Vector2(r.xMax-42+i*10,r.yMax-5),3,.55f);
            }
            if(style==3){
                Line(new Vector2(r.xMin,r.yMin+3),new Vector2(r.xMax,r.yMin+3),1,.8f);
                for(int i=0;i<13;i++){float x=r.xMin+i*r.width/12;Line(new Vector2(x,r.yMin+3),new Vector2(x,r.yMin+(i%4==0?12:7)),1,.7f);}
                Line(new Vector2(r.xMax-24,r.yMax-4),new Vector2(r.xMax,r.yMax-4),3,.85f);
            }
        }
    }
}
