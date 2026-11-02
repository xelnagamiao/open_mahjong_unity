using UnityEngine;
namespace Salasasa.RightEntrances
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EntranceGlyph : UnityEngine.UI.MaskableGraphic
    {
        public enum Symbol { Match, Room, Eye, Flag, Arrow, Close, Mail, Bag, Book, Chat, Scene, Rank }
        public Symbol symbol;
        public float weight=2.5f;
        public bool filled;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;float scale=Mathf.Min(r.width,r.height)/100;
            Vector2 P(float x,float y)=>new Vector2(r.center.x+(x-50)*scale,r.center.y+(50-y)*scale);
            void L(float x,float y,float a,float b)=>EntranceSurface.Line(vh,P(x,y),P(a,b),color,weight*scale);
            void Path(params float[] xy){for(int i=0;i<xy.Length-2;i+=2)L(xy[i],xy[i+1],xy[i+2],xy[i+3]);}
            void Circle(float x,float y,float radius){for(int i=0;i<40;i++){float a=i*Mathf.PI/20,b=(i+1)*Mathf.PI/20;L(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius);}}
            void Poly(params float[] xy){int start=vh.currentVertCount;for(int i=0;i<xy.Length;i+=2)vh.AddVert(P(xy[i],xy[i+1]),color,Vector2.zero);for(int i=1;i<xy.Length/2-1;i++)vh.AddTriangle(start,start+i,start+i+1);}
            if(filled){
                switch(symbol){
                    case Symbol.Match:
                        Poly(14,25,66,25,66,39,14,39);Poly(64,12,86,32,64,52);
                        Poly(34,61,86,61,86,75,34,75);Poly(36,48,14,68,36,88);return;
                    case Symbol.Room:
                        Poly(12,43,50,12,88,43);Poly(25,43,75,43,75,57,25,57);
                        Poly(25,57,43,57,43,85,25,85);Poly(59,57,75,57,75,85,59,85);return;
                    case Symbol.Eye:
                        Path(9,50,27,31,42,25,58,25,73,31,91,50,73,69,58,75,42,75,27,69,9,50);
                        Poly(50,36,60,40,64,50,60,60,50,64,40,60,36,50,40,40);return;
                    case Symbol.Flag:
                        Poly(22,15,31,15,31,86,22,86);Poly(32,16,79,16,68,32,79,48,32,48);
                        Poly(15,80,56,80,56,87,15,87);return;
                }
            }
            switch(symbol){
                case Symbol.Match: Path(18,29,68,29,58,19);Path(68,29,58,39);Path(82,71,32,71,42,61);Path(32,71,42,81);Circle(22,71,7);Circle(78,29,7);break;
                case Symbol.Room: Path(17,45,50,16,83,45);Path(25,39,25,83,75,83,75,39);Path(44,83,44,56,59,56,59,83);break;
                case Symbol.Eye: Path(10,50,25,34,40,25,60,25,75,34,90,50,75,66,60,75,40,75,25,66,10,50);Circle(50,50,14);break;
                case Symbol.Flag: Path(27,86,27,17,74,17,64,33,74,49,27,49);L(16,86,60,86);break;
                case Symbol.Arrow:Path(35,24,61,50,35,76);break;
                case Symbol.Close:L(25,25,75,75);L(75,25,25,75);break;
                case Symbol.Mail:Path(15,26,85,26,85,74,15,74,15,26,50,54,85,26);break;
                case Symbol.Bag:Path(23,33,77,33,83,83,17,83,23,33);Path(37,33,37,20,63,20,63,33);break;
                case Symbol.Book:Path(16,20,45,20,50,25,55,20,84,20,84,80,55,80,50,85,45,80,16,80,16,20);L(50,25,50,85);break;
                case Symbol.Chat:Path(15,22,85,22,85,67,44,67,25,84,25,67,15,67,15,22);L(30,42,70,42);L(30,54,58,54);break;
                case Symbol.Scene:Path(18,18,82,18,82,82,18,82,18,18);Path(20,70,42,47,60,65,70,54,82,68);Circle(65,35,6);break;
                case Symbol.Rank:Path(28,18,72,18,70,46,61,62,39,62,30,46,28,18);Path(28,26,15,26,18,45,32,49);Path(72,26,85,26,82,45,68,49);L(50,62,50,81);L(32,82,68,82);break;
            }
        }
    }
}
