#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public sealed partial class TableFrameHeader
{
    public void BakeLayout(RectTransform headerRect, RectTransform galleryRect, TMP_FontAsset textFont)
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
        colorEditor=TableSurfaceColorEditor.Ensure(GetComponent<TableEdgePanel>());
        colorEditor.InitializeControl(controls[0],font);
        shadow=Slider(controls[1],0,100,null,out shadowValue);
        highlight=Slider(controls[2],0,100,null,out highlightValue);
        var template=TableSeamSelector.FindTemplate(GetComponentInParent<Canvas>());
        if(template!=null)
        {
            outline=Instantiate(template,controls[3],false);
            outline.name="FrameContactOutlineDropdown";
            outline.transform.localScale=Vector3.one;
            outline.onValueChanged=new TMP_Dropdown.DropdownEvent();
            outline.MultiSelect=false;
            outline.ClearOptions(); outline.AddOptions(new System.Collections.Generic.List<string>{"关闭","开启"});
            outline.template.gameObject.SetActive(false);
            foreach(Transform child in outline.transform) if(child.name=="Dropdown List") {child.gameObject.SetActive(false);Destroy(child.gameObject);}
            TableSeamSelector.ConfigureDropdownAppearance(outline);
            foreach(var text in new[]{outline.captionText,outline.itemText})
            {text.color=new Color32(238,243,250,255);text.enableVertexGradient=false;text.fontSize=18;text.enableAutoSizing=true;text.fontSizeMin=13;text.fontSizeMax=18;}
            Fill((RectTransform)outline.transform); outline.gameObject.SetActive(true);
        }
        Layout();
    }

    void Layout()
    {
        if(!HasBakedUi) return;
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
        if (action != null) slider.onValueChanged.AddListener(action);
        value=Text("0",parent,Ink,16);value.alignment=TextAlignmentOptions.Center;
        value.rectTransform.anchorMin=new Vector2(1,0);value.rectTransform.anchorMax=Vector2.one;
        value.rectTransform.offsetMin=new Vector2(-48,0);value.rectTransform.offsetMax=Vector2.zero;
        return slider;
    }
    static void Fill(RectTransform rect) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static void Place(RectTransform rect,float x,float y,float width,float height)
    {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
}
#endif
