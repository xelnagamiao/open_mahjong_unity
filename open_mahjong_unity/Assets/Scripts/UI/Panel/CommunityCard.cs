using TMPro;
using UnityEngine;

namespace Salasasa.Community {
    public sealed class CommunityCard : MonoBehaviour {
        public const float HeaderHeight=64, MinimumHeight=176, MinimumWidth=480;
        public TMP_Text title,channel,description,contact;
        public new TMP_Text tag;
        public CommunityShape surface,actionSurface;
        public UnityEngine.UI.Image topBand,divider,iconBack,accentBar;
        public CommunityGlyph glyph;
        public TMP_Text brandIcon;
        public UnityEngine.UI.Button action;
        public TMP_Text actionLabel;
        public CommunityPanel owner;
        public CommunityEntry Entry { get; private set; }
        public void Bind(CommunityEntry entry,CommunityPanel page){owner=page;Entry=entry;tag.text="";tag.gameObject.SetActive(false);
            glyph.symbol=entry.kind=="qq"?CommunityGlyph.Symbol.QQ:entry.kind=="wechat"?CommunityGlyph.Symbol.Wechat:entry.kind=="telegram"?CommunityGlyph.Symbol.Telegram:CommunityGlyph.Symbol.Discord;glyph.SetVerticesDirty();if(brandIcon){glyph.enabled=false;brandIcon.text=entry.kind=="qq"?"\uf1d6":entry.kind=="wechat"?"\uf1d7":entry.kind=="telegram"?"\uf2c6":"\uf392";}RefreshLanguage();}
        static void InterfaceText(TMP_Text label,string source){
            var localized=label.GetComponent<LocalizedPlatformLabel>();
            if(localized)localized.SetSource(source);else label.text=PlatformText.Get(source);
        }
        public void RefreshAvailability(){
            if(Entry==null)return;action.interactable=Entry.Available;
            string previous=actionLabel.text;
            InterfaceText(actionLabel,Entry.Expired?"邀请已失效":Entry.joinMode=="closed"?(string.IsNullOrEmpty(Entry.DisplayClosedMessage)?"暂未开放":Entry.DisplayClosedMessage):Entry.DisplayActionLabel);
            if(previous!=actionLabel.text&&owner)owner.Layout();
        }
        void RefreshContact(){if(Entry!=null)contact.text=Entry.kind=="wechat"||Entry.kind=="discord"?"":Entry.number??"";}
        void RefreshLanguage(){
            if(Entry==null)return;title.text=Entry.DisplayTitle;description.text=Entry.DisplayDescription;RefreshContact();
            InterfaceText(channel,Entry.kind=="qq"?"QQ":Entry.kind=="wechat"?"微信":Entry.kind=="telegram"?"Telegram":"Discord");
            RefreshAvailability();if(owner)owner.Layout();
        }
        void OnEnable(){ConfigManager.OnLanguageChanged+=RefreshLanguage;RefreshLanguage();}
        void OnDisable(){ConfigManager.OnLanguageChanged-=RefreshLanguage;}
        void Awake(){action.onClick.AddListener(Activate);}
        void OnDestroy(){if(action)action.onClick.RemoveListener(Activate);}
        public void Activate(){if(Entry==null)return;RefreshAvailability();if(!Entry.Available)return;
            if(Entry.joinMode=="number"){GUIUtility.systemCopyBuffer=Entry.number;NotificationManager.Instance.ShowTip("System",true,"已复制群号");}
            else if(Entry.joinMode=="qr")owner.qrDialog.Open(Entry,owner.contentClient);
            else if(Entry.joinMode=="link"&&CommunityEntry.SafeLink(Entry.linkUrl)){
                if(Entry.kind=="discord"){GUIUtility.systemCopyBuffer=Entry.linkUrl;NotificationManager.Instance.ShowTip("System",true,"已复制邀请链接");return;}
                string url=Entry.linkUrl;NotificationManager.Instance.ShowConfirmation("前往社群","即将打开外部社群页面，是否继续？",()=>{if(Entry.Available&&Entry.linkUrl==url)Application.OpenURL(url);});
            }
        }
        public void Apply(CommunityPalette p){
            surface.color=p.card;surface.border=1;surface.borderColor=p.line;surface.cut=0;surface.SetVerticesDirty();
            string kind=Entry?.kind??(glyph.symbol==CommunityGlyph.Symbol.Wechat?"wechat":glyph.symbol==CommunityGlyph.Symbol.Telegram?"telegram":glyph.symbol==CommunityGlyph.Symbol.Discord?"discord":"qq");
            topBand.color=HeaderColorFor(kind);topBand.gameObject.SetActive(true);accentBar.gameObject.SetActive(false);
            title.color=p.text;channel.color=contact.color=Color.white;tag.color=description.color=p.muted;glyph.color=Color.white;if(brandIcon)brandIcon.color=Color.white;iconBack.gameObject.SetActive(false);
            divider.color=p.line;actionSurface.color=Color.white;actionSurface.border=1;actionSurface.borderColor=Color.white;actionSurface.cut=0;actionSurface.SetVerticesDirty();actionLabel.color=Color.black;
            FitContent(((RectTransform)transform).rect.width);
        }
        public static Color HeaderColorFor(string kind){
            return kind=="wechat"?new Color32(32,131,76,255)
                :kind=="telegram"?new Color32(36,120,156,255)
                :kind=="discord"?new Color32(88,101,242,255)
                :new Color32(23,105,170,255);
        }
        static void TopRect(RectTransform r,float left,float top,float right,float height){
            r.anchorMin=new Vector2(0,1);r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,1);
            r.offsetMin=new Vector2(left,-top-height);r.offsetMax=new Vector2(-right,-top);
        }
        static void TopBox(RectTransform r,float x,float top,float width,float height,bool right=false){
            r.anchorMin=r.anchorMax=r.pivot=right?Vector2.one:Vector2.up;
            r.anchoredPosition=new Vector2(right?-x:x,-top);r.sizeDelta=new Vector2(width,height);
        }
        public float FitContent(float width){
            const float inset=20,headerInset=18,gap=12,iconSize=30;
            float available=Mathf.Max(100,width-inset*2);
            TopRect(topBand.rectTransform,1,1,1,HeaderHeight-1);
            TopRect(divider.rectTransform,1,HeaderHeight,1,1);
            TopBox(glyph.rectTransform,headerInset,(HeaderHeight-iconSize)*.5f,iconSize,iconSize);
            if(brandIcon&&brandIcon.transform.parent==transform)TopBox(brandIcon.rectTransform,headerInset,(HeaderHeight-iconSize)*.5f,iconSize,iconSize);
            float channelWidth=Mathf.Clamp(channel.GetPreferredValues(channel.text).x+4,36,114);
            float actionWidth=Mathf.Clamp(actionLabel.GetPreferredValues(actionLabel.text).x+24,88,160);
            bool hasContact=!string.IsNullOrWhiteSpace(contact.text);
            float contactSpace=width-headerInset*2-iconSize-10-channelWidth-actionWidth-gap*(hasContact?2:1);
            float contactWidth=hasContact?Mathf.Max(0,Mathf.Min(contact.GetPreferredValues(contact.text).x+4,contactSpace)):0;
            TopBox(channel.rectTransform,headerInset+iconSize+10,14,channelWidth,36);
            TopBox((RectTransform)action.transform,headerInset,14,actionWidth,36,true);
            TopBox(contact.rectTransform,headerInset+actionWidth+gap,14,contactWidth,36,true);contact.gameObject.SetActive(hasContact);

            float titleTop=HeaderHeight+16;
            float titleHeight=Mathf.Max(32,Mathf.Ceil(title.GetPreferredValues(title.text,available,0).y+2));
            TopRect(title.rectTransform,inset,titleTop,inset,titleHeight);
            bool hasDescription=!string.IsNullOrWhiteSpace(description.text);description.gameObject.SetActive(hasDescription);
            float descriptionTop=titleTop+titleHeight+8;
            float descriptionHeight=hasDescription?Mathf.Ceil(description.GetPreferredValues(description.text,available,0).y+2):0;
            TopRect(description.rectTransform,inset,descriptionTop,inset,descriptionHeight);
            return Mathf.Max(MinimumHeight,(hasDescription?descriptionTop+descriptionHeight:titleTop+titleHeight)+20);
        }
    }
}
