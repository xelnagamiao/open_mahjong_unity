using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>Frame controls share the cloth header layout and never create full-size color textures.</summary>
public sealed class TableFrameHeader : MonoBehaviour
{
    static readonly Color Ink = new Color32(40,58,78,255);
    static readonly Color Dark = new Color32(38,44,56,255);
    static readonly Color Accent = new Color32(241,183,121,255);
    RectTransform header, gallery, title, picker;
    readonly RectTransform[] rows = new RectTransform[4];
    readonly RectTransform[] captions = new RectTransform[4];
    readonly RectTransform[] controls = new RectTransform[4];
    TMP_FontAsset font;
    Button colorButton;
    Image swatch;
    TMP_Text colorCaption;
    Slider shadow, highlight;
    TMP_Text shadowValue, highlightValue;
    TMP_Dropdown outline;
    readonly Slider[] channels = new Slider[4];
    readonly TMP_Text[] values = new TMP_Text[4];
    bool syncing, changed, ready;
    string selectedStyle;

    public void Initialize(RectTransform headerRect, RectTransform galleryRect, TMP_FontAsset textFont)
    {
        if (header != null) return;
        header=headerRect; gallery=galleryRect; font=textFont;
        title=header.GetComponentInChildren<TMP_Text>().rectTransform;
        for(int i=0;i<4;i++)
        {
            rows[i]=Rect("FrameControl"+i,header);
            captions[i]=Text(new[]{"底色","内侧阴影","高光","边框描边"}[i],rows[i],Ink,17).rectTransform;
            controls[i]=Rect("Control",rows[i]);
        }
        colorButton=Button("调色",controls[0],()=> { picker.gameObject.SetActive(!picker.gameObject.activeSelf); RefreshSelection(); });
        Fill((RectTransform)colorButton.transform);
        colorCaption=colorButton.GetComponentInChildren<TMP_Text>();
        swatch=Rect("Swatch",colorButton.transform).gameObject.AddComponent<Image>();
        swatch.raycastTarget=false;
        Place(swatch.rectTransform,10,9,22,22);
        shadow=Slider(controls[1],0,100,v=>Edit(()=>ConfigManager.Instance.SetTableFrameShadowIntensity(v/100)),out shadowValue);
        highlight=Slider(controls[2],0,100,v=>Edit(()=>ConfigManager.Instance.SetTableFrameHighlightIntensity(v/100)),out highlightValue);
        var template=TableSeamSelector.FindTemplate(GetComponentInParent<Canvas>());
        if(template!=null)
        {
            outline=Instantiate(template,controls[3],false);
            outline.name="FrameContactOutlineDropdown";
            outline.transform.localScale=Vector3.one;
            outline.onValueChanged=new TMP_Dropdown.DropdownEvent();
            outline.MultiSelect=false;
            outline.ClearOptions(); outline.AddOptions(new System.Collections.Generic.List<string>{"关闭","开启"});
            outline.onValueChanged.AddListener(v=>Edit(()=>ConfigManager.Instance.SetTableContactOutlineEnabled(v==1)));
            outline.template.gameObject.SetActive(false);
            foreach(Transform child in outline.transform) if(child.name=="Dropdown List") {child.gameObject.SetActive(false);Destroy(child.gameObject);}
            TableSeamSelector.ConfigureDropdownAppearance(outline);
            foreach(var text in new[]{outline.captionText,outline.itemText})
            {text.color=new Color32(238,243,250,255);text.enableVertexGradient=false;text.fontSize=18;text.enableAutoSizing=true;text.fontSizeMin=13;text.fontSizeMax=18;}
            Fill((RectTransform)outline.transform); outline.gameObject.SetActive(true);
        }
        BuildPicker(); ready=true; Layout(); RefreshSelection();
    }

