using System;
using TMPro;
using UnityEngine;

namespace Mahjong.SceneSettingsUI
{
    [DisallowMultipleComponent]
    public sealed class SceneSettingsSidebarStyle : MonoBehaviour
    {
        public const string Preference="Salasasa.SceneSidebar.Main.Layout.V1";
        public const string IconPreference="Salasasa.SceneSidebar.Main.Icons.V1";
        public const string PalettePreference="Salasasa.SceneSidebar.Main.Palette.V1.";
        public static readonly string[] Names={"窄边侧栏"};
        public static readonly string[] IconNames={"原版","实心","线框","方形","圆润"};
        public static readonly string[] LayerNames={"侧栏底色","标题底色","按钮底色","选中底色","标题文字","按钮文字","图标颜色","选中文字","选中图标","重置颜色","工具底色"};
        public static readonly string[] PresetNames={"原版灰蓝","原版浅灰","暖灰朱红","墨黑银白","石墨蓝","深海蓝","纸白墨灰","石墨琥珀","葡萄灰","深棕铜橙","雾蓝青","夜色朱红","雪白朱红","冷灰钴蓝","月白靛蓝","夜蓝金黄","银灰橙红","暗灰冰蓝","深紫银白","奶油珊瑚","砂岩朱红","铁灰玫红","黑金琥珀","夜蓝冰青"};
        [SerializeField] SceneSettingsSidebar sidebar;
        [SerializeField] UnityEngine.UI.Image surface,header;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text[] groupLabels;
        [SerializeField] SceneSettingsSidebarRow[] styledRows;
        [SerializeField] UnityEngine.UI.Button[] choices,iconChoices;
        [SerializeField] TMP_Text[] choiceLabels,iconLabels;
        [SerializeField] UnityEngine.UI.Button appearanceButton;
        [SerializeField] TMP_Text appearanceCaption;
        [SerializeField] LayoutVariant[] layouts;
        [SerializeField] PaletteState[] presets;
        PaletteState[] palettes;
        bool pendingSave;
        float saveAt;
        public event Action AppearanceChanged;
        public int CurrentStyle{get;private set;}
        public int CurrentIconFamily{get;private set;}
        public int LayoutCount=>layouts==null?0:layouts.Length;
        public SceneSettingsSidebar Sidebar=>sidebar;
        public UnityEngine.UI.Button[] Choices=>choices;
        public UnityEngine.UI.Button[] IconChoices=>iconChoices;
        public UnityEngine.UI.Button AppearanceButton=>appearanceButton;
        public SceneSettingsSidebarRow[] Rows=>styledRows;
        public UnityEngine.UI.Image Surface=>surface;
        void Awake()
        {
            CurrentStyle=0;
            CurrentIconFamily=Mathf.Clamp(PlayerPrefs.GetInt(IconPreference,0),0,IconNames.Length-1);
            Apply();
        }
        void OnEnable()=>Apply();
        void Update(){if(pendingSave&&Time.unscaledTime>=saveAt)FlushPalette();}
        void OnDisable()=>FlushPalette();
        void OnApplicationQuit()=>FlushPalette();
        public void SelectStyle(int index)
        {
            CurrentStyle=0;
            if(Application.isPlaying){PlayerPrefs.SetInt(Preference,CurrentStyle);PlayerPrefs.Save();}
            Apply();AppearanceChanged?.Invoke();
        }
        public void SelectIcons(int index)
        {
            CurrentIconFamily=Mathf.Clamp(index,0,IconNames.Length-1);
            if(Application.isPlaying){PlayerPrefs.SetInt(IconPreference,CurrentIconFamily);PlayerPrefs.Save();}
            ApplyColors();AppearanceChanged?.Invoke();
        }
        public Color LayerColor(int index)
        {
            EnsurePalettes();return palettes[CurrentStyle].colors[Mathf.Clamp(index,0,LayerNames.Length-1)];
        }
        public void SetLayerColor(int index,Color value)
        {
            EnsurePalettes();if(index<0||index>=LayerNames.Length||!Valid(value))return;
            palettes[CurrentStyle].colors[index]=value;ChangedPalette();
        }
        public void SelectPreset(int index)
        {
            if(presets==null||index<0||index>=presets.Length)return;
            EnsurePalettes();palettes[CurrentStyle]=PresetForLayout(index);ChangedPalette();
        }
        public bool IsPreset(int index)
        {
            EnsurePalettes();if(presets==null||index<0||index>=presets.Length)return false;
            var expected=PresetForLayout(index).colors;var actual=palettes[CurrentStyle].colors;
            for(int i=0;i<actual.Length;i++)
                if(Mathf.Abs(actual[i].r-expected[i].r)>.003f||Mathf.Abs(actual[i].g-expected[i].g)>.003f||Mathf.Abs(actual[i].b-expected[i].b)>.003f||Mathf.Abs(actual[i].a-expected[i].a)>.003f)return false;
            return true;
        }
        public void ResetPalette(){EnsurePalettes();palettes[CurrentStyle]=new PaletteState{colors=layouts[CurrentStyle].Colors()};ChangedPalette();}
        public void ReloadPalettePreferences(){palettes=null;Apply();AppearanceChanged?.Invoke();}
        public void FlushPalette(){if(!pendingSave)return;pendingSave=false;PlayerPrefs.Save();}
        void ChangedPalette()
        {
            if(Application.isPlaying)
            {
                PlayerPrefs.SetString(PalettePreference+CurrentStyle,JsonUtility.ToJson(palettes[CurrentStyle]));
                if(!pendingSave)saveAt=Time.unscaledTime+.35f;pendingSave=true;
            }
            ApplyColors();AppearanceChanged?.Invoke();
        }
        PaletteState PresetForLayout(int index)
        {
            var result=new PaletteState{colors=(Color[])presets[index].colors.Clone()};
            var defaults=layouts[CurrentStyle].Colors();
            for(int i=0;i<4;i++)result.colors[i].a=defaults[i].a;
            return result;
        }
        void EnsurePalettes()
        {
            if(palettes!=null||layouts==null||layouts.Length==0)return;
            palettes=new PaletteState[layouts.Length];
            for(int i=0;i<palettes.Length;i++)
            {
                palettes[i]=new PaletteState{colors=layouts[i].Colors()};
                if(!Application.isPlaying||!PlayerPrefs.HasKey(PalettePreference+i))continue;
                try
                {
                    var saved=JsonUtility.FromJson<PaletteState>(PlayerPrefs.GetString(PalettePreference+i));
                    if(saved==null||saved.version!=1||saved.colors==null||saved.colors.Length!=LayerNames.Length)continue;
                    bool valid=true;foreach(var color in saved.colors)valid&=Valid(color);
                    if(valid)palettes[i]=saved;
                }
                catch(ArgumentException){ }
            }
        }
        static bool Valid(Color c)=>Finite(c.r)&&Finite(c.g)&&Finite(c.b)&&Finite(c.a);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0&&value<=1;
        public void ConfigureBaked(SceneSettingsSidebar value,UnityEngine.UI.Image background,UnityEngine.UI.Image headingSurface,TMP_Text heading,TMP_Text[] groups,SceneSettingsSidebarRow[] rows,UnityEngine.UI.Button[] buttons,TMP_Text[] captions,UnityEngine.UI.Button[] iconButtons,TMP_Text[] iconCaptions,LayoutVariant[] variants,UnityEngine.UI.Button appearance,TMP_Text appearanceText,PaletteState[] colorPresets)
        {
            sidebar=value;surface=background;header=headingSurface;title=heading;groupLabels=groups;styledRows=rows;choices=buttons;choiceLabels=captions;
            iconChoices=iconButtons;iconLabels=iconCaptions;layouts=variants;appearanceButton=appearance;appearanceCaption=appearanceText;presets=colorPresets;
            palettes=null;CurrentStyle=0;CurrentIconFamily=0;Apply();
        }
        public void Apply()
        {
            if(!surface||!sidebar||layouts==null||layouts.Length==0)return;
            foreach(var pose in layouts[CurrentStyle].poses)pose.Apply();
            ApplyColors();
        }
        void ApplyColors()
        {
            if(!surface||!sidebar||layouts==null||layouts.Length==0)return;
            EnsurePalettes();var c=palettes[CurrentStyle].colors;var layout=layouts[CurrentStyle];
            surface.color=c[0];surface.raycastTarget=c[0].a>.01f;if(header)header.color=c[1];title.color=c[4];
            if(groupLabels!=null)foreach(var group in groupLabels)if(group)group.color=Color.Lerp(c[5],c[0],.30f);
            foreach(var row in styledRows)
            {
                if(!row||!row.band)continue;
                bool reset=row.band.resetAction;
                row.usePalette=true;row.hideMarker=true;
                row.normalInk=reset?c[9]:c[5];row.selectedInk=c[7];
                row.normalIcon=reset?c[9]:c[6];row.selectedIcon=c[8];
                row.normalSecondary=c[6];row.selectedSecondary=c[8];
                row.normalIconBackground=row.band.action?c[10]:c[2];row.selectedIconBackground=c[3];
                row.icon.useRedesign=true;row.icon.family=CurrentIconFamily;row.icon.SetVerticesDirty();
                var band=row.band;band.usePalette=true;band.showIdle=true;band.cornerRadius=0;
                band.idleFill=band.action?c[10]:c[2];band.selectedFill=c[3];band.SetVerticesDirty();row.Draw(true);
            }
            if(appearanceButton){appearanceButton.targetGraphic.color=c[10];appearanceCaption.color=c[5];}
            PaintChoices(choices,choiceLabels,CurrentStyle);
            PaintChoices(iconChoices,iconLabels,CurrentIconFamily);
        }
        static void PaintChoices(UnityEngine.UI.Button[] buttons,TMP_Text[] labels,int selection)
        {
            if(buttons==null)return;
            for(int i=0;i<buttons.Length;i++)
            {
                buttons[i].targetGraphic.color=i==selection?SceneConfigUi.TabOn:SceneConfigUi.TabOff;
                labels[i].color=Color.white;
            }
        }
        [Serializable]public sealed class PaletteState{public int version=1;public Color[] colors;}
        [Serializable]
        public sealed class LayoutVariant
        {
            public string name;
            public RectPose[] poses;
            public Color surface,ink,muted,icon,selectedInk,selectedFill,idleFill,actionFill,accent,danger,headerFill,selectedIcon,titleInk;
            public bool showIdle;
            public float radius;
            public Color[] Colors()=>new[]{surface,headerFill,idleFill,selectedFill,titleInk,ink,icon,selectedInk,selectedIcon,danger,actionFill};
        }
        [Serializable]
        public sealed class RectPose
        {
            public RectTransform target;
            public Vector2 anchorMin,anchorMax,pivot,position,size;
            public bool active=true;
            public bool preserveActive;
            public float fontSize;
            public TextAlignmentOptions alignment=TextAlignmentOptions.MidlineLeft;
            public FontStyles fontStyle=FontStyles.Normal;
            public void Apply()
            {
                if(!target)return;
                if(!preserveActive)target.gameObject.SetActive(active);target.anchorMin=anchorMin;target.anchorMax=anchorMax;target.pivot=pivot;
                target.anchoredPosition=position;target.sizeDelta=size;
                if(fontSize>0&&target.TryGetComponent<TMP_Text>(out var text))
                {text.enableAutoSizing=false;text.fontSize=fontSize;text.alignment=alignment;text.fontStyle=fontStyle;}
            }
        }
    }
}
