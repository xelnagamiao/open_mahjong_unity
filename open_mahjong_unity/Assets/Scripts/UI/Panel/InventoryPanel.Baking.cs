#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed partial class InventoryPanel {
    private static Color WarehouseColor(string hex,float alpha=1) { ColorUtility.TryParseHtmlString("#"+hex,out var color);color.a=alpha;return color; }
    private static Color WarehouseInk => WarehouseColor("244359");
    private static Color WarehouseMuted => WarehouseColor("547286");
    private static Color WarehouseAccent => WarehouseColor("287DA5");
    private static Color WarehousePaper => WarehouseColor("F1F6F9");

    public void BakeWarehouseUi() {
        if (Application.isPlaying) throw new InvalidOperationException("仓库布局只能在编辑模式下烘焙。");
        var authoredLayout = GetComponent<Salasasa.Home.InventoryLayoutVariants>();
        if (authoredLayout && authoredLayout.VariantCount == 1) {
            BakeWarehouseCategories();
            authoredLayout.SelectVariant(0);
            ValidateWarehouseUi();
            return;
        }
        Undo.RegisterFullObjectHierarchyUndo(gameObject,"Bake compact warehouse UI");
        gameObject.name="StorePanel";
        var root=(RectTransform)transform;
        root.anchorMin=root.anchorMax=root.pivot=new Vector2(.5f,.5f);root.sizeDelta=new Vector2(1488,780);root.anchoredPosition=Vector2.zero;
        var scaler=GetComponentInParent<UnityEngine.UI.CanvasScaler>();
        root.localScale=Vector3.one*(scaler ? scaler.referenceResolution.x/1600f : 1);
        var rootImage=GetComponent<UnityEngine.UI.Image>();rootImage.color=WarehousePaper;WarehouseRounded(rootImage);rootImage.raycastTarget=true;
        var canvas=GetComponent<Canvas>();UiCanvasLayers.Apply(canvas, UiCanvasLayers.Overlay, UiCanvasLayers.InventoryOrder);
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/CompleteCityBody.asset") ?? transform.Find("Heading").GetComponent<TMP_Text>().font;
        categorySurface=WarehouseColor("E0EBF1");categorySelectedSurface=WarehouseAccent;categoryInk=WarehouseInk;categorySelectedInk=Color.white;
        WarehouseBox(root,"Shadow",0,8,1488,780,new Color(0,0,0,.28f)).transform.SetAsFirstSibling();
        WarehouseBox(root,"Surface",0,0,1488,780,WarehousePaper).transform.SetSiblingIndex(1);
        WarehouseBox(root,"Header",1,1,1486,68,Color.white).transform.SetSiblingIndex(2);
        WarehouseLabel(root,"Heading",24,13,260,42,"仓库",28,WarehouseInk,font);
        foreach(string hidden in new[]{"Subtitle","CategoryHeading","CategoryDivider","CollectionHint","Divider","PreviewBackground"}) {
            var obsolete=root.Find(hidden);if(obsolete)obsolete.gameObject.SetActive(false);
        }
        var oldSearch=root.Find("Search");if(oldSearch)Undo.DestroyObjectImmediate(oldSearch.gameObject);
        WarehouseBox(root,"CategoryBackground",1,69,180,710,WarehouseColor("E7F0F5")).transform.SetSiblingIndex(3);
        categoryButtons=new UnityEngine.UI.Button[CategoryNames.Length];
        for(int i=0;i<CategoryNames.Length;i++) {
            var button=categoryButtons[i]=WarehouseButton(root,"Category_"+i,1,69+i*60,180,59,CategoryNames[i],font,i==0?WarehouseAccent:categorySurface);
            var text=button.GetComponentInChildren<TMP_Text>();WarehousePosition(text.rectTransform,14,0,152,59);text.fontSize=21;text.alignment=TextAlignmentOptions.Center;text.color=i==0?Color.white:WarehouseInk;
        }
        BakeWarehouseCategories();
        closeButton=WarehouseButton(root,"Close",1423,12,44,44,"×",font,WarehouseAccent);
        closeButton.GetComponentInChildren<TMP_Text>().color=Color.white;
        var scrollRoot=WarehouseRect(root,"ScrollView",204,90,884,586);
        var scroll=scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
        var viewport=(RectTransform)scrollRoot.Find("Viewport");WarehousePosition(viewport,0,0,870,586);
        viewport.GetComponent<UnityEngine.UI.Image>().color=Color.clear;viewport.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
        var mask=viewport.GetComponent<UnityEngine.UI.Mask>();if(mask)Undo.DestroyObjectImmediate(mask);
        if(!viewport.GetComponent<UnityEngine.UI.RectMask2D>())Undo.AddComponent<UnityEngine.UI.RectMask2D>(viewport.gameObject);
        content=(RectTransform)viewport.Find("Content");
        var oldLayout=content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();if(oldLayout)Undo.DestroyObjectImmediate(oldLayout);
        var grid=content.GetComponent<UnityEngine.UI.GridLayoutGroup>() ?? Undo.AddComponent<UnityEngine.UI.GridLayoutGroup>(content.gameObject);
        grid.cellSize=new Vector2(136,132);grid.spacing=new Vector2(10,10);grid.padding=new RectOffset();grid.constraint=UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=6;
        grid.startAxis=UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;grid.childAlignment=TextAnchor.UpperLeft;
        content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(0,1);content.sizeDelta=Vector2.zero;content.anchoredPosition=Vector2.zero;
        var fitter=content.GetComponent<UnityEngine.UI.ContentSizeFitter>();fitter.horizontalFit=UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.content=content;scroll.viewport=viewport;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;
        var barRect=WarehouseRect(scrollRoot,"Scrollbar",876,0,6,586);var bar=barRect.GetComponent<UnityEngine.UI.Scrollbar>();
        if(!bar) {
            WarehouseBox(scrollRoot,"Scrollbar",876,0,6,558,Color.clear);bar=Undo.AddComponent<UnityEngine.UI.Scrollbar>(barRect.gameObject);
            var sliding=WarehouseRect(barRect,"SlidingArea",0,0,6,558);var handle=WarehouseBox(sliding,"Handle",0,0,6,60,WarehouseAccent);handle.raycastTarget=true;bar.handleRect=handle.rectTransform;
        }
        WarehouseScrollbar(bar,WarehouseColor("244359",.10f),WarehouseAccent);
        if(scroll.verticalScrollbar && scroll.verticalScrollbar!=bar)scroll.verticalScrollbar.gameObject.SetActive(false);
        scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        rowTemplate=content.Find("RowTemplate").GetComponent<InventoryRow>();BakeWarehouseCard(rowTemplate,font);rowTemplate.gameObject.SetActive(false);
        emptyText=WarehouseLabel(viewport,"Empty",24,36,808,124,"没有物品",24,WarehouseMuted,font);emptyText.alignment=TextAlignmentOptions.Center;emptyText.gameObject.SetActive(false);
        WarehouseBox(root,"DetailBackground",1110,90,354,624,Color.white).transform.SetSiblingIndex(4);
        detailName=WarehouseLabel(root,"DetailName",1132,108,310,42,"",26,WarehouseInk,font);
        detailName.enableAutoSizing=true;detailName.fontSizeMin=18;detailName.fontSizeMax=26;detailName.overflowMode=TextOverflowModes.Ellipsis;
        detailType=WarehouseLabel(root,"DetailType",1132,155,310,27,"",17,WarehouseMuted,font);
        var previewRect=WarehouseRect(root,"Preview",1197,204,180,180);preview=previewRect.GetComponent<UnityEngine.UI.Image>();preview.raycastTarget=false;preview.preserveAspect=true;preview.color=Color.white;preview.enabled=false;
        var artRect=WarehouseRect(previewRect,"Artwork",0,0,180,180);previewArtwork=artRect.GetComponent<WarehouseIconGraphic>() ?? Undo.AddComponent<WarehouseIconGraphic>(artRect.gameObject);previewArtwork.color=Color.white;previewArtwork.raycastTarget=false;
        previewGlyph=WarehouseLabel(previewRect,"PreviewGlyph",0,58,180,60,"无",43,new Color(.94f,.96f,1),font);previewGlyph.alignment=TextAlignmentOptions.Center;
        detailCount=WarehouseLabel(root,"DetailCount",1132,408,310,28,"",18,WarehouseAccent,font);
        detailRule=WarehouseBox(root,"DetailRule",1132,456,310,1,WarehouseColor("244359",.16f));
        var detailsViewport=WarehouseRect(root,"DetailsViewport",1132,474,310,140);detailsViewport.GetComponent<UnityEngine.UI.Image>().color=Color.clear;
        details=detailsViewport.Find("Details").GetComponent<TMP_Text>();details.font=font;details.fontSize=19;details.color=WarehouseInk;details.richText=false;details.text="";details.lineSpacing=4;
        details.textWrappingMode=TextWrappingModes.Normal;details.overflowMode=TextOverflowModes.Overflow;
        details.rectTransform.anchorMin=new Vector2(0,1);details.rectTransform.anchorMax=Vector2.one;details.rectTransform.pivot=new Vector2(.5f,1);details.rectTransform.sizeDelta=new Vector2(-14,0);details.rectTransform.anchoredPosition=new Vector2(-7,0);
        var detailScroll=detailsViewport.GetComponent<UnityEngine.UI.ScrollRect>();detailScroll.viewport=detailsViewport;detailScroll.content=details.rectTransform;detailScroll.horizontal=false;detailScroll.vertical=true;detailScroll.scrollSensitivity=50;detailScroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;
        var detailBar=WarehouseRect(detailsViewport,"Scrollbar",303,0,7,140).GetComponent<UnityEngine.UI.Scrollbar>();WarehouseScrollbar(detailBar,WarehouseColor("244359",.10f),WarehouseAccent);detailScroll.verticalScrollbar=detailBar;detailScroll.verticalScrollbarVisibility=UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        quantityInput=root.Find("Quantity").GetComponent<TMP_InputField>();WarehousePosition((RectTransform)quantityInput.transform,1132,590,310,40);
        quantityInput.GetComponent<UnityEngine.UI.Image>().color=WarehouseColor("244359",.065f);WarehouseRounded(quantityInput.GetComponent<UnityEngine.UI.Image>());
        var value=WarehouseLabel(quantityInput.transform,"Value",208,2,58,36,"1",21,WarehouseInk,font);value.alignment=TextAlignmentOptions.Center;
        WarehouseLabel(quantityInput.transform,"Caption",10,0,148,40,"使用数量",17,WarehouseMuted,font);
        decreaseButton=WarehouseButton(quantityInput.transform,"Decrease",166,2,38,36,"−",font,categorySurface);
        increaseButton=WarehouseButton(quantityInput.transform,"Increase",270,2,38,36,"+",font,categorySurface);
        quantityInput.textComponent=(TextMeshProUGUI)value;quantityInput.textViewport=(RectTransform)quantityInput.transform;quantityInput.contentType=TMP_InputField.ContentType.IntegerNumber;quantityInput.characterLimit=3;
        quantityInput.customCaretColor=true;quantityInput.caretColor=WarehouseInk;quantityInput.selectionColor=WarehouseColor("287DA5",.25f);quantityInput.gameObject.SetActive(false);
        actionButton=WarehouseButton(root,"Action",1132,642,310,48,"使用",font,WarehouseAccent);actionLabel=actionButton.GetComponentInChildren<TMP_Text>();actionLabel.color=Color.white;
        statusText=WarehouseLabel(root,"Status",204,700,884,32,"",18,WarehouseMuted,font);
        WarehouseBorder(root,"WindowBorder",1488,780,1,WarehouseColor("287DA5",.45f));
        foreach(var text in GetComponentsInChildren<TMP_Text>(true))text.richText=false;
        gameObject.SetActive(false);
        foreach(var component in GetComponentsInChildren<Component>(true))if(component)EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(gameObject);BakeMenuEntry(font);ValidateWarehouseUi();
    }

    private void BakeMenuEntry(TMP_FontAsset font) {
        MeunPanel menu = null;
        foreach (var sceneRoot in gameObject.scene.GetRootGameObjects()) {
            menu = sceneRoot.GetComponentInChildren<MeunPanel>(true);
            if (menu) break;
        }
        if (!menu) throw new InvalidOperationException("主菜单缺失，无法烘焙背包入口。");
        var sidebar = menu.transform.Find("GameObject");
        if (!sidebar) throw new InvalidOperationException("主菜单侧栏缺失。");
        var existing = sidebar.Find("InventoryButton");
        if (existing) return;
        var button = existing ? existing.GetComponent<UnityEngine.UI.Button>()
            : WarehouseButton(sidebar,"InventoryButton",2,732,384,58,"背包",font,new Color(.17f,.23f,.33f));
        button.transform.SetAsLastSibling();
        while (button.onClick.GetPersistentEventCount() > 0)
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick,0);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,Open);
        foreach (var component in button.GetComponentsInChildren<Component>(true)) EditorUtility.SetDirty(component);
    }

    public void ValidateWarehouseUi() {
        if (gameObject.name != "StorePanel")
            throw new InvalidOperationException("仓库场景面板应命名为 StorePanel，请重新烘焙仓库 UI。");
        if (categoryButtons == null || categoryButtons.Length != CategoryNames.Length || !content || !rowTemplate || !preview || !previewArtwork
            || !detailName || !detailType || !detailCount || !detailRule || !emptyText || !quantityInput
            || !actionButton || !actionLabel || !details || !previewGlyph || !statusText || !closeButton
            || !decreaseButton || !increaseButton)
            throw new InvalidOperationException(name + " 仓库引用缺失，请烘焙仓库 UI。");
        foreach (var button in categoryButtons)
            if (!button || !button.targetGraphic || !button.GetComponentInChildren<TMP_Text>(true)) throw new InvalidOperationException("仓库筛选按钮或文本缺失。");
        for (int i = 0; i < categoryButtons.Length; i++)
            if (categoryButtons[i].GetComponentInChildren<TMP_Text>().text != CategoryNames[i])
                throw new InvalidOperationException("仓库分类顺序无效，请重新烘焙。");
        var grid = content.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (rowTemplate.gameObject.activeSelf || !grid)
            throw new InvalidOperationException("仓库物品模板或网格布局无效。");
        var layouts = GetComponent<Salasasa.Home.InventoryLayoutVariants>();
        int expectedColumns = 6;
        if (layouts) {
            if (layouts.VariantCount != 1 || layouts.Variant != 0 || layouts.Grid != grid)
                throw new InvalidOperationException("仓库布局方案或网格引用未完整烘焙。");
            if (layouts.BakedVariants[0].columns != 5 || layouts.BakedVariants[0].fullscreenBackground)
                throw new InvalidOperationException("仓库紧凑布局的列数或背景配置无效。");
            expectedColumns = layouts.BakedVariants[layouts.Variant].columns;
        }
        if (transform.Find("Search") || grid.constraint != UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount || grid.constraintCount != expectedColumns)
            throw new InvalidOperationException("仓库网格不符合已烘焙的布局方案，请重新烘焙。");
        var rowData = new SerializedObject(rowTemplate);
        foreach (string field in new[] {"button","icon","selectionFrame","artwork","glyph","label","badge","kind"})
            if (!rowData.FindProperty(field).objectReferenceValue) throw new InvalidOperationException("仓库卡片引用缺失：" + field);
    }

    private static void BakeWarehouseCard(InventoryRow row, TMP_FontAsset font) {
        const float w = 136, h = 132;
        var root = (RectTransform)row.transform; root.sizeDelta = new Vector2(w,h);
        var layout = root.GetComponent<UnityEngine.UI.LayoutElement>(); if (layout) Undo.DestroyObjectImmediate(layout);
        var background = root.GetComponent<UnityEngine.UI.Image>(); background.color = WarehouseColor("E1EBF1"); WarehouseRounded(background);
        var button = root.GetComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        var kind = WarehouseLabel(root,"Kind",0,0,1,1,"",15,WarehouseMuted,font); kind.gameObject.SetActive(false);
        var iconRect = WarehouseRect(root,"Icon",10,8,116,116);
        var icon = iconRect.GetComponent<UnityEngine.UI.Image>(); icon.raycastTarget = false; icon.preserveAspect = true; icon.color = Color.white;
        var artRect = WarehouseRect(iconRect,"Artwork",0,0,116,116);
        var art = artRect.GetComponent<WarehouseIconGraphic>() ?? Undo.AddComponent<WarehouseIconGraphic>(artRect.gameObject);
        art.color = Color.white; art.raycastTarget = false;
        var glyph = WarehouseLabel(iconRect,"Glyph",0,37,116,39,"无",28,new Color(.94f,.96f,1),font); glyph.alignment = TextAlignmentOptions.Center;
        var label = WarehouseLabel(root,"Label",0,0,1,1,"",15,WarehouseInk,font); label.gameObject.SetActive(false);
        WarehouseBorder(root,"CardBoundary",w,h,1,WarehouseColor("547286",.28f));
        var badgeBackground = WarehouseBox(root,"BadgeBackground",w-66,h-28,60,23,WarehouseColor("E1EBF1",.96f));
        var badge = WarehouseLabel(root,"Badge",7,h-28,w-14,24,"",16,WarehouseInk,font); badge.alignment = TextAlignmentOptions.MidlineRight;
        var frame = WarehouseBorder(root,"SelectionFrame",w,h,3,WarehouseAccent); frame.gameObject.SetActive(false);
        var data = new SerializedObject(row);
        data.FindProperty("button").objectReferenceValue = button; data.FindProperty("icon").objectReferenceValue = icon;
        data.FindProperty("selectionFrame").objectReferenceValue = frame; data.FindProperty("artwork").objectReferenceValue = art;
        data.FindProperty("glyph").objectReferenceValue = glyph; data.FindProperty("label").objectReferenceValue = label;
        data.FindProperty("badge").objectReferenceValue = badge; data.FindProperty("kind").objectReferenceValue = kind;
        data.FindProperty("surface").colorValue = WarehouseColor("E1EBF1"); data.FindProperty("selectedSurface").colorValue = WarehouseColor("D5EAF5");
        data.FindProperty("badgeInk").colorValue = WarehouseInk; data.FindProperty("equippedBadgeInk").colorValue = WarehouseInk;
        data.FindProperty("badgeBackground").objectReferenceValue = badgeBackground; data.FindProperty("compactCard").boolValue = true;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static UnityEngine.UI.Image WarehouseBorder(Transform parent,string name,float w,float h,float thickness,Color color) {
        var frame = WarehouseBox(parent,name,0,0,w,h,Color.clear);
        WarehouseBox(frame.transform,"Top",0,0,w,thickness,color);
        WarehouseBox(frame.transform,"Bottom",0,h-thickness,w,thickness,color);
        WarehouseBox(frame.transform,"Left",0,thickness,thickness,h-2*thickness,color);
        WarehouseBox(frame.transform,"Right",w-thickness,thickness,thickness,h-2*thickness,color);
        frame.transform.SetAsLastSibling(); return frame;
    }
    private static void WarehouseScrollbar(UnityEngine.UI.Scrollbar bar,Color track,Color handle) {
        var image=bar.GetComponent<UnityEngine.UI.Image>();image.color=track;WarehouseRounded(image);
        var handleImage=bar.handleRect.GetComponent<UnityEngine.UI.Image>();handleImage.color=handle;WarehouseRounded(handleImage);
        WarehouseFill((RectTransform)bar.handleRect.parent,0);WarehouseFill(bar.handleRect,0);
        bar.direction=UnityEngine.UI.Scrollbar.Direction.BottomToTop;bar.targetGraphic=handleImage;
    }

    private static RectTransform WarehouseRect(Transform parent, string name, float x, float y, float w, float h) {
        var found = parent.Find(name);
        var rect = found ? (RectTransform)found : new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        if (!found) { Undo.RegisterCreatedObjectUndo(rect.gameObject,"Add warehouse UI"); rect.SetParent(parent,false); }
        WarehousePosition(rect,x,y,w,h); return rect;
    }
    private static void WarehousePosition(RectTransform rect,float x,float y,float w,float h) {
        rect.anchorMin = rect.anchorMax = new Vector2(0,1); rect.pivot = new Vector2(0,1);
        rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(w,h); rect.localScale = Vector3.one;
    }
    private static void WarehouseFill(RectTransform rect,float padding) {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f,.5f);
        rect.offsetMin = new Vector2(padding,padding); rect.offsetMax = new Vector2(-padding,-padding);
    }
    private static UnityEngine.UI.Image WarehouseBox(Transform parent,string name,float x,float y,float w,float h,Color color) {
        var rect = WarehouseRect(parent,name,x,y,w,h);
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        image.color = color; image.raycastTarget = false; WarehouseRounded(image); return image;
    }
    private static TMP_Text WarehouseLabel(Transform parent,string name,float x,float y,float w,float h,string value,float size,Color color,TMP_FontAsset font) {
        float lineHeight=Mathf.Ceil(size*font.faceInfo.lineHeight/font.faceInfo.pointSize+4);
        float height=Mathf.Max(h,lineHeight);
        var rect = WarehouseRect(parent,name,x,y-(height-h)*.5f,w,height);
        var label = rect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        label.font = font; label.text = value; label.fontSize = size; label.color = color; label.raycastTarget = false;
        label.richText = false; label.enableAutoSizing = false; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }
    private static UnityEngine.UI.Button WarehouseButton(Transform parent,string name,float x,float y,float w,float h,string value,TMP_FontAsset font,Color color) {
        var image = WarehouseBox(parent,name,x,y,w,h,color); image.raycastTarget = true;
        WarehouseRounded(image);
        var button = image.GetComponent<UnityEngine.UI.Button>() ?? Undo.AddComponent<UnityEngine.UI.Button>(image.gameObject);
        button.targetGraphic = image; WarehouseButtonColors(button);
        var label = WarehouseLabel(image.transform,"Label",8,4,w-16,h-8,value,22,WarehouseInk,font);
        label.alignment = TextAlignmentOptions.Center; return button;
    }
    private static void WarehouseRounded(UnityEngine.UI.Image image) {
        image.sprite = null; image.type = UnityEngine.UI.Image.Type.Simple;
    }
    private static void WarehouseButtonColors(UnityEngine.UI.Button button) {
        var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1.07f,1.07f,1.07f,1);
        colors.pressedColor = new Color(.86f,.86f,.86f,1); colors.selectedColor = Color.white;
        colors.disabledColor = new Color(.70f,.76f,.80f,1); colors.fadeDuration = .08f; button.colors = colors;
    }
}
#endif
