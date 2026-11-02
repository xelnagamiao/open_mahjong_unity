using UnityEngine;
using UnityEngine.EventSystems;
namespace Salasasa.RightEntrances
{
    public sealed class EntranceButton : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler
    {
        public string route;
        public EntranceRouter router;
        public UnityEngine.UI.Button button;
        public GameObject selectedFrame;
        public CanvasGroup hover;
        public RectTransform visual;
        public EntranceTint surfaceTint;
        public EntranceSurface hoverWash, hoverEdge, selectedOutline, selectedInnerOutline;
        bool pointer,focus,pressed,selected;
        Vector3 initialScale;
        void Awake(){initialScale=visual.localScale;button.onClick.AddListener(Activate);}
        void OnDestroy(){if(button)button.onClick.RemoveListener(Activate);}
        void OnDisable(){pointer=focus=pressed=false;if(hover)hover.alpha=0;if(visual)visual.localScale=Vector3.one;}
        public void ApplyPalette(EntrancePalette palette)
        {
            if (!surfaceTint) surfaceTint = visual.Find("Surface").GetComponent<EntranceTint>();
            if (!hoverWash) hoverWash = hover.transform.Find("Hover wash").GetComponent<EntranceSurface>();
            if (!hoverEdge) hoverEdge = hover.transform.Find("Hover edge").GetComponent<EntranceSurface>();
            if (!selectedOutline) selectedOutline = selectedFrame.transform.Find("Selected frame").GetComponent<EntranceSurface>();
            if (!selectedInnerOutline) selectedInnerOutline = selectedFrame.transform.Find("Inner frame").GetComponent<EntranceSurface>();
            Color background = palette.Get(surfaceTint.tone);
            Color foreground = palette.Get(surfaceTint.tone == EntranceTone.Accent ? EntranceTone.OnAccent
                : surfaceTint.tone == EntranceTone.Dark ? EntranceTone.OnDark : EntranceTone.Ink);
            float opacity = Luminance(background) > .4f ? .08f : .12f;
            Color wash = foreground;
            Color hovered = Color.Lerp(background, wash, opacity);
            bool losesReadability = Contrast(foreground, hovered) < Mathf.Min(4.5f, Contrast(foreground, background));
            if (surfaceTint.tone == EntranceTone.Surface)
                losesReadability |= Contrast(palette.muted, hovered) < Mathf.Min(4.5f, Contrast(palette.muted, background));
            if (losesReadability)
                wash = Contrast(foreground, Color.black) > Contrast(foreground, Color.white) ? Color.black : Color.white;
            wash.a = opacity;
            Color edge = Contrast(palette.accent, background) >= 3f ? palette.accent : foreground;
            hoverWash.color = wash;
            hoverEdge.color = WithAlpha(edge, .72f);
            selectedOutline.color = WithAlpha(edge, 1);
            selectedInnerOutline.color = WithAlpha(foreground, .55f);
        }
        static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
        static float Luminance(Color color)
        {
            var linear = color.linear;
            return .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
        }
        static float Contrast(Color first, Color second)
        {
            float a = Luminance(first), b = Luminance(second);
            return (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);
        }
        public void Activate(){router.Request(route);}
        public void SetSelected(bool value){selected=value;selectedFrame.SetActive(value);}
        public void SetPreviewHover(bool value){pointer=value;}
        public void OnPointerEnter(PointerEventData e){pointer=true;}
        public void OnPointerExit(PointerEventData e){pointer=false;pressed=false;}
        public void OnPointerDown(PointerEventData e){pressed=true;}
        public void OnPointerUp(PointerEventData e){pressed=false;}
        public void OnSelect(BaseEventData e){focus=true;}
        public void OnDeselect(BaseEventData e){focus=false;}
        void Update(){float t=1-Mathf.Exp(-Time.unscaledDeltaTime*20);hover.alpha=Mathf.Lerp(hover.alpha,pointer||focus?1:0,t);visual.localScale=Vector3.Lerp(visual.localScale,initialScale*(pressed?.985f:1),t);}
    }
}
