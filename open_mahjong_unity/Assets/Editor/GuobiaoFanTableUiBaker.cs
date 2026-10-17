using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>固定入口、遮罩、页签及条目模板保存在主场景；运行时仅填充番种列表。</summary>
public static class GuobiaoFanTableUiBaker {
    private static readonly Color Ink = new Color(.065f, .10f, .17f);
    private static readonly Color Card = new Color(.095f, .15f, .24f);
    private static readonly Color Gold = new Color(.93f, .75f, .43f);
    private static readonly Color TextColor = new Color(.89f, .92f, .97f);

    [MenuItem("Tools/Mahjong/Main Scene/Bake Guobiao Fan Table")]
    public static void BakeActiveScene() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出播放模式。");
        var scene = SceneManager.GetActiveScene();
        Bake(scene);
        Validate(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void Bake(Scene scene) {
        foreach (var page in Find<GamePageControl>(scene)) {
            var pageData = new SerializedObject(page);
            var owner = (RulebookPanelController)pageData.FindProperty("rulebookPanelController").objectReferenceValue;
            var returnButton = (UnityEngine.UI.Button)pageData.FindProperty("rulebookButton").objectReferenceValue;
            ConfigureReader(owner, returnButton);
        }
        BakeMenuEntry(scene);
    }

    private static void ConfigureReader(RulebookPanelController owner, UnityEngine.UI.Button returnButton) {
        var ownerData = new SerializedObject(owner);
        var font = ((TMP_Text)ownerData.FindProperty("titleText").objectReferenceValue).font;
        var legacyEntry = owner.transform.Find("FanTableButton");
        if (legacyEntry) Object.DestroyImmediate(legacyEntry.gameObject);
        var panel = ownerData.FindProperty("fanTablePanel").objectReferenceValue as GuobiaoFanTablePanel;
        if (!panel) panel = CreatePanel(owner.transform.parent, font, returnButton);
        ConfigureHeader(panel, font, returnButton);
        ConfigurePages(panel, font);
        Set(panel, "rulebookController", owner);
        var manualLabel = (TMP_Text)ownerData.FindProperty("openButtonText").objectReferenceValue;
        manualLabel.fontSize = 20;
        manualLabel.enableAutoSizing = false;
        manualLabel.textWrappingMode = TextWrappingModes.NoWrap;
        EditorUtility.SetDirty(manualLabel);
        var manualTitle = (TMP_Text)ownerData.FindProperty("titleText").objectReferenceValue;
        manualTitle.fontSize = 34;
        EditorUtility.SetDirty(manualTitle);
        ownerData.FindProperty("fanTablePanel").objectReferenceValue = panel;
        ownerData.ApplyModifiedPropertiesWithoutUndo();
        foreach (var component in panel.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(owner);
    }

    public static void BakeMenuEntry(Scene scene) {
        foreach (var menu in Find<MeunPanel>(scene)) {
            var sidebar = menu.transform.Find("GameObject");
            var inventory = sidebar?.Find("InventoryButton")?.GetComponent<UnityEngine.UI.Button>();
            if (!inventory) throw new InvalidOperationException("主菜单背包入口缺失，无法烘焙番表入口。");
            var button = sidebar.Find("FanTableButton")?.GetComponent<UnityEngine.UI.Button>();
            if (!button) {
                button = Object.Instantiate(inventory, sidebar);
                button.name = "FanTableButton";
                button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            }
            button.transform.SetSiblingIndex(inventory.transform.GetSiblingIndex() + 1);
            button.GetComponentInChildren<TMP_Text>(true).text = "查看番数表";
            while (button.onClick.GetPersistentEventCount() > 0)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, 0);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, menu.OpenFanTable);

            var menuData = new SerializedObject(menu);
            var owner = menuData.FindProperty("rulebookPanelController").objectReferenceValue as RulebookPanelController;
            if (!owner) {
                var overlay = Rect("RulebookOverlay", menu.transform);
                Stretch(overlay, 0, 0, 0, 0);
                // 保持菜单生命周期，同时覆盖大厅聊天等浮层。
                var canvas = overlay.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 11;
                overlay.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                var page = Find<GamePageControl>(scene).First();
                var source = (RulebookPanelController)new SerializedObject(page).FindProperty("rulebookPanelController").objectReferenceValue;
                owner = Object.Instantiate(source, overlay);
                owner.name = "RuleBookController";
                owner.gameObject.SetActive(false);
                // 副本使用自己的阅读面板，不能保留对局面板的引用。
                Set(owner, "fanTablePanel", null);
                Set(menu, "rulebookPanelController", owner);
            }
            ConfigureReader(owner, button);
            foreach (var component in button.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(menu);
        }
    }

    private static GuobiaoFanTablePanel CreatePanel(Transform parent, TMP_FontAsset font, UnityEngine.UI.Button returnButton) {
        var root = Rect("GuobiaoFanTablePanel", parent);
        Stretch(root, 0, 0, 0, 0);
        root.gameObject.AddComponent<CanvasGroup>();
        var popup = root.gameObject.AddComponent<PanelPopupTransition>();
        var panel = root.gameObject.AddComponent<GuobiaoFanTablePanel>();
        var backdrop = Rect("Backdrop", root);
        Stretch(backdrop, 0, 0, 0, 0);
        var veil = Image(backdrop, new Color(0, 0, 0, .72f), true);
        var backdropButton = backdrop.gameObject.AddComponent<UnityEngine.UI.Button>();
        backdropButton.targetGraphic = veil;
        backdropButton.transition = UnityEngine.UI.Selectable.Transition.None;
        backdropButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };

        var frame = Rect("Dialog", root);
        frame.anchorMin = new Vector2(.05f, .055f);
        frame.anchorMax = new Vector2(.95f, .945f);
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        Image(frame, Ink, true);
        var outline = frame.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(.68f, .49f, .26f);
        outline.effectDistance = new Vector2(2, -2);

        var title = Label("Title", frame, font, "查看规则", 32, Gold);
        Top(title.rectTransform, 32, 150, 20, 50);
        var close = Button("CloseButton", frame, font, "关闭", 24);
        var closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1, 1);
        closeRect.sizeDelta = new Vector2(104, 52);
        closeRect.anchoredPosition = new Vector2(-28, -28);

