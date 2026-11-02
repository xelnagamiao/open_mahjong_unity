using UnityEngine;

/// <summary>Small, crisp transport and utility icons; no font-glyph dependency.</summary>
[AddComponentMenu("Mahjong/UI/Table HUD Icon")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TableHudIcon : UnityEngine.UI.MaskableGraphic {
    public enum Symbol { Wall, Rules, List, Help, Settings, Exit, StepBack, StepForward, PreviousTurn, NextTurn, Play, Pause, Home }
    public Symbol symbol;
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh) {
        vh.Clear();
        var r = GetPixelAdjustedRect();
        float s = Mathf.Min(r.width,r.height);
        Vector2 P(float x,float y) => r.center + new Vector2(x-.5f,y-.5f)*s;
        void Tri(Vector2 a,Vector2 b,Vector2 c){int n=vh.currentVertCount;vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);vh.AddTriangle(n,n+1,n+2);}
        void Line(float ax,float ay,float bx,float by,float width=.065f){var a=P(ax,ay);var b=P(bx,by);var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*s*.5f;Tri(a-n,a+n,b+n);Tri(a-n,b+n,b-n);}
        void Box(float x,float y,float w,float h){Line(x,y,x+w,y);Line(x+w,y,x+w,y+h);Line(x+w,y+h,x,y+h);Line(x,y+h,x,y);}
        void Play(float cx,float width,bool reverse){float dir=reverse?-1:1;Tri(P(cx-dir*width*.5f,.19f),P(cx-dir*width*.5f,.81f),P(cx+dir*width*.5f,.5f));}
        switch(symbol){
            case Symbol.Play: Play(.54f,.55f,false);break;
            case Symbol.Pause: Line(.34f,.2f,.34f,.8f,.14f);Line(.66f,.2f,.66f,.8f,.14f);break;
            case Symbol.StepBack: Play(.59f,.48f,true);Line(.18f,.2f,.18f,.8f);break;
            case Symbol.StepForward: Play(.41f,.48f,false);Line(.82f,.2f,.82f,.8f);break;
            case Symbol.PreviousTurn: Play(.35f,.36f,true);Play(.72f,.36f,true);break;
            case Symbol.NextTurn: Play(.28f,.36f,false);Play(.65f,.36f,false);break;
            case Symbol.Wall: Box(.11f,.18f,.22f,.64f);Box(.39f,.18f,.22f,.64f);Box(.67f,.18f,.22f,.64f);break;
            case Symbol.Rules: Box(.23f,.12f,.55f,.76f);Line(.35f,.67f,.66f,.67f);Line(.35f,.5f,.66f,.5f);Line(.35f,.33f,.57f,.33f);break;
            case Symbol.List: for(int i=0;i<3;i++){float y=.26f+i*.24f;Line(.15f,y,.22f,y,.09f);Line(.36f,y,.85f,y);}break;
            case Symbol.Help: Line(.32f,.72f,.43f,.84f);Line(.43f,.84f,.64f,.84f);Line(.64f,.84f,.74f,.71f);Line(.74f,.71f,.69f,.57f);Line(.69f,.57f,.5f,.46f);Line(.5f,.46f,.5f,.35f);Line(.5f,.15f,.5f,.22f,.09f);break;
            case Symbol.Settings: for(int i=0;i<12;i++){float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;Line(.5f+Mathf.Cos(a)*.27f,.5f+Mathf.Sin(a)*.27f,.5f+Mathf.Cos(b)*.27f,.5f+Mathf.Sin(b)*.27f);if(i%2==0)Line(.5f+Mathf.Cos(a)*.27f,.5f+Mathf.Sin(a)*.27f,.5f+Mathf.Cos(a)*.42f,.5f+Mathf.Sin(a)*.42f,.13f);}Box(.43f,.43f,.14f,.14f);break;
            case Symbol.Exit: Line(.52f,.85f,.2f,.85f);Line(.2f,.85f,.2f,.15f);Line(.2f,.15f,.52f,.15f);Line(.43f,.5f,.87f,.5f);Line(.68f,.7f,.88f,.5f);Line(.88f,.5f,.68f,.3f);break;
            case Symbol.Home: Line(.12f,.51f,.5f,.86f);Line(.5f,.86f,.88f,.51f);Line(.25f,.57f,.25f,.16f);Line(.25f,.16f,.75f,.16f);Line(.75f,.16f,.75f,.57f);Box(.42f,.16f,.16f,.26f);break;
        }
    }
}
