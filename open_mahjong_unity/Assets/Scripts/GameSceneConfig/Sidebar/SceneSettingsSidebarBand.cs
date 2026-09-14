using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.SceneSettingsUI
{
    // A single flat polygon. No textures, bevels, outlines, shadows or gradients.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SceneSettingsSidebarBand : MaskableGraphic
    {
        public float emphasis;
        public bool action;
        public bool resetAction;
        public static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect;
            if(!action && emphasis<.001f)return;
            Color fill=Color.Lerp(Hex(resetAction ? "F4E7E4" : action ? "E7ECF9" : "F1F4FC"),Hex(resetAction ? "A85C55" : "586BCC"),emphasis);
            float cut=action?0:10;
            var p = new[] { new Vector2(r.xMin,r.yMin),new Vector2(r.xMax-cut,r.yMin),new Vector2(r.xMax,r.yMin+cut),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax) };
            vh.AddVert(r.center,fill,Vector2.zero);
            for(int i=0;i<p.Length;i++) vh.AddVert(p[i],fill,Vector2.zero);
            for(int i=0;i<p.Length;i++) vh.AddTriangle(0,i+1,(i+1)%p.Length+1);
        }
    }
}
