#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Salasasa.Home;
using Salasasa.Community;

public static class CommunityUiBaker {
    static TMP_FontAsset font;
    static Color C(string hex) { ColorUtility.TryParseHtmlString(hex,out var c);return c; }
    static RectTransform Rect(string name,Transform parent) {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);return r;
    }
    static void Stretch(RectTransform r,float l=0,float b=0,float right=0,float top=0){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(l,b);r.offsetMax=new Vector2(-right,-top);}
    static void Box(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
    static Image Fill(string name,Transform parent,Color color){var r=Rect(name,parent);Stretch(r);var i=r.gameObject.AddComponent<Image>();i.color=color;i.raycastTarget=false;return i;}
    static TMP_Text Text(string name,Transform parent,string value,float size,Color color){var r=Rect(name,parent);Stretch(r);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=color;t.raycastTarget=false;t.richText=false;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Overflow;t.alignment=TextAlignmentOptions.MidlineLeft;return t;}
    static Button Button(string name,Transform parent,string label){var i=Fill(name,parent,C("#33738F"));i.raycastTarget=true;var b=i.gameObject.AddComponent<Button>();b.targetGraphic=i;var colors=b.colors;colors.highlightedColor=new Color(1.12f,1.12f,1.12f);colors.pressedColor=new Color(.8f,.8f,.8f);colors.disabledColor=new Color(.6f,.6f,.6f);b.colors=colors;var t=Text("Text",i.transform,label,20,Color.white);t.alignment=TextAlignmentOptions.Center;Stretch(t.rectTransform,8,2,8,2);return b;}
    static T Find<T>() where T:UnityEngine.Object=>UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).First();
    static void ClearEvents(Button b){while(b.onClick.GetPersistentEventCount()>0)UnityEventTools.RemovePersistentListener(b.onClick,0);}

    [MenuItem("Tools/Salasasa/Bake Community Page")]
    public static void Bake(){
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before baking");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.name!="MainScene")throw new InvalidOperationException("Open MainScene before baking");
        var owner=Find<CompleteLobbyController>();var plans=Find<InterfacePlansController>();
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/LobbySans.asset");
        if(!font)throw new InvalidOperationException("Lobby font is missing");
        var existing=owner.pageRoot.Find("CommunityPanel");
        if(existing)throw new InvalidOperationException("CommunityPanel already exists; adjust the baked hierarchy in place");
        Undo.RegisterFullObjectHierarchyUndo(owner.pageRoot.gameObject,"Bake community page");
        var client=owner.GetComponent<PlatformContentClient>()??owner.gameObject.AddComponent<PlatformContentClient>();
        client.bundledContent=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/PlatformContentSeed.json");
        if(!client.bundledContent)throw new InvalidOperationException("PlatformContentSeed is missing");

        var root=Rect("CommunityPanel",owner.pageRoot);Stretch(root);root.gameObject.SetActive(false);
        root.gameObject.AddComponent<CanvasGroup>();
        var dim=root.gameObject.AddComponent<Image>();dim.color=new Color(0,0,0,100f/255f);dim.raycastTarget=true;
        var panel=root.gameObject.AddComponent<CommunityPanel>();panel.contentClient=client;
        var appearance=root.gameObject.AddComponent<CommunityAppearance>();appearance.panel=panel;panel.appearance=appearance;appearance.palette=FixedPalette();
        var frame=Rect("Panel",root);Stretch(frame,64,24,64,24);
        panel.surface=Fill("Background",frame,C("#303338"));panel.surface.raycastTarget=true;
        panel.header=Fill("Header",frame,C("#363B42"));var hr=panel.header.rectTransform;hr.anchorMin=new Vector2(0,1);hr.anchorMax=Vector2.one;hr.pivot=new Vector2(.5f,1);hr.sizeDelta=new Vector2(0,88);hr.anchoredPosition=Vector2.zero;
        panel.title=Text("Title",hr,"社群",32,Color.white);Stretch(panel.title.rectTransform,30,0,180,0);
        panel.refresh=Button("RefreshButton",hr,"刷新");var rr=(RectTransform)panel.refresh.transform;rr.anchorMin=rr.anchorMax=new Vector2(1,.5f);rr.pivot=new Vector2(1,.5f);rr.anchoredPosition=new Vector2(-28,0);rr.sizeDelta=new Vector2(100,40);panel.refreshLabel=panel.refresh.GetComponentInChildren<TMP_Text>();
        panel.headerLine=Fill("Divider",hr,C("#69717B"));var line=panel.headerLine.rectTransform;line.anchorMin=Vector2.zero;line.anchorMax=Vector2.right;line.pivot=Vector2.zero;line.sizeDelta=new Vector2(0,1);line.anchoredPosition=Vector2.zero;
        var sr=Rect("ScrollView",frame);Stretch(sr,28,46,28,108);panel.scroll=sr.gameObject.AddComponent<ScrollRect>();panel.scroll.horizontal=false;panel.scroll.movementType=ScrollRect.MovementType.Clamped;panel.scroll.scrollSensitivity=38;
        var viewport=Rect("Viewport",sr);Stretch(viewport,0,0,16,0);viewport.gameObject.AddComponent<RectMask2D>();var raycast=viewport.gameObject.AddComponent<Image>();raycast.color=Color.clear;
        var items=Rect("Content",viewport);items.anchorMin=new Vector2(0,1);items.anchorMax=Vector2.one;items.pivot=new Vector2(.5f,1);items.sizeDelta=Vector2.zero;panel.items=items;panel.scroll.viewport=viewport;panel.scroll.content=items;
        var bar=Fill("Scrollbar",sr,C("#22262C"));var br=bar.rectTransform;br.anchorMin=new Vector2(1,0);br.anchorMax=Vector2.one;br.pivot=Vector2.one;br.offsetMin=new Vector2(-5,0);br.offsetMax=Vector2.zero;
        var handle=Fill("Handle",br,C("#89939E"));handle.raycastTarget=true;var scrollbar=br.gameObject.AddComponent<Scrollbar>();scrollbar.handleRect=handle.rectTransform;scrollbar.targetGraphic=handle;scrollbar.direction=Scrollbar.Direction.BottomToTop;panel.scroll.verticalScrollbar=scrollbar;panel.scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        panel.status=Text("StatusText",frame,"",18,C("#CBD0D6"));var st=panel.status.rectTransform;st.anchorMin=Vector2.zero;st.anchorMax=Vector2.right;st.pivot=Vector2.zero;st.offsetMin=new Vector2(28,4);st.offsetMax=new Vector2(-28,36);
        panel.empty=Text("EmptyText",viewport,"暂无社群",24,C("#CBD0D6"));panel.empty.alignment=TextAlignmentOptions.Center;panel.empty.gameObject.SetActive(false);
        var template=CreateCard(items);Directory.CreateDirectory("Assets/Prefabs/Community");
        var asset=PrefabUtility.SaveAsPrefabAsset(template.gameObject,"Assets/Prefabs/Community/CommunityItem.prefab");panel.itemTemplate=asset.GetComponent<CommunityCard>();
        UnityEngine.Object.DestroyImmediate(template.gameObject);
        var doc=PlatformContentClient.Parse(client.bundledContent.text);panel.bakedCards=new CommunityCard[doc.communities.Length];
        for(int i=0;i<doc.communities.Length;i++){
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,items);var card=go.GetComponent<CommunityCard>();go.name="CommunityItem_"+doc.communities[i].id;card.Bind(doc.communities[i],panel);card.Apply(appearance.Current);panel.bakedCards[i]=card;panel.Cards.Add(card);PrefabUtility.RecordPrefabInstancePropertyModifications(card);
        }
        panel.Layout();
        var overlay=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Single(x=>x.name=="OverlayCanvas");panel.qrDialog=CreateQr(overlay.transform);
        var about=Find<AboutUsPanel>();about.contentClient=client;EditorUtility.SetDirty(about);
        var wm=new SerializedObject(Find<WindowsManager>());wm.FindProperty("communityPanel").objectReferenceValue=root.gameObject;wm.ApplyModifiedProperties();
        AddHeader(owner.headerNavigation);ApplyFixed(scene);
        EditorUtility.SetDirty(client);EditorUtility.SetDirty(owner.headerNavigation);EditorUtility.SetDirty(plans);EditorUtility.SetDirty(panel);EditorUtility.SetDirty(appearance);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("Community UI baked into MainScene: fixed nocturne / graphite design");
    }
    static CommunityCard CreateCard(Transform parent){
        var r=Rect("CommunityItem",parent);Box(r,0,0,520,354);var c=r.gameObject.AddComponent<CommunityCard>();
        c.surface=r.gameObject.AddComponent<CommunityShape>();c.surface.raycastTarget=false;r.gameObject.AddComponent<Mask>().showMaskGraphic=true;
        c.topBand=Fill("Header",r,C("#363B42"));var band=c.topBand.rectTransform;band.anchorMin=new Vector2(0,1);band.anchorMax=Vector2.one;band.offsetMin=new Vector2(2,-78);band.offsetMax=new Vector2(-2,-2);
        c.accentBar=Fill("Accent",r,C("#87A7D5"));var ac=c.accentBar.rectTransform;ac.anchorMin=new Vector2(0,1);ac.anchorMax=Vector2.one;ac.offsetMin=new Vector2(0,-4);ac.offsetMax=Vector2.zero;
        c.iconBack=Fill("IconBackground",r,C("#363B42"));Box(c.iconBack.rectTransform,26,22,44,44);
        var icon=Rect("ChannelIcon",r);Box(icon,32,28,32,32);c.glyph=icon.gameObject.AddComponent<CommunityGlyph>();c.glyph.raycastTarget=false;
        c.channel=Text("Channel",r,"QQ",22,Color.white);Box(c.channel.rectTransform,82,24,180,40);
        c.tag=Text("Tag",r,"平台交流",18,C("#CBD0D6"));var tag=c.tag.rectTransform;tag.anchorMin=new Vector2(1,1);tag.anchorMax=Vector2.one;tag.pivot=Vector2.one;tag.anchoredPosition=new Vector2(-26,-22);tag.sizeDelta=new Vector2(166,44);c.tag.alignment=TextAlignmentOptions.MidlineRight;
        c.title=Text("Title",r,"salasasa平台交流群",26,Color.white);c.title.rectTransform.anchorMin=new Vector2(0,1);c.title.rectTransform.anchorMax=Vector2.one;
        c.description=Text("Description",r,"",20,C("#CBD0D6"));c.description.alignment=TextAlignmentOptions.TopLeft;
        c.divider=Fill("Divider",r,C("#69717B"));var d=c.divider.rectTransform;d.anchorMin=Vector2.zero;d.anchorMax=Vector2.right;d.offsetMin=new Vector2(26,78);d.offsetMax=new Vector2(-26,79);
        c.contact=Text("Contact",r,"",22,Color.white);var co=c.contact.rectTransform;co.anchorMin=Vector2.zero;co.anchorMax=Vector2.right;co.offsetMin=new Vector2(26,17);co.offsetMax=new Vector2(-176,64);c.contact.enableAutoSizing=true;c.contact.fontSizeMin=16;c.contact.fontSizeMax=22;
        var action=Rect("JoinButton",r);action.anchorMin=action.anchorMax=Vector2.right;action.pivot=Vector2.right;action.anchoredPosition=new Vector2(-24,18);action.sizeDelta=new Vector2(140,44);c.actionSurface=action.gameObject.AddComponent<CommunityShape>();c.action=action.gameObject.AddComponent<Button>();c.action.targetGraphic=c.actionSurface;c.actionLabel=Text("Text",action,"申请入群",20,C("#182334"));c.actionLabel.alignment=TextAlignmentOptions.Center;Stretch(c.actionLabel.rectTransform,8,0,8,0);c.actionLabel.enableAutoSizing=true;c.actionLabel.fontSizeMin=14;c.actionLabel.fontSizeMax=20;
        return c;
    }
    static CommunityQrDialog CreateQr(Transform parent){
        var r=Rect("CommunityQrPanel",parent);Stretch(r);var q=r.gameObject.AddComponent<CommunityQrDialog>();q.group=r.gameObject.AddComponent<CanvasGroup>();
        q.dismiss=Button("DimBackground",r,"");q.dismiss.image.color=new Color(0,0,0,.58f);
        var frame=Fill("Panel",r,C("#303338"));frame.raycastTarget=true;var f=frame.rectTransform;f.anchorMin=f.anchorMax=f.pivot=new Vector2(.5f,.5f);f.sizeDelta=new Vector2(520,656);f.anchoredPosition=Vector2.zero;
        q.title=Text("Title",f,"",25,Color.white);Box(q.title.rectTransform,28,18,402,58);
        q.close=Button("CloseButton",f,"");Box((RectTransform)q.close.transform,446,24,44,44);q.close.image.color=C("#494E55");var cr=Rect("CloseIcon",q.close.transform);Stretch(cr,10,10,10,10);var glyph=cr.gameObject.AddComponent<CommunityGlyph>();glyph.symbol=CommunityGlyph.Symbol.Close;glyph.color=Color.white;glyph.raycastTarget=false;
        var well=Fill("QrFrame",f,Color.white);Box(well.rectTransform,68,108,384,384);
        var im=Rect("QrImage",well.transform);Stretch(im,16,16,16,16);q.image=im.gameObject.AddComponent<RawImage>();q.image.raycastTarget=false;
        var aspect=im.gameObject.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;aspect.aspectRatio=1;
        q.status=Text("StatusText",well.transform,"",20,C("#303338"));q.status.alignment=TextAlignmentOptions.Center;Stretch(q.status.rectTransform,20,20,20,20);
        q.example=Text("ExampleLabel",f,"示例二维码",22,C("#E5B46A"));Box(q.example.rectTransform,28,514,464,36);q.example.alignment=TextAlignmentOptions.Center;
        q.caption=Text("Caption",f,"",20,C("#D2D6DD"));Box(q.caption.rectTransform,38,558,444,70);q.caption.alignment=TextAlignmentOptions.Top;
        q.example.gameObject.SetActive(false);q.RefreshLayout();
        r.gameObject.SetActive(false);return q;
    }
    static void AddHeader(FourSideHeader header){
        if(header.tabs.Any(x=>x.route=="community"))return;
        var tab=UnityEngine.Object.Instantiate(header.tabs.First(x=>x.route=="room"),header.tabsRoot);tab.name="CommunityButton";tab.route="community";tab.navigation=header;tab.compactIcon=false;ClearEvents(tab.button);
        foreach(var n in new[]{"Icon","IconShadow"}){var old=tab.artwork.Find(n);if(old)old.gameObject.SetActive(false);}
        foreach(var text in tab.GetComponentsInChildren<TMP_Text>(true))text.text="社群";
        var icon=Rect("CommunityIcon",tab.artwork);icon.anchorMin=icon.anchorMax=icon.pivot=new Vector2(.5f,.5f);icon.anchoredPosition=new Vector2(0,7);icon.sizeDelta=new Vector2(38,34);var glyph=icon.gameObject.AddComponent<CommunityGlyph>();glyph.symbol=CommunityGlyph.Symbol.Community;glyph.color=Color.white;glyph.raycastTarget=false;
        var tabs=header.tabs.ToList();var index=tabs.FindIndex(x=>x.route=="friends");tabs.Insert(index<0?tabs.Count:index,tab);header.tabs=tabs.ToArray();header.BakeLayout(0);
    }
    public static CommunityPalette FixedPalette() => new CommunityPalette {
        id="graphite",label="清晰白",surface=C("#F2F4F7"),card=C("#FFFFFF"),header=C("#FFFFFF"),
        text=C("#202A36"),muted=C("#637080"),accent=C("#2869A6"),line=C("#CED5DD"),actionText=Color.white
    };

    public static void ApplyFixed(UnityEngine.SceneManagement.Scene scene) {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before baking");
        var panel=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CommunityPanel>(true)).Single();
        var appearance=panel.appearance;appearance.palette=FixedPalette();
        string prefabPath=AssetDatabase.GetAssetPath(panel.itemTemplate);
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try {
            var template=prefab.GetComponent<CommunityCard>();template.Apply(appearance.Current);
            foreach(var label in prefab.GetComponentsInChildren<TMP_Text>(true))label.fontStyle &= ~FontStyles.Bold;
            PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
        } finally { PrefabUtility.UnloadPrefabContents(prefab); }
        panel.itemTemplate=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<CommunityCard>();
        var doc=PlatformContentClient.Parse(panel.contentClient.bundledContent.text);
        var cards=(panel.bakedCards??Array.Empty<CommunityCard>()).Where(x=>x).ToList();
        for(int i=0;i<doc.communities.Length;i++){
            if(i>=cards.Count)cards.Add(((GameObject)PrefabUtility.InstantiatePrefab(panel.itemTemplate.gameObject,panel.items)).GetComponent<CommunityCard>());
            var card=cards[i];card.name="CommunityItem_"+doc.communities[i].id;card.gameObject.SetActive(true);card.Bind(doc.communities[i],panel);
            card.Apply(appearance.Current);foreach(var label in card.GetComponentsInChildren<TMP_Text>(true))label.fontStyle &= ~FontStyles.Bold;
            PrefabUtility.RecordPrefabInstancePropertyModifications(card);EditorUtility.SetDirty(card);
        }
        for(int i=cards.Count-1;i>=doc.communities.Length;i--){UnityEngine.Object.DestroyImmediate(cards[i].gameObject);cards.RemoveAt(i);}
        panel.bakedCards=cards.ToArray();panel.Cards.Clear();panel.Cards.AddRange(cards);panel.RefreshAppearance();
        var plans=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        plans.choices=plans.choices.Where(x=>(int)x.option!=18&&(int)x.option!=19).ToArray();
        plans.presetDefaults=plans.presetDefaults.Where(x=>(int)x.option!=18&&(int)x.option!=19).ToArray();
        int index=Array.FindIndex(plans.pages,x=>x&&x.name=="Community");
        if(index>=0){
            var page=plans.pages[index];var tab=plans.tabs[index];
            plans.pages=plans.pages.Where((x,i)=>i!=index).ToArray();plans.tabs=plans.tabs.Where((x,i)=>i!=index).ToArray();
            plans.pageHeights=plans.pageHeights.Where((x,i)=>i!=index).ToArray();
            UnityEngine.Object.DestroyImmediate(page);UnityEngine.Object.DestroyImmediate(tab.gameObject);
            for(int i=0;i<plans.tabs.Length;i++){ClearEvents(plans.tabs[i]);UnityEventTools.AddIntPersistentListener(plans.tabs[i].onClick,plans.SelectPage,i);}
        }
        foreach(var room in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<RoomPanel>(true))){
            var speed=new SerializedObject(room).FindProperty("botSpeedDropdown")?.objectReferenceValue as TMP_Dropdown;
            if(speed){speed.SetValueWithoutNotify(2);EditorUtility.SetDirty(speed);}
        }
        EditorUtility.SetDirty(appearance);EditorUtility.SetDirty(panel);EditorUtility.SetDirty(plans);
        BakeCompact(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void BakeCompact(UnityEngine.SceneManagement.Scene scene) {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before baking");
        var panel=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CommunityPanel>(true)).Single();
        if(panel.appearance?.Current==null)throw new InvalidOperationException("The fixed community palette is missing.");
        ConfigureCompactPanel(panel);
        string prefabPath=AssetDatabase.GetAssetPath(panel.itemTemplate);
        if(string.IsNullOrEmpty(prefabPath))throw new InvalidOperationException("The community item prefab is missing.");
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try {
            ConfigureCompactCard(prefab.GetComponent<CommunityCard>(),panel.appearance.Current);
            PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
        } finally { PrefabUtility.UnloadPrefabContents(prefab); }
        panel.itemTemplate=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<CommunityCard>();
        var document=PlatformContentClient.Parse(panel.contentClient.bundledContent.text);
        var cards=(panel.bakedCards??Array.Empty<CommunityCard>()).Where(x=>x).ToArray();
        foreach(var card in cards) {
            var entry=document.communities.SingleOrDefault(x=>card.name=="CommunityItem_"+x.id);
            if(entry==null)throw new InvalidOperationException("No published seed entry matches "+card.name);
            card.Bind(entry,panel);ConfigureCompactCard(card,panel.appearance.Current);
        }
        panel.Cards.Clear();panel.Cards.AddRange(cards);panel.RefreshAppearance();
        foreach(var card in cards)foreach(var component in card.GetComponentsInChildren<Component>(true)) {
            if(!component)continue;
            EditorUtility.SetDirty(component);
            if(PrefabUtility.IsPartOfPrefabInstance(component))PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        EditorUtility.SetDirty(panel);EditorUtility.SetDirty(panel.items);
        EditorSceneManager.MarkSceneDirty(scene);
    }
    static void ConfigureCompactPanel(CommunityPanel panel) {
        var frame=(RectTransform)panel.surface.transform.parent;
        panel.Layout();
        EditorUtility.SetDirty(frame);
        if(!panel.close&&panel.refresh) {
            panel.close=panel.refresh;panel.refresh=null;panel.refreshLabel=null;panel.close.name="CloseButton";
            var converted=panel.close.GetComponentInChildren<TMP_Text>(true);converted.text="×";converted.fontSize=32;converted.alignment=TextAlignmentOptions.Center;
            ((RectTransform)panel.close.transform).sizeDelta=new Vector2(44,44);
        }
        if(!panel.close)throw new InvalidOperationException("The community close button is missing.");
        var close=panel.close;var background=close.GetComponent<UnityEngine.UI.Image>();
        background.color=Color.white;background.raycastTarget=true;close.targetGraphic=background;
        close.transition=UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors=close.colors;colors.normalColor=C("#E9EDF2");colors.highlightedColor=C("#D2DFED");
        colors.pressedColor=C("#B7CADD");colors.selectedColor=C("#DDE7F3");colors.disabledColor=C("#E9EDF280");
        colors.colorMultiplier=1;colors.fadeDuration=.1f;close.colors=colors;
        var outline=close.GetComponent<UnityEngine.UI.Outline>()??close.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor=C("#9AAEC4");outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=true;
        var label=close.GetComponentInChildren<TMP_Text>(true);label.color=C("#202A36");
        foreach(var component in close.GetComponentsInChildren<Component>(true))if(component)EditorUtility.SetDirty(component);
    }
    static void ConfigureCompactCard(CommunityCard card,CommunityPalette palette) {
        CompactText(card.title,26,false,26,TextAlignmentOptions.TopLeft);
        CompactText(card.description,20,false,20,TextAlignmentOptions.TopLeft);
        CompactText(card.channel,20,true,15,TextAlignmentOptions.MidlineLeft);
        CompactText(card.contact,18,true,13,TextAlignmentOptions.MidlineRight);
        CompactText(card.actionLabel,18,true,14,TextAlignmentOptions.Center);
        card.channel.textWrappingMode=card.contact.textWrappingMode=card.actionLabel.textWrappingMode=TextWrappingModes.NoWrap;
        card.channel.overflowMode=card.contact.overflowMode=card.actionLabel.overflowMode=TextOverflowModes.Ellipsis;
        card.title.textWrappingMode=card.description.textWrappingMode=TextWrappingModes.Normal;
        card.title.overflowMode=card.description.overflowMode=TextOverflowModes.Overflow;
        foreach(var label in card.GetComponentsInChildren<TMP_Text>(true)){label.fontStyle&=~FontStyles.Bold;label.fontWeight=FontWeight.Regular;}
        card.actionSurface.raycastTarget=true;card.action.targetGraphic=card.actionSurface;
        var colors=card.action.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.86f,.93f,1,1);colors.pressedColor=new Color(.7f,.8f,.9f,1);colors.disabledColor=new Color(.6f,.6f,.6f,.65f);card.action.colors=colors;
        card.Apply(palette);
        var rect=(RectTransform)card.transform;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,card.FitContent(rect.rect.width));
    }
    static void CompactText(TMP_Text label,float size,bool auto,float minimum,TextAlignmentOptions alignment) {
        label.fontSize=size;label.fontSizeMax=size;label.fontSizeMin=minimum;label.enableAutoSizing=auto;label.alignment=alignment;label.richText=false;
        var localization=label.GetComponent<LocalizedPlatformLabel>();
        if(localization){localization.authoredSize=size;localization.minimumSize=minimum;localization.authoredAutoSize=auto;localization.typographyBaked=true;EditorUtility.SetDirty(localization);}
    }
    public static object ValidateCompact(UnityEngine.SceneManagement.Scene scene) {
        var panel=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CommunityPanel>(true)).Single();
        var cards=(panel.bakedCards??Array.Empty<CommunityCard>()).Where(x=>x).ToArray();
        if(cards.Length==0||panel.appearance.Current.id!="graphite")throw new InvalidOperationException("The fixed community design or baked cards are missing.");
        ValidateCompactLayout(panel,cards);
        foreach(var card in cards) {
            if(card.Entry==null)throw new InvalidOperationException("Bind published entries before compact validation: "+card.name);
            if(card.topBand.color!=CommunityCard.HeaderColorFor(card.Entry.kind))throw new InvalidOperationException("Incorrect community header color: "+card.name);
            if(card.actionLabel.color!=Color.black||card.actionSurface.color!=Color.white)
                throw new InvalidOperationException("Community actions require black text on a white surface: "+card.name);
            if((card.Entry.kind=="wechat"||card.Entry.kind=="discord")&&card.contact.gameObject.activeSelf)
                throw new InvalidOperationException("Community header contains a redundant contact placeholder: "+card.name);
            if(!Inside(card.action.transform as RectTransform,card.topBand.rectTransform)
                ||card.contact.gameObject.activeSelf&&!Inside(card.contact.rectTransform,card.topBand.rectTransform)
                ||!Inside(card.channel.rectTransform,card.topBand.rectTransform))
                throw new InvalidOperationException("Community header content extends outside its band: "+card.name);
            if(card.contact.gameObject.activeSelf&&Right(card.channel.rectTransform)>Left(card.contact.rectTransform)-6)
                throw new InvalidOperationException("Community channel overlaps its contact: "+card.name);
            if(card.contact.gameObject.activeSelf&&Right(card.contact.rectTransform)>Left((RectTransform)card.action.transform)-6)
                throw new InvalidOperationException("Community contact overlaps its action: "+card.name);
            if(((RectTransform)card.action.transform).anchorMax.y!=1||card.contact.rectTransform.anchorMax.y!=1)
                throw new InvalidOperationException("Community action still uses the retired footer: "+card.name);
            foreach(var label in new[]{card.title,card.description,card.channel,card.contact,card.actionLabel})
                if((label.fontStyle&FontStyles.Bold)!=0||label.fontWeight!=FontWeight.Regular)
                    throw new InvalidOperationException("Community text must remain regular weight: "+label.name);
            if(!card.actionSurface.raycastTarget||card.action.targetGraphic!=card.actionSurface)
                throw new InvalidOperationException("Community action lost its input target: "+card.name);
        }
        var template=panel.itemTemplate;
        if(!panel.close||panel.close.colors.highlightedColor==Color.white||!panel.close.GetComponent<UnityEngine.UI.Outline>())
            throw new InvalidOperationException("The community close button requires a visible hover surface and border.");
        if(((RectTransform)template.action.transform).anchorMax.y!=1||template.contact.rectTransform.anchorMax.y!=1)
            throw new InvalidOperationException("The dynamic community prefab was not compacted.");
        return new { cards=cards.Length,headers=cards.Select(x=>new{id=x.Entry.id,kind=x.Entry.kind,color=ColorUtility.ToHtmlStringRGB(x.topBand.color),height=((RectTransform)x.transform).rect.height}).ToArray(),palette=panel.appearance.Current.id,minimumCardHeight=CommunityCard.MinimumHeight };
    }
    static void ValidateCompactLayout(CommunityPanel panel,CommunityCard[] cards) {
        var frame=(RectTransform)panel.surface.transform.parent;
        float expectedWidth=Mathf.Min(CommunityPanel.CompactFrameWidth,((RectTransform)panel.transform).rect.width*CommunityPanel.FrameWidthRatio);
        if(Mathf.Abs(frame.rect.width-expectedWidth)>.5f||frame.localScale!=Vector3.one||frame.anchoredPosition!=Vector2.zero)
            throw new InvalidOperationException("The community frame must keep its centered compact width.");
        if(panel.items.GetComponentsInChildren<CommunityCard>(true).Any(card=>card.gameObject.activeSelf&&!cards.Contains(card)))
            throw new InvalidOperationException("An active community card is outside the baked layout list.");
        float width=panel.scroll.viewport.rect.width;
        int columns=width>=2*CommunityCard.MinimumWidth+CommunityPanel.CardGap?2:1;
        float cellWidth=(width-CommunityPanel.CardGap*(columns-1))/columns;
        for(int i=0;i<cards.Length;i++) {
            var rect=(RectTransform)cards[i].transform;
            float x=(i%columns)*(cellWidth+CommunityPanel.CardGap);
            if(Mathf.Abs(rect.rect.width-cellWidth)>.5f||Mathf.Abs(rect.anchoredPosition.x-x)>.5f||rect.localScale!=Vector3.one)
                throw new InvalidOperationException("Community cards must use the compact one/two-column grid: "+cards[i].name);
            if(rect.anchoredPosition.x+rect.rect.width>width+.5f)
                throw new InvalidOperationException("Community card extends past its viewport: "+cards[i].name);
            if(i>=columns&&rect.anchoredPosition.y>=((RectTransform)cards[i-columns].transform).anchoredPosition.y)
                throw new InvalidOperationException("Community rows overlap: "+cards[i].name);
        }
    }
    static bool Inside(RectTransform child,RectTransform parent) {
        var corners=new Vector3[4];child.GetWorldCorners(corners);var rect=parent.rect;
        return corners.Select(parent.InverseTransformPoint).All(p=>p.x>=rect.xMin-1&&p.x<=rect.xMax+1&&p.y>=rect.yMin-1&&p.y<=rect.yMax+1);
    }
    static float Left(RectTransform rect) {var corners=new Vector3[4];rect.GetWorldCorners(corners);return corners.Min(x=>x.x);}
    static float Right(RectTransform rect) {var corners=new Vector3[4];rect.GetWorldCorners(corners);return corners.Max(x=>x.x);}
}
#endif
