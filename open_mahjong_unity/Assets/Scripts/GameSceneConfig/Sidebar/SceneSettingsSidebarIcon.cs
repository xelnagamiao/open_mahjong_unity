using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SceneSettingsSidebarIcon : MaskableGraphic
    {
        public int kind;
        public bool useRedesign;
        public int family;
        public Color negativeColor,secondaryColor;
        float symbolScale=1;
        Color ink,symbolColor;
        VertexHelper mesh;
        public void SetDetailColors(Color background,Color secondary)
        {
            background.a=1;
            if(negativeColor==background&&secondaryColor==secondary)return;
            negativeColor=background;secondaryColor=secondary;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();mesh=vh;symbolScale=1;ink=color;
            if(kind==9){DrawSmoothReset();return;}
            if(useRedesign){symbolColor=color;symbolColor.a=1;DrawRedesign();return;}
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
        void DrawSmoothReset()
        {
            const int segments=96;
            const float radius=12,half=2.1f;
            float scale=Mathf.Max(.01f,Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)/40f*(canvas!=null?canvas.scaleFactor:1));
            float feather=1f/scale;
            int start=mesh.currentVertCount;
            for(int i=0;i<=segments;i++)
            {
                float angle=Mathf.Lerp(-135,155,i/(float)segments)*Mathf.Deg2Rad;
                var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                var center=new Vector2(20,20);
                SoftVertex(center+direction*(radius-half-feather),0);
                SoftVertex(center+direction*(radius-half),1);
                SoftVertex(center+direction*(radius+half),1);
                SoftVertex(center+direction*(radius+half+feather),0);
            }
            for(int i=0;i<segments;i++)for(int band=0;band<3;band++)
            {
                int a=start+i*4+band,b=a+4;
                mesh.AddTriangle(a,b,a+1);mesh.AddTriangle(a+1,b,b+1);
            }
            float tailAngle=155*Mathf.Deg2Rad;
            SoftDisk(new Vector2(20+Mathf.Cos(tailAngle)*radius,20+Mathf.Sin(tailAngle)*radius),half,feather);
            float headAngle=-135*Mathf.Deg2Rad;
            var anchor=new Vector2(20+Mathf.Cos(headAngle)*radius,20+Mathf.Sin(headAngle)*radius);
            var tangent=new Vector2(Mathf.Sin(headAngle),-Mathf.Cos(headAngle));
            var normal=new Vector2(-tangent.y,tangent.x);
            SoftTriangle(anchor+tangent*5,anchor-tangent*2+normal*4,anchor-tangent*2-normal*4,feather);
        }
        void SoftVertex(Vector2 point,float alpha)
        {
            var vertexColor=color;vertexColor.a*=alpha;mesh.AddVert(Point(point),vertexColor,Vector2.zero);
        }
        void SoftDisk(Vector2 center,float radius,float feather)
        {
            const int segments=32;int start=mesh.currentVertCount;SoftVertex(center,1);
            for(int i=0;i<=segments;i++)
            {
                float angle=i*Mathf.PI*2/segments;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                SoftVertex(center+direction*radius,1);SoftVertex(center+direction*(radius+feather),0);
            }
            for(int i=0;i<segments;i++)
            {
                int a=start+1+i*2,b=a+2;mesh.AddTriangle(start,a,b);mesh.AddTriangle(a,a+1,b);mesh.AddTriangle(a+1,b+1,b);
            }
        }
        void SoftTriangle(Vector2 a,Vector2 b,Vector2 c,float feather)
        {
            var points=new[]{a,b,c};float signed=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
            float orientation=signed>=0?1:-1;int start=mesh.currentVertCount;
            for(int i=0;i<3;i++)
            {
                var previous=(points[i]-points[(i+2)%3]).normalized;var next=(points[(i+1)%3]-points[i]).normalized;
                var n0=new Vector2(previous.y,-previous.x)*orientation;var n1=new Vector2(next.y,-next.x)*orientation;
                var outward=(n0+n1).normalized;float distance=feather/Mathf.Max(.25f,Vector2.Dot(outward,n1));
                SoftVertex(points[i],1);SoftVertex(points[i]+outward*distance,0);
            }
            mesh.AddTriangle(start,start+2,start+4);
            for(int i=0;i<3;i++)
            {
                int a0=start+i*2,b0=start+((i+1)%3)*2;mesh.AddTriangle(a0,b0,a0+1);mesh.AddTriangle(a0+1,b0,b0+1);
            }
        }

        void DrawRedesign()
        {
            if(family==0){DrawOriginal();return;}
            ink=symbolColor;
            if(kind>=6){DrawActions(family==4);return;}
            if(family==2){DrawOutline();return;}
            if(family==3){DrawSquare();return;}
            if(family==4){DrawRound();return;}
            switch(kind)
            {
                case 0:Poly(new Vector2(4,10),new Vector2(30,5),new Vector2(36,30),new Vector2(10,35));break;
                case 1:Frame(4,4,32,32,6);break;
                case 2:Rect(5,5,10,10);Rect(25,5,10,10);Rect(5,25,10,10);Rect(25,25,10,10);Disk(20,20,5);break;
                case 3:Rect(4,5,30,11);Rect(30,15,4,9);Rect(18,21,16,4);Rect(16,24,7,14);break;
                case 4:Rect(14,10,22,28);Rect(4,3,5,28);Rect(9,3,20,4);break;
            }
        }
        void DrawOriginal()
        {
            ink=symbolColor;
            Color detail=Color.Lerp(symbolColor,negativeColor,.52f);
            switch(kind)
            {
                case 0:
                    Poly(new Vector2(4,10),new Vector2(29,5),new Vector2(36,29),new Vector2(10,35));
                    ink=detail;Poly(new Vector2(29,5),new Vector2(32,17),new Vector2(21,12));
                    Poly(new Vector2(9,28),new Vector2(34,23),new Vector2(36,29),new Vector2(10,35));break;
                case 1:
                    Rect(4,5,14,6);Rect(4,5,6,14);Rect(22,5,14,6);Rect(30,5,6,14);
                    ink=symbolColor;Rect(4,29,14,6);Rect(4,21,6,14);Rect(22,29,14,6);Rect(30,21,6,14);break;
                case 2:
                    Disk(20,20,12);Rect(17,2,6,7);Rect(17,31,6,7);Rect(2,17,7,6);Rect(31,17,7,6);
                    ink=negativeColor;Disk(20,20,6);ink=symbolColor;Disk(20,20,3);break;
                case 3:
                    Rect(4,7,22,29);ink=negativeColor;Rect(8,11,14,15);
                    ink=detail;Poly(new Vector2(17,27),new Vector2(32,4),new Vector2(37,8),new Vector2(22,31));
                    ink=symbolColor;Poly(new Vector2(17,27),new Vector2(22,31),new Vector2(14,36));break;
                case 4:
                    Poly(new Vector2(20,3),new Vector2(37,12),new Vector2(20,21),new Vector2(3,12));
                    ink=detail;Poly(new Vector2(3,15),new Vector2(18,23),new Vector2(18,38),new Vector2(3,30));
                    ink=Color.Lerp(symbolColor,negativeColor,.25f);
                    Poly(new Vector2(22,23),new Vector2(37,15),new Vector2(37,30),new Vector2(22,38));break;
                case 6:
                    ink=detail;Rect(3,4,23,23);ink=negativeColor;Disk(9,10,2.2f);Disk(20,21,2.2f);
                    ink=symbolColor;Rect(14,15,23,23);ink=negativeColor;Disk(20,21,2.3f);Disk(25.5f,26.5f,2.3f);Disk(31,32,2.3f);break;
                case 7:case 8:
                    Rect(3,6,34,5);Rect(3,11,5,23);Rect(32,11,5,23);Rect(3,29,29,5);
                    ink=detail;Rect(11,14,5,12);ink=symbolColor;
                    if(kind==7)Poly(new Vector2(20,14),new Vector2(20,26),new Vector2(13,20));
                    else Poly(new Vector2(22,14),new Vector2(29,20),new Vector2(22,26));break;
                default:
                    for(int i=0;i<32;i++){float a=(-145+i*8)*Mathf.Deg2Rad,b=(-145+(i+1)*8)*Mathf.Deg2Rad;Stroke(21+Mathf.Cos(a)*12,21+Mathf.Sin(a)*12,21+Mathf.Cos(b)*12,21+Mathf.Sin(b)*12,5);}
                    Poly(new Vector2(4,5),new Vector2(17,8),new Vector2(7,18));break;
            }
        }
        void DrawOutline()
        {
            switch(kind)
            {
                case 0:Stroke(5,10,30,6,3.5f);Stroke(30,6,35,30,3.5f);Stroke(35,30,10,34,3.5f);Stroke(10,34,5,10,3.5f);break;
                case 1:Frame(5,5,30,30,3.5f);break;
                case 2:Frame(9,9,22,22,3.5f);Stroke(20,3,20,9,3.5f);Stroke(20,31,20,37,3.5f);Stroke(3,20,9,20,3.5f);Stroke(31,20,37,20,3.5f);Disk(20,20,3);break;
                case 3:Frame(4,5,29,11,3);Stroke(33,13,36,13,3);Stroke(36,13,36,23,3);Stroke(36,23,20,23,3);Frame(17,23,6,14,3);break;
                case 4:Frame(13,10,23,27,3.5f);Stroke(5,31,5,3,3.5f);Stroke(5,3,28,3,3.5f);break;
            }
        }
        void DrawSquare()
        {
            switch(kind)
            {
                case 0:Rect(4,7,32,23);Rect(4,30,7,5);Rect(16,30,8,5);Rect(29,30,7,5);break;
                case 1:Rect(4,4,13,6);Rect(4,10,6,7);Rect(23,4,13,6);Rect(30,10,6,7);Rect(4,23,6,7);Rect(4,30,13,6);Rect(30,23,6,7);Rect(23,30,13,6);break;
                case 2:Rect(5,5,13,13);Rect(22,5,13,13);Rect(5,22,13,13);Rect(22,22,13,13);break;
                case 3:Rect(5,4,30,12);Rect(29,16,6,10);Rect(17,22,12,5);Rect(15,27,7,11);break;
                case 4:Frame(12,12,25,25,5);Rect(3,3,27,5);Rect(3,8,5,22);Rect(21,21,7,7);break;
            }
        }
        void DrawRound()
        {
            switch(kind)
            {
                case 0:RoundBox(4,7,32,27,4);break;
                case 1:RoundStroke(7,7,33,7,5);RoundStroke(33,7,33,33,5);RoundStroke(33,33,7,33,5);RoundStroke(7,33,7,7,5);break;
                case 2:Disk(20,20,10);RoundStroke(20,4,20,10,5);RoundStroke(20,30,20,36,5);RoundStroke(4,20,10,20,5);RoundStroke(30,20,36,20,5);break;
                case 3:RoundBox(4,5,30,12,4);RoundStroke(33,14,36,14,3);RoundStroke(36,14,36,23,3);RoundStroke(36,23,20,23,3);RoundBox(17,23,7,15,3);break;
                case 4:RoundBox(14,10,22,28,3);RoundStroke(5,29,5,5,4);RoundStroke(5,5,28,5,4);break;
            }
        }
        void DrawActions(bool rounded)
        {
            float t=family==2?3.2f:4.5f;
            if(kind==6)
            {
                Segment(4,10,11,10,t,rounded);Segment(11,10,28,30,t,rounded);Segment(28,30,34,30,t,rounded);
                Segment(4,30,11,30,t,rounded);Segment(11,30,28,10,t,rounded);Segment(28,10,34,10,t,rounded);
                Poly(new Vector2(29,3),new Vector2(38,10),new Vector2(29,17));Poly(new Vector2(29,23),new Vector2(38,30),new Vector2(29,37));
            }
            else if(kind==7||kind==8)
            {
                Frame(4,5,32,30,t);Rect(8,9,5,22);
                if(kind==7)Poly(new Vector2(25,12),new Vector2(17,20),new Vector2(25,28));
                else Poly(new Vector2(19,12),new Vector2(27,20),new Vector2(19,28));
            }
            else
            {
                for(int i=0;i<30;i++){float a=(-140+i*9)*Mathf.Deg2Rad,b=(-140+(i+1)*9)*Mathf.Deg2Rad;Segment(21+Mathf.Cos(a)*12,21+Mathf.Sin(a)*12,21+Mathf.Cos(b)*12,21+Mathf.Sin(b)*12,t,rounded);}
                Poly(new Vector2(4,4),new Vector2(17,7),new Vector2(6,17));
            }
        }
        void Segment(float x,float y,float u,float v,float t,bool rounded){if(rounded)RoundStroke(x,y,u,v,t);else Stroke(x,y,u,v,t);}
        void RoundStroke(float x,float y,float u,float v,float t){Stroke(x,y,u,v,t);Disk(x,y,t*.5f);Disk(u,v,t*.5f);}
        void RoundBox(float x,float y,float w,float h,float radius)
        {
            var points=new Vector2[24];
            for(int corner=0;corner<4;corner++)
            {
                var center=new Vector2(corner<2?x+w-radius:x+radius,corner==0||corner==3?y+radius:y+h-radius);
                for(int k=0;k<6;k++){float angle=(-90+corner*90+k*18)*Mathf.Deg2Rad;points[corner*6+k]=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;}
            }
            Poly(points);
        }
        Vector2 Point(Vector2 p) {var r=rectTransform.rect;p=(p-Vector2.one*20)*symbolScale+Vector2.one*20;return new Vector2(r.xMin+p.x/40*r.width,r.yMax-p.y/40*r.height);}
        void Poly(params Vector2[] p){int k=mesh.currentVertCount;var vertexColor=ink;if(useRedesign)vertexColor.a*=color.a;foreach(var v in p)mesh.AddVert(Point(v),vertexColor,Vector2.zero);for(int i=1;i<p.Length-1;i++)mesh.AddTriangle(k,k+i,k+i+1);}
        void Rect(float x,float y,float w,float h)=>Poly(new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h));
        void Stroke(float x,float y,float u,float v,float t=2){var a=new Vector2(x,y);var b=new Vector2(u,v);var n=new Vector2(-(b-a).y,(b-a).x).normalized*t*.5f;Poly(a-n,a+n,b+n,b-n);}
        void Frame(float x,float y,float w,float h,float t){Rect(x,y,w,t);Rect(x,y+h-t,w,t);Rect(x,y,t,h);Rect(x+w-t,y,t,h);}
        void Ring(float x,float y,float r,float t){for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Stroke(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r,x+Mathf.Cos(b)*r,y+Mathf.Sin(b)*r,t);}}
        void Disk(float x,float y,float r){var p=new Vector2[32];for(int i=0;i<p.Length;i++){float a=i*Mathf.PI/16;p[i]=new Vector2(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r);}Poly(p);}
    }
}
