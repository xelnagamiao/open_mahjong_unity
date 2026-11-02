#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RewardUiBaker {
    static TMP_FontAsset font;
    static InventoryPalette.Palette palette;
    static Color ColorOf(InventoryPalette.Role role) => palette[role];
    static Color Ink => ColorOf(InventoryPalette.Role.Ink);
    static Color Muted => ColorOf(InventoryPalette.Role.Muted);
    static Color Accent => ColorOf(InventoryPalette.Role.Accent);
    static Color OnAccent => ColorOf(InventoryPalette.Role.OnAccent);

    [MenuItem("Tools/Mahjong/Main Scene/Bake Reward UI")]
    public static void BakeActive() => Bake(SceneManager.GetActiveScene());
    public static void Bake(Scene scene) {
        if (Application.isPlaying) throw new InvalidOperationException("请在编辑模式烘焙奖励界面。");
        var inventory=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<InventoryPanel>(true)).Single();
        var overlay=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Canvas>(true)).Single(c=>c.name=="OverlayCanvas").transform;
        font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset")
            ?? inventory.GetComponentsInChildren<TMP_Text>(true).First(t=>t.font).font;
        var warehousePalette=inventory.GetComponent<InventoryPalette>();
        palette=warehousePalette ? warehousePalette.Current : InventoryPalette.Graphite();
        var redeem=BuildRedeem(overlay);
        var rewards=BuildRewards(overlay);
        var button=BakeEntry(inventory);
        Undo.RecordObject(inventory,"Bind warehouse redemption");
        inventory.redeemButton=button;inventory.redeemPanel=redeem;
        if(warehousePalette)warehousePalette.ApplyCurrent();
        foreach(var root in new[]{redeem.gameObject,rewards.gameObject,button.gameObject})
            foreach(var component in root.GetComponentsInChildren<Component>(true)) if(component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(inventory);EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }

    static Button BakeEntry(InventoryPanel inventory) {
        var source=inventory.transform.Find("Category_1").GetComponent<Button>();
        var sourceRect=(RectTransform)source.transform;
        var button=Button("RedeemButton",inventory.transform,"兑换码",ColorOf(InventoryPalette.Role.Category),ColorOf(InventoryPalette.Role.SidebarInk));
        var rect=(RectTransform)button.transform;
        rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;
        rect.anchoredPosition=new Vector2(sourceRect.anchoredPosition.x,1);
        rect.sizeDelta=sourceRect.sizeDelta;
        var sourceImage=source.targetGraphic as Image;
        var image=(Image)button.targetGraphic;
        image.sprite=sourceImage.sprite;image.type=sourceImage.type;image.material=sourceImage.material;
        image.pixelsPerUnitMultiplier=sourceImage.pixelsPerUnitMultiplier;
        button.colors=source.colors;button.transition=source.transition;
        var text=button.GetComponentInChildren<TMP_Text>();
        var sourceText=source.GetComponentInChildren<TMP_Text>();
        EditorUtility.CopySerialized(sourceText,text);
        var localization=text.GetComponent<LocalizedPlatformLabel>();
        var sourceLocalization=sourceText.GetComponent<LocalizedPlatformLabel>();
        if(sourceLocalization)EditorUtility.CopySerialized(sourceLocalization,localization);
        var binding=text.GetComponent<PlatformFontBinding>();
        var sourceBinding=sourceText.GetComponent<PlatformFontBinding>();
        if(sourceBinding)EditorUtility.CopySerialized(sourceBinding,binding);
        text.color=ColorOf(InventoryPalette.Role.SidebarInk);text.text="兑换码";
        localization.key="兑换码";localization.SetSource("兑换码");
        var labelRect=text.rectTransform;var sourceLabel=sourceText.rectTransform;
        labelRect.anchorMin=sourceLabel.anchorMin;labelRect.anchorMax=sourceLabel.anchorMax;labelRect.pivot=sourceLabel.pivot;
        labelRect.anchoredPosition=sourceLabel.anchoredPosition;labelRect.sizeDelta=sourceLabel.sizeDelta;
        Remove(button.transform,"TopBorder");
        return button;
    }

    static RedeemPanel BuildRedeem(Transform parent) {
        var root=Popup("RedeemPanel",parent,UiCanvasLayers.ModalOrder,new Vector2(660,330),out var frame,out var fade);
        var panel=Get<RedeemPanel>(root);panel.fade=fade;
        panel.close=Heading(frame,"兑换码",660,true);
        var hint=Text("Hint",frame,"输入兑换码，获取奖励。",20,Ink);Top(hint.rectTransform,30,84,600,36);
        var inputImage=Box("CodeInput",frame,ColorOf(InventoryPalette.Role.Sidebar));Top(inputImage.rectTransform,30,134,600,54);inputImage.raycastTarget=true;
        var input=Get<TMP_InputField>(inputImage.transform);panel.codeInput=input;input.targetGraphic=inputImage;
        var viewport=Rect("TextArea",input.transform);Stretch(viewport,16,4,16,4);Get<RectMask2D>(viewport);
        var text=Text("Text",viewport,"",24,Ink);Fill(text.rectTransform);
        UnityEngine.Object.DestroyImmediate(text.GetComponent<LocalizedPlatformLabel>());
        var placeholder=Text("Placeholder",viewport,"请输入兑换码",23,Muted);Fill(placeholder.rectTransform);
        input.textViewport=viewport;input.textComponent=(TextMeshProUGUI)text;input.placeholder=placeholder;input.characterLimit=128;
        input.contentType=TMP_InputField.ContentType.Standard;input.lineType=TMP_InputField.LineType.SingleLine;
        input.customCaretColor=true;input.caretColor=Ink;var selection=Accent;selection.a=.3f;input.selectionColor=selection;
        panel.status=Text("Status",frame,"",17,Muted);Top(panel.status.rectTransform,30,196,600,43);panel.status.textWrappingMode=TextWrappingModes.Normal;
        panel.submit=Button("Redeem",frame,"确定",Accent,OnAccent);Top((RectTransform)panel.submit.transform,240,260,180,44);panel.submitLabel=panel.submit.GetComponentInChildren<TMP_Text>();
        panel.changeCode=Button("ChangeCode",frame,"更换兑换码",ColorOf(InventoryPalette.Role.Card),Ink);Top((RectTransform)panel.changeCode.transform,30,260,174,44);panel.changeCode.gameObject.SetActive(false);
        root.gameObject.SetActive(false);return panel;
    }

    static RewardPresentation BuildRewards(Transform parent) {
        var owner=Rect("RewardPresentation",parent);Fill(owner);
        var presenter=Get<RewardPresentation>(owner);
        var root=Popup("RewardPanel",owner,UiCanvasLayers.ModalOrder+20,new Vector2(800,380),out var frame,out var fade);presenter.popup=fade;
        Heading(frame,"获得物品",800,false);
        foreach(var obsolete in new[]{"DetailDivider","ItemName","DescriptionScroll","Note"})Remove(frame,obsolete);
        var scrollRoot=Rect("ItemsScroll",frame);Top(scrollRoot,28,88,744,204);
        var scroll=Get<ScrollRect>(scrollRoot);presenter.scroll=scroll;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
        var viewport=Box("Viewport",scrollRoot,Color.clear);Stretch(viewport.rectTransform,0,0,16,0);viewport.raycastTarget=true;Get<RectMask2D>(viewport.transform);scroll.viewport=viewport.rectTransform;
        var content=Rect("Items",viewport.transform);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;scroll.content=presenter.content=content;
        var grid=Get<GridLayoutGroup>(content);grid.cellSize=new Vector2(136,180);grid.spacing=new Vector2(10,12);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=5;grid.childAlignment=TextAnchor.UpperCenter;grid.padding=new RectOffset(0,0,4,4);
        var fitter=Get<ContentSizeFitter>(content);fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var barImage=Box("Scrollbar",scrollRoot,ColorOf(InventoryPalette.Role.Sidebar));barImage.rectTransform.anchorMin=new Vector2(1,0);barImage.rectTransform.anchorMax=Vector2.one;barImage.rectTransform.pivot=new Vector2(1,.5f);barImage.rectTransform.sizeDelta=new Vector2(8,0);barImage.rectTransform.anchoredPosition=Vector2.zero;barImage.raycastTarget=true;
        var bar=Get<Scrollbar>(barImage.transform);bar.direction=Scrollbar.Direction.BottomToTop;
        var handle=Box("Handle",bar.transform,Accent);Fill(handle.rectTransform);handle.raycastTarget=true;bar.handleRect=handle.rectTransform;bar.targetGraphic=handle;scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        presenter.itemTemplate=Item(content);
        presenter.confirm=Button("Confirm",frame,"确定",Accent,OnAccent);Top((RectTransform)presenter.confirm.transform,310,312,180,44);
        root.gameObject.SetActive(false);
        return presenter;
    }

    static RewardItemView Item(Transform parent) {
        var surface=Box("ItemTemplate",parent,ColorOf(InventoryPalette.Role.Detail));
        var obsoleteButton=surface.GetComponent<Button>();if(obsoleteButton)UnityEngine.Object.DestroyImmediate(obsoleteButton);
        Remove(surface.transform,"Selection");Remove(surface.transform,"Surface");
        var card=Get<RewardItemView>(surface.transform);
        card.icon=Box("Icon",surface.transform,Color.white);Top(card.icon.rectTransform,30,12,76,76);card.icon.preserveAspect=true;
        var fallback=Rect("FallbackIcon",surface.transform);Top(fallback,30,12,76,76);card.fallbackIcon=Get<WarehouseIconGraphic>(fallback);card.fallbackIcon.raycastTarget=false;
        card.title=Text("Name",surface.transform,"",19,Ink);Top(card.title.rectTransform,8,98,120,38);card.title.alignment=TextAlignmentOptions.Center;card.title.enableAutoSizing=true;card.title.fontSizeMin=14;card.title.fontSizeMax=19;card.title.GetComponent<LocalizedPlatformLabel>().authoredAutoSize=true;
        card.quantity=Text("Quantity",surface.transform,"",24,Accent);Top(card.quantity.rectTransform,8,140,120,30);card.quantity.alignment=TextAlignmentOptions.Center;
        surface.gameObject.SetActive(false);return card;
    }

    static RectTransform Popup(string name,Transform parent,int order,Vector2 size,out RectTransform frame,out BakedModalFade fade) {
        var root=Rect(name,parent);Fill(root);
        var canvas=Get<Canvas>(root);UiCanvasLayers.Apply(canvas,UiCanvasLayers.Modal,order);Get<GraphicRaycaster>(root);
        fade=Get<BakedModalFade>(root);
        var shade=Box("DimBackground",root,new Color(0,0,0,.48f));Fill(shade.rectTransform);shade.raycastTarget=true;
        var fitting=Rect("Fit",root);fitting.anchorMin=fitting.anchorMax=fitting.pivot=Vector2.one*.5f;fitting.sizeDelta=Vector2.zero;
        var surface=Box("Window",fitting,ColorOf(InventoryPalette.Role.Surface));frame=surface.rectTransform;frame.anchorMin=frame.anchorMax=frame.pivot=Vector2.one*.5f;frame.sizeDelta=size;surface.raycastTarget=true;
        var fit=Get<ViewportPanelFit>(fitting);fit.panel=frame;fit.margin=20;
        Remove(frame,"Accent");
        return root;
    }
    static Button Heading(RectTransform frame,string name,float width,bool withClose) {
        var bar=Box("Header",frame,ColorOf(InventoryPalette.Role.Header));Top(bar.rectTransform,0,0,width,64);
        var title=Text("Title",bar.transform,name,28,ColorOf(InventoryPalette.Role.HeaderInk));Top(title.rectTransform,28,0,width-104,64);
        if(!withClose){Remove(bar.transform,"Close");return null;}
        var close=Button("Close",bar.transform,"×",ColorOf(InventoryPalette.Role.Close),ColorOf(InventoryPalette.Role.CloseInk));Top((RectTransform)close.transform,width-56,12,40,40);close.GetComponentInChildren<TMP_Text>().fontSize=32;
        return close;
    }
    static T Get<T>(Transform t) where T:Component => t.GetComponent<T>() ?? t.gameObject.AddComponent<T>();
    static void Remove(Transform parent,string name) { var node=parent.Find(name);if(node)Undo.DestroyObjectImmediate(node.gameObject); }
    static RectTransform Rect(string name,Transform parent) {
        var found=parent.Find(name);if(found)return (RectTransform)found;
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.gameObject.layer=parent.gameObject.layer;r.SetParent(parent,false);return r;
    }
    static Image Box(string name,Transform parent,Color color) { var r=Rect(name,parent);var i=Get<Image>(r);i.color=color;i.raycastTarget=false;return i; }
    static TMP_Text Text(string name,Transform parent,string text,float size,Color color) {
        var r=Rect(name,parent);var t=Get<TextMeshProUGUI>(r);t.font=font;t.text=text;t.fontSize=size;t.color=color;t.fontStyle=FontStyles.Normal;t.fontWeight=FontWeight.Regular;t.richText=false;t.raycastTarget=false;t.alignment=TextAlignmentOptions.MidlineLeft;t.textWrappingMode=TextWrappingModes.NoWrap;
        var language=Get<LocalizedPlatformLabel>(r);language.key=text;language.authoredSize=size;language.minimumSize=Mathf.Min(size,14);language.typographyBaked=true;
        var binding=Get<PlatformFontBinding>(r);binding.authoredFont=font;binding.authoredMaterial=font.material;binding.authoredStyle=FontStyles.Normal;binding.authoredWeight=FontWeight.Regular;binding.styleCaptured=true;
        return t;
    }
    static Button Button(string name,Transform parent,string text,Color surface,Color ink) {
        var image=Box(name,parent,surface);image.raycastTarget=true;var b=Get<Button>(image.transform);b.targetGraphic=image;
        var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1.12f,1.12f,1.12f);colors.pressedColor=new Color(.82f,.82f,.82f);colors.selectedColor=Color.white;b.colors=colors;
        if(text!="") { var label=Text("Label",b.transform,text,20,ink);Fill(label.rectTransform);label.alignment=TextAlignmentOptions.Center; }
        return b;
    }
    static void Fill(RectTransform r)=>Stretch(r,0,0,0,0);
    static void Stretch(RectTransform r,float l,float t,float right,float bottom) { r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(l,bottom);r.offsetMax=new Vector2(-right,-t); }
    static void Top(RectTransform r,float x,float y,float w,float h) { r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h); }
    public static void Validate(Scene scene) {
        var roots=scene.GetRootGameObjects();
        var redeem=roots.SelectMany(r=>r.GetComponentsInChildren<RedeemPanel>(true)).Single();
        var reward=roots.SelectMany(r=>r.GetComponentsInChildren<RewardPresentation>(true)).Single();
        var inventory=roots.SelectMany(r=>r.GetComponentsInChildren<InventoryPanel>(true)).Single();
        if(!redeem.codeInput||!redeem.submit||!redeem.close||!redeem.fade||!reward.popup||!reward.itemTemplate||!reward.content||!reward.confirm)
            throw new InvalidOperationException("奖励界面引用缺失。");
        if(!inventory.redeemButton||inventory.redeemPanel!=redeem)throw new InvalidOperationException("仓库兑换入口未绑定。");
        foreach(var component in new[]{redeem.gameObject,reward.popup.gameObject})
            if(component.GetComponent<Canvas>().sortingLayerName!=UiCanvasLayers.Modal)throw new InvalidOperationException("奖励界面层级错误。");
        var frame=reward.popup.transform.Find("Fit/Window");
        if(frame.Find("DescriptionScroll")||frame.Find("Note")||frame.Find("Header/Close")||reward.itemTemplate.GetComponent<Button>())
            throw new InvalidOperationException("奖励界面仍残留说明或多余操作。");
    }
}
#endif
