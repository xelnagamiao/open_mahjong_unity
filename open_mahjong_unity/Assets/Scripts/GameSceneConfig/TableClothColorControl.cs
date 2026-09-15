using TMPro;
using UnityEngine;

/// <summary>Color editor for parameter-only cloth presets; brightness never rewrites RGB.</summary>
public sealed class TableClothColorControl : MonoBehaviour
{
    static readonly Color Ink = new Color32(40,58,78,255);
    static readonly Color Dark = new Color32(38,44,56,255);
    UnityEngine.UI.Button button;
    TMP_Text caption;
    UnityEngine.UI.Image swatch;
    RectTransform popup, header;
    readonly UnityEngine.UI.Slider[] sliders = new UnityEngine.UI.Slider[4];
    readonly TMP_Text[] values = new TMP_Text[4];
    TMP_FontAsset font;
    TableClothPanel panel;
    string style;
    bool dirty;

    public void Initialize(RectTransform host, RectTransform headerRect, TableClothPanel owner, TMP_FontAsset textFont)
    {
        if(button!=null)return;
        header=headerRect;panel=owner;font=textFont;
        button=MakeButton("调色",host,Toggle);
        Fill((RectTransform)button.transform);
        caption=button.GetComponentInChildren<TMP_Text>();
        swatch=Rect("Swatch",button.transform).gameObject.AddComponent<UnityEngine.UI.Image>();
        swatch.raycastTarget=false;Place(swatch.rectTransform,10,9,22,22);
        popup=Rect("ClothColorPicker",header);
        popup.gameObject.AddComponent<UnityEngine.UI.Image>().color=SceneConfigUi.SurfaceHeaderBackground;
        Place(Label("桌布底色",popup,22).rectTransform,20,12,220,32);
        var close=MakeButton("关闭",popup,()=>{popup.gameObject.SetActive(false);Flush();});
        var closeRect=(RectTransform)close.transform;
        closeRect.anchorMin=closeRect.anchorMax=closeRect.pivot=Vector2.one;
        closeRect.anchoredPosition=new Vector2(-14,-12);closeRect.sizeDelta=new Vector2(72,32);
        for(int i=0;i<4;i++)
        {
            int channel=i;
            Place(Label(new[]{"R","G","B","明暗"}[i],popup,18).rectTransform,20,60+i*50,50,40);
            var row=Rect("Channel"+i,popup);
            row.anchorMin=new Vector2(0,1);row.anchorMax=Vector2.one;
            row.offsetMin=new Vector2(76,-100-i*50);row.offsetMax=new Vector2(-20,-60-i*50);
            var root=Rect("Slider",row);Fill(root);root.offsetMax=new Vector2(-48,0);
            root.gameObject.AddComponent<UnityEngine.UI.Image>().color=Dark;
            var track=Rect("Track",root);track.anchorMin=new Vector2(0,.5f);track.anchorMax=new Vector2(1,.5f);
            track.offsetMin=new Vector2(10,-2);track.offsetMax=new Vector2(-10,2);
            var trackImage=track.gameObject.AddComponent<UnityEngine.UI.Image>();trackImage.color=new Color32(98,116,137,255);trackImage.raycastTarget=false;
            var area=Rect("HandleArea",root);area.anchorMin=new Vector2(0,.5f);area.anchorMax=new Vector2(1,.5f);
            area.offsetMin=new Vector2(10,-12);area.offsetMax=new Vector2(-10,12);
            var handle=Rect("Handle",area);handle.sizeDelta=new Vector2(12,0);
            var graphic=handle.gameObject.AddComponent<UnityEngine.UI.Image>();graphic.color=new Color32(241,183,121,255);
            var slider=root.gameObject.AddComponent<UnityEngine.UI.Slider>();sliders[i]=slider;
            slider.handleRect=handle;slider.targetGraphic=graphic;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue=i==3?-100:0;slider.maxValue=i==3?100:255;slider.wholeNumbers=true;
            slider.onValueChanged.AddListener(v=>Edit(channel,v));
            values[i]=Label("0",row,16);values[i].alignment=TextAlignmentOptions.Center;
            var valueRect=values[i].rectTransform;valueRect.anchorMin=new Vector2(1,0);valueRect.anchorMax=Vector2.one;
            valueRect.offsetMin=new Vector2(-48,0);valueRect.offsetMax=Vector2.zero;
        }
        popup.gameObject.SetActive(false);RefreshSelection();
    }
    void Toggle(){RefreshSelection();if(button.interactable){popup.gameObject.SetActive(!popup.gameObject.activeSelf);Layout();if(!popup.gameObject.activeSelf)Flush();}}
    public void RefreshSelection()
    {
        if(button==null||popup==null)return;
        var config=ConfigManager.Instance;
        var selected=config!=null?config.GetSelectedTableCloth():("",false);
        if(style!=selected.Item1)popup.gameObject.SetActive(false);
        style=selected.Item1;
        bool solid=config!=null&&!selected.Item2&&TableClothStyles.IsSolid(style);
        button.interactable=solid;caption.text=solid?"调色":"图片模式";swatch.gameObject.SetActive(solid);
        if(!solid){popup.gameObject.SetActive(false);return;}
        swatch.color=config.GetTableClothDisplayColor(style);
        Color color=config.GetTableClothColor(style);
        for(int i=0;i<3;i++)SceneConfigColorUi.SyncChannel(sliders[i],values[i],color[i]);
        SceneConfigColorUi.SyncBrightness(sliders[3],values[3],config.GetTableClothBrightness(style));
    }
    void Edit(int channel,float value)
    {
        if(SceneConfigColorUi.IsLayoutRefresh||ConfigManager.Instance==null||!button.interactable)return;
        var config=ConfigManager.Instance;
        if(channel==3)config.SetTableClothBrightness(style,value/100);
        else {Color color=config.GetTableClothColor(style);color[channel]=value/255;config.SetTableClothColor(style,color);}
        dirty=true;RefreshSelection();Desktop.Instance?.RefreshAppearance();
        foreach(var item in panel.contentParent.GetComponentsInChildren<TableCloth>(true))item.RefreshSelection();
    }
    public void Layout()
    {
        if(popup==null)return;
        popup.anchorMin=popup.anchorMax=popup.pivot=new Vector2(0,1);
        popup.anchoredPosition=new Vector2(8,-header.rect.height-8);
        popup.sizeDelta=new Vector2(Mathf.Min(400,header.rect.width-16),278);popup.SetAsLastSibling();
    }
    void OnEnable()=>RefreshSelection();
    void OnDisable(){if(popup!=null)popup.gameObject.SetActive(false);Flush();}
    void OnApplicationPause(bool paused){if(paused)Flush();}
    void OnApplicationQuit()=>Flush();
    void Flush(){if(dirty){PlayerPrefs.Save();dirty=false;}}
    RectTransform Rect(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    TMP_Text Label(string text,Transform parent,float size){var label=Rect(text,parent).gameObject.AddComponent<TextMeshProUGUI>();label.text=text;label.font=font;label.fontSize=size;label.color=Ink;label.alignment=TextAlignmentOptions.MidlineLeft;label.textWrappingMode=TextWrappingModes.NoWrap;label.raycastTarget=false;return label;}
    UnityEngine.UI.Button MakeButton(string text,Transform parent,UnityEngine.Events.UnityAction action){var rect=Rect(text,parent);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Dark;var result=rect.gameObject.AddComponent<UnityEngine.UI.Button>();result.targetGraphic=image;result.onClick.AddListener(action);var label=Label(text,rect,18);label.color=Color.white;label.alignment=TextAlignmentOptions.Center;Fill(label.rectTransform);return result;}
    static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static void Place(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
}
