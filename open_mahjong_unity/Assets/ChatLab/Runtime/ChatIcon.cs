using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.ChatLab
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ChatIcon : MaskableGraphic
    {
        public enum Kind {Chat,Person,Search,Plus,Close,Menu,Mail,Bell,Settings,Arrow,Chevron,Send}
        public Kind kind;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;
            void Line(float ax,float ay,float bx,float by,float thick=1.7f){
                Vector2 a=new Vector2(r.xMin+ax*r.width,r.yMin+(1-ay)*r.height),b=new Vector2(r.xMin+bx*r.width,r.yMin+(1-by)*r.height);
                Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*thick*.5f;int i=vh.currentVertCount;
                vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);vh.AddVert(a-n,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
            }
            void Box(float l,float t,float rr,float b){Line(l,t,rr,t);Line(rr,t,rr,b);Line(rr,b,l,b);Line(l,b,l,t);}
            void Circle(float x,float y,float radius){for(int i=0;i<20;i++){float a=i*Mathf.PI/10,b=(i+1)*Mathf.PI/10;Line(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius);}}
            switch(kind){
                case Kind.Close:Line(.2f,.2f,.8f,.8f);Line(.8f,.2f,.2f,.8f);break;
                case Kind.Plus:Line(.15f,.5f,.85f,.5f);Line(.5f,.15f,.5f,.85f);break;
                case Kind.Search:Circle(.42f,.42f,.27f);Line(.63f,.63f,.88f,.88f);break;
                case Kind.Person:Circle(.5f,.28f,.19f);Line(.15f,.84f,.2f,.62f);Line(.2f,.62f,.5f,.52f);Line(.5f,.52f,.8f,.62f);Line(.8f,.62f,.85f,.84f);Line(.15f,.84f,.85f,.84f);break;
                case Kind.Chat:Box(.12f,.16f,.88f,.68f);Line(.24f,.68f,.24f,.88f);Line(.24f,.88f,.44f,.68f);Line(.28f,.34f,.72f,.34f);Line(.28f,.48f,.6f,.48f);break;
                case Kind.Mail:Box(.1f,.24f,.9f,.8f);Line(.1f,.24f,.5f,.58f);Line(.5f,.58f,.9f,.24f);break;
                case Kind.Bell:Line(.2f,.7f,.28f,.3f);Line(.28f,.3f,.5f,.15f);Line(.5f,.15f,.72f,.3f);Line(.72f,.3f,.8f,.7f);Line(.18f,.7f,.82f,.7f);Line(.4f,.85f,.6f,.85f);break;
                case Kind.Settings:Circle(.5f,.5f,.28f);Circle(.5f,.5f,.1f);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;Line(.5f+Mathf.Cos(a)*.28f,.5f+Mathf.Sin(a)*.28f,.5f+Mathf.Cos(a)*.42f,.5f+Mathf.Sin(a)*.42f);}break;
                case Kind.Arrow:Line(.15f,.5f,.85f,.5f);Line(.15f,.5f,.44f,.2f);Line(.15f,.5f,.44f,.8f);break;
                case Kind.Chevron:Line(.2f,.35f,.5f,.65f);Line(.5f,.65f,.8f,.35f);break;
                case Kind.Send:Line(.1f,.2f,.9f,.5f);Line(.9f,.5f,.1f,.8f);Line(.1f,.8f,.29f,.5f);Line(.29f,.5f,.1f,.2f);Line(.29f,.5f,.9f,.5f);break;
                default:Line(.18f,.25f,.82f,.25f);Line(.18f,.5f,.82f,.5f);Line(.18f,.75f,.82f,.75f);break;
            }
        }
    }
}
