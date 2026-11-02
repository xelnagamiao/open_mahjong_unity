using UnityEngine;

namespace Salasasa.Home
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HeaderDataGraphic : UnityEngine.UI.MaskableGraphic
    {
        public enum Symbol { Report, Database, Dashboard, Radar }
        public Symbol symbol;
        public float weight = 3.2f;

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 P(float x, float y) => new Vector2(rect.xMin + rect.width * x, rect.yMax - rect.height * y);
            void Line(float x, float y, float x2, float y2, float scale = 1)
            {
                var a = P(x, y); var b = P(x2, y2);
                var n = new Vector2(-(b-a).y, (b-a).x).normalized * weight * scale * .5f;
                Quad(mesh, a-n, a+n, b+n, b-n);
            }
            void Box(float x, float y, float w, float h)
            {
                Quad(mesh, P(x,y+h), P(x,y), P(x+w,y), P(x+w,y+h));
            }
            void Ellipse(float x, float y, float rx, float ry, float from = 0, float to = 360)
            {
                const int steps = 36;
                for (int i=0;i<steps;i++)
                {
                    float a=Mathf.Lerp(from,to,(float)i/steps)*Mathf.Deg2Rad;
                    float b=Mathf.Lerp(from,to,(float)(i+1)/steps)*Mathf.Deg2Rad;
                    Line(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry,x+Mathf.Cos(b)*rx,y+Mathf.Sin(b)*ry);
                }
            }
            switch (symbol)
            {
                case Symbol.Report:
                    Line(.19f,.12f,.68f,.12f); Line(.19f,.12f,.19f,.88f);
                    Line(.19f,.88f,.82f,.88f); Line(.82f,.88f,.82f,.28f);
                    Line(.68f,.12f,.82f,.28f); Line(.68f,.12f,.68f,.28f); Line(.68f,.28f,.82f,.28f);
                    Box(.31f,.28f,.23f,.065f); Box(.31f,.41f,.36f,.06f);
                    Line(.32f,.72f,.43f,.60f); Line(.43f,.60f,.56f,.68f); Line(.56f,.68f,.69f,.54f);
                    break;
                case Symbol.Database:
                    Ellipse(.5f,.24f,.31f,.12f); Line(.19f,.24f,.19f,.76f); Line(.81f,.24f,.81f,.76f);
                    Ellipse(.5f,.49f,.31f,.12f,0,180); Ellipse(.5f,.76f,.31f,.12f,0,180);
                    break;
                case Symbol.Dashboard:
                    Box(.13f,.14f,.34f,.29f); Box(.55f,.14f,.31f,.12f);
                    Box(.55f,.34f,.31f,.09f); Box(.13f,.52f,.22f,.33f);
                    Line(.47f,.83f,.47f,.54f); Line(.47f,.83f,.86f,.83f);
                    Line(.53f,.70f,.64f,.60f); Line(.64f,.60f,.73f,.69f); Line(.73f,.69f,.86f,.51f);
                    break;
                case Symbol.Radar:
                    var points = new Vector2[6];
                    for(int i=0;i<6;i++)
                    {
                        float a=(i*60f-90)*Mathf.Deg2Rad;
                        points[i]=new Vector2(.5f+Mathf.Cos(a)*.39f,.5f+Mathf.Sin(a)*.39f);
                    }
                    for(int i=0;i<6;i++)
                    {
                        var a=points[i]; var b=points[(i+1)%6];
                        Line(a.x,a.y,b.x,b.y); Line(.5f,.5f,a.x,a.y,.48f);
                    }
                    Vector2[] data={new Vector2(.5f,.21f),new Vector2(.77f,.35f),new Vector2(.69f,.61f),new Vector2(.5f,.77f),new Vector2(.34f,.60f),new Vector2(.21f,.33f)};
                    for(int i=0;i<6;i++) { var a=data[i]; var b=data[(i+1)%6]; Line(a.x,a.y,b.x,b.y,1.2f); }
                    break;
            }
        }

        void Quad(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int i=mesh.currentVertCount;
            mesh.AddVert(a,color,Vector2.zero);mesh.AddVert(b,color,Vector2.zero);
            mesh.AddVert(c,color,Vector2.zero);mesh.AddVert(d,color,Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
        }
    }
}