    void BuildPicker()
    {
        picker=Rect("FrameColorPicker",transform);
        var background=picker.gameObject.AddComponent<Image>(); background.color=SceneConfigUi.SurfaceHeaderBackground;
        var heading=Text("边框底色",picker,Ink,22); Place(heading.rectTransform,20,12,220,32);
        var close=Button("关闭",picker,()=>{picker.gameObject.SetActive(false);Flush();});
        var closeRect=(RectTransform)close.transform; closeRect.anchorMin=closeRect.anchorMax=closeRect.pivot=Vector2.one;
        closeRect.anchoredPosition=new Vector2(-14,-12);closeRect.sizeDelta=new Vector2(72,32);
        for(int i=0;i<4;i++)
        {
            int index=i;
            var label=Text(new[]{"R","G","B","明暗"}[i],picker,Ink,18);Place(label.rectTransform,20,60+i*50,50,40);
            var row=Rect("Channel"+i,picker);row.anchorMin=new Vector2(0,1);row.anchorMax=Vector2.one;row.pivot=new Vector2(0,1);
            row.offsetMin=new Vector2(76,-100-i*50);row.offsetMax=new Vector2(-20,-60-i*50);
            channels[i]=Slider(row,i==3?-100:0,i==3?100:255,v=>SetChannel(index,v),out values[i]);
        }
        picker.gameObject.SetActive(false);
    }

    void SetChannel(int channel,float value)
    {
        Edit(()=> {
            var config=ConfigManager.Instance;
            if(channel==3) config.SetTableFrameBrightness(selectedStyle,value/100);
            else {Color color=config.GetTableFrameColor(selectedStyle);color[channel]=value/255;config.SetTableFrameColor(selectedStyle,color);}
        });
    }

    void Edit(UnityAction action)
    {
        if(syncing || SceneConfigColorUi.IsLayoutRefresh || ConfigManager.Instance==null) return;
        action(); changed=true;
        Desktop.Instance?.RefreshAppearance();
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        if(!ready) return;
        var config=ConfigManager.Instance;
        if(config==null) return;
        var selected=config.GetSelectedTableEdge();
        if(selectedStyle!=selected.path && picker!=null) picker.gameObject.SetActive(false);
        selectedStyle=selected.path;
        bool solid=!selected.isCustom && TableFrameStyles.IsSolid(selectedStyle);
        syncing=true;
        colorButton.interactable=solid;
        colorCaption.text=solid?"调色":"图片模式";
        swatch.gameObject.SetActive(solid);
        if(solid) swatch.color=config.GetTableFrameDisplayColor(selectedStyle);
        shadow.interactable=highlight.interactable=!selected.isCustom;
        shadow.SetValueWithoutNotify(config.GetTableFrameShadowIntensity()*100);
        highlight.SetValueWithoutNotify(config.GetTableFrameHighlightIntensity()*100);
        shadowValue.text=Mathf.RoundToInt(shadow.value)+"%";
        highlightValue.text=Mathf.RoundToInt(highlight.value)+"%";
        if(outline!=null) {outline.interactable=!selected.isCustom;outline.SetValueWithoutNotify(config.GetTableContactOutlineEnabled()?1:0);}
        if(picker!=null)
        {
            if(!solid) picker.gameObject.SetActive(false);
            var color=config.GetTableFrameColor(selectedStyle);
            for(int i=0;i<3;i++) SceneConfigColorUi.SyncChannel(channels[i],values[i],color[i]);
            SceneConfigColorUi.SyncBrightness(channels[3],values[3],config.GetTableFrameBrightness(selectedStyle));
        }
        syncing=false;
    }

