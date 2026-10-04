#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public sealed partial class TableSurfaceColorEditor
{
    public void BakePalette() { if (modal == null) Build(); UpgradePaletteLayout(); UpgradeDropdownLayout(); }
    private void UpgradePaletteLayout()
    {
        EnsureNameInput();
        if (modal == null) return;
        var box = modal.Find("Palette") as RectTransform;
        if (box != null) box.sizeDelta = new Vector2(440, 420);
        string[] labels = { "R", "G", "B", "明暗" };
        for (int i = 0; i < 4; i++)
        {
            if (channels != null && i < channels.Length && channels[i] != null)
                Place(channels[i].transform as RectTransform, 78, 108 + i * 49, 274, 36);
            if (values != null && i < values.Length && values[i] != null)
                Place(values[i].rectTransform, 362, 108 + i * 49, 60, 36);
            if (box != null)
            {
                var label = box.Find(labels[i]) as RectTransform;
                if (label != null) Place(label, 22, 108 + i * 49, 48, 36);
            }
        }
        if (box != null)
        {
            var backButton = box.Find("返回") as RectTransform;
            var confirmButton = box.Find("确认") as RectTransform;
            if (backButton != null) Place(backButton, 78, 346, 132, 40);
            if (confirmButton != null) Place(confirmButton, 230, 346, 132, 40);
        }
    }
    private void EnsureNameInput()
    {
        if (modal == null) return;
        var box = modal.Find("Palette") as RectTransform;
        if (box == null) return;
        var label = box.Find("名称") as RectTransform;
        if (label == null) label = Text("名称", box, 18).rectTransform;
        Place(label, 22, 58, 48, 36);
        var field = box.Find("NameInput") as RectTransform;
        if (field == null)
        {
            field = Rect("NameInput", box, new Color32(38, 44, 56, 255));
            nameInput = field.gameObject.AddComponent<TMP_InputField>();
            nameInput.targetGraphic = field.GetComponent<Image>();
            var viewport = Rect("Viewport", field); Fill(viewport); viewport.offsetMin = new Vector2(10, 5); viewport.offsetMax = new Vector2(-10, -5);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Text("", viewport, 18); text.name = "Text"; Fill(text.rectTransform); text.alignment = TextAlignmentOptions.MidlineLeft; text.color = new Color32(238, 243, 250, 255);
            var placeholder = Text("输入自定义名称", viewport, 18); placeholder.name = "Placeholder"; Fill(placeholder.rectTransform); placeholder.color = new Color32(150, 160, 176, 255);
            nameInput.textViewport = viewport; nameInput.textComponent = text; nameInput.placeholder = placeholder;
            nameInput.characterLimit = 24; nameInput.lineType = TMP_InputField.LineType.SingleLine;
        }
        else nameInput = field.GetComponent<TMP_InputField>();
        // Reapply to existing baked controls too; align with the RGB control column.
        Place(field, 78, 58, 274, 36);
        if (nameInput != null) {
            nameInput.richText = false;
            nameInput.textComponent.richText = false;
            nameInput.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            nameInput.gameObject.SetActive(true);
        }
    }
    public void UpgradeDropdownLayout()
    {
        if (mode == null || mode.template == null) return;
        foreach (var text in new[] { mode.captionText, mode.itemText }) {
            text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }
        var template = mode.template;
        template.anchorMin = Vector2.zero; template.anchorMax = new Vector2(1,0);
        template.pivot = new Vector2(.5f,1); template.anchoredPosition = new Vector2(0,-4);
        template.sizeDelta = new Vector2(0,88);
        var scroll = template.GetComponent<UnityEngine.UI.ScrollRect>();
        if (scroll == null || scroll.viewport == null || scroll.content == null) return;
        // Cloned controls carried a -112px viewport bottom inset and a 20px-wide handle.
        // Keep these two 40px options within the popup, independent of the source dropdown.
        Fill(scroll.viewport); scroll.viewport.offsetMin = new Vector2(4,4); scroll.viewport.offsetMax = new Vector2(-16,-4);
        scroll.content.anchorMin = new Vector2(0,1); scroll.content.anchorMax = Vector2.one;
        scroll.content.pivot = new Vector2(.5f,1); scroll.content.anchoredPosition = Vector2.zero;
        scroll.content.sizeDelta = new Vector2(0,40);
        var item = mode.itemText.GetComponentInParent<UnityEngine.UI.Toggle>(true).transform as RectTransform;
        Fill(item); item.pivot = new Vector2(.5f,.5f);
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        var bar = scroll.verticalScrollbar;
        if (bar != null) {
            var rail = (RectTransform)bar.transform;
            rail.anchorMin = new Vector2(1,0); rail.anchorMax = Vector2.one;
            rail.offsetMin = new Vector2(-10,4); rail.offsetMax = new Vector2(-4,-4);
            var area = (RectTransform)bar.handleRect.parent;
            if (area != rail) Fill(area);
            Fill(bar.handleRect);
            bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            bar.size = 1;
            foreach (var graphic in bar.GetComponentsInChildren<UnityEngine.UI.Image>(true)) {
                graphic.sprite = null; graphic.type = UnityEngine.UI.Image.Type.Simple;
            }
        }
    }
    public void InitializeControl(RectTransform host, TMP_FontAsset textFont)
    {
        if (mode != null) return;
        font = textFont;
        var template = TableSeamSelector.FindTemplate(GetComponentInParent<Canvas>());
        if (template == null) return;
        mode = Instantiate(template,host,false); mode.name = "SurfaceColorMode";
        mode.onValueChanged = new TMP_Dropdown.DropdownEvent(); mode.MultiSelect = false;
        mode.ClearOptions(); mode.AddOptions(new List<string>{"图片模式","新建颜色"});
        mode.template.gameObject.SetActive(false);
        mode.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,100);
        foreach (Transform child in mode.transform) if (child.name == "Dropdown List") {
            child.gameObject.SetActive(false); Destroy(child.gameObject);
        }
        TableSeamSelector.ConfigureDropdownAppearance(mode);
        foreach (var text in new[]{mode.captionText,mode.itemText}) {
            text.font=font; text.color=new Color32(238,243,250,255);text.enableVertexGradient=false;
            text.enableAutoSizing=true;text.fontSizeMin=13;text.fontSizeMax=18;text.textWrappingMode=TextWrappingModes.NoWrap;
        }
        Fill((RectTransform)mode.transform); mode.gameObject.SetActive(true);
        mode.SetValueWithoutNotify(0);
        UpgradeDropdownLayout();
    }

    void Build()
    {
        modal=Rect("NewSurfaceColor",transform,new Color(0,0,0,.45f));Fill(modal);
        var box=Rect("Palette",modal,SceneConfigUi.SurfaceHeaderBackground);
        box.anchorMin=box.anchorMax=box.pivot=new Vector2(.5f,.5f);box.sizeDelta=new Vector2(440,420);
        Place(Text(owner.IsClothSurface?"新建桌布颜色":"新建边框颜色",box,24).rectTransform,22,16,330,36);
        swatch=Rect("Preview",box,Color.white).GetComponent<Image>();Place(swatch.rectTransform,374,17,42,32);
        Place(Text("名称",box,18).rectTransform,22,60,48,36);
        EnsureNameInput();
        for(int i=0;i<4;i++){
            Place(Text(new[]{"R","G","B","明暗"}[i],box,18).rectTransform,22,108+i*49,48,36);
            var root=Rect("Channel"+i,box,new Color32(38,44,56,255));Place(root,78,108+i*49,274,36);
            var track=Rect("Track",root,new Color32(98,116,137,255));
            track.anchorMin=new Vector2(0,.5f);track.anchorMax=new Vector2(1,.5f);
            track.offsetMin=new Vector2(10,-2);track.offsetMax=new Vector2(-10,2);
            var area=Rect("HandleArea",root);Fill(area);area.offsetMin=new Vector2(10,8);area.offsetMax=new Vector2(-10,-8);
            var handle=Rect("Handle",area,new Color32(241,183,121,255));handle.sizeDelta=new Vector2(12,0);
            var slider=root.gameObject.AddComponent<Slider>();channels[i]=slider;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();
            slider.minValue=i==3?-100:0;slider.maxValue=i==3?100:255;slider.wholeNumbers=true;
            values[i]=Text("0",box,18);Place(values[i].rectTransform,362,108+i*49,60,36);values[i].alignment=TextAlignmentOptions.Center;
        }
        back=Button("返回",box,null);Place((RectTransform)back.transform,78,346,132,40);
        confirm=Button("确认",box,null);Place((RectTransform)confirm.transform,230,346,132,40);
        modal.gameObject.SetActive(false);
    }
    RectTransform Rect(string name,Transform parent,Color? color=null){var go=new GameObject(name,typeof(RectTransform));go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);if(color.HasValue)go.AddComponent<Image>().color=color.Value;return (RectTransform)go.transform;}
    TMP_Text Text(string caption,Transform parent,float size){var text=Rect(caption,parent).gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.text=caption;text.fontSize=size;text.color=Ink;text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;return text;}
    Button Button(string caption,Transform parent,UnityEngine.Events.UnityAction action){var rect=Rect(caption,parent,Color.white);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();var colors=ColorBlock.defaultColorBlock;colors.normalColor=SceneConfigUi.TabOff;colors.highlightedColor=SceneConfigUi.TabOn;colors.selectedColor=colors.highlightedColor;colors.pressedColor=SceneConfigUi.TabOff;colors.disabledColor=new Color32(65,71,83,255);button.colors=colors;var label=Text(caption,rect,20);label.color=Color.white;label.alignment=TextAlignmentOptions.Center;Fill(label.rectTransform);return button;}
    static void Fill(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static void Place(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
}
#endif
