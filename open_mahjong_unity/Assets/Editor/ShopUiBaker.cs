#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the shop in MainScene; runtime only binds server data.</summary>
public static class ShopUiBaker {
    static TMP_FontAsset font;
    static readonly Color Surface = C("303030"), Card = C("555555"), Ink = C("F5F5F5");
    static readonly Color Muted = C("D0D0D0"), Accent = C("ECA15B"), Stroke = C("737373");
    static Color C(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }

    [MenuItem("Tools/Mahjong/Main Scene/Bake Developer Shop")]
    public static void BakeActiveScene() => Bake(SceneManager.GetActiveScene());

    public static void Bake(Scene scene) {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
        var owner = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        if (!owner.developerOptions || !owner.developerOptions.classroomButton) throw new InvalidOperationException("开发者选项入口缺失。");
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset")
            ?? owner.developerOptions.classroomButton.GetComponentInChildren<TMP_Text>(true).font;
        if (!font) throw new InvalidOperationException("商店字体缺失。");
        if (!owner.shop) {
            var overlay = scene.GetRootGameObjects().Single(root => root.name == "OverlayCanvas").transform;
            BuildShop(owner, overlay);
        }
        RefineShop(owner.shop);
        BakeEntry(owner);
        GameEndRewardsUiBaker.BakeShop(owner);
        FitTextHeights(owner.shop.GetComponentsInChildren<TMP_Text>(true)
            .Concat(owner.developerOptions.shopButton.GetComponentsInChildren<TMP_Text>(true)));
        foreach (var component in owner.shop.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(owner); EditorUtility.SetDirty(owner.developerOptions);
        owner.shop.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        Validate(scene);
    }

    static void BuildShop(CompleteLobbyController owner, Transform overlay) {
        var root = Rect("ShopPanel", overlay); Fill(root);
        var shop = root.gameObject.AddComponent<ShopPanel>(); owner.shop = shop;
        var canvas = root.gameObject.AddComponent<Canvas>();
        UiCanvasLayers.Apply(canvas, UiCanvasLayers.Overlay, UiCanvasLayers.OverlayOrder + 320);
        root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        root.gameObject.AddComponent<CanvasGroup>(); root.gameObject.AddComponent<BakedModalFade>();
        shop.backdrop = Button("Backdrop", root, "", new Color(0,0,0,.48f), Ink);
        Fill((RectTransform)shop.backdrop.transform);
        var window = Rect("ShopWindow", root); Center(window, 1280, 744);
        Box("Outline", window, 0,0,1280,744,Stroke);
        Box("Surface", window, 1,1,1278,742,Surface).raycastTarget = true;
        Box("Header", window, 1,1,1278,104,C("202020"));
        Label("Title",window,32,22,270,44,"商店",32,Ink);
        Label("BalanceCaption",window,688,18,280,24,"持有萨拉币",15,Muted).alignment = TextAlignmentOptions.Right;
        shop.balanceLabel = Label("Balance",window,668,42,300,40,"—",30,Accent);
        shop.balanceLabel.alignment = TextAlignmentOptions.Right;
        shop.closeButton = Button("Close",window,"×",C("AE463B"),Ink); Top((RectTransform)shop.closeButton.transform,1210,30,42,42);
        shop.goodsTab = Button("GoodsTab",window,"全部物品",C("68584A"),Ink); Top((RectTransform)shop.goodsTab.transform,32,120,146,40);
        shop.historyTab = Button("HistoryTab",window,"收支明细",C("414141"),Ink); Top((RectTransform)shop.historyTab.transform,190,120,146,40);
        Label("RewardRule",window,364,112,882,56,ShopPanel.RewardRuleText,16,Muted);

        var goods = Rect("Goods",window); Top(goods,32,182,850,474); shop.goodsRoot=goods.gameObject;
        shop.goodsScroll = Scroll("GoodsScroll",goods,0,0,850,474,out var goodsContent); shop.goodsContent=goodsContent;
        var grid = goodsContent.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        grid.cellSize=new Vector2(270,306); grid.spacing=new Vector2(20,20);
        grid.constraint=UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount=3;
        grid.childAlignment=TextAnchor.UpperLeft;
        shop.rowTemplate=BuildRow(goodsContent); shop.rowTemplate.gameObject.SetActive(false);
        shop.emptyGoods=Label("Empty",goods,0,80,850,100,"暂无上架商品",22,Muted); shop.emptyGoods.alignment=TextAlignmentOptions.Center;

        var detail = Rect("Detail",window); Top(detail,914,182,334,520); shop.detailRoot=detail.gameObject;
        Box("Surface",detail,0,0,334,520,C("3A3A3A"));
        shop.detailName=Label("Name",detail,22,16,290,36,"",25,Ink);
        shop.detailKind=Label("Kind",detail,22,58,290,24,"",16,Muted);
        shop.detailIcon=Box("Icon",detail,92,92,150,150,Color.white); shop.detailIcon.preserveAspect=true; shop.detailIcon.enabled=false;
        var detailArt=Rect("Artwork",detail); Top(detailArt,92,92,150,150);
        shop.detailArtwork=detailArt.gameObject.AddComponent<WarehouseIconGraphic>(); shop.detailArtwork.color=Color.white; shop.detailArtwork.raycastTarget=false;
        shop.detailGlyph=Label("Glyph",detail,92,145,150,58,"",38,Ink); shop.detailGlyph.alignment=TextAlignmentOptions.Center;
        shop.descriptionScroll=Scroll("DescriptionScroll",detail,22,254,290,72,out var descriptionContent);
        shop.detailDescription=Label("Description",descriptionContent,0,0,290,0,"",17,Muted);
        var descriptionRect=shop.detailDescription.rectTransform;
        descriptionRect.anchorMin=new Vector2(0,1);descriptionRect.anchorMax=Vector2.one;descriptionRect.pivot=new Vector2(.5f,1);descriptionRect.sizeDelta=Vector2.zero;descriptionRect.anchoredPosition=Vector2.zero;
        shop.detailDescription.textWrappingMode=TextWrappingModes.Normal;shop.detailDescription.overflowMode=TextOverflowModes.Overflow;
        var textFit=descriptionRect.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();textFit.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        // The text itself is the one fitted content of this viewport.
        UnityEngine.Object.DestroyImmediate(descriptionContent.GetComponent<UnityEngine.UI.ContentSizeFitter>());
        shop.descriptionScroll.content=descriptionRect;
        shop.detailOwned=Label("Owned",detail,22,336,290,24,"",16,Muted);
        Label("QuantityCaption",detail,22,374,96,36,"购买数量",17,Muted);
        shop.decreaseButton=Button("Decrease",detail,"−",C("555555"),Ink);Top((RectTransform)shop.decreaseButton.transform,134,374,38,36);
        var input=Rect("Quantity",detail);Top(input,178,374,78,36); input.gameObject.AddComponent<UnityEngine.UI.Image>().color=C("202020");
        shop.quantityInput=input.gameObject.AddComponent<TMP_InputField>();
        shop.quantityInput.targetGraphic=input.GetComponent<UnityEngine.UI.Image>();
        var inputText=Label("Value",input,0,0,78,36,"1",19,Ink);inputText.alignment=TextAlignmentOptions.Center;
        var binding=inputText.GetComponent<LocalizedPlatformLabel>();if(binding)UnityEngine.Object.DestroyImmediate(binding);
        shop.quantityInput.textComponent=(TextMeshProUGUI)inputText;shop.quantityInput.textViewport=input;
        shop.quantityInput.contentType=TMP_InputField.ContentType.IntegerNumber;shop.quantityInput.characterLimit=3;
        shop.quantityInput.customCaretColor=true;shop.quantityInput.caretColor=Ink;shop.quantityInput.text="1";
        shop.increaseButton=Button("Increase",detail,"+",C("555555"),Ink);Top((RectTransform)shop.increaseButton.transform,264,374,38,36);
        shop.detailPrice=Label("Total",detail,22,418,290,28,"",21,Accent);shop.detailPrice.alignment=TextAlignmentOptions.Right;
        shop.buyButton=Button("Purchase",window,"购买",Accent,C("161616"));Top((RectTransform)shop.buyButton.transform,936,666,290,44);
        shop.buyLabel=shop.buyButton.GetComponentInChildren<TMP_Text>();
        // Purchase belongs to Detail so switching to history also removes its hit target.
        shop.buyButton.transform.SetParent(detail,false);Top((RectTransform)shop.buyButton.transform,22,470,290,44);
        var history=Rect("History",window);Top(history,32,182,1216,474);shop.historyRoot=history.gameObject;
        Label("DateHeader",history,20,0,200,36,"时间",17,Muted);
        Label("SourceHeader",history,252,0,450,36,"来源",17,Muted);
        Label("AmountHeader",history,752,0,200,36,"金额",17,Muted).alignment=TextAlignmentOptions.Right;
        Label("BalanceHeader",history,986,0,210,36,"余额",17,Muted).alignment=TextAlignmentOptions.Right;
        Box("Divider",history,0,42,1216,1,Stroke);
        shop.historyScroll=Scroll("HistoryScroll",history,0,52,1216,422,out var historyContent);shop.historyContent=historyContent;
        var vertical=historyContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();vertical.spacing=4;vertical.childControlHeight=true;vertical.childControlWidth=true;vertical.childForceExpandHeight=false;
        shop.historyTemplate=BuildHistoryRow(historyContent);shop.historyTemplate.gameObject.SetActive(false);
        shop.emptyHistory=Label("Empty",history,0,110,1216,80,"暂无收支记录",22,Muted);shop.emptyHistory.alignment=TextAlignmentOptions.Center;
        shop.moreButton=Button("More",window,"加载更多",C("414141"),Ink);Top((RectTransform)shop.moreButton.transform,1078,670,170,40);
        Box("FooterDivider",window,32,730,1216,1,C("555555"));
        shop.statusLabel=Label("Status",window,32,668,860,36,"",16,Muted);
        shop.earnedLabel=Label("Earned",window,32,706,860,24,"",15,Muted);
        // Stateful labels are localized by ShopPanel after rendering live data.
        foreach (var text in new[] { shop.balanceLabel, shop.emptyGoods, shop.buyLabel }) Dynamic(text);
        history.gameObject.SetActive(false);shop.moreButton.gameObject.SetActive(false);
        root.gameObject.SetActive(false);
    }

    static ShopRow BuildRow(RectTransform parent) {
        var root=Rect("RowTemplate",parent);root.sizeDelta=new Vector2(270,306);
        var row=root.gameObject.AddComponent<ShopRow>();
        row.surface=root.gameObject.AddComponent<UnityEngine.UI.Image>();row.surface.color=Card;row.surface.raycastTarget=true;
        row.button=root.gameObject.AddComponent<UnityEngine.UI.Button>();row.button.targetGraphic=row.surface;row.button.transition=UnityEngine.UI.Selectable.Transition.None;
        var selection=Rect("Selection",root);Fill(selection);row.selection=selection.gameObject;
        Box("Top",selection,0,0,270,2,Accent);Box("Bottom",selection,0,304,270,2,Accent);
        Box("Left",selection,0,0,2,306,Accent);Box("Right",selection,268,0,2,306,Accent);
        row.kind=Label("Kind",root,18,14,234,24,"",15,Muted);
        row.icon=Box("Icon",root,69,50,132,132,Color.white);row.icon.preserveAspect=true;row.icon.enabled=false;
        var artwork=Rect("Artwork",root);Top(artwork,69,50,132,132);row.artwork=artwork.gameObject.AddComponent<WarehouseIconGraphic>();row.artwork.color=Color.white;row.artwork.raycastTarget=false;
        row.glyph=Label("Glyph",root,69,92,132,58,"",34,Ink);row.glyph.alignment=TextAlignmentOptions.Center;
        row.label=Label("Name",root,18,196,234,34,"",24,Ink);
        row.price=Label("Price",root,18,240,234,30,"",23,Accent);
        row.owned=Label("Owned",root,18,278,234,22,"",15,Muted);
        return row;
    }

    static ShopWalletRow BuildHistoryRow(RectTransform parent) {
        var root=Rect("RowTemplate",parent);root.gameObject.AddComponent<UnityEngine.UI.Image>().color=C("3A3A3A");
        var layout=root.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();layout.preferredHeight=46;
        var row=root.gameObject.AddComponent<ShopWalletRow>();
        row.date=Label("Date",root,20,0,200,46,"",18,Muted);
        row.source=Label("Source",root,252,0,450,46,"",18,Ink);
        row.amount=Label("Amount",root,752,0,200,46,"",20,Ink);row.amount.alignment=TextAlignmentOptions.Right;
        row.balance=Label("Balance",root,986,0,210,46,"",18,Muted);row.balance.alignment=TextAlignmentOptions.Right;
        return row;
    }

    static void BakeEntry(CompleteLobbyController owner) {
        var developer=owner.developerOptions;
        if (!developer.shopButton) {
            var source=developer.classroomButton;
            var content=(RectTransform)source.transform.parent;
            var frame=(RectTransform)content.parent;
            Undo.RegisterFullObjectHierarchyUndo(developer.gameObject,"Add developer shop entry");
            float width=content.sizeDelta.x;
            content.sizeDelta+=new Vector2(0,88);frame.sizeDelta+=new Vector2(0,88);
            Top((RectTransform)source.transform,0,0,width,76);
            var button=Button("OpenShopButton",content,"",C("414141"),Ink);Top((RectTransform)button.transform,0,88,width,76);
            Label("Title",button.transform,18,8,width-36,30,"商店",22,Ink);
            Label("Description",button.transform,18,42,250,22,"使用萨拉币购买物品",15,Muted);
            developer.walletLabel=Label("Wallet",button.transform,width-266,30,248,30,"持有萨拉币 —",17,Accent);
            developer.walletLabel.alignment=TextAlignmentOptions.Right;
            Dynamic(developer.walletLabel);
            developer.shopButton=button;
        }
    }

    static void RefineShop(ShopPanel shop) {
        var window = shop.transform.Find("ShopWindow");
        foreach (var name in new[] { "Refresh", "Subtitle" }) {
            var unused = window.Find(name);
            if (unused) UnityEngine.Object.DestroyImmediate(unused.gameObject);
        }
        Top((RectTransform)window.Find("Title"),32,28,600,48);
        Top((RectTransform)window.Find("BalanceCaption"),908,18,280,24);
        Top(shop.balanceLabel.rectTransform,888,42,300,40);
        var rule = window.Find("RewardRule").GetComponent<TMP_Text>();
        Top(rule.rectTransform,364,112,882,56);
        rule.textWrappingMode = TextWrappingModes.Normal;
        rule.overflowMode = TextOverflowModes.Overflow;
        rule.fontSize = rule.fontSizeMax = 16; rule.fontSizeMin = 14;
        rule.enableAutoSizing = true;
        var localized = rule.GetComponent<LocalizedPlatformLabel>();
        localized.SetSource(ShopPanel.RewardRuleText);
        localized.authoredSize = 16; localized.minimumSize = 14; localized.authoredAutoSize = true;
        EditorUtility.SetDirty(localized); EditorUtility.SetDirty(rule);
        Top((RectTransform)shop.moreButton.transform,1128,670,120,40);
        shop.moreButton.name = "NextHistoryPage";
        shop.moreButton.GetComponentInChildren<LocalizedPlatformLabel>(true).SetSource("下一页");
        var previous = window.Find("PreviousHistoryPage");
        shop.previousPageButton = previous ? previous.GetComponent<UnityEngine.UI.Button>() : Button("PreviousHistoryPage",window,"上一页",C("414141"),Ink);
        Top((RectTransform)shop.previousPageButton.transform,872,670,120,40);
        var pageLabel = window.Find("HistoryPageLabel");
        shop.historyPageLabel = pageLabel ? pageLabel.GetComponent<TMP_Text>() : Label("HistoryPageLabel",window,996,670,128,40,"",17,Ink);
        shop.historyPageLabel.alignment = TextAlignmentOptions.Center;
        shop.previousPageButton.gameObject.SetActive(false);shop.historyPageLabel.gameObject.SetActive(false);
        Top(shop.statusLabel.rectTransform,32,668,820,36);
    }

    public static void Validate(Scene scene) {
        var owner=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        var shop=owner.shop;
        if(!shop||!owner.developerOptions.shopButton||!owner.developerOptions.walletLabel)throw new InvalidOperationException("商店入口未烘焙。");
        foreach(var name in new[]{"backdrop","closeButton","goodsTab","historyTab","buyButton","moreButton","previousPageButton","historyPageLabel","balanceLabel","earnedLabel","statusLabel","buyLabel","quantityInput","goodsRoot","historyRoot","detailRoot","goodsContent","historyContent","rowTemplate","historyTemplate","goodsScroll","historyScroll","descriptionScroll"}) {
            var property=new SerializedObject(shop).FindProperty(name);
            if(property==null||!property.objectReferenceValue)throw new InvalidOperationException("商店序列化引用缺失: "+name);
        }
        foreach(var text in shop.GetComponentsInChildren<TMP_Text>(true))
            if((text.fontStyle&FontStyles.Bold)!=0||text.fontWeight!=FontWeight.Regular)throw new InvalidOperationException("商店中文文本字重须为正常。");
        if(!shop.goodsScroll.viewport.GetComponent<UnityEngine.UI.Mask>()||!shop.historyScroll.viewport.GetComponent<UnityEngine.UI.Mask>())throw new InvalidOperationException("商店滚动区域缺少遮罩。");
    }

    static void FitTextHeights(System.Collections.Generic.IEnumerable<TMP_Text> texts) {
        foreach(var text in texts) {
            var rect=text.rectTransform;
            // CJK fallback fonts have a taller line box than the authored Latin font.
            // Ellipsis hides even one line when its vertical box is too short.
            if(rect.anchorMin.y==rect.anchorMax.y && rect.sizeDelta.y>0)
                rect.sizeDelta=new Vector2(rect.sizeDelta.x,Mathf.Max(rect.sizeDelta.y,Mathf.Ceil(text.fontSize*1.6f)));
        }
    }
    static void Dynamic(TMP_Text text) {var label=text.GetComponent<LocalizedPlatformLabel>();if(label)UnityEngine.Object.DestroyImmediate(label);}
    static RectTransform Rect(string name,Transform parent) {var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"Author shop UI");go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    static void Top(RectTransform rect,float x,float y,float width,float height) {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
    static void Fill(RectTransform rect) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=Vector2.zero;rect.anchoredPosition=Vector2.zero;}
    static void Center(RectTransform rect,float width,float height) {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(width,height);}
    static UnityEngine.UI.Image Box(string name,Transform parent,float x,float y,float width,float height,Color color) {var rect=Rect(name,parent);Top(rect,x,y,width,height);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=false;return image;}
    static TMP_Text Label(string name,Transform parent,float x,float y,float width,float height,string value,float size,Color color) {
        var rect=Rect(name,parent);Top(rect,x,y,width,height);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font;text.fontSize=size;text.fontStyle=FontStyles.Normal;text.fontWeight=FontWeight.Regular;text.color=color;text.richText=false;text.raycastTarget=false;text.text=value;
        text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.alignment=TextAlignmentOptions.MidlineLeft;
        var binding=rect.gameObject.AddComponent<PlatformFontBinding>();binding.authoredFont=font;binding.authoredMaterial=font.material;binding.authoredStyle=FontStyles.Normal;binding.authoredWeight=FontWeight.Regular;binding.styleCaptured=true;
        if(!string.IsNullOrEmpty(value)){var label=rect.gameObject.AddComponent<LocalizedPlatformLabel>();label.key=value;label.authoredSize=size;label.minimumSize=Math.Min(size,13);label.fitTranslation=true;}
        return text;
    }
    static UnityEngine.UI.Button Button(string name,Transform parent,string value,Color surface,Color ink) {
        var rect=Rect(name,parent);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=surface;image.raycastTarget=true;
        var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
        var colors=button.colors;colors.highlightedColor=new Color(1.08f,1.08f,1.08f);colors.pressedColor=new Color(.84f,.84f,.84f);colors.disabledColor=new Color(.52f,.52f,.52f,.75f);button.colors=colors;
        if(!string.IsNullOrEmpty(value)){var label=Label("Label",rect,0,0,0,0,value,19,ink);Fill(label.rectTransform);label.alignment=TextAlignmentOptions.Center;}
        return button;
    }
    static UnityEngine.UI.ScrollRect Scroll(string name,Transform parent,float x,float y,float width,float height,out RectTransform content) {
        var root=Rect(name,parent);Top(root,x,y,width,height);var scroll=root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport=Rect("Viewport",root);Fill(viewport);var image=viewport.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Color.white;image.raycastTarget=true;
        var mask=viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();mask.showMaskGraphic=false;
        content=Rect("Content",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
        var fit=content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();fit.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.scrollSensitivity=38;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;
        return scroll;
    }
}
#endif