        var tabs = Rect("FanTabs", frame);
        Top(tabs, 32, 32, 110, 54);
        var tabsLayout = tabs.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        tabsLayout.spacing = new Vector2(6, 6);
        tabsLayout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        tabsLayout.constraintCount = 13;
        tabsLayout.cellSize = new Vector2(100, 54);
        var buttons = new UnityEngine.UI.Button[GuobiaoFanCatalog.FanValues.Length];
        for (int i = 0; i < buttons.Length; i++) {
            buttons[i] = Button("Fan" + GuobiaoFanCatalog.FanValues[i], tabs, font, GuobiaoFanCatalog.FanValues[i] + "番", 25);
        }
        // 显式左右导航避免聚焦到被遮罩挡住的牌桌。
        for (int i = 0; i < buttons.Length; i++) buttons[i].navigation = new UnityEngine.UI.Navigation {
            mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnLeft = buttons[(i + buttons.Length - 1) % buttons.Length],
            selectOnRight = buttons[(i + 1) % buttons.Length], selectOnUp = close, selectOnDown = close
        };
        close.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnDown = buttons[0], selectOnUp = buttons[0] };

        var scrollRoot = Rect("ScrollView", frame);
        Stretch(scrollRoot, 32, 32, 182, 28);
        Image(scrollRoot, Color.clear, true);
        var scroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 95;
        var viewport = Rect("Viewport", scrollRoot);
        Stretch(viewport, 0, 22, 0, 0);
        Image(viewport, Color.white, true);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = Vector2.zero;
        var list = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        list.spacing = 12;
        list.childControlWidth = list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        list.padding = new RectOffset(0, 0, 0, 8);
        content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        scroll.viewport = viewport;

        var bar = Rect("Scrollbar", scrollRoot);
        bar.anchorMin = new Vector2(1, 0);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(1, .5f);
        bar.sizeDelta = new Vector2(9, 0);
        Image(bar, new Color(.17f, .22f, .31f), true);
        var handle = Rect("Handle", bar);
        Stretch(handle, 0, 0, 0, 0);
        var handleImage = Image(handle, Gold, true);
        var scrollbar = bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;

        var row = CreateRow(content, font);
        Set(panel, "popup", popup, "closeButton", close, "backdropButton", backdropButton, "returnButton", returnButton,
            "scroll", scroll, "rowTemplate", row, "tabsLayout", tabsLayout);
        var data = new SerializedObject(panel);
        var array = data.FindProperty("fanButtons");
        array.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
        return panel;
    }

    private static GuobiaoFanRow CreateRow(RectTransform content, TMP_FontAsset font) {
        var root = Rect("RowTemplate", content);
        Image(root, Card, false);
        var column = root.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        column.padding = new RectOffset(22, 22, 14, 14);
        column.spacing = 7;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        var header = Rect("Header", root);
        header.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 48;
        var name = Label("Name", header, font, "一般高", 30, Gold);
        Stretch(name.rectTransform, 0, 110, 0, 0);
        var value = Label("Value", header, font, "1 番", 25, Gold);
        value.alignment = TextAlignmentOptions.Right;
        Stretch(value.rectTransform, 0, 0, 0, 0);
        var description = Label("Description", root, font, "同一门两副相同的顺子。", 25, TextColor);
        var hand = Rect("Hand", root);
        hand.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 96;
        var handView = hand.gameObject.AddComponent<PlayerInfoHandView>();
        var handData = new SerializedObject(handView);
        handData.FindProperty("tileWidth").floatValue = 64;
        handData.FindProperty("meldGap").floatValue = 16;
        handData.FindProperty("tileBackground").objectReferenceValue = Resources.Load<Texture2D>("image/Cards/Surfaces/backgrounds/hand-default");
        handData.ApplyModifiedPropertiesWithoutUndo();
        var context = Label("Context", root, font, "", 20, new Color(.51f, .79f, .72f));
        context.gameObject.SetActive(false);
        var note = Label("Note", root, font, "计番说明", 21, new Color(.68f, .75f, .84f));
        var row = root.gameObject.AddComponent<GuobiaoFanRow>();
        Set(row, "nameText", name, "valueText", value, "descriptionText", description, "contextText", context, "noteText", note, "handView", handView);
        root.gameObject.SetActive(false);
        return row;
    }

    public static void Validate(Scene scene) {
        foreach (var menu in Find<MeunPanel>(scene)) {
            var owner = new SerializedObject(menu).FindProperty("rulebookPanelController").objectReferenceValue as RulebookPanelController;
            var sidebar = menu.transform.Find("GameObject");
            var inventory = sidebar?.Find("InventoryButton");
            var button = sidebar?.Find("FanTableButton")?.GetComponent<UnityEngine.UI.Button>();
            if (!owner || !owner.transform.IsChildOf(menu.transform) || !inventory || !button
                || button.transform.GetSiblingIndex() != inventory.GetSiblingIndex() + 1
                || button.GetComponentInChildren<TMP_Text>(true).text != "查看番数表"
                || button.onClick.GetPersistentEventCount() != 1
                || button.onClick.GetPersistentTarget(0) != menu
                || button.onClick.GetPersistentMethodName(0) != nameof(MeunPanel.OpenFanTable))
                throw new InvalidOperationException("首页番表入口未烘焙或绑定错误。");
        }
        foreach (var owner in Find<RulebookPanelController>(scene)) {
            var data = new SerializedObject(owner);
            if (!data.FindProperty("fanTablePanel").objectReferenceValue)
                throw new InvalidOperationException("国标番表入口未烘焙：" + owner.name);
            var panel = (GuobiaoFanTablePanel)data.FindProperty("fanTablePanel").objectReferenceValue;
            var panelData = new SerializedObject(panel);
            foreach (string field in new[] { "popup", "closeButton", "backdropButton", "returnButton", "scroll", "rowTemplate", "ruleDropdown", "fullRulebookButton", "tabsLayout", "rulebookController", "pageNavigation", "guideTemplate" })
                if (!panelData.FindProperty(field).objectReferenceValue) throw new InvalidOperationException("国标番表引用缺失：" + field);
            if (panelData.FindProperty("fanButtons").arraySize != GuobiaoFanCatalog.FanValues.Length) throw new InvalidOperationException("国标番数分类不完整。");
            var pages = panelData.FindProperty("pageButtons");
            if (pages.arraySize != 3) throw new InvalidOperationException("规则页面导航不完整。");
            for (int i = 0; i < pages.arraySize; i++)
                if (!pages.GetArrayElementAtIndex(i).objectReferenceValue) throw new InvalidOperationException("规则页面按钮缺失。");
            var guide = new SerializedObject(panelData.FindProperty("guideTemplate").objectReferenceValue);
            foreach (string field in new[] { "titleText", "bodyText", "headerBackground" })
                if (!guide.FindProperty(field).objectReferenceValue) throw new InvalidOperationException("规则正文模板缺失：" + field);
            var scroll = (UnityEngine.UI.ScrollRect)panelData.FindProperty("scroll").objectReferenceValue;
            if (!scroll.content || !scroll.viewport || !scroll.viewport.GetComponent<UnityEngine.UI.Mask>() || !scroll.verticalScrollbar)
                throw new InvalidOperationException("国标番表滚动结构缺失。");
            if (panel.gameObject.activeSelf) throw new InvalidOperationException("国标番表初始应隐藏。");
            var row = new SerializedObject(panelData.FindProperty("rowTemplate").objectReferenceValue);
            if (!row.FindProperty("headerBackground").objectReferenceValue) throw new InvalidOperationException("番种标题背景缺失。");
            var dropdown = (TMP_Dropdown)panelData.FindProperty("ruleDropdown").objectReferenceValue;
            if (!dropdown.template || !dropdown.options.Select(x => x.text).SequenceEqual(MahjongFanCatalog.RuleNames))
                throw new InvalidOperationException("番表规则下拉框未配置。");
        }
    }

    private static void ConfigureHeader(GuobiaoFanTablePanel panel, TMP_FontAsset font, UnityEngine.UI.Button returnButton) {
        var frame = (RectTransform)panel.transform.Find("Dialog");
        frame.anchorMin = new Vector2(.10f, .11f);
        frame.anchorMax = new Vector2(.90f, .89f);
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        foreach (string name in new[] { "Subtitle", "Footer", "Selection" }) {
            var legacy = frame.Find(name);
            if (legacy) Object.DestroyImmediate(legacy.gameObject);
        }
        var title = frame.Find("Title").GetComponent<TMP_Text>();
        title.text = "查看规则";
        title.fontSize = 32;
        Place(title.rectTransform, 24, 24, 144, 48);
        var dropdown = frame.Find("RuleDropdown")?.GetComponent<TMP_Dropdown>();
        if (!dropdown) {
            var go = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources {
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd")
            });
            go.name = "RuleDropdown";
            go.transform.SetParent(frame, false);
            dropdown = go.GetComponent<TMP_Dropdown>();
            foreach (var child in go.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = frame.gameObject.layer;
        }
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        dropdown.ClearOptions();
        dropdown.AddOptions(new System.Collections.Generic.List<string>(MahjongFanCatalog.RuleNames));
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        Place((RectTransform)dropdown.transform, 184, 24, 220, 48);
        dropdown.GetComponent<UnityEngine.UI.Image>().color = new Color(.14f, .21f, .33f);
        dropdown.template.GetComponent<UnityEngine.UI.Image>().color = Card;
        dropdown.template.sizeDelta = new Vector2(0, 56 * MahjongFanCatalog.RuleNames.Length + 8);
        dropdown.template.anchoredPosition = new Vector2(0, -6);
        var content = (RectTransform)dropdown.template.Find("Viewport/Content");
        content.sizeDelta = new Vector2(0, 64);
        var item = (RectTransform)content.Find("Item");
        item.sizeDelta = new Vector2(0, 56);
        item.Find("Item Background").GetComponent<UnityEngine.UI.Image>().color = new Color(.20f, .28f, .40f);
        item.Find("Item Checkmark").GetComponent<UnityEngine.UI.Image>().color = Gold;
        foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true)) {
            text.font = font;
            text.fontSize = 22;
            text.color = TextColor;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;
        }
        dropdown.captionText.rectTransform.offsetMin = new Vector2(16, 0);
        dropdown.captionText.rectTransform.offsetMax = new Vector2(-34, 0);
        dropdown.itemText.rectTransform.offsetMin = new Vector2(30, 0);
        dropdown.itemText.rectTransform.offsetMax = new Vector2(-8, 0);
        var manual = frame.Find("FullRulebookButton")?.GetComponent<UnityEngine.UI.Button>();
        if (!manual) manual = Button("FullRulebookButton", frame, font, "打开完整说明书", 24);
        Place((RectTransform)manual.transform, 420, 24, 240, 48);
        manual.GetComponentInChildren<TMP_Text>(true).fontSize = 22;
        var close = frame.Find("CloseButton").GetComponent<UnityEngine.UI.Button>();
        var closeRect = (RectTransform)close.transform;
        closeRect.sizeDelta = new Vector2(96, 48);
        closeRect.anchoredPosition = new Vector2(-24, -24);
        close.GetComponentInChildren<TMP_Text>(true).fontSize = 22;
        var data = new SerializedObject(panel);
        var firstTab = (UnityEngine.UI.Button)data.FindProperty("fanButtons").GetArrayElementAtIndex(0).objectReferenceValue;
        var tabs = data.FindProperty("fanButtons");
        for (int i = 0; i < tabs.arraySize; i++) {
            var button = (UnityEngine.UI.Button)tabs.GetArrayElementAtIndex(i).objectReferenceValue;
            button.GetComponentInChildren<TMP_Text>(true).fontSize = 22;
            var navigation = button.navigation;
            navigation.selectOnUp = dropdown;
            button.navigation = navigation;
        }
        dropdown.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnRight = manual, selectOnDown = firstTab };
        manual.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnLeft = dropdown, selectOnRight = close, selectOnDown = firstTab };
        close.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnLeft = manual, selectOnDown = firstTab };
        var scrollRect = (RectTransform)frame.Find("ScrollView");
        Stretch(scrollRect, 24, 24, 152, 24);
        Top((RectTransform)frame.Find("FanTabs"), 24, 24, 92, 46);
        ConfigureRow((GuobiaoFanRow)data.FindProperty("rowTemplate").objectReferenceValue);
        Set(panel, "ruleDropdown", dropdown, "fullRulebookButton", manual, "returnButton", returnButton);
    }

    private static void ConfigureRow(GuobiaoFanRow row) {
        var column = row.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        column.padding = new RectOffset();
        column.spacing = 0;
        var header = (RectTransform)row.transform.Find("Header");
        header.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 44;
        var headerImage = header.GetComponent<UnityEngine.UI.Image>();
        if (!headerImage) headerImage = Image(header, new Color(.17f, .24f, .38f), false);
        Stretch((RectTransform)header.Find("Name"), 18, 190, 0, 0);
        Stretch((RectTransform)header.Find("Value"), 18, 18, 0, 0);
        header.Find("Name").GetComponent<TMP_Text>().fontSize = 26;
        header.Find("Value").GetComponent<TMP_Text>().fontSize = 22;
        var body = row.transform.Find("Body") as RectTransform;
        if (!body) {
            body = Rect("Body", row.transform);
            var layout = body.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 10, 14);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            foreach (string name in new[] { "Description", "Hand", "Context", "Note" })
                row.transform.Find(name).SetParent(body, false);
        }
        body.Find("Description").GetComponent<TMP_Text>().fontSize = 22;
        body.Find("Note").GetComponent<TMP_Text>().fontSize = 19;
        var context = body.Find("Context");
        context.GetComponent<TMP_Text>().text = "";
        context.gameObject.SetActive(false);
        var hand = body.Find("Hand");
        hand.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 81;
        var handData = new SerializedObject(hand.GetComponent<PlayerInfoHandView>());
        handData.FindProperty("tileWidth").floatValue = 56;
        handData.FindProperty("meldGap").floatValue = 12;
        handData.ApplyModifiedPropertiesWithoutUndo();
        Set(row, "headerBackground", headerImage);
    }

    private static void ConfigurePages(GuobiaoFanTablePanel panel, TMP_FontAsset font) {
        var frame = (RectTransform)panel.transform.Find("Dialog");
        var navigation = frame.Find("PageNavigation") as RectTransform;
        if (!navigation) navigation = Rect("PageNavigation", frame);
        Place(navigation, 24, 92, 132, 280);
        var column = navigation.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (!column) column = navigation.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        column.spacing = 12;
        column.childControlHeight = column.childControlWidth = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;
        string[] names = { "Fans", "Rules", "Scoring" };
        string[] labels = { "番种表", "游戏规则", "计分原则" };
        var buttons = new UnityEngine.UI.Button[3];
        for (int i = 0; i < buttons.Length; i++) {
            var button = navigation.Find(names[i])?.GetComponent<UnityEngine.UI.Button>();
            if (!button) button = Button(names[i], navigation, font, labels[i], 24);
            var size = button.GetComponent<UnityEngine.UI.LayoutElement>();
            if (!size) size = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.preferredHeight = 84;
            var text = button.GetComponentInChildren<TMP_Text>(true);
            text.fontSize = 24;
            text.color = i == 0 ? Gold : new Color(.77f, .82f, .89f);
            text.text = labels[i];
            button.image.color = i == 0 ? new Color(.17f, .24f, .38f) : new Color(.11f, .17f, .26f);
            buttons[i] = button;
        }
        var data = new SerializedObject(panel);
        var pages = data.FindProperty("pageButtons");
        pages.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++) pages.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        var scroll = (UnityEngine.UI.ScrollRect)data.FindProperty("scroll").objectReferenceValue;
        var template = scroll.content.GetComponentInChildren<MahjongRuleGuideRow>(true);
        if (!template) template = CreateGuideRow(scroll.content, font);
        Set(panel, "pageNavigation", navigation, "guideTemplate", template);
        Stretch((RectTransform)scroll.transform, 174, 24, 204, 24);
        Top((RectTransform)frame.Find("FanTabs"), 174, 24, 92, 98);
        foreach (string name in new[] { "RuleDropdown", "FullRulebookButton", "CloseButton" }) {
            var selectable = frame.Find(name).GetComponent<UnityEngine.UI.Selectable>();
            var nav = selectable.navigation;
            nav.selectOnDown = buttons[0];
            selectable.navigation = nav;
        }
    }

    private static MahjongRuleGuideRow CreateGuideRow(RectTransform content, TMP_FontAsset font) {
        var root = Rect("GuideTemplate", content);
        Image(root, Card, false);
        GuideColumn(root, new RectOffset());
        var header = Rect("Header", root);
        var background = Image(header, new Color(.17f, .24f, .38f), false);
        GuideColumn(header, new RectOffset(18, 18, 8, 8));
        var title = Label("Title", header, font, "规则简介", 26, Gold);
        title.textWrappingMode = TextWrappingModes.Normal;
        var body = Rect("Body", root);
        GuideColumn(body, new RectOffset(18, 18, 14, 18));
        var text = Label("Text", body, font, "", 22, TextColor);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.lineSpacing = 8;
        var row = root.gameObject.AddComponent<MahjongRuleGuideRow>();
        Set(row, "titleText", title, "bodyText", text, "headerBackground", background);
        root.gameObject.SetActive(false);
        return row;
    }

    private static void GuideColumn(RectTransform root, RectOffset padding) {
        var layout = root.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = padding;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(true)).ToArray();
    private static RectTransform Rect(string name, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    private static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycast) {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }
    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string text, float size, Color color) {
        var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.enableAutoSizing = false;
        label.color = color;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }
    private static UnityEngine.UI.Button Button(string name, Transform parent, TMP_FontAsset font, string text, float size) {
        var rect = Rect(name, parent);
        var image = Image(rect, new Color(.14f, .21f, .33f), true);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var label = Label("Text", rect, font, text, size, TextColor);
        Stretch(label.rectTransform, 3, 3, 0, 0);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom) {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
    private static void Top(RectTransform rect, float left, float right, float top, float height) {
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(.5f, 1);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }
    private static void Set(Object target, params object[] pairs) {
        var data = new SerializedObject(target);
        for (int i = 0; i < pairs.Length; i += 2) data.FindProperty((string)pairs[i]).objectReferenceValue = (Object)pairs[i + 1];
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
