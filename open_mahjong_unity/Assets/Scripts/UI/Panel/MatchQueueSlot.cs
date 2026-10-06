using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MatchQueueSlot : MonoBehaviour {
    [SerializeField] private GameObject[] tierBackgrounds;
    [SerializeField] private TMP_Text tierText;
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private Button removeButton;
    [SerializeField] private LayoutElement animatedLayout;
    [SerializeField] private CanvasGroup opacity;
    [SerializeField, Min(.01f)] private float transitionDuration=.24f;
    [SerializeField, Min(0)] private float horizontalPadding=12;
    [SerializeField, Min(0)] private float textSpacing=12;
    private MatchButton entry;
    private MatchButton replacement;
    private bool replacementEditable;
    private MatchLobbyView owner;
    private bool desiredVisible,animating;
    private float restWidth=-1,fromWidth,fromAlpha,elapsed;
    private TMP_FontAsset originalTierFont;
    private Material originalTierMaterial;
    private RectTransform visual;
    private float minimumVisualWidth,visualGap,naturalWidth,tierWidth,modeWidth,tierFontSize,modeFontSize,textRightBoundary;
    public MatchButton Entry=>replacement!=null?replacement:entry;
    public bool IsVisible=>gameObject.activeSelf;
    public float PreferredWidth=>visual!=null?naturalWidth:animatedLayout.preferredWidth;
    private void Awake() { removeButton.onClick.AddListener(Remove); }
    private void Remove() { if(entry != null) owner.RemoveSelection(entry); }
    public void Bind(MatchLobbyView view,MatchButton value,bool editable,bool animate=false) {
        owner=view;
        if(animate&&value!=null&&replacement==value){replacementEditable=editable;return;}
        if(animate&&value!=null&&entry!=null&&entry!=value&&gameObject.activeSelf&&animatedLayout!=null&&opacity!=null){
            Bind(view,null,false,true);
            replacement=value;replacementEditable=editable;return;
        }
        replacement=null;
        bool visible=value!=null;
        bool changed=visible!=desiredVisible||(visible&&entry!=value);
        desiredVisible=visible;
        if(animatedLayout!=null&&restWidth<0)restWidth=animatedLayout.preferredWidth;
        if(visible){
            UpdateContent(value);
            if(changed)restWidth=naturalWidth;
            LayoutContent(restWidth);
            if(!gameObject.activeSelf){gameObject.SetActive(true);if(animate&&animatedLayout!=null){animatedLayout.preferredWidth=animatedLayout.minWidth=0;opacity.alpha=0;}}
            entry=value;
        }
        if(opacity!=null){opacity.interactable=visible&&editable;opacity.blocksRaycasts=visible;}
        if(!animate||animatedLayout==null||opacity==null){
            animating=false;if(animatedLayout!=null)animatedLayout.preferredWidth=animatedLayout.minWidth=restWidth;
            if(opacity!=null)opacity.alpha=1;
            gameObject.SetActive(visible);if(!visible)entry=null;
        }else if(changed){
            fromWidth=animatedLayout.preferredWidth;fromAlpha=opacity.alpha;elapsed=0;animating=true;
        }
        removeButton.gameObject.SetActive(visible&&editable);
        if(!visible)return;
        for(int i=0;i<tierBackgrounds.Length;i++)tierBackgrounds[i].SetActive(i==value.TierIndex);
    }
    private void UpdateContent(MatchButton value){
        if(visual==null){
            visual=(RectTransform)tierText.transform.parent;
            minimumVisualWidth=visual.rect.width;
            visualGap=Mathf.Max(0,restWidth-minimumVisualWidth);
            originalTierFont=tierText.font;originalTierMaterial=tierText.fontSharedMaterial;
            tierFontSize=tierText.fontSize;modeFontSize=modeText.fontSize;
            textRightBoundary=modeText.rectTransform.anchorMax.x;
        }
        tierText.font=value.IsElo?modeText.font:originalTierFont;
        tierText.fontSharedMaterial=value.IsElo?modeText.fontSharedMaterial:originalTierMaterial;
        tierText.text=value.TierTitle;
        modeText.text=value.ModeTitle;
        tierText.enableAutoSizing=modeText.enableAutoSizing=true;
        tierText.fontSizeMax=tierFontSize;modeText.fontSizeMax=modeFontSize;
        tierText.textWrappingMode=modeText.textWrappingMode=TextWrappingModes.NoWrap;
        tierWidth=Mathf.Ceil(tierText.GetPreferredValues(tierText.text,Mathf.Infinity,Mathf.Infinity).x)+1;
        modeWidth=Mathf.Ceil(modeText.GetPreferredValues(modeText.text,Mathf.Infinity,Mathf.Infinity).x)+1;
        naturalWidth=Mathf.Max(minimumVisualWidth,(horizontalPadding+textSpacing+tierWidth+modeWidth)/textRightBoundary)+visualGap;
    }
    private void LayoutContent(float width){
        float visualWidth=Mathf.Max(1,width-visualGap);
        visual.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,visualWidth);
        float textWidth=Mathf.Max(1,visualWidth*textRightBoundary-horizontalPadding-textSpacing);
        float titleWidth=tierWidth+modeWidth>textWidth?textWidth*tierWidth/(tierWidth+modeWidth):tierWidth;
        SetTextBounds(tierText.rectTransform,horizontalPadding,titleWidth);
        SetTextBounds(modeText.rectTransform,horizontalPadding+titleWidth+textSpacing,textWidth-titleWidth);
    }
    private static void SetTextBounds(RectTransform rect,float left,float width){
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.up;rect.pivot=new Vector2(0,.5f);
        rect.offsetMin=new Vector2(left,0);rect.offsetMax=new Vector2(left+width,0);
    }
    public void FitWidth(float width,bool animate){
        if(visual==null||Mathf.Approximately(restWidth,width))return;
        restWidth=width;LayoutContent(width);
        if(!desiredVisible)return;
        if(!animate||animatedLayout==null||opacity==null){
            if(animatedLayout!=null)animatedLayout.preferredWidth=animatedLayout.minWidth=width;
        }else if(!animating){
            fromWidth=animatedLayout.preferredWidth;fromAlpha=opacity.alpha;elapsed=0;animating=true;
        }
    }
    private void Update(){AdvanceTransition(Time.unscaledDeltaTime);}
    private void AdvanceTransition(float delta){
        if(!animating)return;
        elapsed+=delta;float t=Mathf.Clamp01(elapsed/transitionDuration);float eased=t*t*(3-2*t);
        animatedLayout.preferredWidth=animatedLayout.minWidth=Mathf.Lerp(fromWidth,desiredVisible?restWidth:0,eased);
        opacity.alpha=Mathf.Lerp(fromAlpha,desiredVisible?1:0,eased);
        if(t<1)return;
        animating=false;
        if(!desiredVisible){
            var next=replacement;bool editable=replacementEditable;replacement=null;
            entry=null;gameObject.SetActive(false);
            if(next!=null)Bind(owner,next,editable,true);
        }
    }
    private void OnDisable(){
        animating=false;
        if(animatedLayout!=null&&restWidth>=0)animatedLayout.preferredWidth=animatedLayout.minWidth=restWidth;
        if(opacity!=null)opacity.alpha=1;
    }
}
