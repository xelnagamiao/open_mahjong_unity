using UnityEngine;


    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InventoryUiGlyph : UnityEngine.UI.MaskableGraphic {
        public enum Symbol { Bag, Title, Portrait, Frame, Close, Avatar }
        public Symbol symbol;
        public float cut = 12, stroke = 2;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh) {
            vh.Clear(); var r = GetPixelAdjustedRect();
            float w=r.width,h=r.height,l=r.xMin,b=r.yMin;
            void Poly(params Vector2[] pts) { int n=vh.currentVertCount; foreach(var p in pts) vh.AddVert(new Vector2(l+p.x*w,b+p.y*h),color,Vector2.zero); for(int i=1;i<pts.Length-1;i++)vh.AddTriangle(n,n+i,n+i+1); }
            void Box(float x,float y,float a,float z) { Poly(new Vector2(x,y),new Vector2(x+a,y),new Vector2(x+a,y+z),new Vector2(x,y+z)); }
            void Line(Vector2 a,Vector2 z,float thickness) { var aa=new Vector2(a.x*w,a.y*h);var zz=new Vector2(z.x*w,z.y*h);var normal=new Vector2(-(zz-aa).y,(zz-aa).x).normalized*thickness*.5f; int n=vh.currentVertCount;foreach(var p in new[]{aa-normal,aa+normal,zz+normal,zz-normal})vh.AddVert(new Vector2(l,b)+p,color,Vector2.zero);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3); }
            float sx=stroke/w,sy=stroke/h;
            switch(symbol) {
                case Symbol.Close:Line(new Vector2(.26f,.26f),new Vector2(.74f,.74f),stroke);Line(new Vector2(.26f,.74f),new Vector2(.74f,.26f),stroke);break;
                case Symbol.Bag:Box(.17f,.13f,.66f,.55f);Box(.31f,.68f,.12f,.16f);Box(.57f,.68f,.12f,.16f);Box(.31f,.78f,.38f,.09f);break;
                case Symbol.Title: Poly(new Vector2(.25f,.40f),new Vector2(.38f,.08f),new Vector2(.50f,.23f),new Vector2(.62f,.08f),new Vector2(.75f,.40f));Poly(new Vector2(.16f,.44f),new Vector2(.16f,.76f),new Vector2(.32f,.9f),new Vector2(.68f,.9f),new Vector2(.84f,.76f),new Vector2(.84f,.44f));break;
                case Symbol.Portrait: Poly(new Vector2(.33f,.57f),new Vector2(.30f,.76f),new Vector2(.40f,.89f),new Vector2(.60f,.89f),new Vector2(.70f,.76f),new Vector2(.67f,.57f));Poly(new Vector2(.15f,.12f),new Vector2(.20f,.40f),new Vector2(.35f,.51f),new Vector2(.65f,.51f),new Vector2(.80f,.40f),new Vector2(.85f,.12f));break;
                case Symbol.Avatar:
                    Box(.13f,.13f,.74f,.08f);Box(.13f,.79f,.74f,.08f);Box(.13f,.21f,.08f,.58f);Box(.79f,.21f,.08f,.58f);
                    Poly(new Vector2(.38f,.52f),new Vector2(.35f,.65f),new Vector2(.43f,.74f),new Vector2(.57f,.74f),new Vector2(.65f,.65f),new Vector2(.62f,.52f));
                    Poly(new Vector2(.27f,.27f),new Vector2(.30f,.40f),new Vector2(.40f,.48f),new Vector2(.60f,.48f),new Vector2(.70f,.40f),new Vector2(.73f,.27f));break;
                case Symbol.Frame: Box(.13f,.13f,.74f,.11f);Box(.13f,.76f,.74f,.11f);Box(.13f,.24f,.11f,.52f);Box(.76f,.24f,.11f,.52f);Box(.37f,.37f,.26f,.26f);break;
                default:Poly(new Vector2(.5f,.12f),new Vector2(.88f,.5f),new Vector2(.5f,.88f),new Vector2(.12f,.5f));break;
            }
        }
    }
