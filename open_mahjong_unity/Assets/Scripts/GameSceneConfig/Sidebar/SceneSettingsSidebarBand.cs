using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SceneSettingsSidebarBand : MaskableGraphic
    {
        public float emphasis;
        public bool action;
        public bool resetAction;
        public bool usePalette;
        public Color idleFill, selectedFill, edgeColor;
        public int treatment;
        public bool showIdle;
        public float cornerRadius;
        public static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect;
            if(usePalette)
            {
                float amount=Mathf.Clamp01(emphasis);
                if(!action&&!showIdle&&amount<.001f)return;
                Rounded(vh,r,Color.Lerp(idleFill,selectedFill,amount),cornerRadius);
                return;
            }
            if(!action && emphasis<.001f)return;
            Color fill=Color.Lerp(Hex(resetAction ? "F4E7E4" : action ? "E7ECF9" : "F1F4FC"),Hex(resetAction ? "A85C55" : "586BCC"),emphasis);
            float cut=action?0:10;
            var p = new[] { new Vector2(r.xMin,r.yMin),new Vector2(r.xMax-cut,r.yMin),new Vector2(r.xMax,r.yMin+cut),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax) };
            vh.AddVert(r.center,fill,Vector2.zero);
            for(int i=0;i<p.Length;i++) vh.AddVert(p[i],fill,Vector2.zero);
            for(int i=0;i<p.Length;i++) vh.AddTriangle(0,i+1,(i+1)%p.Length+1);
        }
        static void Quad(VertexHelper vh,Rect r,Color color)
        {
            int k=vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin,r.yMin),color,Vector2.zero);
            vh.AddVert(new Vector2(r.xMin,r.yMax),color,Vector2.zero);
            vh.AddVert(new Vector2(r.xMax,r.yMax),color,Vector2.zero);
            vh.AddVert(new Vector2(r.xMax,r.yMin),color,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
        }
        static void Rounded(VertexHelper vh,Rect r,Color color,float radius)
        {
            radius=Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f);
            if(radius<.5f){Quad(vh,r,color);return;}
            int start=vh.currentVertCount;
            vh.AddVert(r.center,color,Vector2.zero);
            for(int corner=0;corner<4;corner++)
            {
                var center=new Vector2(corner<2?r.xMax-radius:r.xMin+radius,corner==0||corner==3?r.yMax-radius:r.yMin+radius);
                for(int step=0;step<=6;step++)
                {
                    float angle=(90-corner*90-step*15)*Mathf.Deg2Rad;
                    vh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,color,Vector2.zero);
                }
            }
            const int count=28;
            for(int i=0;i<count;i++)vh.AddTriangle(start,start+1+i,start+1+(i+1)%count);
        }
    }
}
