using UnityEngine;
using UnityEngine.UI;

/// <summary>Small vector emblems for catalog entries without bundled artwork.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WarehouseIconGraphic : MaskableGraphic {
    [SerializeField] private int kind;
    public void SetKind(int value) { if (kind == value) return; kind = value; SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();
        var r = rectTransform.rect;
        Vector2 center = r.center;
        float scale = Mathf.Min(r.width, r.height) / 120f;
        Color ink = new Color(.23f,.31f,.47f);
        Color line = kind == 0 ? new Color(.48f,.57f,.72f) : new Color(.70f,.81f,1f);
        Color gold = new Color(.94f,.72f,.39f);
        void Quad(float x, float y, float w, float h, Color c, float angle = 0) {
            int start = vh.currentVertCount;
            var rotation = Quaternion.Euler(0,0,angle);
            foreach (var point in new[] {new Vector2(-w/2,-h/2),new Vector2(-w/2,h/2),new Vector2(w/2,h/2),new Vector2(w/2,-h/2)}) {
                var p = (Vector2)(rotation * point) + new Vector2(x,y);
                vh.AddVert(center + p * scale, c * color, Vector2.zero);
            }
            vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
        }
        void Polygon(Color c, params Vector2[] points) {
            int start = vh.currentVertCount;
            foreach (var point in points) vh.AddVert(center + point * scale, c * color, Vector2.zero);
            for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(start,start+i,start+i+1);
        }
        if (kind == 2) {
            Quad(-6,-2,73,91,new Color(.12f,.18f,.29f),-9);
            Polygon(line,new Vector2(-36,-48),new Vector2(25,-48),new Vector2(36,-37),new Vector2(36,48),new Vector2(-36,48));
            Polygon(ink,new Vector2(-33,-45),new Vector2(24,-45),new Vector2(33,-36),new Vector2(33,45),new Vector2(-33,45));
            Quad(0,30,40,4,new Color(.47f,.63f,.88f));
            Quad(-5,-27,31,2,line); Quad(-10,-34,21,2,new Color(.46f,.58f,.75f));
            Quad(30,-20,8,43,gold,-38);
            Quad(42,-4,8,5,new Color(1f,.86f,.61f),-38);
            Polygon(new Color(1f,.89f,.69f),new Vector2(12,-43),new Vector2(18,-32),new Vector2(24,-37));
        } else if (kind == 1) {
            Quad(-21,-30,17,41,new Color(.28f,.42f,.69f),-16);
            Quad(21,-30,17,41,new Color(.28f,.42f,.69f),16);
            Polygon(gold,new Vector2(-50,-21),new Vector2(-50,21),new Vector2(-38,31),new Vector2(38,31),new Vector2(50,21),new Vector2(50,-21),new Vector2(38,-31),new Vector2(-38,-31));
            Polygon(ink,new Vector2(-46,-19),new Vector2(-46,19),new Vector2(-36,27),new Vector2(36,27),new Vector2(46,19),new Vector2(46,-19),new Vector2(36,-27),new Vector2(-36,-27));
            Quad(-32,0,4,4,gold,45); Quad(32,0,4,4,gold,45);
        } else {
            Polygon(line,new Vector2(-46,-24),new Vector2(-46,24),new Vector2(-36,32),new Vector2(36,32),new Vector2(46,24),new Vector2(46,-24),new Vector2(36,-32),new Vector2(-36,-32));
            Polygon(ink,new Vector2(-43,-22),new Vector2(-43,22),new Vector2(-35,29),new Vector2(35,29),new Vector2(43,22),new Vector2(43,-22),new Vector2(35,-29),new Vector2(-35,-29));
            Quad(0,22,24,2,line); Quad(0,-22,24,2,line);
        }
    }
}
