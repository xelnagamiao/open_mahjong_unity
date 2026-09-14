using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SceneSettingsSidebarIcon : MaskableGraphic
    {
        public int kind;
        VertexHelper mesh;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();mesh=vh;
            switch(kind)
            {
                case 9:
                    for(int i=0;i<36;i++){float a=(-140+i*8)*Mathf.Deg2Rad,b=(-140+(i+1)*8)*Mathf.Deg2Rad;Stroke(20+Mathf.Cos(a)*12,20+Mathf.Sin(a)*12,20+Mathf.Cos(b)*12,20+Mathf.Sin(b)*12);}
                    Stroke(10,4,10,13);Stroke(10,13,19,13);break;
                case 0:
                    Stroke(5,12,30,7);Stroke(30,7,35,28);Stroke(35,28,10,33);Stroke(10,33,5,12);Stroke(10,27,29,23);break;
                case 1: Frame(6,7,28,26,2);Stroke(12,13,28,13);Stroke(12,13,12,27);Stroke(12,27,28,27);Stroke(28,13,28,27);break;
                case 2: Frame(8,8,24,24,2);Ring(20,20,6,2);Stroke(20,3,20,8);Stroke(20,32,20,37);Stroke(3,20,8,20);Stroke(32,20,37,20);break;
                case 3: Frame(13,5,20,28,2);Stroke(7,11,7,37);Stroke(7,37,27,37);Disk(23,13,2);Disk(23,20,2);Disk(23,27,2);break;
                case 4: Stroke(6,12,20,5);Stroke(20,5,34,12);Stroke(34,12,20,20);Stroke(20,20,6,12);Stroke(6,12,6,29);Stroke(6,29,20,36);Stroke(20,36,34,29);Stroke(34,29,34,12);Stroke(20,20,20,36);break;
                case 5: Ring(20,11,6,2);Stroke(6,35,7,29);Stroke(7,29,12,24);Stroke(12,24,28,24);Stroke(28,24,33,29);Stroke(33,29,34,35);Stroke(6,35,34,35);break;
                case 6: Stroke(5,10,12,10);Stroke(12,10,27,30);Stroke(27,30,35,30);Stroke(5,30,12,30);Stroke(12,30,27,10);Stroke(27,10,35,10);Poly(new Vector2(31,5),new Vector2(38,10),new Vector2(31,15));Poly(new Vector2(31,25),new Vector2(38,30),new Vector2(31,35));break;
                default: Stroke(3,20,10,12);Stroke(10,12,20,9);Stroke(20,9,30,12);Stroke(30,12,37,20);Stroke(37,20,30,28);Stroke(30,28,20,31);Stroke(20,31,10,28);Stroke(10,28,3,20);Ring(20,20,5,2);if(kind==7)Stroke(6,35,34,5,2.5f);break;
            }
        }
        Vector2 Point(Vector2 p) {var r=rectTransform.rect;return new Vector2(r.xMin+p.x/40*r.width,r.yMax-p.y/40*r.height);}
        void Poly(params Vector2[] p){int k=mesh.currentVertCount;foreach(var v in p)mesh.AddVert(Point(v),color,Vector2.zero);for(int i=1;i<p.Length-1;i++)mesh.AddTriangle(k,k+i,k+i+1);}
        void Rect(float x,float y,float w,float h)=>Poly(new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h));
        void Stroke(float x,float y,float u,float v,float t=2){var a=new Vector2(x,y);var b=new Vector2(u,v);var n=new Vector2(-(b-a).y,(b-a).x).normalized*t*.5f;Poly(a-n,a+n,b+n,b-n);}
        void Frame(float x,float y,float w,float h,float t){Rect(x,y,w,t);Rect(x,y+h-t,w,t);Rect(x,y,t,h);Rect(x+w-t,y,t,h);}
        void Ring(float x,float y,float r,float t){for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Stroke(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r,x+Mathf.Cos(b)*r,y+Mathf.Sin(b)*r,t);}}
        void Disk(float x,float y,float r){var p=new Vector2[32];for(int i=0;i<p.Length;i++){float a=i*Mathf.PI/16;p[i]=new Vector2(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r);}Poly(p);}
    }
}
