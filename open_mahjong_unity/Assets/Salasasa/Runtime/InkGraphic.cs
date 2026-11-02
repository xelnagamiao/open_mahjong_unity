using UnityEngine;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InkGraphic : UnityEngine.UI.MaskableGraphic
    {
        public enum Form { Disc, Ring, Arrow, Spark, User, Trophy, History, Friends, Settings, Grid, Close, Mail, Sound, Eye, Diamond, Chevron, Arc, Lines, Info, Bell, Chart }
        public Form form;
        public float stroke = 2f;
        public int segments = 64;

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 P(float x, float y) => new Vector2(r.xMin + x * r.width, r.yMax - y * r.height);
            void L(float x, float y, float a, float b) => Line(vh, P(x,y), P(a,b), stroke);
            void Circle(float x, float y, float radius, bool fill)
            {
                Vector2 center = P(x,y);
                float rad = radius * Mathf.Min(r.width, r.height);
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments;
                    float b = (i + 1) * Mathf.PI * 2 / segments;
                    Vector2 p = center + new Vector2(Mathf.Cos(a),Mathf.Sin(a))*rad;
                    Vector2 q = center + new Vector2(Mathf.Cos(b),Mathf.Sin(b))*rad;
                    if (fill) Tri(vh, center, p, q); else Line(vh,p,q,stroke);
                }
            }
            switch (form)
            {
                case Form.Chart:
                    Line(vh,P(.18f,.88f),P(.18f,.52f),r.width*.18f);
                    Line(vh,P(.50f,.88f),P(.50f,.16f),r.width*.18f);
                    Line(vh,P(.82f,.88f),P(.82f,.36f),r.width*.18f);
                    break;
                case Form.Bell: L(.18f,.73f,.82f,.73f);L(.18f,.73f,.28f,.58f);L(.28f,.58f,.28f,.36f);L(.28f,.36f,.38f,.22f);L(.38f,.22f,.62f,.22f);L(.62f,.22f,.72f,.36f);L(.72f,.36f,.72f,.58f);L(.72f,.58f,.82f,.73f);Circle(.5f,.16f,.035f,false);L(.4f,.83f,.6f,.83f);break;
                case Form.Disc: Circle(.5f,.5f,.5f,true); break;
                case Form.Ring: Circle(.5f,.5f,.48f,false); break;
                case Form.Arrow: L(.15f,.5f,.85f,.5f);L(.58f,.23f,.85f,.5f);L(.58f,.77f,.85f,.5f);break;
                case Form.Chevron: L(.32f,.18f,.68f,.5f);L(.32f,.82f,.68f,.5f);break;
                case Form.Spark: L(.5f,.08f,.5f,.92f);L(.08f,.5f,.92f,.5f);L(.22f,.22f,.78f,.78f);L(.22f,.78f,.78f,.22f);break;
                case Form.User: Circle(.5f,.3f,.16f,false);L(.2f,.85f,.24f,.64f);L(.24f,.64f,.42f,.54f);L(.42f,.54f,.58f,.54f);L(.58f,.54f,.76f,.64f);L(.76f,.64f,.8f,.85f);break;
                case Form.Friends: Circle(.36f,.31f,.14f,false);Circle(.72f,.38f,.12f,false);L(.12f,.82f,.16f,.62f);L(.16f,.62f,.36f,.55f);L(.36f,.55f,.57f,.62f);L(.57f,.62f,.6f,.82f);L(.65f,.61f,.83f,.68f);L(.83f,.68f,.87f,.82f);break;
                case Form.Trophy: L(.3f,.15f,.7f,.15f);L(.3f,.15f,.34f,.55f);L(.7f,.15f,.66f,.55f);L(.34f,.55f,.5f,.65f);L(.66f,.55f,.5f,.65f);L(.5f,.65f,.5f,.85f);L(.32f,.85f,.68f,.85f);L(.3f,.25f,.13f,.25f);L(.13f,.25f,.17f,.46f);L(.17f,.46f,.34f,.55f);L(.7f,.25f,.87f,.25f);L(.87f,.25f,.83f,.46f);L(.83f,.46f,.66f,.55f);break;
                case Form.History: Circle(.52f,.5f,.33f,false);L(.52f,.28f,.52f,.53f);L(.52f,.53f,.69f,.61f);L(.1f,.2f,.1f,.43f);L(.1f,.43f,.31f,.43f);break;
                case Form.Settings: Circle(.5f,.5f,.17f,false);Circle(.5f,.5f,.32f,false);for(int i=0;i<8;i++){float a=i*Mathf.PI/4;L(.5f+Mathf.Cos(a)*.32f,.5f+Mathf.Sin(a)*.32f,.5f+Mathf.Cos(a)*.43f,.5f+Mathf.Sin(a)*.43f);}break;
                case Form.Grid: for(int i=0;i<2;i++)for(int j=0;j<2;j++){float x=.16f+i*.4f,y=.16f+j*.4f;L(x,y,x+.24f,y);L(x+.24f,y,x+.24f,y+.24f);L(x+.24f,y+.24f,x,y+.24f);L(x,y+.24f,x,y);}break;
                case Form.Close: L(.25f,.25f,.75f,.75f);L(.75f,.25f,.25f,.75f);break;
                case Form.Info: Circle(.5f,.5f,.38f,false);Circle(.5f,.3f,.04f,true);L(.5f,.44f,.5f,.72f);break;
                case Form.Mail: L(.12f,.25f,.88f,.25f);L(.88f,.25f,.88f,.75f);L(.88f,.75f,.12f,.75f);L(.12f,.75f,.12f,.25f);L(.12f,.25f,.5f,.55f);L(.5f,.55f,.88f,.25f);break;
                case Form.Sound: L(.15f,.4f,.32f,.4f);L(.15f,.4f,.15f,.65f);L(.15f,.65f,.32f,.65f);L(.32f,.4f,.55f,.2f);L(.55f,.2f,.55f,.8f);L(.55f,.8f,.32f,.65f);L(.7f,.35f,.78f,.5f);L(.78f,.5f,.7f,.66f);break;
                case Form.Eye: L(.08f,.5f,.28f,.3f);L(.28f,.3f,.72f,.3f);L(.72f,.3f,.92f,.5f);L(.92f,.5f,.72f,.7f);L(.72f,.7f,.28f,.7f);L(.28f,.7f,.08f,.5f);Circle(.5f,.5f,.11f,false);break;
                case Form.Diamond: L(.5f,.08f,.92f,.5f);L(.92f,.5f,.5f,.92f);L(.5f,.92f,.08f,.5f);L(.08f,.5f,.5f,.08f);break;
                case Form.Lines: L(.1f,.25f,.9f,.25f);L(.1f,.5f,.72f,.5f);L(.1f,.75f,.9f,.75f);break;
                case Form.Arc: for(int i=0;i<segments;i++){float a=(.1f+i*.8f/segments)*Mathf.PI;float b=(.1f+(i+1)*.8f/segments)*Mathf.PI;Line(vh,P(.5f+Mathf.Cos(a)*.47f,.6f-Mathf.Sin(a)*.47f),P(.5f+Mathf.Cos(b)*.47f,.6f-Mathf.Sin(b)*.47f),stroke);}break;
            }
        }
        void Tri(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,Vector2 c)
        {
            int i=vh.currentVertCount;vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);
        }
        void Line(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,float width)
        {
            Vector2 d=b-a;if(d.sqrMagnitude<.0001f)return;
            Vector2 n=new Vector2(-d.y,d.x).normalized*width*.5f;
            int i=vh.currentVertCount;vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
