#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Shape=Salasasa.Home.SettingsShapeGraphic.Shape;
namespace Salasasa.Home
{
    public sealed partial class SettingsStyleCollection
    {
        Palette palette;
        public SettingsChoiceAppearance[] BakeChoiceStates(SettingsStyleField view)
        {
            int value=view.dropdown.value;bool editable=view.dropdown.interactable;
            var result=new SettingsChoiceAppearance[4];
            for(int state=0;state<4;state++)
            {
                view.dropdown.interactable=state<2;view.dropdown.SetValueWithoutNotify(state%2==1?0:1);
                PaintAuthoringChoice(view);
                var button=view.options[0];var shape=button.GetComponent<SettingsShapeGraphic>();
                result[state]=new SettingsChoiceAppearance{fill=shape.color,border=shape.borderColor,borderWidth=shape.borderWidth,radius=shape.radius,shape=shape.shape,text=button.GetComponentInChildren<TMP_Text>(true).color,interaction=button.colors};
            }
            view.dropdown.interactable=editable;view.dropdown.SetValueWithoutNotify(value);PaintAuthoringChoice(view);
            return result;
        }
        sealed class Palette
        {
            public Color paper,ink,muted,line,accent,onAccent,well,row,tab,header,shadow;
            public Shape panelShape,buttonShape;
            public float radius,width,headerHeight,rowHeight;
            public Palette(string paper,string ink,string muted,string line,string accent,string onAccent,string well,string row,string tab,string header,Shape panelShape,Shape buttonShape,float radius,float width,float headerHeight,float rowHeight)
            {
                this.paper=Hex(paper);this.ink=Hex(ink);this.muted=Hex(muted);this.line=Hex(line);this.accent=Hex(accent);this.onAccent=Hex(onAccent);this.well=Hex(well);this.row=Hex(row);this.tab=Hex(tab);this.header=Hex(header);
                this.panelShape=panelShape;this.buttonShape=buttonShape;this.radius=radius;this.width=width;this.headerHeight=headerHeight;this.rowHeight=rowHeight;shadow=Hex("0B111D66");
            }
        }
        static Palette Theme(int index)
        {
            switch(index)
            {
                case 1:return new Palette("F9FCFD","263D58","526882","B6C5D4","41679E","FFFFFF","FFFFFF","EDF2F7","E8EDF4","F9FCFD",Shape.Round,Shape.Square,10,1120,116,54);
                case 2:return new Palette("21222A","FFFCF6","C9C5CE","77727E","B44D6B","FFF9F2","34343F","2B2C34","363541","21222A",Shape.Square,Shape.Square,0,1136,94,54);
                default:return new Palette("F4EADB","362D29","806F61","D4BCA0","BA4930","FFF6E7","FFFBF2","EBDDCA","E7D6BD","F4EADB",Shape.Cut,Shape.Slant,12,1120,94,54);
            }
        }
        public void BakeLayout(int variant, int category)
        {
            Variant=variant;Category=category;
            if(stage==null||controls==null||controls.Length==0)return;
            palette=Theme(Variant);Fit();var p=palette;
            float width=p.width,inside=width-56;
            if(handPreview!=null)handPreview.gameObject.SetActive(Category==2);
            string[][] headings={new[]{"音量","提示音效"},new[]{"对局操作","观战与直播"},new[]{"理牌顺序","画面表现","运行与语言"}};
            foreach(var view in controls)view.root.gameObject.SetActive(view.category==Category);
            float y=0;
            for(int g=0;g<sections.Length;g++)
            {
                var section=sections[g];bool active=g<headings[Category].Length;section.root.gameObject.SetActive(active);if(!active)continue;
                var fields=controls.Where(c=>c.category==Category&&c.section==g).ToArray();
                int columns=Category==2&&g==2?3:2;
                float x=0,groupWidth=inside;
                float row=fields[0].slider!=null?68:p.rowHeight;
                int rows=Mathf.CeilToInt(fields.Length/(float)columns);
                float head=32,gap=4;
                float height=head+6+rows*row+(rows-1)*gap;
                float fieldsHeight=height;
                if(Category==2&&g==0)height+=112;
                Place(section.root,x,y,groupWidth,height);section.title.text=headings[Category][g];
                StyleSection(section,groupWidth,height,head);
                float cell=(groupWidth-(columns-1)*12)/columns;
                for(int n=0;n<fields.Length;n++)
                {
                    var view=fields[n];view.root.SetParent(section.root,false);
                    float cellWidth=cell;
                    Place(view.root,n%columns*(cell+12),head+6+n/columns*(row+gap),cellWidth,row);
                    StyleField(view,cellWidth,row);
                }
                if(Category==2&&g==0&&handPreview!=null)
                {
                    handPreview.transform.SetParent(section.root,false);
                    Place((RectTransform)handPreview.transform,0,fieldsHeight+8,groupWidth,104);
                    handPreview.Layout(groupWidth,sans,p.muted,p.row);
                }
                y+=height+16;
            }
            float contentHeight=y-16;
            float panelHeight=p.headerHeight+contentHeight+26;
            float panelY=Mathf.Max(24,(756-panelHeight)/2);
            Place(panel,(1600-width)/2,panelY,width,panelHeight);
            Place(panelShadow.rectTransform,4,6,width,panelHeight);
            panelShadow.Paint(p.shadow,Color.clear,0,p.panelShape,p.radius);
            Place(panelSurface.rectTransform,0,0,width,panelHeight);
            panelSurface.Paint(p.paper,p.line,1,p.panelShape,p.radius);
            Place(ornament.rectTransform,4,4,width-8,panelHeight-8);
            ornament.gameObject.SetActive(false);
            Place(headerSurface.rectTransform,16,16,width-32,p.headerHeight-30);
            headerSurface.Paint(p.header,Color.clear,0,p.panelShape,p.radius-4);
            SetText(title,Variant==2?sans:bold,30,p.ink);
            Place(title.rectTransform,Variant==0?40:28,Variant==1?18:22,152,40);
            title.alignment=TextAlignmentOptions.MidlineLeft;
            Place(titleAccent.rectTransform,27,30,3,26);
            titleAccent.gameObject.SetActive(Variant==0);
            titleAccent.Paint(p.accent,Color.clear,0,Shape.Square);
            float tabY=Variant==1?66:24;
            float tabWidth=Variant==1?(width-56)/3:(width-316)/3;
            float tabGap=6;
            if(Variant==1)tabWidth=(width-68)/3;
            if(Variant==0)tabWidth=218;
            float tabX=Variant==1?28:202;
            for(int i=0;i<tabs.Length;i++)
            {Place((RectTransform)tabs[i].transform,tabX+i*(tabWidth+tabGap),tabY,tabWidth,44);PaintButton(tabs[i],i==Category,22,true);}
            BakeCloseButton(close,Variant,width);
            divider.gameObject.SetActive(Variant==0);
            Place(divider.rectTransform,28,p.headerHeight-14,width-56,1);divider.Paint(p.line,Color.clear,0,Shape.Square);
            Place(content,28,p.headerHeight,inside,contentHeight);
            Place(reviewStrip,510,774,580,48);Place(reviewSurface.rectTransform,0,0,580,48);
            reviewSurface.Paint(Hex("161C2AEA"),Hex("6B748160"),1,Shape.Round,9);
            for(int i=0;i<variants.Length;i++)
            {
                var button=variants[i];int position=System.Array.IndexOf(DisplayVariantOrder,i);
                Place((RectTransform)button.transform,6+position*190,5,184,38);
                var graphic=button.GetComponent<SettingsShapeGraphic>();
                graphic.Paint(i==Variant?p.accent:Color.clear,i==Variant?Alpha(p.onAccent,.2f):Color.clear,1,Shape.Round,5);
                SetText(button.GetComponentInChildren<TMP_Text>(),sans,19,i==Variant?p.onAccent:Hex("D7DFE8"));
                button.colors=InteractionColors();
            }

        }
        public static void BakeCloseButton(RectTransform buttonRect,int variant,float panelWidth)
        {
            var button=buttonRect.GetComponent<UnityEngine.UI.Button>();
            var surface=buttonRect.GetComponent<SettingsShapeGraphic>();
            var icon=buttonRect.GetComponentInChildren<InkGraphic>(true);
            if(button==null||surface==null||icon==null)throw new System.InvalidOperationException("Existing settings close control is incomplete.");
            Color normal,hover,pressed,edge,ink;
            if(variant==1)
            {
                normal=Hex("F9FCFD");hover=Hex("E6EDF7");pressed=Hex("D1DDEE");
                edge=Hex("8FA6C1");ink=Hex("345781");
            }
            else if(variant==2)
            {
                normal=Hex("302D37");hover=Hex("47313E");pressed=Hex("5D3549");
                edge=Hex("8E6378");ink=Hex("FFF5F1");
            }
            else
            {
                normal=Hex("BA4930");hover=Hex("CE5E40");pressed=Hex("983723");
                edge=Hex("8F3824");ink=Hex("FFF6E7");
            }
            var shape=variant==0?Shape.Cut:Shape.Square;
            float cut=variant==0?5:0;
            Place(buttonRect,panelWidth-76,variant==1?12:20,48,48);
            surface.Paint(Color.white,Color.clear,0,shape,cut);surface.raycastTarget=true;
            button.targetGraphic=surface;button.transition=UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors=UnityEngine.UI.ColorBlock.defaultColorBlock;
            colors.normalColor=normal;colors.highlightedColor=hover;colors.pressedColor=pressed;colors.selectedColor=hover;
            colors.disabledColor=Color.Lerp(normal,Hex(variant==2?"21222A":"E8E6E1"),.6f);
            colors.colorMultiplier=1;colors.fadeDuration=.1f;button.colors=colors;
            var border=buttonRect.Find("CloseBorder")?.GetComponent<SettingsShapeGraphic>();
            if(border==null)
            {
                var go=new GameObject("CloseBorder",typeof(RectTransform),typeof(CanvasRenderer),typeof(SettingsShapeGraphic));
                go.layer=buttonRect.gameObject.layer;go.transform.SetParent(buttonRect,false);
                UnityEditor.Undo.RegisterCreatedObjectUndo(go,"Refine settings close border");
                border=go.GetComponent<SettingsShapeGraphic>();
            }
            Place(border.rectTransform,0,0,48,48);border.Paint(Color.clear,edge,1,shape,cut);border.raycastTarget=false;
            border.transform.SetAsLastSibling();
            Place(icon.rectTransform,6,6,36,36);icon.form=InkGraphic.Form.Close;icon.stroke=2.25f;
            icon.color=ink;icon.raycastTarget=false;icon.transform.SetAsLastSibling();icon.SetVerticesDirty();
            var label=button.GetComponentInChildren<TMPro.TMP_Text>(true);
            if(label!=null&&string.IsNullOrEmpty(label.text))label.gameObject.SetActive(false);
        }
        void StyleSection(SettingsStyleSection section,float width,float height,float head)
        {
            var p=palette;
            Place(section.surface.rectTransform,0,head+6,width,height-head-6);
            section.surface.Paint(Color.clear,Color.clear,0,Shape.Square);
            Place(section.header,0,0,width,head);
            float titleBarWidth=Variant==0?182:width;
            Place(section.headerSurface.rectTransform,0,0,titleBarWidth,head);
            var fill=Variant==0?p.accent:Variant==1?Hex("E2EAF4"):Hex("44323D");
            section.headerSurface.Paint(fill,Color.clear,0,Variant==0?Shape.Slant:Shape.Square,8);
            Place(section.title.rectTransform,Variant==0?20:12,0,titleBarWidth-24,head);
            SetText(section.title,bold,21,Variant==0?p.onAccent:Variant==2?Hex("FFF3F1"):p.ink);
            section.title.alignment=TextAlignmentOptions.MidlineLeft;
            section.accent.gameObject.SetActive(false);
            float ruleX=Variant==0?194:0;
            Place(section.rule.rectTransform,ruleX,head-1,width-ruleX,1);
            section.rule.gameObject.SetActive(true);
            section.rule.Paint(Alpha(p.line,.7f),Color.clear,0,Shape.Square);
        }
        void StyleField(SettingsStyleField view,float width,float height)
        {
            var p=palette;
            view.surface.Paint(Variant==0?p.row:Color.clear,Color.clear,0,Shape.Square);
            Place(view.rowRule.rectTransform,0,height-1,width,1);
            view.rowRule.gameObject.SetActive(true);
            view.rowRule.Paint(Alpha(p.line,.48f),Color.clear,0,Shape.Square);
            SetText(view.label,sans,20,p.ink);
            view.label.alignment=TextAlignmentOptions.MidlineLeft;
            view.label.enableAutoSizing=false;view.label.textWrappingMode=TextWrappingModes.NoWrap;
            if(view.slider!=null)
            {
                Place(view.label.rectTransform,16,2,width-100,34);
                Place(view.value.rectTransform,width-84,2,68,34);SetText(view.value,sans,20,p.accent);view.value.alignment=TextAlignmentOptions.MidlineRight;
                Place(view.control,18,height-32,width-36,28);StyleSlider(view);return;
            }
            float labelWidth=width<400?104:182;
            float inputX=labelWidth+14,inputWidth=width-inputX-8;
            Place(view.label.rectTransform,8,0,labelWidth,height);
            Place(view.control,inputX,(height-38)/2,inputWidth,38);
            Place(view.choices,inputX,(height-38)/2,inputWidth,38);
            StyleDropdown(view);
            view.choiceSurface.gameObject.SetActive(Variant!=0);
            Place(view.choiceSurface.rectTransform,0,0,inputWidth,38);
            view.choiceSurface.Paint(p.well,p.line,1,Shape.Square,0);
            for(int i=0;i<view.options.Length;i++)
            {
                if(Variant==0)Place((RectTransform)view.options[i].transform,i*(inputWidth+4)/2,0,(inputWidth-4)/2,38);
                else Place((RectTransform)view.options[i].transform,1+i*(inputWidth-2)/2,1,(inputWidth-2)/2,36);
            }
            PaintAuthoringChoice(view);
        }
        void StyleSlider(SettingsStyleField view)
        {
            var p=palette;var slider=view.slider;
            var track=slider.transform.Find("Background")?.GetComponent<Image>();if(track!=null)track.color=p.line;
            if(slider.fillRect!=null)slider.fillRect.GetComponent<Image>().color=p.accent;
            if(view.handleSurface!=null)view.handleSurface.Paint(Variant==0?p.accent:p.well,p.accent,Variant==0?0:2,Shape.Square,0);
            slider.colors=InteractionColors();
        }
        void StyleDropdown(SettingsStyleField view)
        {
            var p=palette;var dropdown=view.dropdown;
            view.inputSurface.enabled=!view.UseSegments;
            dropdown.captionText.enabled=!view.UseSegments;
            if(view.dropdownChevron!=null)view.dropdownChevron.enabled=!view.UseSegments;
            view.inputSurface.Paint(dropdown.interactable?p.well:p.row,p.line,1,Variant==0?Shape.Cut:Shape.Square,Variant==0?5:0);
            SetText(dropdown.captionText,sans,20,dropdown.interactable?p.ink:p.muted);
            dropdown.captionText.alignment=TextAlignmentOptions.MidlineLeft;
            dropdown.captionText.enableAutoSizing=true;dropdown.captionText.fontSizeMin=17;dropdown.captionText.fontSizeMax=20;
            if(view.dropdownChevron!=null)view.dropdownChevron.color=dropdown.interactable?p.accent:Color.clear;
            var inputColors=InteractionColors();inputColors.disabledColor=Color.white;dropdown.colors=inputColors;
            var template=dropdown.template;template.GetComponent<Image>().color=p.well;
            template.sizeDelta=new Vector2(0,Mathf.Min(264,dropdown.options.Count*40+8));
            SetText(dropdown.itemText,sans,20,p.ink);
            var item=dropdown.itemText.GetComponentInParent<Toggle>(true);
            var colors=InteractionColors();colors.highlightedColor=Color.Lerp(p.well,p.accent,.22f);colors.pressedColor=Color.Lerp(p.well,p.accent,.36f);colors.selectedColor=colors.highlightedColor;item.colors=colors;
            foreach(var image in template.GetComponentsInChildren<Image>(true))
            {
                if(image==item.graphic)image.color=p.accent;
                else if(image!=template.GetComponent<Image>())image.color=image==item.targetGraphic?Color.white:p.line;
            }
            if(item.targetGraphic!=null)item.targetGraphic.color=p.well;
        }
        void PaintAuthoringChoice(SettingsStyleField view)
        {
            if(!view.UseSegments||palette==null)return;
            for(int i=0;i<view.options.Length;i++)
            {
                var button=view.options[i];button.interactable=view.dropdown.interactable;
                PaintButton(button,view.dropdown.value==i,20,false);
                if(Variant!=0)button.GetComponent<SettingsShapeGraphic>().Paint(view.dropdown.value==i?palette.accent:Color.clear,Color.clear,0,Shape.Square,0);
                if(!view.dropdown.interactable)
                {
                    var colors=button.colors;colors.disabledColor=Color.white;button.colors=colors;
                    button.GetComponent<SettingsShapeGraphic>().Paint(view.dropdown.value==i?palette.tab:Variant==0?palette.paper:Color.clear,Variant==0?palette.line:Color.clear,Variant==0?1:0,palette.buttonShape,Variant==0?6:0);
                    button.GetComponentInChildren<TMP_Text>().color=view.dropdown.value==i?palette.ink:palette.muted;
                }
            }
        }
        void PaintButton(Button button,bool selected,float size,bool tab)
        {
            var p=palette;var shape=button.GetComponent<SettingsShapeGraphic>();
            Color fill=selected?p.accent:tab?p.tab:p.well;
            shape.Paint(fill,selected?fill:p.line,tab?0:1,p.buttonShape,Variant==0?(tab?8:6):0);
            var text=button.GetComponentInChildren<TMP_Text>();if(text!=null)
            {SetText(text,Variant==2&&tab?sans:tab?bold:sans,size,selected?p.onAccent:p.ink);text.alignment=TextAlignmentOptions.Center;}
            button.colors=InteractionColors();
        }
    }
}
#endif
