#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed partial class InventoryPanel {
    private static Color WarehouseInk => new Color(.94f,.96f,1f);
    private static Color WarehouseMuted => new Color(.64f,.70f,.81f);
    private static Color WarehouseAccent => new Color(.52f,.66f,.95f);
    private static Color WarehousePaper => new Color(.10f,.12f,.19f);

    /// <summary>Edits the existing scene hierarchy; player code only populates item cards.</summary>
    public void BakeWarehouseUi() {
        if (Application.isPlaying) throw new InvalidOperationException("仓库布局只能在编辑模式下烘焙。");
        Undo.RegisterFullObjectHierarchyUndo(gameObject, "Bake warehouse UI");
        gameObject.name = "StorePanel";
        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f,.5f); root.pivot = new Vector2(.5f,.5f);
        root.sizeDelta = new Vector2(1440,760); root.anchoredPosition = new Vector2(0,12);
        GetComponent<UnityEngine.UI.Image>().color = new Color(.28f,.33f,.46f);
        var canvas = GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 260;
        var font = transform.Find("Heading").GetComponent<TMP_Text>().font;
        var shadow = WarehouseBox(root,"Shadow",-8,8,1456,764,new Color(0,0,0,.25f));
        shadow.gameObject.SetActive(true); shadow.transform.SetAsFirstSibling();
        WarehouseBox(root,"Surface",1,1,1438,758,WarehousePaper).transform.SetSiblingIndex(1);
        WarehouseBox(root,"Header",1,1,1438,79,new Color(.17f,.19f,.31f)).transform.SetSiblingIndex(2);
        WarehouseBox(root,"Accent",28,79,1384,1,new Color(1,1,1,.10f));
        WarehouseLabel(root,"Heading",28,17,240,48,"仓库",30,WarehouseInk,font);
        WarehouseLabel(root,"Subtitle",156,37,260,34,"",20,WarehouseMuted,font).gameObject.SetActive(false);
        summaryText = WarehouseLabel(root,"Summary",830,25,320,32,"可用改名次数  0",20,new Color(.82f,.86f,.95f),font);
        summaryText.alignment = TextAlignmentOptions.MidlineRight;
        summaryText.gameObject.SetActive(false);
        renameButton = WarehouseButton(root,"Rename",1160,25,120,32,"点击改名",font,Color.clear);
        var renameLabel = renameButton.GetComponentInChildren<TMP_Text>();
        renameLabel.fontSize = 20; renameLabel.color = WarehouseAccent;
        renameLabel.textWrappingMode = TextWrappingModes.NoWrap;
        renameButton.targetGraphic = renameLabel;
        renameButton.gameObject.SetActive(false);
        closeButton = WarehouseButton(root,"Close",1304,22,108,38,"关闭",font,new Color(.24f,.27f,.40f));
        WarehouseBox(root,"CategoryBackground",16,104,168,632,new Color(.12f,.14f,.22f)).transform.SetSiblingIndex(3);
        WarehouseLabel(root,"CategoryHeading",48,139,128,32,"",18,WarehouseMuted,font).gameObject.SetActive(false);
        categoryButtons = new UnityEngine.UI.Button[CategoryNames.Length];
        for (int i = 0; i < categoryButtons.Length; i++) {
            categoryButtons[i] = WarehouseButton(root,"Category_" + i,24,112 + i * 64,152,52,CategoryNames[i],font,
                i == 0 ? new Color(.24f,.30f,.47f) : new Color(.12f,.14f,.22f));
            WarehouseBox(categoryButtons[i].transform,"SelectedMark",0,12,3,28,WarehouseAccent).gameObject.SetActive(i == 0);
        }
        var oldCategory = root.Find("Category_4");
        if (oldCategory) oldCategory.gameObject.SetActive(false);
        WarehouseBox(root,"CategoryDivider",184,112,1,616,Color.clear).gameObject.SetActive(false);
        var oldSearch = root.Find("Search");
        if (oldSearch) Undo.DestroyObjectImmediate(oldSearch.gameObject);
        listCountText = WarehouseLabel(root,"ListCount",208,121,656,26,"背包  ·  0 件",18,WarehouseMuted,font);
        WarehouseLabel(root,"CollectionHint",240,722,624,28,"",18,WarehouseMuted,font).gameObject.SetActive(false);
        statusText = WarehouseLabel(root,"Status",208,704,764,32,"",18,WarehouseMuted,font);
        refreshButton = WarehouseButton(root,"Refresh",876,112,96,44,"刷新",font,new Color(.20f,.24f,.35f));
        var scrollRoot = WarehouseRect(root,"ScrollView",200,168,772,520);
        var scroll = scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
        var viewport = (RectTransform)scrollRoot.Find("Viewport");
        WarehouseFill(viewport,0);
        viewport.GetComponent<UnityEngine.UI.Image>().color = new Color(.08f,.10f,.16f);
        content = (RectTransform)viewport.Find("Content");
        var oldLayout = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (oldLayout) Undo.DestroyObjectImmediate(oldLayout);
        var grid = content.GetComponent<UnityEngine.UI.GridLayoutGroup>() ?? Undo.AddComponent<UnityEngine.UI.GridLayoutGroup>(content.gameObject);
        grid.cellSize = new Vector2(178,182); grid.spacing = new Vector2(12,12); grid.padding = new RectOffset(12,12,12,12);
        grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 4;
        grid.startAxis = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal; grid.childAlignment = TextAnchor.UpperLeft;
        content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1);
        content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        var fitter = content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 70;
        rowTemplate = content.Find("RowTemplate").GetComponent<InventoryRow>();
        BakeWarehouseCard(rowTemplate,font);
        rowTemplate.gameObject.SetActive(false);
        emptyText = WarehouseLabel(viewport,"Empty",24,210,724,100,"背包里还没有道具",23,WarehouseMuted,font);
        emptyText.alignment = TextAlignmentOptions.Center; emptyText.lineSpacing = 12; emptyText.gameObject.SetActive(false);
        WarehouseBox(root,"Divider",988,112,1,616,Color.clear).gameObject.SetActive(false);
        var detailBackground = WarehouseBox(root,"DetailBackground",992,104,424,632,new Color(.14f,.17f,.25f));
        detailBackground.transform.SetSiblingIndex(3); WarehouseRounded(detailBackground);
        detailType = WarehouseLabel(root,"DetailType",1020,124,368,28,"",18,WarehouseMuted,font);
        var previewBackground = WarehouseBox(root,"PreviewBackground",1020,168,368,164,new Color(.11f,.14f,.22f));
        WarehouseRounded(previewBackground); previewBackground.transform.SetSiblingIndex(detailBackground.transform.GetSiblingIndex() + 1);
        var previewRect = WarehouseRect(root,"Preview",1116,178,176,144);
        preview = previewRect.GetComponent<UnityEngine.UI.Image>(); preview.raycastTarget = false; preview.preserveAspect = true;
        preview.color = Color.white; preview.enabled = false;
        var artRect = WarehouseRect(previewRect,"Artwork",0,0,176,144);
        previewArtwork = artRect.GetComponent<WarehouseIconGraphic>() ?? Undo.AddComponent<WarehouseIconGraphic>(artRect.gameObject);
        previewArtwork.color = Color.white; previewArtwork.raycastTarget = false; previewArtwork.SetKind(0);
        previewGlyph = WarehouseLabel(previewRect,"PreviewGlyph",0,39,176,60,"无",32,WarehouseInk,font);
        previewGlyph.alignment = TextAlignmentOptions.Center; previewGlyph.transform.SetAsLastSibling();
        detailName = WarehouseLabel(root,"DetailName",1020,354,368,48,"暂无物品",30,WarehouseInk,font);
        detailName.enableAutoSizing = true; detailName.fontSizeMin = 22; detailName.fontSizeMax = 30;
        detailCount = WarehouseLabel(root,"DetailCount",1020,407,368,28,"",19,WarehouseMuted,font);
        WarehouseBox(root,"DetailRule",1020,453,368,1,new Color(1,1,1,.09f));
        var detailsViewport = WarehouseRect(root,"DetailsViewport",1020,474,368,118);
        detailsViewport.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
        details = detailsViewport.Find("Details").GetComponent<TMP_Text>();
        details.font = font; details.fontSize = 20; details.color = WarehouseInk; details.richText = false;
        details.text = ""; details.lineSpacing = 4;
        details.textWrappingMode = TextWrappingModes.Normal; details.overflowMode = TextOverflowModes.Overflow;
        details.rectTransform.anchorMin = new Vector2(0,1); details.rectTransform.anchorMax = Vector2.one;
        details.rectTransform.pivot = new Vector2(.5f,1); details.rectTransform.sizeDelta = new Vector2(-14,0); details.rectTransform.anchoredPosition = new Vector2(-7,0);
        var detailScroll = detailsViewport.GetComponent<UnityEngine.UI.ScrollRect>();
        detailScroll.viewport = detailsViewport; detailScroll.content = details.rectTransform;
        detailScroll.horizontal = false; detailScroll.vertical = true; detailScroll.scrollSensitivity = 50;
        detailScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        var bar = WarehouseRect(detailsViewport,"Scrollbar",361,0,7,118).GetComponent<UnityEngine.UI.Scrollbar>();
        bar.GetComponent<UnityEngine.UI.Image>().color = new Color(.12f,.16f,.24f);
        bar.handleRect.GetComponent<UnityEngine.UI.Image>().color = WarehouseAccent;
        detailScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        quantityInput = transform.Find("Quantity").GetComponent<TMP_InputField>();
        WarehousePosition((RectTransform)quantityInput.transform,1020,610,368,44);
        quantityInput.GetComponent<UnityEngine.UI.Image>().color = new Color(.10f,.13f,.20f);
        var value = WarehouseLabel(quantityInput.transform,"Value",232,3,64,38,"1",22,WarehouseInk,font); value.alignment = TextAlignmentOptions.Center;
        WarehouseLabel(quantityInput.transform,"Caption",14,3,144,38,"使用数量",19,WarehouseMuted,font);
        decreaseButton = WarehouseButton(quantityInput.transform,"Decrease",184,4,40,36,"-",font,new Color(.23f,.28f,.39f));
        increaseButton = WarehouseButton(quantityInput.transform,"Increase",308,4,44,36,"+",font,new Color(.23f,.28f,.39f));
        quantityInput.textComponent = (TextMeshProUGUI)value; quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber; quantityInput.characterLimit = 3;
        quantityInput.textViewport = (RectTransform)quantityInput.transform;
        if (!quantityInput.GetComponent<WebGLSupport.WebGLInput>()) Undo.AddComponent<WebGLSupport.WebGLInput>(quantityInput.gameObject);
        quantityInput.gameObject.SetActive(false);
        actionButton = WarehouseButton(root,"Action",1020,678,368,48,"使用",font,new Color(.40f,.53f,.79f));
        actionLabel = actionButton.GetComponentInChildren<TMP_Text>();
        var obsolete = transform.Find("Unequip"); if (obsolete) Undo.DestroyObjectImmediate(obsolete.gameObject);
        foreach (var text in GetComponentsInChildren<TMP_Text>(true)) text.richText = false;
        gameObject.SetActive(false);
        foreach (var component in GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(gameObject);
        BakeMenuEntry(font);
        ValidateWarehouseUi();
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
        var button = WarehouseButton(sidebar,"InventoryButton",2,732,384,58,"背包",font,new Color(.17f,.23f,.33f));
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
            || !detailName || !detailType || !detailCount || !listCountText || !emptyText || !quantityInput
            || !actionButton || !actionLabel || !details || !previewGlyph || !summaryText || !statusText || !refreshButton || !closeButton
            || !decreaseButton || !increaseButton || !renameButton)
            throw new InvalidOperationException(name + " 仓库引用缺失，请烘焙仓库 UI。");
        foreach (var button in categoryButtons)
            if (!button || !button.transform.Find("SelectedMark")) throw new InvalidOperationException("仓库筛选按钮或选中标记缺失。");
        for (int i = 0; i < categoryButtons.Length; i++)
            if (categoryButtons[i].GetComponentInChildren<TMP_Text>().text != CategoryNames[i])
                throw new InvalidOperationException("仓库分类顺序无效，请重新烘焙。");
        var grid = content.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (rowTemplate.gameObject.activeSelf || !grid)
            throw new InvalidOperationException("仓库物品模板或网格布局无效。");
        if (transform.Find("Search") || grid.constraint != UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount || grid.constraintCount != 4)
            throw new InvalidOperationException("仓库应使用无搜索栏的四列布局，请重新烘焙。");
        var rowData = new SerializedObject(rowTemplate);
        foreach (string field in new[] {"button","icon","selectionFrame","artwork","glyph","label","badge","kind"})
            if (!rowData.FindProperty(field).objectReferenceValue) throw new InvalidOperationException("仓库卡片引用缺失：" + field);
    }

    private static void BakeWarehouseCard(InventoryRow row, TMP_FontAsset font) {
        var root = (RectTransform)row.transform; root.sizeDelta = new Vector2(178,182);
        var layout = root.GetComponent<UnityEngine.UI.LayoutElement>(); if (layout) Undo.DestroyObjectImmediate(layout);
        var background = root.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(.16f,.19f,.29f); WarehouseRounded(background);
        var button = root.GetComponent<UnityEngine.UI.Button>(); WarehouseButtonColors(button);
        var kind = WarehouseLabel(root,"Kind",10,10,158,24,"默认物品",15,WarehouseMuted,font);
        kind.gameObject.SetActive(false);
        var iconRect = WarehouseRect(root,"Icon",43,12,92,92);
        var icon = iconRect.GetComponent<UnityEngine.UI.Image>(); icon.raycastTarget = false; icon.preserveAspect = true; icon.color = Color.white;
        var artRect = WarehouseRect(iconRect,"Artwork",0,0,92,92);
        var art = artRect.GetComponent<WarehouseIconGraphic>() ?? Undo.AddComponent<WarehouseIconGraphic>(artRect.gameObject);
        art.color = Color.white; art.raycastTarget = false;
        var glyph = WarehouseLabel(iconRect,"Glyph",0,28,92,36,"无",22,WarehouseInk,font);
        glyph.alignment = TextAlignmentOptions.Center; glyph.transform.SetAsLastSibling();
        var label = WarehouseLabel(root,"Label",10,106,158,44,ConfigManager.DefaultTitleName,18,WarehouseInk,font);
        label.alignment = TextAlignmentOptions.Center; label.overflowMode = TextOverflowModes.Ellipsis;
        var badgeBackground = WarehouseBox(root,"BadgeBackground",43,154,92,22,new Color(0,0,0,.16f)); WarehouseRounded(badgeBackground);
        var badge = WarehouseLabel(root,"Badge",10,154,158,22,"使用中",15,WarehouseMuted,font);
        badge.alignment = TextAlignmentOptions.Center; badge.transform.SetAsLastSibling();
        var frame = WarehouseBox(root,"SelectionFrame",0,0,178,182,Color.clear);
        WarehouseBox(frame.transform,"Top",5,0,168,2,WarehouseAccent);
        WarehouseBox(frame.transform,"Bottom",5,180,168,2,WarehouseAccent);
        WarehouseBox(frame.transform,"Left",0,5,2,172,WarehouseAccent);
        WarehouseBox(frame.transform,"Right",176,5,2,172,WarehouseAccent);
        frame.transform.SetAsLastSibling(); frame.gameObject.SetActive(false);
        var data = new SerializedObject(row);
        data.FindProperty("button").objectReferenceValue = button; data.FindProperty("icon").objectReferenceValue = icon;
        data.FindProperty("selectionFrame").objectReferenceValue = frame; data.FindProperty("artwork").objectReferenceValue = art;
        data.FindProperty("glyph").objectReferenceValue = glyph; data.FindProperty("label").objectReferenceValue = label;
        data.FindProperty("badge").objectReferenceValue = badge; data.FindProperty("kind").objectReferenceValue = kind;
        data.ApplyModifiedPropertiesWithoutUndo();
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
        image.color = color; image.raycastTarget = false; return image;
    }
    private static TMP_Text WarehouseLabel(Transform parent,string name,float x,float y,float w,float h,string value,float size,Color color,TMP_FontAsset font) {
        var rect = WarehouseRect(parent,name,x,y,w,h);
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
        var label = WarehouseLabel(image.transform,"Label",8,4,w-16,h-8,value,22,new Color(.98f,.96f,.90f),font);
        label.alignment = TextAlignmentOptions.Center; return button;
    }
    private static void WarehouseRounded(UnityEngine.UI.Image image) {
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = UnityEngine.UI.Image.Type.Sliced;
    }
    private static void WarehouseButtonColors(UnityEngine.UI.Button button) {
        var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1.07f,1.07f,1.07f,1);
        colors.pressedColor = new Color(.86f,.86f,.86f,1); colors.selectedColor = Color.white;
        colors.disabledColor = new Color(.75f,.75f,.75f,.65f); colors.fadeDuration = .08f; button.colors = colors;
    }
}
#endif
