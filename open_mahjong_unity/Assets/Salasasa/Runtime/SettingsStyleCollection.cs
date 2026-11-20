using System;
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
        public Transform[] themeRoots;
        public bool UsesPagePrefabs => themeRoots != null && themeRoots.Length == 3;
        public bool IsBaked => UsesPagePrefabs || (bakedPages != null && bakedPages.Length == 9);
        static readonly string[] ThemeFolders = { "WarmCity", "SummerCampus", "NightTheatre" };
        static readonly string[] CategoryNames = { "Audio", "Gameplay", "Display" };
        public static string PageResource(int variant, int category) => $"SettingsPages/{ThemeFolders[variant]}/{CategoryNames[category]}";
        public int Variant {get;private set;}
        public int Category {get;private set;}
        public bool Ready {get;private set;}
        Vector2 measuredParent;
        bool synchronizing;
        readonly SettingsBakedPage[,] instances = new SettingsBakedPage[3,3];
        readonly Dictionary<string,int> lastValues = new Dictionary<string,int>();
        SettingsBakedPage currentPage;
        bool lastMeldEditable;

        void Awake()
        {
            if (!IsBaked) throw new InvalidOperationException("Settings pages have not been baked.");
            Variant = Mathf.Clamp(PlayerPrefs.GetInt(StylePreference, DefaultVariant), 0, 2);
            if (int.TryParse(CompleteLobbyEnvironment.Value("-settings-theme"), out var choice)) Variant = Mathf.Clamp(choice, 0, 2);
            if (int.TryParse(CompleteLobbyEnvironment.Value("-settings-page"), out var pageIndex)) Category = Mathf.Clamp(pageIndex, 0, 2);
        }

        void BindPage(SettingsBakedPage page)
        {
            for (int i = 0; i < page.tabs.Length; i++)
            {
                int category = i;
                page.tabs[i].onClick.AddListener(() => SelectCategory(category));
            }
            for (int i = 0; i < page.variants.Length; i++)
            {
                int variant = i;
                page.variants[i].onClick.AddListener(() => SelectVariant(variant));
            }
            page.close.GetComponent<Button>().onClick.AddListener(() => lobby.OpenHome());
            foreach (var field in page.controls)
            {
                if (field.slider)
                    field.slider.onValueChanged.AddListener(value => WriteSetting(field.key, Mathf.RoundToInt(value)));
                else
                {
                    field.dropdown.onValueChanged.AddListener(value => WriteSetting(field.key, value));
                    for (int i = 0; i < field.options.Length; i++)
                    {
                        int option = i;
                        field.options[i].onClick.AddListener(() => WriteSetting(field.key, option));
                    }
                }
            }
        }

        void Start()
        {
            Ready = true;
            if (isActiveAndEnabled) RefreshLayout();
        }

        void OnEnable()
        {
            ConfigManager.GameplaySettingsChanged += SyncVisibleControls;
            RuleRegistry.CurrentChanged += SyncVisibleControls;
            if (Ready) RefreshLayout();
        }

        void OnDisable()
        {
            ConfigManager.GameplaySettingsChanged -= SyncVisibleControls;
            RuleRegistry.CurrentChanged -= SyncVisibleControls;
        }

        void OnRectTransformDimensionsChange()
        {
            if (stage && ((RectTransform)transform).rect.size != measuredParent) Fit();
        }

        void Fit(){measuredParent=((RectTransform)transform).rect.size;stage.localScale=Vector3.one*Mathf.Max(.01f,Mathf.Min(measuredParent.x/1600,measuredParent.y/832));}
        public void SelectVariant(int index)
        {
            CloseDropdowns();
            Variant = Mathf.Clamp(index, 0, 2);
            PlayerPrefs.SetInt(StylePreference, Variant);
            if (isActiveAndEnabled && Ready) RefreshLayout();
        }

        public void SelectCategory(int index)
        {
            CloseDropdowns();
            Category = Mathf.Clamp(index, 0, 2);
            if (isActiveAndEnabled && Ready) RefreshLayout();
        }

        void CloseDropdowns()
        {
            if (currentPage)
                foreach (var view in currentPage.controls)
                    if (view.dropdown) view.dropdown.Hide();
        }

        public void RefreshLayout(bool updateCanvases=true)
        {
            if (!IsBaked) return;
            ShowBakedPage(Variant, Category);
            SyncVisibleControls();
            Fit();
            if (updateCanvases) Canvas.ForceUpdateCanvases();
        }

        public void ShowBakedPage(int variant, int category)
        {
            Variant = Mathf.Clamp(variant, 0, 2);
            Category = Mathf.Clamp(category, 0, 2);
            var page = instances[Variant, Category];
            if (!page)
            {
                if (UsesPagePrefabs)
                {
                    var prefab = Resources.Load<GameObject>(PageResource(Variant, Category));
                    if (!prefab) throw new InvalidOperationException($"Missing settings page {Variant}/{Category}");
                    page = Instantiate(prefab, themeRoots[Variant], false).GetComponent<SettingsBakedPage>();
                    page.name = prefab.name;
                    page.GetComponent<SettingsPagePresentation>().settings = this;
                }
                else page = bakedPages.Single(p => p.variant == Variant && p.category == Category);
                if (Application.isPlaying) BindPage(page);
                instances[Variant, Category] = page;
            }
            if (!currentPage && !UsesPagePrefabs)
                foreach (var item in bakedPages) if (item != page) item.gameObject.SetActive(false);
            if (currentPage != page)
            {
                if (currentPage) currentPage.gameObject.SetActive(false);
                currentPage = page;
                lastValues.Clear();
            }
            stage=page.stage;panel=page.panel;content=page.content;close=page.close;reviewStrip=page.reviewStrip;title=page.title;
            panelSurface=page.panelSurface;panelShadow=page.panelShadow;headerSurface=page.headerSurface;
            titleAccent=page.titleAccent;divider=page.divider;reviewSurface=page.reviewSurface;ornament=page.ornament;
            tabs=page.tabs;variants=page.variants;sections=page.sections;controls=page.controls;handPreview=page.handPreview;
            if (!page.gameObject.activeSelf) page.gameObject.SetActive(true);
        }

        void SyncVisibleControls()
        {
            if (!currentPage || !ConfigManager.Instance || synchronizing) return;
            bool refreshPreview = false;
            synchronizing = true;
            try
            {
                foreach (var view in currentPage.controls)
                {
                    int value = ReadSetting(view.key);
                    bool editable = SettingEditable(view.key);
                    if (lastValues.TryGetValue(view.key, out int previous) && previous == value &&
                        (view.key != "MeldSpacingRow" || lastMeldEditable == editable)) continue;
                    lastValues[view.key] = value;
                    if (view.key == "MeldSpacingRow") lastMeldEditable = editable;
                    if (view.slider)
                    {
                        view.slider.SetValueWithoutNotify(value);
                        view.value.text = value + "%";
                    }
                    else
                    {
                        view.dropdown.SetValueWithoutNotify(value);
                        view.dropdown.interactable = editable;
                        if (view.UseSegments)
                            for (int i = 0; i < view.options.Length; i++)
                            {
                                var button = view.options[i];
                                button.interactable = editable;
                                view.choiceStates[(editable ? 0 : 2) + (value == i ? 1 : 0)].Apply(button);
                            }
                    }
                    refreshPreview |= view.key.StartsWith("HandSort", StringComparison.Ordinal) || view.key == "ConfigWhiteType";
                }
            }
            finally { synchronizing = false; }
            if (refreshPreview && handPreview) handPreview.Refresh();
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
