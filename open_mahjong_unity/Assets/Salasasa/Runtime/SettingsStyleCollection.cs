using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Home
{
    [Serializable]
    public sealed class SettingsChoiceAppearance
    {
        public Color fill,border,text;
        public float borderWidth,radius;
        public SettingsShapeGraphic.Shape shape;
        public ColorBlock interaction;
        public void Apply(Button button)
        {
            button.GetComponent<SettingsShapeGraphic>().Paint(fill,border,borderWidth,shape,radius);
            button.GetComponentInChildren<TMP_Text>(true).color=text;
            button.colors=interaction;
        }
    }
    [Serializable]
    public sealed class SettingsStyleField
    {
        public string key;
        public int category,section;
        public RectTransform root,control,choices;
        public TMP_Text label,value;
        public SettingsShapeGraphic surface,inputSurface,handleSurface,rowRule,choiceSurface;
        public InkGraphic dropdownChevron;
        public Slider slider;
        public TMP_Dropdown dropdown;
        [SerializeField] bool useSegments;
        public bool UseSegments {get=>useSegments;set=>useSegments=value;}
        public Button[] options;
        public SettingsChoiceAppearance[] choiceStates;
        public RectTransform ActiveInput=>UseSegments?choices:control;
    }
    [Serializable]
    public sealed class SettingsStyleSection
    {
        public RectTransform root,header;
        public TMP_Text title;
        public SettingsShapeGraphic surface,headerSurface,rule,accent;
    }
    public sealed partial class SettingsStyleCollection : MonoBehaviour
    {
        public const string StylePreference = "SettingsCollectionStyleV2";
        public const int DefaultVariant = 1;
        public static readonly int[] DisplayVariantOrder = { 1, 0, 2 };
        public static readonly string[] StyleNames={"暖色都市","夏日学院","夜色剧场"};
        public RectTransform stage,panel,content,close,reviewStrip;
        public TMP_Text title;
        public SettingsShapeGraphic panelSurface,panelShadow,headerSurface,titleAccent,divider,reviewSurface;
        public SettingsOrnamentGraphic ornament;
        public SettingsHandOrderPreview handPreview;
        public Button[] tabs,variants;
        public SettingsStyleField[] controls;
        public SettingsStyleSection[] sections;
        public TMP_FontAsset sans,bold;
        public CompleteLobbyController lobby;
        public SettingsBakedPage[] bakedPages;
        public bool IsBaked=>bakedPages!=null&&bakedPages.Length==9;
        public int Variant {get;private set;}
        public int Category {get;private set;}
        public bool Ready {get;private set;}
        Vector2 measuredParent;
        bool synchronizing;
        readonly Dictionary<string,List<SettingsStyleField>> views=new Dictionary<string,List<SettingsStyleField>>();
        readonly Dictionary<string,int> lastValues=new Dictionary<string,int>();
        bool lastMeldEditable;

        void Awake()
        {
            if(!IsBaked)throw new InvalidOperationException("Settings pages have not been baked into the scene.");
            foreach(var page in bakedPages)
            {
                for(int i=0;i<page.tabs.Length;i++){int n=i;page.tabs[i].onClick.AddListener(()=>SelectCategory(n));}
                for(int i=0;i<page.variants.Length;i++){int n=i;page.variants[i].onClick.AddListener(()=>SelectVariant(n));}
                page.close.GetComponent<Button>().onClick.AddListener(()=>lobby.OpenHome());
                foreach(var view in page.controls)
                {
                    if(!views.TryGetValue(view.key,out var targets)) views.Add(view.key,targets=new List<SettingsStyleField>());
                    targets.Add(view);
                    if(view.slider!=null)
                        view.slider.onValueChanged.AddListener(value=>WriteSetting(view.key,Mathf.RoundToInt(value)));
                    else
                    {
                        view.dropdown.onValueChanged.AddListener(value=>WriteSetting(view.key,value));
                        for(int i=0;i<view.options.Length;i++){int option=i;view.options[i].onClick.AddListener(()=>WriteSetting(view.key,option));}
                    }
                }
            }
            Variant=Mathf.Clamp(PlayerPrefs.GetInt(StylePreference,DefaultVariant),0,2);
            if(int.TryParse(CompleteLobbyEnvironment.Value("-settings-theme"),out var choice))Variant=Mathf.Clamp(choice,0,2);
            if(int.TryParse(CompleteLobbyEnvironment.Value("-settings-page"),out var pageIndex))Category=Mathf.Clamp(pageIndex,0,2);
        }
        IEnumerator Start(){yield return null;Ready=true;RefreshLayout();}
        void OnEnable(){if(Ready)RefreshLayout();}
        void LateUpdate()
        {
            if(stage!=null&&((RectTransform)transform).rect.size!=measuredParent)Fit();
            if(Ready && ConfigManager.Instance)
                foreach(var key in views.Keys)
                    if(!lastValues.TryGetValue(key,out var value) || value!=ReadSetting(key) || (key=="MeldSpacingRow" && lastMeldEditable!=SettingEditable(key))) SyncField(key);
        }
        void Fit(){measuredParent=((RectTransform)transform).rect.size;stage.localScale=Vector3.one*Mathf.Max(.01f,Mathf.Min(measuredParent.x/1600,measuredParent.y/832));}
        public void SelectVariant(int index)
        {CloseDropdowns();Variant=Mathf.Clamp(index,0,2);PlayerPrefs.SetInt(StylePreference,Variant);RefreshLayout();}
        public void SelectCategory(int index)
        {CloseDropdowns();Category=Mathf.Clamp(index,0,2);RefreshLayout();}
        void CloseDropdowns(){foreach(var view in controls)if(view.dropdown!=null)view.dropdown.Hide();}
        public void RefreshLayout(bool updateCanvases=true)
        {
            if(!IsBaked)return;
            ShowBakedPage(Variant,Category);
            if(Ready && ConfigManager.Instance)foreach(var key in views.Keys)SyncField(key);
            Fit();
            if(Application.isPlaying)handPreview?.Refresh();
            if(updateCanvases)Canvas.ForceUpdateCanvases();
        }
        public void ShowBakedPage(int variant,int category)
        {
            Variant=Mathf.Clamp(variant,0,2);Category=Mathf.Clamp(category,0,2);
            var page=bakedPages.Single(p=>p.variant==Variant&&p.category==Category);
            foreach(var item in bakedPages)if(item.gameObject.activeSelf!=(item==page))item.gameObject.SetActive(item==page);
            stage=page.stage;panel=page.panel;content=page.content;close=page.close;reviewStrip=page.reviewStrip;title=page.title;
            panelSurface=page.panelSurface;panelShadow=page.panelShadow;headerSurface=page.headerSurface;
            titleAccent=page.titleAccent;divider=page.divider;reviewSurface=page.reviewSurface;ornament=page.ornament;
            tabs=page.tabs;variants=page.variants;sections=page.sections;
            controls=bakedPages.Where(p=>p.variant==Variant).OrderBy(p=>p.category).SelectMany(p=>p.controls).ToArray();
            handPreview=bakedPages.Single(p=>p.variant==Variant&&p.category==2).handPreview;
        }
        void SyncField(string key)
        {
            if(!views.TryGetValue(key,out var targets) || !ConfigManager.Instance)return;
            int value=ReadSetting(key);bool editable=SettingEditable(key);
            synchronizing=true;
            try
            {
                foreach(var view in targets)
                {
                    if(view.slider!=null)
                    {
                        view.slider.SetValueWithoutNotify(value);view.value.text=value+"%";
                    }
                    else
                    {
                        view.dropdown.SetValueWithoutNotify(value);view.dropdown.interactable=editable;
                        if(view.UseSegments)for(int i=0;i<view.options.Length;i++)
                        {
                            var button=view.options[i];button.interactable=editable;
                            view.choiceStates[(editable?0:2)+(value==i?1:0)].Apply(button);
                        }
                    }
                }
                lastValues[key]=value;
                if(key=="MeldSpacingRow")lastMeldEditable=editable;
            }
            finally{synchronizing=false;}
        }
        public static ColorBlock InteractionColors()
        {var c=ColorBlock.defaultColorBlock;c.normalColor=Color.white;c.highlightedColor=new Color(.92f,.92f,.92f,1);c.pressedColor=new Color(.8f,.8f,.8f,1);c.selectedColor=Color.white;c.disabledColor=new Color(.7f,.7f,.7f,.75f);c.fadeDuration=.08f;return c;}
        static void SetText(TMP_Text text,TMP_FontAsset font,float size,Color color)
        {if(text.font!=font){text.font=font;text.fontSharedMaterial=font.material;}text.fontSize=size;text.fontStyle=FontStyles.Normal;text.color=color;text.raycastTarget=false;text.margin=Vector4.zero;}
        public static Color Hex(string value){ColorUtility.TryParseHtmlString("#"+value,out var c);return c;}
        static Color Alpha(Color color,float alpha){color.a=alpha;return color;}
        public static void Place(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
    }
}
