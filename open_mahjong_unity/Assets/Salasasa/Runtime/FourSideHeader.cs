using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Salasasa.Home
{
    public sealed class FourSideHeader : MonoBehaviour
    {
        public const string Preference = "SalasasaInterface.HeaderSide";
        public const string HomeCurtainPreference = "SalasasaInterface.HomeCurtain";
        public const int DefaultHomeCurtain = 1;
        public CompleteLobbyController owner;
        public Canvas canvas;
        public CanvasGroup visibility;
        public RectTransform surface, tabsRoot, utilityCard;
        public Image surfaceImage, homeCurtain;
        public FourSideHeaderButton main, more;
        public FourSideHeaderButton[] tabs;
        public HeaderReturnGame returnGame;
        public GameObject utilityPanel;
        public Button utilityDismiss;
        public Button[] utilityActions, sideChoices;
        public string[] utilityRoutes;
        public Image[] sideMarks;
        public CanvasGroup friendBadge, noticeBadge;
        public TMP_Text friendCount, noticeCount;
        [SerializeField] int initialSide;
        public int Side { get; private set; }
        public int DefaultSide => initialSide;
        public bool UtilityOpen => utilityPanel.activeSelf;
        public Vector4 Insets { get; private set; }
        public float CurtainProgress => curtain;
        public bool HomeCurtainEnabled { get; private set; } = true;
        Vector2 lastSize;
        float lastCanvasScale;
        bool lastHome;
        float curtain;
        string lastRoute;
        bool dirty = true;
        void OnEnable() { PlatformTypography.Changed += RequestLayout; }
        void OnDisable() { PlatformTypography.Changed -= RequestLayout; }
        void Awake()
        {
            utilityDismiss.onClick.AddListener(CloseUtility);
            for(int i=0;i<utilityActions.Length;i++) {string route=utilityRoutes[i];utilityActions[i].onClick.AddListener(()=>Execute(route));}
            for(int i=0;i<sideChoices.Length;i++) {int side=i;sideChoices[i].onClick.AddListener(()=>SelectSide(side));}
            Side=Mathf.Clamp(PlayerPrefs.GetInt(Preference,initialSide),0,3);
            HomeCurtainEnabled=PlayerPrefs.GetInt(HomeCurtainPreference,DefaultHomeCurtain)!=0;
            utilityPanel.SetActive(false);
            RefreshDeveloperEntry();
            ApplyCurtain();
            UpdateChoices();
        }
        public void SelectSide(int value)
        {
            if(returnGame)returnGame.CloseMenu();
            Side=Mathf.Clamp(value,0,3);dirty=true;
            PlayerPrefs.SetInt(Preference,Side);PlayerPrefs.Save();
            UpdateChoices();CloseUtility();
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(null);
        }
        public void SelectHomeCurtain(int value)
        {
            HomeCurtainEnabled=value!=0;
            if(Application.isPlaying){PlayerPrefs.SetInt(HomeCurtainPreference,HomeCurtainEnabled?1:0);PlayerPrefs.Save();}
            ApplyCurtain();
        }
        void UpdateChoices()
        {
            for(int i=0;i<sideMarks.Length;i++)sideMarks[i].enabled=i==Side;
        }
        public bool HandleEscape()
        {
            if(returnGame&&returnGame.CloseMenu())return true;
            if(UtilityOpen){CloseUtility();return true;}
            if(owner.artwork.previewControls.activeSelf){owner.artwork.previewControls.SetActive(false);return true;}
            return false;
        }
        public void Execute(string route)
        {
            if(returnGame)returnGame.CloseMenu();
            if(route=="utilities"){LayoutUtilityCard();utilityPanel.SetActive(!UtilityOpen);more.SetSelected(UtilityOpen);return;}
            CloseUtility();
            if(route=="variants") {owner.Open("controls");return;}
            owner.artwork.previewControls.SetActive(false);
            owner.NavigateFromHeader(route);
        }
        public void CloseUtility(){utilityPanel.SetActive(false);if(more!=null)more.SetSelected(false);}
        public FourSideHeaderButton ButtonFor(string route)=>route=="home"?main:route=="utilities"?more:tabs.FirstOrDefault(b=>b.route==route);
        public void Tick(bool logged,string window,bool home)
        {
            bool show=logged&&!owner.artwork.overlay.IsOpen;
            if(returnGame){
                bool canReturn=logged&&WindowsManager.Instance&&WindowsManager.Instance.CanReturnToGame;
                if(returnGame.gameObject.activeSelf!=canReturn){returnGame.gameObject.SetActive(canReturn);dirty=true;}
            }
            visibility.alpha=show?1:0;visibility.blocksRaycasts=visibility.interactable=show;
            if(!show){CloseUtility();if(returnGame)returnGame.CloseMenu();}
            RefreshDeveloperEntry();
            var size=((RectTransform)canvas.transform).rect.size;
            if(size!=lastSize){lastSize=size;dirty=true;}
            float canvasScale=CanvasReferenceLayout.EffectiveScale(canvas);
            if(!Mathf.Approximately(lastCanvasScale,canvasScale)){lastCanvasScale=canvasScale;dirty=true;}
            if(lastHome!=home){lastHome=home;dirty=true;}
            if(dirty){Layout();dirty=false;}
            curtain=Mathf.MoveTowards(curtain,logged&&!home?1:0,Time.unscaledDeltaTime/.25f);
            ApplyCurtain();
            if(lastRoute!=owner.SelectedRoute)
            {
                lastRoute=owner.SelectedRoute;
                main.SetSelected(home);
                foreach(var button in tabs)button.SetSelected(button.route==lastRoute);
            }
            if(more.IsSelected!=UtilityOpen)more.SetSelected(UtilityOpen);
            int friends=UnreadBadgeStore.FriendUnread,notices=UnreadBadgeStore.NoticeUnread;
            friendBadge.alpha=friends>0?1:0;friendCount.text=friends>99?"99+":friends.ToString();
            noticeBadge.alpha=notices>0?1:0;noticeCount.text=notices>99?"99+":notices.ToString();
        }
        void RefreshDeveloperEntry()
        {
            for(int i=0;i<utilityActions.Length;i++)
            {
                if(utilityRoutes[i]!="developer" || !utilityActions[i])continue;
                bool show=CompleteLobbyEnvironment.IsDevelopment;
                if(utilityActions[i].gameObject.activeSelf==show)continue;
                utilityActions[i].gameObject.SetActive(show);
                LayoutUtilityCard();
            }
        }
        public void RequestLayout() => dirty = true;
        HeaderPresentationProfile PresentationProfile => main && main.presentation ? main.presentation.profile : null;
        float LabelGap => PresentationProfile ? PresentationProfile.labelGap : 8;
        float UtilityGap => PresentationProfile ? PresentationProfile.utilityGap : 28;
        static float LabelWidth(FourSideHeaderButton button) => button.presentation ? button.presentation.DesiredWidth : 72;
        void Layout()
        {
            var labelled=tabs.Where(b=>!b.compactIcon).ToArray();
            var icons=tabs.Where(b=>b.compactIcon).ToArray();
            float captionWidth=labelled.Sum(LabelWidth)+Mathf.Max(0,labelled.Length-1)*LabelGap;
            bool hasReturn=returnGame&&returnGame.gameObject.activeSelf;
            if(hasReturn)captionWidth+=LabelGap+returnGame.DesiredWidth;
            float iconWidth=icons.Length*44+Mathf.Max(0,icons.Length-1)*6;
            float width=captionWidth+(icons.Length>0?UtilityGap+iconWidth:0);
            float mainWidth=LabelWidth(main);
            float topHeight=PixelAligned(80);
            float bottomHeight=PixelAligned(84);
            float sideWidth=PixelAligned(Mathf.Max(92,labelled.Select(LabelWidth).Append(mainWidth).Max()+20));
            float utilityHeight=24+54*utilityActions.Length-6;
            switch(Side)
            {
                case 0:
                    Place(surface,new Vector2(0,1),new Vector2(0,1),0,0,0,topHeight);surface.anchorMax=Vector2.one;
                    Place(main.transform,new Vector2(0,1),new Vector2(0,1),24,-8,mainWidth,64);
                    Place(tabsRoot,Vector2.one,Vector2.one,-74,-8,width,64);HorizontalGroups(labelled,icons,captionWidth);
                    Place(more.transform,Vector2.one,Vector2.one,-24,-18,44,44);
                    Place(utilityCard,Vector2.one,Vector2.one,-24,-92,232,utilityHeight);
                    Insets=new Vector4(0,topHeight,0,0);break;
                case 1: case 3:
                    bool left=Side==3;var anchor=new Vector2(left?0:1,0);
                    Place(surface,anchor,anchor,0,0,sideWidth,0);surface.anchorMax=new Vector2(anchor.x,1);
                    Place(main.transform,new Vector2(anchor.x,1),new Vector2(anchor.x,1),left?14:-14,-20,sideWidth-28,64);
                    float iconHeight=icons.Length*44+Mathf.Max(0,icons.Length-1)*6;
                    float groupGap=icons.Length>0?24:0;
                    int labelCount=labelled.Length+(hasReturn?1:0);
                    float height=Mathf.Min(64,(lastSize.y-184-groupGap-iconHeight-Mathf.Max(0,labelCount-1)*4)/Mathf.Max(1,labelCount));
                    float labelledHeight=labelCount*height+Mathf.Max(0,labelCount-1)*4;
                    Place(tabsRoot,new Vector2(anchor.x,1),new Vector2(anchor.x,1),left?14:-14,-96,sideWidth-28,labelledHeight+groupGap+iconHeight);
                    for(int i=0;i<labelled.Length;i++)Place(labelled[i].transform,new Vector2(0,1),new Vector2(0,1),0,-i*(height+4),sideWidth-28,height);
                    if(returnGame)Place(returnGame.transform,new Vector2(0,1),new Vector2(0,1),0,-labelled.Length*(height+4),sideWidth-28,height);
                    for(int i=0;i<icons.Length;i++)Place(icons[i].transform,new Vector2(0,1),new Vector2(0,1),(sideWidth-72)/2,-labelledHeight-groupGap-i*50,44,44);
                    Place(more.transform,anchor,anchor,left?(sideWidth-44)/2:-(sideWidth-44)/2,20,44,44);
                    Place(utilityCard,anchor,anchor,left?sideWidth+16:-sideWidth-16,20,232,utilityHeight);
                    Insets=left?new Vector4(sideWidth,0,0,0):new Vector4(0,0,sideWidth,0);break;
                default:
                    Place(surface,Vector2.zero,Vector2.zero,0,0,0,bottomHeight);surface.anchorMax=new Vector2(1,0);
                    Place(main.transform,Vector2.zero,Vector2.zero,24,10,mainWidth,64);
                    Place(tabsRoot,new Vector2(1,0),new Vector2(1,0),-74,10,width,64);HorizontalGroups(labelled,icons,captionWidth);
                    Place(more.transform,new Vector2(1,0),new Vector2(1,0),-24,20,44,44);
                    Place(utilityCard,new Vector2(1,0),new Vector2(1,0),-24,100,232,utilityHeight);
                    Insets=new Vector4(0,0,0,bottomHeight);break;
            }
            main.Fit();more.Fit();foreach(var button in tabs)button.Fit();if(returnGame)returnGame.Fit();
            LayoutUtilityCard();
            float scale=CanvasReferenceLayout.EffectiveScale(owner.artwork.canvas)/CanvasReferenceLayout.EffectiveScale(owner.productionCanvas);
            owner.pageRoot.offsetMin=new Vector2(Insets.x,Insets.w)*scale;
            owner.pageRoot.offsetMax=new Vector2(-Insets.z,-Insets.y)*scale;
            var body=(RectTransform)owner.bodyLayer.transform;
            var pageLayout=body.GetComponent<CanvasReferenceLayout>();
            if(pageLayout)pageLayout.Apply(true);
            else {body.offsetMin=new Vector2(Insets.x,Insets.w);body.offsetMax=new Vector2(-Insets.z,0);}
            ApplyCurtain();
        }
        void ApplyCurtain()
        {
            var color=new Color32(24,30,37,245);
            if(Side==0)
            {
                float eased=1-Mathf.Pow(1-curtain,3);
                float retainedWidth=HomeCurtainEnabled?LabelWidth(main)+48:0;
                surface.anchoredPosition=new Vector2(retainedWidth,Insets.y*(1-eased));
                surface.sizeDelta=new Vector2(-retainedWidth,Insets.y);
            }
            surfaceImage.color=color;
            if(homeCurtain)
            {
                homeCurtain.gameObject.SetActive(Side==0 && HomeCurtainEnabled);
                Place(homeCurtain.transform,new Vector2(0,1),new Vector2(0,1),0,0,LabelWidth(main)+48,Insets.y);
                homeCurtain.color=color;
            }
        }
        float PixelAligned(float value){float scale=CanvasReferenceLayout.EffectiveScale(canvas);return Mathf.Ceil(value*Mathf.Max(.01f,scale))/Mathf.Max(.01f,scale);}
        void LayoutUtilityCard()
        {
            if(!utilityCard || !more || !(utilityCard.parent is RectTransform parent)) return;
            var actions=utilityActions.Where(b=>b && b.gameObject.activeSelf).ToArray();
            float height=24+actions.Length*48+Mathf.Max(0,actions.Length-1)*6;
            var corners=new Vector3[4];((RectTransform)more.transform).GetWorldCorners(corners);
            var points=corners.Select(p=>(Vector2)parent.InverseTransformPoint(p)).ToArray();
            var bounds=parent.rect;
            float left=points.Min(p=>p.x),right=points.Max(p=>p.x),bottom=points.Min(p=>p.y),top=points.Max(p=>p.y);
            float x=right,y=bounds.yMax-Insets.y-12;
            if(Side==1){x=bounds.xMax-Insets.z-12;y=top;}
            else if(Side==2)y=bounds.yMin+Insets.w+12+height;
            else if(Side==3){x=bounds.xMin+Insets.x+12+232;y=top;}
            x=Mathf.Clamp(x,bounds.xMin+232+8,bounds.xMax-8);
            y=Mathf.Clamp(y,bounds.yMin+height+8,bounds.yMax-8);
            utilityCard.anchorMin=utilityCard.anchorMax=Vector2.one*.5f;utilityCard.pivot=Vector2.one;
            utilityCard.sizeDelta=new Vector2(232,height);utilityCard.localScale=Vector3.one;
            utilityCard.anchoredPosition=new Vector2(x,y)-bounds.center;
            for(int i=0;i<actions.Length;i++)Place(actions[i].transform,Vector2.up,Vector2.up,12,-12-i*54,208,48);
        }
        void HorizontalGroups(FourSideHeaderButton[] labelled,FourSideHeaderButton[] icons,float captionWidth)
        {
            float x=0;
            for(int i=0;i<labelled.Length;i++){float width=LabelWidth(labelled[i]);Place(labelled[i].transform,new Vector2(0,1),new Vector2(0,1),x,0,width,64);x+=width+LabelGap;}
            if(returnGame)Place(returnGame.transform,new Vector2(0,1),new Vector2(0,1),x,0,returnGame.DesiredWidth,64);
            for(int i=0;i<icons.Length;i++)Place(icons[i].transform,new Vector2(0,1),new Vector2(0,1),captionWidth+UtilityGap+i*50,-10,44,44);
        }
        static void Place(Transform target,Vector2 anchor,Vector2 pivot,float x,float y,float w,float h)
        {var r=(RectTransform)target;r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;}
        public void BakeLayout(int value)
        {Side=value;curtain=0;lastSize=((RectTransform)canvas.transform).rect.size;Layout();UpdateChoices();}
    }
}
