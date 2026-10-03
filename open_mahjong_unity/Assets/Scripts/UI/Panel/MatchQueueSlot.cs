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
    private MatchButton entry;
    private MatchButton replacement;
    private bool replacementEditable;
    private MatchLobbyView owner;
    private bool desiredVisible,animating;
    private float restWidth=-1,fromWidth,fromAlpha,elapsed;
    public MatchButton Entry=>replacement!=null?replacement:entry;
    public bool IsVisible=>gameObject.activeSelf;
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
        tierText.text=value.TierTitle;
        string mode=value.ModeTitle;
        modeText.text=mode;
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
