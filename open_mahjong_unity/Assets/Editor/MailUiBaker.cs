#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MailUiBaker
{
    static TMP_FontAsset font;
    static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
    public static void Bake(Scene scene)
    {
        var owner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        font = owner.headerNavigation.GetComponent<HeaderPresentation>().profile.font;
        if (!owner.mail) Build(owner, scene.GetRootGameObjects().Single(r => r.name == "OverlayCanvas").transform);
        owner.mail.owner=owner;
        AddHeader(owner);
        LobbyIconSelectionBaker.BindMailIcon(owner);
        owner.mail.palettes=new[]{Palette("清晰白","F4F6F8","FFFFFF","E9EDF2","FFFFFF","D9E9FA","202A36","637080","2869A6","CED5DD","FFFFFF")};
        owner.mail.defaultPalette=0;
        var plans=owner.artwork.previewControls.GetComponent<InterfacePlansController>();
        var choices=plans.choices.FirstOrDefault(c=>c.option==DesignOption.MailPalette);
        if(choices!=null){var row=choices.buttons.FirstOrDefault(b=>b)?.transform.parent;if(row)UnityEngine.Object.DestroyImmediate(row.gameObject);}
        plans.choices=plans.choices.Where(c=>c.option!=DesignOption.MailPalette).ToArray();
        plans.presetDefaults=plans.presetDefaults.Where(c=>c.option!=DesignOption.MailPalette).ToArray();
        EditorUtility.SetDirty(plans);
        owner.mail.ApplyPalette();
        owner.headerNavigation.BakeLayout(owner.headerNavigation.Side);
        owner.mail.RefreshLayout();
        EditorUtility.SetDirty(owner); EditorUtility.SetDirty(owner.mail);
    }
    static void Build(CompleteLobbyController owner, Transform overlay)
    {
        var root = Rect("MailPanel", overlay); Fill(root);
        var panel = root.gameObject.AddComponent<MailPanel>(); owner.mail = panel;
        var canvas = root.gameObject.AddComponent<Canvas>();
        UiCanvasLayers.Apply(canvas, UiCanvasLayers.Overlay, UiCanvasLayers.MailOrder);
        root.gameObject.AddComponent<GraphicRaycaster>();
        panel.backdrop = Button("Backdrop", root, "", Color.clear);
        Fill((RectTransform)panel.backdrop.transform); panel.backdrop.GetComponent<Image>().color = new Color(0,0,0,.38f);
        var frame = Rect("MailWindow", root); Center(frame, Vector2.zero, new Vector2(1180,664));
        panel.frame = frame;
        panel.contentFade = frame.gameObject.AddComponent<CanvasGroup>();
        var outline = Box("Border", frame, Color.white); Fill(outline.rectTransform);
        panel.surface = Box("Surface", frame, Color.white); Stretch(panel.surface.rectTransform, 1,1,1,1);panel.surface.raycastTarget=true;
        panel.header = Box("TitleBar", frame, Color.white); Top(panel.header.rectTransform,0,0,1180,68);
        panel.heading = Text("Title", panel.header.transform, "邮件", 28, true); Top(panel.heading.rectTransform,28,0,350,68);
        panel.close = Button("CloseButton", panel.header.transform, "×", Color.white);
        RightTop((RectTransform)panel.close.transform,18,14,40,40);
        panel.close.GetComponentInChildren<TMP_Text>().fontSize = 32;
        panel.refresh = Button("RefreshButton", panel.header.transform,"刷新",Color.white);
        RightTop((RectTransform)panel.refresh.transform,76,16,88,36);
        panel.secondarySurfaces = new[]{panel.close.GetComponent<Image>(),panel.refresh.GetComponent<Image>()};
        panel.secondaryLabels = new[]{panel.close.GetComponentInChildren<TMP_Text>(),panel.refresh.GetComponentInChildren<TMP_Text>()};
        panel.listSurface = Box("MailList",frame,Color.white); Stretch(panel.listSurface.rectTransform,24,92,864,26);
        var list = Scroll("ScrollView",panel.listSurface.transform,false,out var listContent); Stretch((RectTransform)list.transform,8,8,8,8);
        panel.listContent = listContent;
        var vertical = listContent.gameObject.AddComponent<VerticalLayoutGroup>(); vertical.spacing=8;vertical.childControlHeight=true;vertical.childControlWidth=true;vertical.childForceExpandWidth=true;vertical.childForceExpandHeight=false;
        var listSize=listContent.gameObject.AddComponent<ContentSizeFitter>();listSize.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        panel.emptyList = Text("EmptyList",panel.listSurface.transform,"暂无邮件",20,true); Fill(panel.emptyList.rectTransform);panel.emptyList.alignment=TextAlignmentOptions.Center;
        panel.rowTemplate = BuildRow(listContent);
        var line=Box("ColumnDivider",frame,Color.white); Stretch(line.rectTransform,328,92,851,26);
        var content = Rect("MailContent",frame); Stretch(content,350,92,24,26);
        panel.bodySurface=Box("ContentSurface",content,Color.white);Fill(panel.bodySurface.rectTransform);
        panel.emptyDetail=Text("EmptyContent",content,"暂无邮件",22,true);Fill(panel.emptyDetail.rectTransform);panel.emptyDetail.alignment=TextAlignmentOptions.Center;
        var detail=Rect("Detail",content);Fill(detail);panel.detail=detail.gameObject;
        panel.subject=Text("Subject",detail,"",26,false);StretchTop(panel.subject.rectTransform,28,20,28,42);
        panel.subject.overflowMode=TextOverflowModes.Ellipsis;
        panel.sender=Text("Sender",detail,"",16,false);Top(panel.sender.rectTransform,28,64,340,26);
        panel.date=Text("Date",detail,"",15,false);RightTop(panel.date.rectTransform,28,64,225,26);panel.date.alignment=TextAlignmentOptions.Right;
        var bodyLine=Box("ContentDivider",detail,Color.white);StretchTop(bodyLine.rectTransform,28,106,28,1);
        var bodyScroll=Scroll("BodyScroll",detail,false,out var bodyContent);Stretch((RectTransform)bodyScroll.transform,28,124,28,168);panel.bodyScroll=bodyScroll;
        var body=Text("Body",bodyContent,"",21,false);Fill(body.rectTransform);body.alignment=TextAlignmentOptions.TopLeft;body.textWrappingMode=TextWrappingModes.Normal;body.overflowMode=TextOverflowModes.Overflow;body.lineSpacing=8;
        bodyContent.sizeDelta=Vector2.zero;
        body.rectTransform.anchorMin=new Vector2(0,1);body.rectTransform.anchorMax=Vector2.one;body.rectTransform.pivot=new Vector2(.5f,1);
        var fit=body.gameObject.AddComponent<ContentSizeFitter>();fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        bodyScroll.content=body.rectTransform; UnityEngine.Object.DestroyImmediate(bodyContent.gameObject.GetComponent<LayoutElement>());
        panel.body=body;
        var reward=Rect("Attachments",detail);reward.anchorMin=reward.anchorMax=Vector2.zero;reward.anchorMax=new Vector2(1,0);reward.pivot=new Vector2(.5f,0);reward.sizeDelta=new Vector2(-56,146);reward.anchoredPosition=new Vector2(0,14);panel.rewardArea=reward.gameObject;
        panel.footer=Box("AttachmentSurface",reward,Color.white);Fill(panel.footer.rectTransform);
        var rewardLine=Box("AttachmentDivider",reward,Color.white);StretchTop(rewardLine.rectTransform,0,0,0,1);
        var rewardScroll=Scroll("RewardScroll",reward,true,out var rewardContent);Stretch((RectTransform)rewardScroll.transform,0,12,0,52);panel.rewardContent=rewardContent;
        var horizontal=rewardContent.gameObject.AddComponent<HorizontalLayoutGroup>();horizontal.spacing=10;horizontal.childControlWidth=true;horizontal.childControlHeight=true;horizontal.childForceExpandHeight=true;horizontal.childForceExpandWidth=false;
        var rewardFit=rewardContent.gameObject.AddComponent<ContentSizeFitter>();rewardFit.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
        panel.rewardTemplate=BuildReward(rewardContent);
        panel.claim=Button("ClaimButton",reward,"领取",Color.white);var claimRect=(RectTransform)panel.claim.transform;claimRect.anchorMin=claimRect.anchorMax=new Vector2(1,0);claimRect.pivot=new Vector2(1,0);claimRect.sizeDelta=new Vector2(144,40);claimRect.anchoredPosition=Vector2.zero;
        panel.claimLabel=panel.claim.GetComponentInChildren<TMP_Text>();panel.actionSurface=panel.claim.GetComponent<Image>();
        panel.status=Text("Status",frame,"",16,true);var status=panel.status.rectTransform;status.anchorMin=status.anchorMax=new Vector2(0,0);status.pivot=Vector2.zero;status.sizeDelta=new Vector2(820,22);status.anchoredPosition=new Vector2(28,1);
        panel.dividers=new[]{outline,line,bodyLine,rewardLine};
        panel.palettes=new[]{
            Palette("夜色信笺","192134","222D43","131A29","202A3D","303E57","F0EFEA","9DA9BF","DDC18C","45516A","202431"),
            Palette("白纸墨印","E9E8E3","D7D6D0","DBDAD4","F8F7F2","DEDED5","292B2D","71716D","414C50","B8BBB6","FFFFFF"),
            Palette("冰蓝信纸","DAE5E9","C1D4DF","CCDCE3","F1F6F8","D2E5EF","273F51","627D8F","366B8B","A6BFCF","FFFFFF"),
            Palette("暖杏邮局","E8DCD0","D9C2AA","DDCDBD","FAF3E9","EEDBC4","433C36","8C7764","A66040","BAA18C","FFF8EE")};
        panel.ApplyPalette();panel.detail.SetActive(false);root.gameObject.SetActive(false);
    }
    static MailPalette Palette(string name,params string[] c)=>new MailPalette{name=name,surface=C(c[0]),header=C(c[1]),list=C(c[2]),card=C(c[3]),selected=C(c[4]),ink=C(c[5]),muted=C(c[6]),accent=C(c[7]),line=C(c[8]),actionInk=C(c[9])};
    static MailListItem BuildRow(Transform parent)
    {
        var button=Button("MailItem",parent,"",Color.white);var row=button.gameObject.AddComponent<MailListItem>();row.button=button;row.surface=button.GetComponent<Image>();
        var layout=button.gameObject.AddComponent<LayoutElement>();layout.preferredHeight=88;
        row.stripe=Box("Selection",button.transform,Color.white);Stretch(row.stripe.rectTransform,0,0,0,0);row.stripe.rectTransform.anchorMax=new Vector2(0,1);row.stripe.rectTransform.sizeDelta=new Vector2(3,0);
        row.title=Text("Subject",button.transform,"",19,false);StretchTop(row.title.rectTransform,16,12,24,30);row.title.overflowMode=TextOverflowModes.Ellipsis;
        row.date=Text("Date",button.transform,"",14,false);StretchTop(row.date.rectTransform,16,51,12,23);
        row.unread=Box("Unread",button.transform,Color.white);RightTop(row.unread.rectTransform,10,23,5,5);
        row.gameObject.SetActive(false);return row;
    }
    static MailRewardItem BuildReward(Transform parent)
    {
        var image=Box("RewardItem",parent,Color.white);var row=image.gameObject.AddComponent<MailRewardItem>();row.surface=image;
        var layout=image.gameObject.AddComponent<LayoutElement>();layout.preferredWidth=138;
        row.icon=Box("Icon",image.transform,Color.white);Top(row.icon.rectTransform,10,12,40,40);row.icon.preserveAspect=true;
        row.quantity=Text("Quantity",image.transform,"",18,false);Top(row.quantity.rectTransform,58,12,72,40);row.quantity.alignment=TextAlignmentOptions.MidlineRight;
        row.title=Text("Name",image.transform,"",14,false);StretchTop(row.title.rectTransform,10,56,10,24);row.title.overflowMode=TextOverflowModes.Ellipsis;
        row.gameObject.SetActive(false);return row;
    }
    static void AddHeader(CompleteLobbyController owner)
    {
        var nav=owner.headerNavigation;if(nav.tabs.Any(t=>t.route=="mail"))return;
        var source=nav.tabs.First(t=>t.route=="notice");var template=nav.tabs.First(t=>t.route=="settings");var tab=UnityEngine.Object.Instantiate(template,source.transform.parent);tab.name="MailButton";tab.route="mail";tab.navigation=nav;tab.compactIcon=true;
        tab.button.onClick=new Button.ButtonClickedEvent();
        foreach(var binding in tab.GetComponentsInChildren<LobbyIconBinding>(true))UnityEngine.Object.DestroyImmediate(binding);
        var presentation=tab.GetComponent<HeaderButtonPresentation>();
        if(presentation.icon)presentation.icon.gameObject.SetActive(false);if(presentation.iconShadow)presentation.iconShadow.gameObject.SetActive(false);if(presentation.iconAccent)presentation.iconAccent.gameObject.SetActive(false);
        foreach(var label in tab.GetComponentsInChildren<TMP_Text>(true))label.gameObject.SetActive(false);
        presentation.icon=null;presentation.iconShadow=presentation.iconAccent=null;
        presentation.button=tab;tab.presentation=presentation;
        if(tab.cornerSelection)tab.cornerSelection.artwork=Array.Empty<Graphic>();
        var tabs=nav.tabs.ToList();tabs.Insert(tabs.IndexOf(source),tab);nav.tabs=tabs.ToArray();
        var profile=nav.GetComponent<HeaderPresentation>().profile;
        profile.entries.Add(new HeaderEntryPresentation{route="mail",simplifiedChinese="邮件",traditionalChinese="郵件",english="Mail",minimumWidth=44,maximumWidth=44,growForTranslation=false,iconSize=new Vector2(32,32),iconOffset=Vector2.zero});
        EditorUtility.SetDirty(profile);EditorUtility.SetDirty(nav);
    }
    static void AddPaletteChoices(InterfacePlansController plans)
    {
        if(plans.choices.Any(c=>c.option==DesignOption.MailPalette))return;
        var page=plans.pages[1].transform;
        var last=page.Cast<RectTransform>().Where(r=>r.gameObject.activeSelf).Select(r=>-r.anchoredPosition.y+r.sizeDelta.y).DefaultIfEmpty(0).Max();
        var row=Rect("MailPalette",page);Top(row,24,last+8,552,98);
        var title=Text("Title",row,"邮件配色",18,true);Top(title.rectTransform,0,0,150,28);
        var names=plans.owner.mail.palettes.Select(p=>p.name).ToArray();
        var buttons=new Button[names.Length];
        for(int i=0;i<buttons.Length;i++)
        {
            buttons[i]=Button("Option"+i,row,names[i],new Color(.22f,.25f,.29f));Top((RectTransform)buttons[i].transform,i*138,36,130,40);
            UnityEventTools.AddIntPersistentListener(buttons[i].onClick,plans.SelectMailPalette,i);
        }
        plans.choices=plans.choices.Append(new DesignChoiceGroup{option=DesignOption.MailPalette,buttons=buttons,values=new[]{0,1,2,3}}).ToArray();
        plans.presetDefaults=plans.presetDefaults.Append(new DesignPresetChoice{option=DesignOption.MailPalette,value=0}).ToArray();
        plans.pageHeights[1]=Mathf.Max(plans.pageHeights[1],last+8+98+128);EditorUtility.SetDirty(plans);
    }
    static RectTransform Rect(string name,Transform parent){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);return r;}
    static Image Box(string name,Transform parent,Color color){var r=Rect(name,parent);var image=r.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;}
    static TMP_Text Text(string name,Transform parent,string text,float size,bool localized)
    {
        var r=Rect(name,parent);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=text;t.fontSize=size;t.color=Color.white;t.raycastTarget=false;t.richText=false;t.fontStyle=FontStyles.Normal;t.alignment=TextAlignmentOptions.MidlineLeft;t.textWrappingMode=TextWrappingModes.NoWrap;
        if(localized){var l=r.gameObject.AddComponent<LocalizedPlatformLabel>();l.key=text;l.authoredSize=size;l.minimumSize=14;l.typographyBaked=true;}
        return t;
    }
    static Button Button(string name,Transform parent,string label,Color color)
    {
        var image=Box(name,parent,color);image.raycastTarget=true;var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;
        if(!string.IsNullOrEmpty(label)){var text=Text("Label",image.transform,label,18,true);Fill(text.rectTransform);text.alignment=TextAlignmentOptions.Center;}
        return button;
    }
    static ScrollRect Scroll(string name,Transform parent,bool horizontal,out RectTransform content)
    {
        var root=Rect(name,parent);var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.horizontal=horizontal;scroll.vertical=!horizontal;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
        var viewport=Box("Viewport",root,Color.clear);Fill(viewport.rectTransform);viewport.raycastTarget=true;viewport.gameObject.AddComponent<RectMask2D>();scroll.viewport=viewport.rectTransform;
        content=Rect("Content",viewport.transform);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
        if(horizontal){content.anchorMin=Vector2.zero;content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,.5f);}
        scroll.content=content;return scroll;
    }
    static void Fill(RectTransform r)=>Stretch(r,0,0,0,0);
    static void Stretch(RectTransform r,float left,float top,float right,float bottom){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=Vector2.one*.5f;r.offsetMin=new Vector2(left,bottom);r.offsetMax=new Vector2(-right,-top);}
    static void Center(RectTransform r,Vector2 pos,Vector2 size){r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.anchoredPosition=pos;r.sizeDelta=size;}
    static void Top(RectTransform r,float x,float y,float width,float height){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);}
    static void RightTop(RectTransform r,float x,float y,float width,float height){r.anchorMin=r.anchorMax=new Vector2(1,1);r.pivot=new Vector2(1,1);r.anchoredPosition=new Vector2(-x,-y);r.sizeDelta=new Vector2(width,height);}
    static void StretchTop(RectTransform r,float left,float top,float right,float height){r.anchorMin=new Vector2(0,1);r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,1);r.offsetMin=new Vector2(left,-top-height);r.offsetMax=new Vector2(-right,-top);}
}
#endif
