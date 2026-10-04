using UnityEngine;
using UnityEngine.UI;

// Solid black panel with a constant-width, clipped-corner outline.
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public class TipsCornerFrame : MaskableGraphic
{
    public float corner = 16, border = 2;
    public Color fill = new Color32(15, 18, 26, 255);
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r=rectTransform.rect;
        Vector2[] Ring(float inset)
        {
            float c=Mathf.Min(corner,Mathf.Min(r.width,r.height)*.25f);
            float d=Mathf.Max(0,c-inset*.586f);
            float l=r.xMin+inset, b=r.yMin+inset, t=r.yMax-inset, right=r.xMax-inset;
            return new[]{new Vector2(l+d,t),new Vector2(right-d,t),new Vector2(right,t-d),new Vector2(right,b+d),new Vector2(right-d,b),new Vector2(l+d,b),new Vector2(l,b+d),new Vector2(l,t-d)};
        }
        var outer=Ring(0); var inner=Ring(border);
        for(int i=0;i<8;i++) vh.AddVert(outer[i],color,Vector2.zero);
        for(int i=0;i<8;i++) vh.AddVert(inner[i],fill,Vector2.zero);
        vh.AddVert(r.center,fill,Vector2.zero);
        // Separate border vertices keep the edge flat rather than interpolating into the fill.
        for(int i=0;i<8;i++) vh.AddVert(inner[i],color,Vector2.zero);
        for(int i=0;i<8;i++) { int j=(i+1)%8; vh.AddTriangle(i,j,17+j); vh.AddTriangle(i,17+j,17+i); vh.AddTriangle(16,8+i,8+j); }
    }
}
