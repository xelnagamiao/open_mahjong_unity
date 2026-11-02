using UnityEngine;

namespace Salasasa.Community {
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CommunityGlyph : UnityEngine.UI.MaskableGraphic {
        public enum Symbol { Community, QQ, Wechat, Telegram, Discord, Close }
        public Symbol symbol;
        public float stroke = 2.5f;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh) {
            vh.Clear(); var r = GetPixelAdjustedRect();
            Vector2 Point(float x, float y) => new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
            void Line(Vector2 a, Vector2 b) { var n = new Vector2(-(b-a).y, (b-a).x).normalized * stroke * .5f; int start = vh.currentVertCount; foreach(var p in new[]{a-n,a+n,b+n,b-n}) vh.AddVert(p,color,Vector2.zero); vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3); }
            void Path(bool close, params Vector2[] points) { for(int i=1;i<points.Length;i++) Line(points[i-1],points[i]); if(close)Line(points[points.Length-1],points[0]); }
            void Circle(float x,float y,float radius) { var points=new Vector2[20]; for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2/points.Length;points[i]=Point(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius);}Path(true,points); }
            switch(symbol) {
                case Symbol.Community:
                    Path(true,Point(.08f,.8f),Point(.72f,.8f),Point(.72f,.34f),Point(.38f,.34f),Point(.2f,.18f),Point(.2f,.34f),Point(.08f,.34f));
                    Path(false,Point(.83f,.63f),Point(.94f,.63f),Point(.94f,.14f),Point(.8f,.14f),Point(.8f,.05f),Point(.64f,.14f),Point(.45f,.14f));
                    Circle(.28f,.58f,.025f);Circle(.5f,.58f,.025f);break;
                case Symbol.Telegram:
                    Path(true,Point(.07f,.5f),Point(.94f,.9f),Point(.76f,.1f),Point(.49f,.32f),Point(.35f,.18f),Point(.39f,.42f),Point(.76f,.73f),Point(.25f,.46f));break;
                case Symbol.Discord:
                    Path(true,Point(.17f,.72f),Point(.36f,.78f),Point(.41f,.68f),Point(.59f,.68f),Point(.64f,.78f),Point(.83f,.72f),Point(.94f,.23f),Point(.76f,.14f),Point(.65f,.26f),Point(.35f,.26f),Point(.24f,.14f),Point(.06f,.23f));Circle(.36f,.46f,.06f);Circle(.64f,.46f,.06f);break;
                case Symbol.Wechat:
                    Path(false,Point(.55f,.83f),Point(.28f,.85f),Point(.09f,.72f),Point(.05f,.52f),Point(.19f,.36f),Point(.15f,.2f),Point(.31f,.29f));
                    Path(true,Point(.42f,.65f),Point(.69f,.7f),Point(.92f,.56f),Point(.94f,.34f),Point(.81f,.2f),Point(.84f,.08f),Point(.65f,.17f),Point(.42f,.2f),Point(.29f,.37f),Point(.31f,.53f));Circle(.48f,.45f,.025f);Circle(.74f,.45f,.025f);break;
                case Symbol.QQ:
                    Path(true,Point(.28f,.62f),Point(.27f,.8f),Point(.38f,.91f),Point(.62f,.91f),Point(.73f,.8f),Point(.72f,.62f),Point(.86f,.28f),Point(.73f,.3f),Point(.73f,.12f),Point(.59f,.09f),Point(.5f,.2f),Point(.41f,.09f),Point(.27f,.12f),Point(.27f,.3f),Point(.14f,.28f));Line(Point(.3f,.49f),Point(.7f,.49f));Circle(.42f,.73f,.02f);Circle(.58f,.73f,.02f);break;
                case Symbol.Close:Line(Point(.2f,.2f),Point(.8f,.8f));Line(Point(.2f,.8f),Point(.8f,.2f));break;
            }
        }
    }
}
