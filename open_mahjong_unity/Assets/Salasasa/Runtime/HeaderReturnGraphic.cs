using UnityEngine;

namespace Salasasa.Home
{
    /// <summary>Monochrome return symbols on the same 48 px grid as header artwork.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HeaderReturnGraphic : UnityEngine.UI.MaskableGraphic
    {
        public enum Symbol { FoldBack, Table, Resume, DropDown }
        public Symbol symbol;

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = GetPixelAdjustedRect();
            Vector2 P(float x, float y) => new Vector2(r.xMin + x * r.width / 48, r.yMax - y * r.height / 48);
            void Quad(float x, float y, float w, float h)
            {
                int n=mesh.currentVertCount;
                mesh.AddVert(P(x,y),color,Vector2.zero);mesh.AddVert(P(x+w,y),color,Vector2.zero);
                mesh.AddVert(P(x+w,y+h),color,Vector2.zero);mesh.AddVert(P(x,y+h),color,Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
            }
            void Triangle(float x,float y,float x2,float y2,float x3,float y3)
            {
                int n=mesh.currentVertCount;
                mesh.AddVert(P(x,y),color,Vector2.zero);mesh.AddVert(P(x2,y2),color,Vector2.zero);mesh.AddVert(P(x3,y3),color,Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2);
            }
            switch(symbol)
            {
                case Symbol.FoldBack:
                    Triangle(6,20,22,6,22,34);Quad(21,16.5f,17,7);
                    Quad(31,23,7,16);Quad(19,32,12,7);
                    break;
                case Symbol.Table:
                    Quad(6,8,36,5);Quad(6,13,5,27);Quad(11,35,31,5);Quad(37,27,5,8);
                    Quad(16,17,7,12);Quad(28,19,10,6);Triangle(34,14,44,22,34,30);
                    break;
                case Symbol.Resume:
                    Quad(7,7,34,5);Quad(7,12,5,29);Quad(12,36,29,5);
                    Triangle(21,16,21,32,36,24);
                    break;
                case Symbol.DropDown:
                    Quad(21,6,6,18);Triangle(10,21,38,21,24,35);Quad(8,39,32,5);
                    break;
            }
        }
    }
}