    void OnEnable() { RefreshSelection(); Layout(); }
    void OnDisable() { if(picker!=null)picker.gameObject.SetActive(false); if(outline!=null)outline.Hide(); Flush(); }
    void OnApplicationPause(bool paused) {if(paused)Flush();}
    void OnApplicationQuit()=>Flush();
    void Flush() {if(changed) {PlayerPrefs.Save();changed=false;}}
    void OnRectTransformDimensionsChange()=>Layout();
    void Layout()
    {
        if(!ready) return;
        float width=((RectTransform)transform).rect.width;
        bool above=width<600;
        float left=above?0:120, top=above?44:0, available=width-left;
        int columns=available>=1050?4:available>=600?2:1;
        bool stacked=columns>1 || available<320;
        float rowHeight=stacked?64:40;
        int count=Mathf.CeilToInt(4f/columns);
        float height=top+16+count*rowHeight+(count-1)*8;
        header.anchorMin=new Vector2(0,1);header.anchorMax=Vector2.one;
        header.offsetMin=new Vector2(0,-height);header.offsetMax=Vector2.zero;
        gallery.offsetMax=new Vector2(gallery.offsetMax.x,-height);
        Place(title,20,8,above?Mathf.Max(1,width-40):88,above?32:height-16);
        for(int i=0;i<4;i++)
        {
            float cell=Mathf.Max(1,(available-16-(columns-1)*12)/columns);
            Place(rows[i],left+8+i%columns*(cell+12),top+8+i/columns*(rowHeight+8),cell,rowHeight);
            Place(captions[i],0,0,stacked?cell:96,stacked?22:40);
            Place(controls[i],stacked?0:104,stacked?24:0,Mathf.Max(1,cell-(stacked?0:104)),40);
        }
        if(picker!=null)
        {
            picker.anchorMin=picker.anchorMax=picker.pivot=new Vector2(1,1);
            picker.anchoredPosition=new Vector2(-12,-height-8);
            picker.sizeDelta=new Vector2(Mathf.Min(400,width-24),278);
            picker.SetAsLastSibling();
        }
    }

    RectTransform Rect(string name,Transform parent)
    {
        var go=new GameObject(name,typeof(RectTransform));go.layer=gameObject.layer;
        var rect=(RectTransform)go.transform;rect.SetParent(parent,false);return rect;
    }
    TMP_Text Text(string caption,Transform parent,Color color,float size)
    {
        var text=Rect(caption,parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font;text.text=caption;text.fontSize=size;text.color=color;text.raycastTarget=false;
        text.alignment=TextAlignmentOptions.MidlineLeft;text.textWrappingMode=TextWrappingModes.NoWrap;return text;
    }
    Button Button(string caption,Transform parent,UnityAction action)
    {
        var rect=Rect(caption,parent);var image=rect.gameObject.AddComponent<Image>();image.color=Dark;
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);
        var text=Text(caption,rect,Color.white,18);text.alignment=TextAlignmentOptions.Center;Fill(text.rectTransform);
        return button;
    }
    Slider Slider(RectTransform parent,float min,float max,UnityAction<float> action,out TMP_Text value)
    {
        var root=Rect("Slider",parent);root.anchorMax=Vector2.one;root.anchorMin=Vector2.zero;root.offsetMin=Vector2.zero;root.offsetMax=new Vector2(-48,0);
        var hit=root.gameObject.AddComponent<Image>();hit.color=Dark;
        var track=Rect("Track",root);track.anchorMin=new Vector2(0,.5f);track.anchorMax=new Vector2(1,.5f);track.offsetMin=new Vector2(10,-2);track.offsetMax=new Vector2(-10,2);
        track.gameObject.AddComponent<Image>().color=new Color32(98,116,137,255);
        var area=Rect("HandleArea",root);area.anchorMin=new Vector2(0,.5f);area.anchorMax=new Vector2(1,.5f);area.offsetMin=new Vector2(10,-12);area.offsetMax=new Vector2(-10,12);
        var handle=Rect("Handle",area);handle.sizeDelta=new Vector2(12,0);var graphic=handle.gameObject.AddComponent<Image>();graphic.color=Accent;
        var slider=root.gameObject.AddComponent<Slider>();slider.handleRect=handle;slider.targetGraphic=graphic;
        slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;slider.minValue=min;slider.maxValue=max;slider.wholeNumbers=true;
        slider.onValueChanged.AddListener(action);
        value=Text("0",parent,Ink,16);value.alignment=TextAlignmentOptions.Center;
        value.rectTransform.anchorMin=new Vector2(1,0);value.rectTransform.anchorMax=Vector2.one;
        value.rectTransform.offsetMin=new Vector2(-48,0);value.rectTransform.offsetMax=Vector2.zero;
        return slider;
    }
    static void Fill(RectTransform rect) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static void Place(RectTransform rect,float x,float y,float width,float height)
    {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
}
